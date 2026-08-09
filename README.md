# Monster Sanctuary Aspect Ratio Fix

A BepInEx mod for **Monster Sanctuary** that improves display support for 4:3 and 16:10 resolutions while preserving the game's original presentation at 16:9.

**Current release: v2.1.27**

**Current development version: v2.1.29**

## Features

- Supports centered 4:3 gameplay using a 360×270 visible world area.
- Supports centered 16:10 gameplay using a 432×270 visible world area.
- Leaves standard 16:9 resolutions unchanged.
- Preserves the game's original 480×270 internal render resolution.
- Keeps menus, dialogue, combat interfaces, tooltips, and other full UI screens properly framed.
- Uses separate UI presentation layers so menus retain their intended scale instead of being cropped with the game world.
- Repositions edge-anchored HUD elements for the active aspect ratio.
- Corrects mouse input mapping for the separately composited UI.
- Supports switching between compatible resolutions during the same session.
- Applies menu and dynamically generated UI corrections through lifecycle events rather than continuous menu polling.
- Refreshes camera composites, HUD layout, and shade coverage only when presentation state changes.

## Current development highlights

- Completes the 4:3 presentation pass with no remaining visual artifacts found during testing.
- Uses a pixel-preserving expanded UI canvas so menus retain their original scale and proportions.
- Extends the native menu shade across the complete frame without duplicated bands or visible seams.
- Preserves full-frame main-menu and scene-transition fades.
- Expands the Map backing, anchors its controls and arrows to the added vertical space, and preserves native scrolling behavior.
- Adds extra Map-background overscan for clean edge coverage under Proton and GameNative scaling.
- Expands the Following Monster, catalyst evolution, Switch Shift, and Select Shift backgrounds across the complete canvas.
- Keeps combat Buff Info icons aligned with health bars while rendering them above the shade and combat menus.
- Corrects Skills, Inventory, Feed, and Consumables tooltip presentation during monster switching.

## Requirements

- Monster Sanctuary for Windows
- BepInEx 5.x

The mod was developed and tested with BepInEx 5.4.23.5 and Monster Sanctuary running under Unity 2018.4.30.

## Installation

### Windows

1. Install the Windows x64 version of BepInEx 5 in the Monster Sanctuary game directory.
2. Launch Monster Sanctuary once so BepInEx creates its folders.
3. Copy `lemonacle.MonsterSanctuary.AspectRatioFix.dll` into:

   ```text
   Monster Sanctuary\BepInEx\plugins
   ```

4. Remove any older copy of the Aspect Ratio Fix DLL from the plugins folder.
5. Launch the game normally.

### Steam Deck

1. Switch the Steam Deck to Desktop Mode.
2. Open the Monster Sanctuary installation directory.
3. Install the Windows x64 version of BepInEx 5 in the game directory.
4. Copy `lemonacle.MonsterSanctuary.AspectRatioFix.dll` into:

   ```text
   Monster Sanctuary/BepInEx/plugins
   ```

5. Return to Steam and open **Monster Sanctuary → Properties → Compatibility**.
6. Enable **Force the use of a specific Steam Play compatibility tool**.
7. Select **Proton 9.0-4**.
8. Open **Monster Sanctuary → Properties → General**.
9. Enter the following under **Launch Options**:

   ```text
   WINEDLLOVERRIDES="winhttp=n,b" %command%
   ```

10. Launch Monster Sanctuary through Steam.

Proton 9.0-4 is the tested compatibility version and may need to be forced for BepInEx to load correctly.

The variable name must be spelled exactly as `WINEDLLOVERRIDES`, including both `R` characters in `OVERRIDES`.

### GameNative on Android

These instructions were tested on an Anbernic RG405M.

1. Create or open the Monster Sanctuary container in GameNative.
2. Place the Windows x64 version of BepInEx 5 in the Monster Sanctuary game root inside the container.
3. Copy `lemonacle.MonsterSanctuary.AspectRatioFix.dll` into:

   ```text
   BepInEx/plugins
   ```

