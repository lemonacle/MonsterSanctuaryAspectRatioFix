# Monster Sanctuary Aspect Ratio Fix

A BepInEx mod for Monster Sanctuary that improves display support for 4:3 and 16:10 resolutions while preserving the game's original behavior at 16:9.

## Features

- Supports centered 4:3 gameplay presentation using a 360×270 visible world area.
- Supports centered 16:10 gameplay presentation using a 432×270 visible world area.
- Leaves standard 16:9 resolutions unchanged.
- Preserves the game's original 480×270 internal render resolution.
- Keeps menus, dialogue, combat interfaces, and other full UI screens properly framed.
- Repositions edge-anchored HUD elements for the active aspect ratio.
- Corrects mouse input mapping for the separately composited UI.
- Supports switching between compatible resolutions during the same session.

## Requirements

- Monster Sanctuary for Windows
- BepInEx 5.x

The mod was developed and tested with BepInEx 5.4.23.5 and Monster Sanctuary running under Unity 2018.4.30.

## Installation

1. Install BepInEx 5 for Monster Sanctuary.
2. Launch the game once so BepInEx creates its folders.
3. Copy `lemonacle.MonsterSanctuary.AspectRatioFix.dll` into:

   `Monster Sanctuary\BepInEx\plugins`

4. Launch the game.

The BepInEx log should contain:

```text
Loading [Aspect Ratio Fix 2.0.0]
```

## Supported aspect ratios

| Aspect ratio | Example resolution | Behavior |
|---|---:|---|
| 4:3 | 1600×1200 | Uses a centered 360×270 world crop |
| 16:10 | 1600×1000 | Uses a centered 432×270 world crop |
| 16:9 | 1920×1080 | Leaves the original game presentation unchanged |

Other aspect ratios are currently left unchanged.

## Building

The project targets .NET Framework 4.8.

By default, the project expects Monster Sanctuary at:

```text
C:\Program Files (x86)\Steam\steamapps\common\Monster Sanctuary
```

To build:

```powershell
dotnet restore ".\MonsterSanctuaryAspectRatioFix.slnx"
dotnet build ".\MonsterSanctuaryAspectRatioFix.slnx"
```

The compiled plugin is created at:

```text
bin\Debug\net48\lemonacle.MonsterSanctuary.AspectRatioFix.dll
```

When a BepInEx `plugins` folder exists in the default game directory, the build also copies the DLL there automatically.

A different installation path can be supplied through the `MonsterSanctuaryDir` MSBuild property.

## Attribution

This project began through work based on the GPL-licensed [garfieldbanks/MonsterSanctuaryMods](https://github.com/garfieldbanks/MonsterSanctuaryMods) repository. It was subsequently separated into an independent project and substantially reworked to provide a focused aspect-ratio correction without depending on the other mods in that repository.

Monster Sanctuary is developed by Moi Rai Games and published by Team17. This project is an independent community modification and is not affiliated with or endorsed by the game's developers or publisher.

## License

This project is licensed under the GNU General Public License version 3. See [LICENSE](LICENSE).
