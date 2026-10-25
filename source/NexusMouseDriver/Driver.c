#include <ntddk.h>
#include <wdf.h>
#include <hidport.h>
#include <vhf.h>
#include "Protocol.h"

typedef struct _DEVICE_CONTEXT {
    VHFHANDLE Vhf;
    WDFWAITLOCK ReportLock;
} DEVICE_CONTEXT;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, DeviceContext)

typedef struct _FILE_CONTEXT { BOOLEAN Closing; } FILE_CONTEXT;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(FILE_CONTEXT, FileContext)

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD NexusDeviceAdd;
EVT_WDF_OBJECT_CONTEXT_CLEANUP NexusDeviceCleanup;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL NexusIoControl;
EVT_WDF_FILE_CLEANUP NexusFileCleanup;

// Report 1: three buttons, five padding bits, signed relative 16-bit X/Y.
static UCHAR ReportDescriptor[] = {
    0x05,0x01, 0x09,0x02, 0xA1,0x01, 0x85,0x01,
    0x09,0x01, 0xA1,0x00,
    0x05,0x09, 0x19,0x01, 0x29,0x03, 0x15,0x00, 0x25,0x01,
    0x75,0x01, 0x95,0x03, 0x81,0x02,
    0x75,0x05, 0x95,0x01, 0x81,0x03,
    0x05,0x01, 0x09,0x30, 0x09,0x31,
    0x16,0x01,0x80, 0x26,0xFF,0x7F,
    0x75,0x10, 0x95,0x02, 0x81,0x06, 0xC0,0xC0
};

static NTSTATUS Submit(DEVICE_CONTEXT* context, NEXUS_MOUSE_REPORT* report)
{
    HID_XFER_PACKET packet = {0};
    packet.reportBuffer = (PUCHAR)report;
    packet.reportBufferLen = sizeof(*report);
    packet.reportId = NEXUS_REPORT_ID;
    // VHF's default buffering copies the report before returning.
    if (context->Vhf == NULL) return STATUS_DEVICE_NOT_READY;
    return VhfReadReportSubmit(context->Vhf, &packet);
}

NTSTATUS DriverEntry(PDRIVER_OBJECT driverObject, PUNICODE_STRING registryPath)
{
    WDF_DRIVER_CONFIG config;
    WDF_DRIVER_CONFIG_INIT(&config, NexusDeviceAdd);
    return WdfDriverCreate(driverObject, registryPath, WDF_NO_OBJECT_ATTRIBUTES,
        &config, WDF_NO_HANDLE);
}

