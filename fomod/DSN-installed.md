# DSN installation file

If this file exists in `<Your Skyrim Installation Directory>\Data\SKSE\DSN\` folder, the DSN has been properly installed by your MOD manager.

SKSE is not a DSN dependency, the file put here only to be compatible with MO2, avoiding it marking the DSN package `No valid game data`.

## Dependencies

[xSHADOWMANx's Dll Loader](https://www.nexusmods.com/skyrimspecialedition/mods/3619) is the unique dependency of DSN.

You must manually install the loader to your game root folder. Installation through any MOD manager will make the DSN not works.

If you install through the MOD manager, the loader will be installed to `<Your Skyrim Installation Directory>\Data\` instead of `<Your Skyrim Installation Directory>\`.

You should not place the loader in `Data`, but instead should be placed in the parent of `Data`, side by side with `Data`.

## Troubleshooting & FAQ

See [the description page on NexusMod](https://www.nexusmods.com/skyrimspecialedition/mods/16514?tab=description).