4. Open the game or container settings in GameNative.
5. Set the compatibility option to **Proton 9.0-4**.
6. Add the following environment variable:

   ```text
   Name:  WINEDLLOVERRIDES
   Value: winhttp=n,b
   ```

7. Launch Monster Sanctuary through GameNative.

Proton 9.0-4 is the tested compatibility option for GameNative. Other versions may work, but this is the configuration verified on the RG405M.

GameNative should receive `WINEDLLOVERRIDES` through its environment-variable or container settings. Do not include Steam's `%command%` text.

A misspelling such as `WINEDLLOVERIDES` will prevent Wine from loading the BepInEx proxy DLL.

## Verifying the installation

After launching the game, open:

```text
BepInEx/LogOutput.log
```

A successful installation should include entries similar to:

```text
Loading [Aspect Ratio Fix 2.1.27]
Aspect Ratio Fix 2.1.27 Monster Shift background coverage patch loaded.
```

If the log file is not created or the plugin does not appear in it, check that:

- BepInEx is installed in the game root rather than a subfolder.
- The mod DLL is inside `BepInEx/plugins`.
- Only one version of the Aspect Ratio Fix DLL is installed.
- `WINEDLLOVERRIDES` is spelled correctly.
- Proton 9.0-4 is selected on Steam Deck or GameNative.
- The Windows x64 release of BepInEx 5 is being used.

## Supported aspect ratios

| Aspect ratio | Example resolution | Behavior | Testing status |
|---|---:|---|---|
| 4:3 | 1600×1200 | Uses a centered 360×270 world crop | Extensively tested |
| 16:10 | 1600×1000 | Uses a centered 432×270 world crop | Supported; broader regression testing is planned |
| 16:9 | 1920×1080 | Leaves the original presentation unchanged | Native game behavior |

Other aspect ratios are currently left unchanged.

## Known limitations

- The familiar-selection screen may briefly begin at its original position before centering as its interactive elements appear.
- 16:10 uses the same presentation system as 4:3, but has not yet received the same exhaustive regression pass.

## Roadmap

The following items are planned for future updates. They are priorities rather than guaranteed release dates.

### Performance and code cleanup

- Remove obsolete compatibility and tooltip-suppression code left behind by earlier implementations.
- Prune destroyed Unity objects from cached layer, camera, and position collections after scene changes.
- Centralize remaining UI teardown and repeated lifecycle handling.
- Consider splitting the large plugin source into smaller partial-class files without changing runtime behavior.

### Testing and compatibility

- Complete a dedicated 16:10 regression pass.
- Continue testing less common menus, alternate game modes, and scene transitions.
- Investigate additional handheld and compatibility-layer configurations as hardware is available.

### Future presentation work

- Explore optional presentation enhancements that make deliberate use of the additional vertical space.
- Address additional aspect ratios only where they can be supported without compromising the existing 4:3, 16:10, and 16:9 behavior.

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

For a release build, select the **Release** configuration in Visual Studio. The resulting DLL is created under:

```text
bin\Release\net48\lemonacle.MonsterSanctuary.AspectRatioFix.dll
```

When a BepInEx `plugins` folder exists in the default game directory, the build also copies the DLL there automatically.

A different installation path can be supplied through the `MonsterSanctuaryDir` MSBuild property.

## Attribution

This project began through work based on the GPL-licensed [garfieldbanks/MonsterSanctuaryMods](https://github.com/garfieldbanks/MonsterSanctuaryMods) repository. It was subsequently separated into an independent project and substantially reworked to provide a focused aspect-ratio correction without depending on the other mods in that repository.

Monster Sanctuary is developed by Moi Rai Games and published by Team17. This project is an independent community modification and is not affiliated with or endorsed by the game's developers or publisher.

## License

This project is licensed under the GNU General Public License version 3. See [LICENSE](LICENSE).
