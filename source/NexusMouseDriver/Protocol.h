#pragma once

// Include ntddk.h and wdf.h before this header.
#define IOCTL_NEXUS_SUBMIT_REPORT CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_WRITE_DATA)
#define NEXUS_REPORT_ID 1

#pragma pack(push, 1)
typedef struct _NEXUS_MOUSE_REPORT {
    UCHAR ReportId;
    UCHAR Buttons;
    SHORT X;
    SHORT Y;
} NEXUS_MOUSE_REPORT;
#pragma pack(pop)
C_ASSERT(sizeof(NEXUS_MOUSE_REPORT) == 6);
