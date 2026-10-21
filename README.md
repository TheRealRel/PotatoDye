# 🎨 Potato Dye

**Dye your cape at the barber.** Sit in the barber's chair and a **Cape color** panel opens beside the hair settings, with three sliders. Your cape changes color as you move them.

- **Hue** picks the color from a rainbow strip.
- **Saturation** is how strong the dye is: at 0 the cape keeps its own colors.
- **Brightness** makes it lighter or darker.


- **Apply** keeps the color, **Cancel** puts it back, and **No dye** returns the cape to its own colors.
- **Free**, like changing a hair style.
- Colors are **saved with your character**, one per kind of cape: dye your Linen cape red and your Wolf cape blue, and each shows its own color whenever you wear it.
- It dyes the **cape you see**. With **Vanity Outfit** showing a different cape, that's the one you dye, even if you aren't wearing one underneath.
- **Other players with Potato Dye see your colors.** Players without it see the cape's normal colors; nothing breaks either way.

The color tints the cape's own texture, so light capes (Linen) take it best and dark, furry ones (Wolf, Lox) only shift a little. Capes only for now.

Part of the **PotatoServer** pack for the Potato Server, alongside [Potato Tweaks](https://github.com/TheRealRel/PotatoTweaks). Bug reports: this repository's Issues.

## Settings

In `BepInEx/config/potato.dye.cfg`, or in game with Configuration Manager. Both are personal.

| Setting | Default | What it does |
|---|---|---|
| Show cape colors | On | Show dyed capes in their colors, yours and other players'. |
| Barber panel | On | Show the Cape color panel at the barber. |

## Requirements

- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)

## Research notes

[docs/armor-dye-test.md](docs/armor-dye-test.md): how Valheim and mod armor are built (painted-on chest and leg textures, one material per vanilla piece, the game's shader not protecting metal) and what an in-game dye test showed. It covers what dyeing armor would take, for anyone recoloring Valheim gear.

## Building

- Source: `src/`. Thunderstore files: `package/`.
- References come from Valheim's `valheim_Data/Managed` folder and a BepInEx profile (BepInEx and Harmony from `BepInEx/core`).
- `build.sh` compiles with the .NET SDK's Roslyn compiler into `out/PotatoDye.dll`. Point it at your files with `VALHEIM_MANAGED`, `BEPINEX` and, optionally, `CSC`:

```
VALHEIM_MANAGED=".../Valheim/valheim_Data/Managed" BEPINEX=".../profiles/MyProfile/BepInEx" ./build.sh
```

- To package, zip `package/manifest.json`, `README.md`, `CHANGELOG.md`, `LICENSE` and `icon.png` at the root, with the DLL at `plugins/PotatoDye.dll`.
- `tools/ArmorTest.cs` is a test-only console command (`dyetest`). Copy it into `src/` for a test build; never ship it.