NTSTATUS NexusDeviceAdd(WDFDRIVER driver, PWDFDEVICE_INIT deviceInit)
{
    NTSTATUS status;
    WDFDEVICE device;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_FILEOBJECT_CONFIG files;
    WDF_IO_QUEUE_CONFIG queue;
    VHF_CONFIG vhf;
    DEVICE_CONTEXT* context;
    DECLARE_CONST_UNICODE_STRING(name, L"\\Device\\NexusVirtualMouse");
    DECLARE_CONST_UNICODE_STRING(link, L"\\DosDevices\\NexusVirtualMouse");
    DECLARE_CONST_UNICODE_STRING(security, L"D:P(A;;GA;;;SY)(A;;GA;;;BA)");
    UNREFERENCED_PARAMETER(driver);

    WdfDeviceInitSetDeviceType(deviceInit, FILE_DEVICE_UNKNOWN);
    WdfDeviceInitSetCharacteristics(deviceInit, FILE_DEVICE_SECURE_OPEN, FALSE);
    WdfDeviceInitSetExclusive(deviceInit, TRUE);
    status = WdfDeviceInitAssignName(deviceInit, &name);
    if (!NT_SUCCESS(status)) return status;
    status = WdfDeviceInitAssignSDDLString(deviceInit, &security);
    if (!NT_SUCCESS(status)) return status;

    WDF_FILEOBJECT_CONFIG_INIT(&files, WDF_NO_EVENT_CALLBACK, WDF_NO_EVENT_CALLBACK, NexusFileCleanup);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, FILE_CONTEXT);
    WdfDeviceInitSetFileObjectConfig(deviceInit, &files, &attributes);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, DEVICE_CONTEXT);
    attributes.ExecutionLevel = WdfExecutionLevelPassive;
    attributes.EvtCleanupCallback = NexusDeviceCleanup;
    status = WdfDeviceCreate(&deviceInit, &attributes, &device);
    if (!NT_SUCCESS(status)) return status;
    context = DeviceContext(device);

    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ParentObject = device;
    status = WdfWaitLockCreate(&attributes, &context->ReportLock);
    if (!NT_SUCCESS(status)) return status;

    VHF_CONFIG_INIT(&vhf, WdfDeviceWdmGetDeviceObject(device), sizeof(ReportDescriptor), ReportDescriptor);
    status = VhfCreate(&vhf, &context->Vhf);
    if (!NT_SUCCESS(status)) return status;
    status = VhfStart(context->Vhf);
    if (!NT_SUCCESS(status)) return status;

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queue, WdfIoQueueDispatchSequential);
    queue.EvtIoDeviceControl = NexusIoControl;
    status = WdfIoQueueCreate(device, &queue, WDF_NO_OBJECT_ATTRIBUTES, WDF_NO_HANDLE);
    if (!NT_SUCCESS(status)) return status;
    return WdfDeviceCreateSymbolicLink(device, &link);
}

VOID NexusIoControl(WDFQUEUE queue, WDFREQUEST request, size_t outputLength,
    size_t inputLength, ULONG code)
{
    NTSTATUS status;
    NEXUS_MOUSE_REPORT* report;
    DEVICE_CONTEXT* context = DeviceContext(WdfIoQueueGetDevice(queue));
    UNREFERENCED_PARAMETER(outputLength);
    if (code != IOCTL_NEXUS_SUBMIT_REPORT) {
        WdfRequestComplete(request, STATUS_INVALID_DEVICE_REQUEST);
        return;
    }
    if (inputLength != sizeof(*report)) {
        WdfRequestComplete(request, STATUS_INFO_LENGTH_MISMATCH);
        return;
    }
    status = WdfRequestRetrieveInputBuffer(request, sizeof(*report), (PVOID*)&report, NULL);
    if (NT_SUCCESS(status)) {
        if (report->ReportId != NEXUS_REPORT_ID || (report->Buttons & ~7) != 0 ||
            report->X == -32768 || report->Y == -32768) {
            status = STATUS_INVALID_PARAMETER;
        } else {
            WdfWaitLockAcquire(context->ReportLock, NULL);
            if (WdfRequestGetFileObject(request) == NULL ||
                FileContext(WdfRequestGetFileObject(request))->Closing)
                status = STATUS_FILE_CLOSED;
            else
                status = Submit(context, report);
            WdfWaitLockRelease(context->ReportLock);
        }
    }
    WdfRequestComplete(request, status);
}

VOID NexusFileCleanup(WDFFILEOBJECT file)
{
    DEVICE_CONTEXT* context = DeviceContext(WdfFileObjectGetDevice(file));
    NEXUS_MOUSE_REPORT release = {NEXUS_REPORT_ID, 0, 0, 0};
    WdfWaitLockAcquire(context->ReportLock, NULL);
    FileContext(file)->Closing = TRUE;
    (void)Submit(context, &release);
    WdfWaitLockRelease(context->ReportLock);
}

VOID NexusDeviceCleanup(WDFOBJECT device)
{
    DEVICE_CONTEXT* context = DeviceContext(device);
    if (context->Vhf != NULL) {
        VhfDelete(context->Vhf, TRUE);
        context->Vhf = NULL;
    }
}
