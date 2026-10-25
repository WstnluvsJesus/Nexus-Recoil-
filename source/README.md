# Nexus Recoil

A lightweight WPF recoil-control assistant for Windows. Simulates micro mouse movements to counter weapon recoil patterns in tactical shooters.

## Features

- **Per-weapon profiles** — attacker, defender, and fully custom presets
- **Sensitivity multiplier** — scales all recoil values to match your in-game sensitivity
- **Recoil curve** — smooth ramp-up using an exponential model so the first shots aren't over-corrected
- **Rapid fire** — auto-clicks at a configurable rate (useful for semi-auto weapons)
- **Quick-swap keybinds** — cycle through presets mid-game without opening the UI
- **Preset import / export** — share or back up individual profiles as JSON files
- **Overlay watermark** — on-screen status indicator excluded from screen capture
- **Stream proof** — hides the main window from OBS and recording software
- **Live theme editor** — 5 color pickers for full UI customization
- **Auto-update checker** — polls GitHub releases and shows a changelog popup on first new version

## Requirements

- Windows 10 / 11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## Usage

1. Download `NexusRecoil.exe` from [Releases](https://github.com/n1k0fr/Nexus-Recoil/releases/latest)
2. Run the executable — no installation needed
3. Accept the disclaimer on first launch
4. Select a weapon preset and press your Enable hotkey (default F1) to activate

## Default Hotkeys

| Action | Key |
|---|---|
| Toggle UI | Insert |
| Enable recoil | F1 |
| Disable recoil | F2 |
| Next preset | Page Down |
| Previous preset | Page Up |

All hotkeys are rebindable from the Settings tab.

## Disclaimer

You are solely responsible for ensuring your use of this software complies with the terms of service of any game. The authors accept no liability for any consequences of use.

## License

MIT — see [LICENSE](LICENSE)

## Optional kernel mouse driver

See [Nexus Virtual Mouse](NexusMouseDriver/README.md) for the Windows WDK driver,
build/signing steps, and the `--kernel-mouse` launch option. Windows driver validation
is still required before using this backend.

For the optional third-party Interception backend, see [Interception setup](INTERCEPTION.md)
and launch with `--interception` after installing its driver and x64 library.
