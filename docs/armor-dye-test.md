# PotatoDye: armor dye test (2026-10-20)

**Status:** research done, armor dyeing not built. PotatoDye 1.0.0 shipped with capes only, and capes are tested in game, including with Vanity Outfit and dark capes.

## How the test worked
Two test builds of PotatoDye 1.0.0 added a console command, `dyetest`, which tints your own worn pieces locally. Only you see the color, and it's gone when you log out. The source is in [`tools/ArmorTest.cs`](../tools/ArmorTest.cs); it is not part of the release build.

- `dyetest list` shows each worn piece's renderers, their materials and shader, and how each material is sorted (DYE / METAL / GLOW).
- `dyetest <cape|chest|legs|helmet|shield|all> <RRGGBB|off>` tints those pieces.
- **Test 1** tinted whole pieces through the game's material manager (`MaterialMan`, `_Color`).
- **Test 2** tinted one material at a time with per-material property blocks, copying the renderer's own block first so the style and snow settings stay.

## How armor is built
- **Vanilla chest and legs paint part of their look onto the body.** `ItemData.m_shared.m_armorMaterial` sets `_ChestTex` / `_LegsTex` on the body material. Those painted parts can't be dyed without tinting the skin too; it would need recolored texture copies per player, which isn't worth it. Only the separate 3D parts (`attach_skin` / `attach` children) can be dyed.
- **Vanilla armor uses one material per piece.**
  - Bronze chest and legs share `BronzeArmorMesh_Mat` (Custom/Creature). Its leather straps and skirt and its bronze bracers and shin guards always tint together.
  - Bronze helmet: `helmet_bronze_mat`.
  - Vanilla Wolf chest and legs: `SilverArmourChest_mat`, fur and silver in one material, and also painted onto the body.
- **Valheim's own shader (Custom/Creature) does NOT protect metal.** It has a `_MetalColor` property, but tinting `_Color` still darkened the Bronze bracers, shin guards and helmet in game.
- **Mod armor can be split by material.** Therzie Armory's Wolf set ("Vidar", Standard shader) has several materials per renderer:
  - Chest: `VidarBMChest_mat` (main body; fur and plates in one), `BlobtarGreen` (glow) and `DarkLeather_mat`.
  - Legs: `VidarBMChest_mat`.
  - Helmet: `blackmetal_rough`, `bronzedark_mat` and `blackmetal`.
  - On the Standard shader a `_Color` tint also tints the metal's shine.
- **Capes:** Custom/Creature. The Wolf cape has `WolfCape` (fur) and `WolfCapeChain`. A whole-cape tint looked fine in game.
- **Colors multiply the texture.** Strong colors on brown leather come out very dark (blue on Bronze looked nearly black), so lighter, less saturated picks read better.

## Sorting rules if armor dyeing gets built
- **GLOW:** emission on with a bright `_EmissionColor`, or a name containing glow / blob / emiss / rune / crystal / flame / fire / magic. Never dyed.
- **METAL:** a name containing a metal word (metal, iron, bronze, silver, steel, gold, copper, chain, mail, flametal, rivet, buckle, brass) **and no garment word** (armor, armour, chest, legs, cape, tunic, robe, shirt, pants, skirt). Never dyed. For example `helmet_bronze_mat`, `blackmetal`, `bronzedark`.
  - Correction from test 2: don't exempt Custom/Creature from this rule.
- **DYE:** everything else, including set-wide materials like `BronzeArmorMesh_Mat`, `SilverArmourChest_mat` and `VidarBMChest_mat`. These tint all or nothing.

## Plan if armor dyeing gets built (not scheduled)
- **Barber panel:** a slot picker (Cape, Helmet, Chest, Legs) above the same hue, saturation and brightness sliders.
- **Saved colors:** keep them per character, keyed by the drawn item prefab name (generic already).
- **Sharing:** one shared color plus "item it's for" pair per slot. Chest, legs and helmet use VisEquipment `m_chestItemInstances`, `m_legItemInstances` and `m_helmetItemInstance`, with ZDO `s_chestItem`, `s_legItem` and `s_helmetItem`. Vanity Outfit swaps these too, so the dye follows the look.
- **Armor slots:** tint per material with the rules above. **Capes:** keep the tested whole-cape tint.
- **What to expect:**
  - Metal helmets untouched; leather and cloth helmets dye.
  - Chest and legs 3D parts dye as a whole where leather and metal share a material; the painted parts never change.
  - Mod armor with split materials dyes cleanly.
