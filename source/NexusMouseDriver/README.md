# Nexus Virtual Mouse (Windows x64)

An optional KMDF driver uses Microsoft's Virtual HID Framework (VHF) to expose a
relative mouse on Windows 10 build 16299 or newer, including Windows 11.
Start NexusRecoil with `--kernel-mouse` to send movement and left
button reports through this device. Without that option, the app uses its existing
Windows input APIs. Keyboard hotkeys and held-key detection remain in user mode.

## Build

Install Visual Studio 2022 with Desktop development with C++, a matching Windows
SDK and Windows Driver Kit (WDK), and the WDK Visual Studio extension. This driver
is a separate project so building the existing .NET solution does not require WDK.
From a Visual Studio Developer PowerShell at the repository root:

```powershell
msbuild source\NexusMouseDriver\NexusMouseDriver.vcxproj /m /p:Configuration=Release /p:Platform=x64
dotnet publish source\Nexus-Recoil\Nexus-Recoil.csproj -c Release
```

The driver output is under `source\NexusMouseDriver\bin\x64\Release`. Use the
WDK-generated package containing the INF, SYS and catalog together. Driver signing
is required: use Microsoft's driver signing process for distribution, or the
WDK's documented test signing procedure on a dedicated Windows test machine.
A locally built unsigned SYS is not an installable production driver.

## Install and run

After building and signing the driver package, open an elevated WDK command prompt
in the package directory. Use the WDK's x64 DevCon tool to create the root device:

```powershell
devcon install .\NexusMouseDriver.inf 'Root\NexusVirtualMouse'
```

Run this once; `devcon install` creates another device if repeated. For subsequent
signed package updates, use `pnputil /add-driver .\NexusMouseDriver.inf /install`.
PnPUtil alone does not create the initial root device. Check Device Manager for
**Nexus Virtual Mouse** and confirm it reports no errors.

Start the published application from elevated PowerShell:

```powershell
.\NexusRecoil.exe --kernel-mouse
```

The device permits one administrator or SYSTEM client. A missing, busy or
inaccessible driver produces a startup error. A failed input request stops the
engine and displays an error; restart the app after fixing the driver connection.
To remove the device, close the app and run:

```powershell
devcon remove 'Root\NexusVirtualMouse'
```

Remove the matching OEM package from the driver store with PnPUtil if necessary;
identify its published name with `pnputil /enum-drivers` first.

## Interface and lifecycle

`\\.\NexusVirtualMouse` accepts only the buffered, write-access IOCTL `0x0022A004`.
The exact six-byte report in `Protocol.h` is report ID 1, three button bits and
signed little-endian 16-bit relative X/Y coordinates (-32767 through 32767).
Other IDs, reserved button bits, lengths, and IOCTLs are rejected. The app preserves
button state across movement reports. The driver sends a zero-button report when
the client handle closes, including process termination. VHF owns report buffering;
the device's cleanup callback deletes VHF synchronously.

Windows combines physical and virtual mouse state. Virtual button-up reports do
not guarantee release of a button held on another device. Test rapid fire with your
physical mouse and target application; virtual HID delivery may behave differently
from the existing Windows input backend. No compatibility with a particular game
or protected application is implied.

## Managed regression checks

From the repository root (no WDK or driver installation required):

```powershell
dotnet run --project tests/MouseOutput.Tests/MouseOutput.Tests.csproj -c Release
```

These tests exercise the real engine with a synthetic mouse backend and check the
managed wire layout; they do not execute kernel code.

## Validation status and Windows acceptance checks

The Linux development sandbox cannot compile a WDK driver, sign or install it, or
run WPF/Windows input checks. This source needs the following Windows checks before
it is considered ready to use:

1. Build the driver with warnings as errors and run WDK InfVerif on the generated INF.
2. Install a signed package on a Windows test machine; verify Device Manager status.
3. Check relative movement, button press/release, movement while pressed, and rapid
   fire with the physical mouse. Confirm normal mode still works without a driver.
4. Verify access denial for an unelevated client and rejection of malformed IOCTLs.
5. Stop/disable the engine and terminate the app while a virtual button is down;
   verify release. Test driver removal, reconnect, sleep/resume, and app restart.
6. Run Driver Verifier for this driver on the test machine and inspect any failures.

Reference: [Microsoft Virtual HID Framework](https://learn.microsoft.com/windows-hardware/drivers/hid/virtual-hid-framework--vhf-).
