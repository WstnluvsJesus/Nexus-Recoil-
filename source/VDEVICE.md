# Vdevice mouse backend

The `--vdevice` option uses a virtual USB mouse created through TitanCSA's documented
Vdevice C++ SDK. Profiles, sliders, hotkeys and recoil settings use the existing app
UI. The mouse backend creates a separate HID device; Windows combines its input
with physical devices. Physical mouse button interactions, including rapid fire,
must be tested on Windows. No game compatibility is claimed.

## Requirements

- Windows 10/11 x64 and administrator rights for creating the device.
- Visual Studio 2022 Desktop development with C++, including **C++ CMake tools for
  Windows**, plus the Windows SDK. No WDK is required to build this user-mode bridge.
- .NET 8 SDK.
- The vendor's Vdevice driver. This repository includes only its MIT-licensed SDK
  source, pinned to `v1.0.5.0-demo`, commit `4eb018655d432296dd5a23e8da6784c3217ae3ab`.

## Install the vendor driver

Download `VdeviceSetup.exe` from the [official pinned release](https://github.com/TitanCSA/Vdevice/releases/tag/v1.0.5.0-demo).
Use its Install action and restart Windows if requested. Its installer provides
status/diagnostics; verify it reports ready before launching the app. The vendor
advertises a Microsoft-signed driver package; we have not independently checked
its signature or loaded it in this Linux sandbox. Installer EXE signing and driver
package signing are separate. No Windows signing-policy changes are needed by this
integration, and it does not install drivers automatically.

**The public demo permits one hour per Windows kernel boot**, shared across
applications, starting at the first device creation. Closing the app or reinstalling
does not reset it. Expiry disconnects the virtual mouse; the app reports an error on
its next send and stops input. An unrestricted edition is available from the vendor
under separate terms. The app uses the official API without changing demo enforcement.

## Build the app and native bridge

Extract the source ZIP. Open **x64 Native Tools Command Prompt for VS2022**, change
to the extracted `Nexus-Recoil` folder, then run:

```cmd
source\NexusVdeviceBridge\build-app.cmd
```

The script builds `NexusVdeviceBridge.dll`, publishes the .NET app, and copies the
bridge and its SDK license into:

```text
source\Nexus-Recoil\bin\Release\net8.0-windows\win-x64\publish
```

Standalone bridge commands:

```cmd
cmake -S source\NexusVdeviceBridge -B source\NexusVdeviceBridge\obj -G "Visual Studio 17 2022" -A x64
cmake --build source\NexusVdeviceBridge\obj --config Release
```

The bridge DLL is ordinary user-mode code and does not require kernel driver signing.
Keep it beside the executable. A plain .NET solution build does not build this C++
bridge; the existing app can still run its other modes without the bridge.

## In-app toggle

In **Settings**, turn **Use Vdevice driver** on for Vdevice or off for the
original Windows input. Switching takes effect immediately, releases the old
backend and leaves the engine inactive. Enable the engine again when ready.
The selected preference is saved for the next launch, so no command-line option
is needed. If opening Vdevice fails, the current mode is retained and an error is
shown. A saved driver mode that cannot start reports the error and opens with
original input for that session so you can change the setting or retry.

Explicit legacy `--kernel-mouse` and `--interception` modes disable this toggle;
restart without those options to use it. An output failure requires restarting
the app before switching again.

## Run

Close other copies of Nexus Recoil. From administrator Command Prompt:

```cmd
source\Nexus-Recoil\bin\Release\net8.0-windows\win-x64\publish\NexusRecoil.exe --vdevice
```

Do not combine `--vdevice` with `--kernel-mouse` or `--interception`. There is no
silent fallback to another output method. Missing bridge/driver, an expired demo,
or an enumeration timeout produces a startup error. Startup waits up to five
seconds for the device to configure, while servicing host output/control events.
The bridge keeps servicing them for the device's lifetime.

On normal exit the app sends a neutral report and deletes the virtual mouse.
The vendor's SDK/device ownership also disconnects it on process termination.
If the driver disappears or rejects a report, the engine stops and shows an error;
restart the app after resolving the installation, device or license issue.

## Validation and removal

Managed backend tests use fake native APIs. Native bridge tests use a fake Vdevice
SDK, not a real Windows driver. They cannot establish Windows signing, USB/HID
binding, or input behavior. On Windows verify:

- Native bridge build and vendor installer status.
- App launch, relative movement, button release and rapid fire.
- Engine disable, app close, device removal and demo-expiry errors.
- Actual device loading under your normal Windows security settings.

Use the official Vdevice installer to uninstall Vdevice. This backend does not
require Interception or our custom Nexus Virtual Mouse driver. Installing Vdevice
does not remove those drivers or change any test-signing settings you previously enabled.
