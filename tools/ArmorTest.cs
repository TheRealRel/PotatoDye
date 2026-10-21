// Potato Dye - armor dye test 2 (test build only): tints your own worn pieces from the console, material by material:
// cloth, leather and fur take the color; metal (on mods' Standard-shader armor) and glowing parts are left alone.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace PotatoDye
{
    internal static class ArmorTest
    {
        private static readonly Dictionary<string, Color> Tests = new Dictionary<string, Color>();
        private static readonly string[] Slots = { "cape", "chest", "legs", "helmet", "shield" };
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly HashSet<GameObject> Tinted = new HashSet<GameObject>();
        private static float next;

        private static readonly AccessTools.FieldRef<VisEquipment, List<GameObject>> Chest = AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_chestItemInstances");
        private static readonly AccessTools.FieldRef<VisEquipment, List<GameObject>> Legs = AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_legItemInstances");
        private static readonly AccessTools.FieldRef<VisEquipment, List<GameObject>> Cape = AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_shoulderItemInstances");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> Helmet = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_helmetItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> Shield = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftItemInstance");
        private static readonly AccessTools.FieldRef<Humanoid, VisEquipment> Vis = AccessTools.FieldRefAccess<Humanoid, VisEquipment>("m_visEquipment");

        private static readonly string[] MetalWords = { "metal", "iron", "bronze", "silver", "steel", "gold", "copper", "chain", "mail", "flametal", "rivet", "buckle", "brass", "tin_", "_tin" };
        private static readonly string[] GlowWords = { "glow", "blob", "emiss", "rune", "crystal", "flame", "fire", "magic" };
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // "dye", "metal" or "glow". Valheim's own armor shader keeps metal apart by itself, so only mods' armor is sorted by name.
        internal static string Classify(Material material)
        {
            if (!material || !material.HasProperty(ColorId)) return "skip";
            string name = material.name.ToLowerInvariant();
            bool glowing = material.IsKeywordEnabled("_EMISSION") && material.HasProperty(EmissionId) && material.GetColor(EmissionId).maxColorComponent > 0.05f;
            if (glowing || GlowWords.Any(name.Contains)) return "glow";
            bool valheimShader = material.shader && material.shader.name == "Custom/Creature";
            if (!valheimShader && MetalWords.Any(name.Contains)) return "metal";
            return "dye";
        }

        private static IEnumerable<GameObject> Pieces(VisEquipment vis, string slot)
        {
            switch (slot)
            {
                case "cape": return Cape(vis) ?? new List<GameObject>();
                case "chest": return Chest(vis) ?? new List<GameObject>();
                case "legs": return Legs(vis) ?? new List<GameObject>();
                case "helmet": return Helmet(vis) ? new[] { Helmet(vis) } : new GameObject[0];
                case "shield": return Shield(vis) ? new[] { Shield(vis) } : new GameObject[0];
            }
            return new GameObject[0];
        }

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        internal static class Command
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("dyetest",
                    "[cape|chest|legs|helmet|shield|all] [RRGGBB|off] - test a dye on your own worn pieces; 'dyetest list' shows their materials",
                    args => Run(args));
            }
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            var player = Player.m_localPlayer;
            var vis = player ? Vis(player) : null;
            if (!vis) { args.Context.AddString("Load into a world first."); return; }
            if (args.Length < 2 || args[1] == "list")
            {
                foreach (var slot in Slots)
                    foreach (var piece in Pieces(vis, slot))
                        foreach (var renderer in piece.GetComponentsInChildren<Renderer>(true))
                        {
                            if (renderer is ParticleSystemRenderer) continue;
                            var text = new StringBuilder($"{slot}: {renderer.name} ->");
                            foreach (var material in renderer.sharedMaterials.Where(m => m))
                                text.Append($" {material.name} [{material.shader?.name}{(material.HasProperty("_MetalColor") ? ", metal color" : "")}] = {Classify(material).ToUpperInvariant()};");
                            args.Context.AddString(text.ToString());
                        }
                args.Context.AddString("Usage: dyetest chest FF2020   |   dyetest all off");
                return;
            }
            string which = args[1].ToLowerInvariant();
            var targets = which == "all" ? Slots : Slots.Where(slot => slot == which).ToArray();
            if (targets.Length == 0 || args.Length < 3) { args.Context.AddString("Usage: dyetest chest FF2020   |   dyetest all off"); return; }
            if (args[2].ToLowerInvariant() == "off")
            {
                foreach (var slot in targets) Tests.Remove(slot);
                foreach (var target in Tinted) Clear(target);
                Tinted.Clear();
                next = 0f;
                args.Context.AddString("Test dye off for " + which + ".");
                return;
            }
            if (!Plugin.TryParse(args[2].TrimStart('#').ToUpperInvariant(), out var color))
            {
                args.Context.AddString("Give a color as RRGGBB, like FF2020 (red) or 2060FF (blue).");
                return;
            }
            foreach (var slot in targets) Tests[slot] = color;
            next = 0f;
            args.Context.AddString($"Test dye #{args[2].TrimStart('#').ToUpperInvariant()} on {which}. Only you see it, and it's gone when you log out.");
        }

        // Kept on (the game's snow and wet effects can refresh the same settings), and follows a piece you swap.
        [HarmonyPatch(typeof(VisEquipment), "UpdateVisuals")]
        internal static class Keep
        {
            private static void Postfix(VisEquipment __instance)
            {
                if (Tests.Count == 0 || Time.time < next || !Player.m_localPlayer || Vis(Player.m_localPlayer) != __instance || !MaterialMan.instance) return;
                next = Time.time + 1f;
                foreach (var pair in Tests)
                    foreach (var piece in Pieces(__instance, pair.Key))
                    {
                        if (!piece) continue;
                        Tint(piece, pair.Value);
                        Tinted.Add(piece);
                    }
            }
        }

        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        // Material by material: each dyed material gets the color times its own; metal and glow keep theirs.
        private static void Tint(GameObject piece, Color dye)
        {
            foreach (var renderer in piece.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) continue;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (Classify(material) != "dye") continue;
                    // Start from the piece's own settings (style, snow) so the material keeps them.
                    renderer.GetPropertyBlock(Block);
                    Block.SetColor(ColorId, material.GetColor(ColorId) * dye);
                    renderer.SetPropertyBlock(Block, i);
                }
            }
        }

        private static void Clear(GameObject piece)
        {
            if (!piece) return;
            foreach (var renderer in piece.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) continue;
                for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                {
                    try { renderer.SetPropertyBlock(null, i); }
                    catch (Exception)
                    {
                        renderer.GetPropertyBlock(Block);
                        renderer.SetPropertyBlock(Block, i);
                    }
                }
            }
        }
    }
}
