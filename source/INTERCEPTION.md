# Optional Interception mouse output (Windows x64)

Launch NexusRecoil with `--interception` to use the third-party
[Interception](https://github.com/oblitum/Interception) driver/library API for mouse
movement and left-button transitions. The app does not set interception filters or
receive/block physical keyboard or mouse input. Hotkeys and held-key detection
continue to use Windows APIs. The existing modes remain available: no option for
Windows input APIs, or `--kernel-mouse` for Nexus Virtual Mouse. Choose one mode.

## Install the official package

1. Get the release ZIP from the project's [official releases](https://github.com/oblitum/Interception/releases).
2. Extract it. In Command Prompt **as administrator**, change to its
   `command line installer` folder and run:

   ```cmd
   install-interception.exe /install
   ```

3. Restart Windows. Installing the package alone does not prove that Windows loaded
   its drivers. Check Device Manager and Code Integrity logs if the app cannot connect.
4. Copy `library\x64\interception.dll` from that release next to the published
   `NexusRecoil.exe`. Keep the project's applicable license/notices with its files.
   The x86 DLL will not work with this x64 application.

This repository does not ship Interception binaries, sign them, or alter Windows
security settings. The upstream README documents testing from Windows XP through
Windows 10. Signature acceptance and Windows 11 compatibility must be checked on
your machine; no claim that the package will run with test-signing disabled is made.
Use the official release rather than repackaged DLLs or drivers.

## Build and launch

From this repository's root:

```cmd
dotnet publish source\Nexus-Recoil\Nexus-Recoil.csproj -c Release
```

Copy the DLL into `source\Nexus-Recoil\bin\Release\net8.0-windows\win-x64\publish`,
then launch from administrator Command Prompt:

```cmd
source\Nexus-Recoil\bin\Release\net8.0-windows\win-x64\publish\NexusRecoil.exe --interception
```

Do not pass `--kernel-mouse` at the same time. The app exits with an error if the
library or driver is missing or no mouse is found; it does not fall back to another
backend. It selects the first connected mouse reported by Interception (IDs 11–20).
With multiple mice, verify input reaches the intended device. Reconnects require an
app restart. Close other Interception clients before starting the app.

Input failure stops the engine and displays an error. Normal shutdown attempts to
release the left button before destroying the library context. Process termination
cannot guarantee release; unlike our VHF driver, the library has no app-specific
file cleanup protocol. Test movement, enable/disable, clicks, shutdown and device
unplug/reconnect on Windows before relying on this mode.

Interception uses different licenses for noncommercial and commercial use. Review
its [license terms](https://github.com/oblitum/Interception#license) before redistribution.

## Remove Interception

Close all clients. From its official installer folder in administrator Command Prompt:

```cmd
install-interception.exe /uninstall
```

Restart Windows afterward. This removes Interception; it does not change the
separate Nexus Virtual Mouse installation or your test-signing setting.

## Validation

Managed transport/lifecycle and native struct layout tests:

```cmd
dotnet run --project tests\MouseOutput.Tests\MouseOutput.Tests.csproj -c Release
```

Tests use a fake Interception API and do not load the real DLL/driver. Native Windows
installation, signing acceptance and physical mouse behavior need Windows testing.
