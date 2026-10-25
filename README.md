# RCM Range Display

A small quality-of-life mod for Rogue Command: shows every range a unit has, on the units you hover or select.

The game draws one range circle per unit, the one its prefab picks, and only while the unit is selected. A turret's reach, a support unit's skill range or an artillery shell's splash usually go unseen, and enemies show nothing. With Range Display on, hovered and selected units get one tinted circle per range:

- **red**: weapon range
- **blue**: skill range (skills with a limited range)
- **orange**: effect radii (splash, auras, skill areas)
- **grey**: sight range (off by default)

Hovered enemy units and buildings show their ranges too (can be switched off).

## Controls

- **F8** shows or hides all ranges. Change it, also to a combination like `F8 + LeftControl`, in `BepInEx\config\RCM.plugins.rangedisplay.cfg` under `Hotkeys.ToggleRanges`.
- The RCM panel (**F6**) has a Range Display widget with a checkbox per range type.

## Install

Needs BepInEx 5 and RCM GUI (`RCM_GUI.dll` + `RCM_GUI_assets` from [RCM-Manager](https://github.com/RCM-development/RCM-Manager)). Put `RCM_RangeDisplay.dll` into `BepInEx\plugins`.

## Build

Clone next to RCM-Manager and run:

```bash
dotnet build RCM-RangeDisplay/RCM_RangeDisplay.csproj -c Release
```
