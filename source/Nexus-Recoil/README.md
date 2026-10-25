# Nexus Recoil

A lightweight mouse recoil compensation tool for Rainbow Six Siege. It moves your mouse downward (and optionally sideways) at a configurable rate while you hold your aim key, offsetting weapon recoil. All mouse movement is performed through the standard Windows `mouse_event` API — no drivers, no injection.

---

## Quick Start

1. **Launch** `Nexus-Recoil.exe`
2. Press **Insert** to open the UI
3. Pick the **Attackers** or **Defenders** tab in the Weapon Presets section
4. Select your weapon from the dropdown and click **Load**
5. In-game, hold your aim key (default: right mouse button) while firing — recoil compensation runs automatically
6. Press **F1** to enable compensation, **F2** to disable it
7. Press **Insert** again (or click ✕) to hide the UI — the tool keeps running in the background

---

## Controls

| Control | Default | Description |
|---------|---------|-------------|
| Toggle UI | Insert | Show / hide the overlay window |
| Enable | F1 | Enable recoil compensation |
| Disable | F2 | Disable recoil compensation |
| Aim Key | Right Mouse Button | Key that must be held for compensation to fire |

---

## Rebinding Hotkeys

1. Open the UI (Insert)
2. In the **Hotkeys** section, click the pill button next to the hotkey you want to change (e.g. "Insert")
3. The button shows **"Press any key..."** — press the new key
4. Press **Escape** at any point to cancel
5. The new binding is saved automatically to `settings.json`

---

## Sliders

| Slider | Direction | Default |
|--------|-----------|---------|
| Down | Pulls mouse down | 12 |
| Up | Pulls mouse up (counter-pull) | 0 |
| Right | Pulls mouse right | 4 |
| Left | Pulls mouse left | 0 |

Each step is one pixel per tick interval. The **Delay** slider (0–100 ms) controls how often the movement fires — lower delay = more frequent corrections.

---

## Weapon Presets

Built-in presets for all Attackers and Defenders weapons are pre-loaded with tuned values. They are read-only.

### Loading a preset
1. Pick the **Attackers** or **Defenders** tab
2. Select a weapon from the dropdown
3. Click **Load** — the sliders update immediately

### Creating a custom preset
1. Adjust the sliders to your liking
2. Click **New** in the Weapon Presets section
3. Enter a name and click **OK**
4. The preset appears in the **Custom** tab and is saved to `profiles.json`

### Saving changes to a custom preset
1. Select your custom preset in the **Custom** tab
2. Adjust the sliders
3. Click **Save** — values overwrite the stored preset

### Deleting a custom preset
1. Select the preset in the **Custom** tab
2. Click **Delete**

Built-in presets (Attackers / Defenders) cannot be saved over or deleted.

---

## Build

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download) and Windows.

```
dotnet build -c Release
```

To produce a single self-contained executable:

```
dotnet publish -c Release
```

The output is in `bin\Release\net8.0-windows\win-x64\publish\`.

---

## Disclaimer

Nexus Recoil simulates mouse input to assist with recoil control. **You are solely responsible for ensuring your use complies with the terms of service of any game.** Use at your own risk.
