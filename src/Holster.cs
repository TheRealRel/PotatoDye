// Potato Dye - moving the weapons, tools and shield on your back (and Better Archery's quiver) with the keyboard.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PotatoDye
{
    // Hold Right Ctrl (Holster adjust key) with the arrow keys and Page Up / Page Down to move what's on your back: up and
    // down your back, left and right, away from and closer to it. Add Right Shift to tilt it instead. Home picks the next
    // thing on your back (two weapons, a weapon and a shield, the quiver), End puts the picked one back where the game
    // puts it (Tab would open the inventory). One press moves it 1 cm or tilts it 3 degrees; holding a key repeats.
    //
    // Each kind of item keeps its own place (every Iron Sword sits where you put yours), saved with the character like the
    // cape colors and shared through the character, so other players with Potato Dye see your weapons where you put them.
    // The quiver is Better Archery's: its place is written into Better Archery's own quiver settings, on your game only.
    //
    // A place is kept in the item's own hanging point's terms (the game hangs everything on the back from points on the
    // spine), so it stays put while the body moves.
    internal static class Holster
    {
        internal const string SaveKey = "PotatoDye.Holster";
        private static readonly int ZdoKey = "PotatoDye.Holster".GetStableHashCode();
        private const float Step = 0.01f, TiltStep = 3f, RepeatDelay = 0.35f, RepeatEvery = 0.08f;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> AdjustKey;

        private struct Place
        {
            internal Vector3 Move;
            internal Quaternion Turn;
        }

        // ---------------------------------------------------------------- places, saved and shared as "SwordIron=x,y,z,rx,ry,rz;..."
        // Places are drawn every frame, so each saved text is read once and kept.
        private static readonly Dictionary<string, Dictionary<string, Place>> Parsed = new Dictionary<string, Dictionary<string, Place>>();

        private static Dictionary<string, Place> Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return new Dictionary<string, Place>();
            if (Parsed.TryGetValue(text, out var known)) return new Dictionary<string, Place>(known);
            if (Parsed.Count > 32) Parsed.Clear();
            var places = new Dictionary<string, Place>();
            foreach (var entry in text.Split(';'))
            {
                int split = entry.IndexOf('=');
                if (split <= 0) continue;
                var parts = entry.Substring(split + 1).Split(',');
                if (parts.Length != 6) continue;
                var n = new float[6];
                bool ok = true;
                for (int i = 0; i < 6 && ok; i++) ok = float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]);
                if (ok) places[entry.Substring(0, split)] = new Place { Move = new Vector3(n[0], n[1], n[2]), Turn = Quaternion.Euler(n[3], n[4], n[5]) };
            }
            Parsed[text] = places;
            return new Dictionary<string, Place>(places);
        }

        private static string Write(Dictionary<string, Place> places) => string.Join(";", places.Select(pair =>
        {
            var m = pair.Value.Move; var r = pair.Value.Turn.eulerAngles;
            return pair.Key + "=" + string.Join(",", new[] { m.x, m.y, m.z, r.x, r.y, r.z }.Select(f => f.ToString("R", CultureInfo.InvariantCulture)));
        }));

        private static Dictionary<string, Place> Mine(Player player) =>
            Parse(player != null && player.m_customData.TryGetValue(SaveKey, out var text) ? text : null);

        private static void SaveMine(Player player, Dictionary<string, Place> places)
        {
            if (places.Count == 0) player.m_customData.Remove(SaveKey);
            else player.m_customData[SaveKey] = Write(places);
            Publish(player);
        }

        private static readonly AccessTools.FieldRef<Character, ZNetView> CharacterView = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        // Your places go into your character's shared data, so the others' games can draw them.
        private static void Publish(Player player)
        {
            var zdo = player ? CharacterView(player)?.GetZDO() : null;
            if (zdo == null || !zdo.IsOwner()) return;
            string text = player.m_customData.TryGetValue(SaveKey, out var saved) ? saved : "";
            if (zdo.GetString(ZdoKey) != text) zdo.Set(ZdoKey, text);
        }

        // The places to draw for a wearer: yours from your character, another player's from their shared data.
        private static Dictionary<string, Place> PlacesFor(VisEquipment vis)
        {
            var player = vis.GetComponent<Player>();
            if (!player) return null;
            if (player == Player.m_localPlayer) return Mine(player);
            var zdo = CharacterView(player)?.GetZDO();
            return zdo == null ? null : Parse(zdo.GetString(ZdoKey));
        }

        // ---------------------------------------------------------------- drawing
        // Where the game put each item on the back, before any of ours, so a place can be changed again and again.
        private static readonly Dictionary<GameObject, Place> GamePlace = new Dictionary<GameObject, Place>();
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> LeftInstance = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftBackItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> RightInstance = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightBackItemInstance");
        // The game keeps which items are on the back as their prefab hashes.
        private static readonly AccessTools.FieldRef<VisEquipment, int> LeftHash = AccessTools.FieldRefAccess<VisEquipment, int>("m_leftBackItem");
        private static readonly AccessTools.FieldRef<VisEquipment, int> RightHash = AccessTools.FieldRefAccess<VisEquipment, int>("m_rightBackItem");

        private static string PrefabName(int hash)
        {
            var prefab = hash != 0 && ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(hash) : null;
            return prefab ? prefab.name : null;
        }

        private static void Apply(GameObject item, string name, Dictionary<string, Place> places)
        {
            if (!item || string.IsNullOrEmpty(name)) return;
            if (!GamePlace.TryGetValue(item, out var game))
            {
                if (GamePlace.Count > 64) foreach (var gone in GamePlace.Keys.Where(k => !k).ToList()) GamePlace.Remove(gone);
                game = new Place { Move = item.transform.localPosition, Turn = item.transform.localRotation };
                GamePlace[item] = game;
            }
            var place = Enabled.Value && places != null && places.TryGetValue(name, out var p) ? p : new Place { Turn = Quaternion.identity };
            item.transform.localPosition = game.Move + place.Move;
            item.transform.localRotation = place.Turn * game.Turn;
        }

        private static void ApplyAll(VisEquipment vis)
        {
            var places = PlacesFor(vis);
            Apply(LeftInstance(vis), PrefabName(LeftHash(vis)), places);
            Apply(RightInstance(vis), PrefabName(RightHash(vis)), places);
        }

        [HarmonyPatch(typeof(VisEquipment), "SetBackEquipped")]
        internal static class Attached
        {
            private static void Postfix(VisEquipment __instance) => ApplyAll(__instance);
        }

        // Another player's places can change while their items stay on their back: check now and then.
        private static readonly Dictionary<ZDOID, string> Seen = new Dictionary<ZDOID, string>();
        private static float nextCheck;

        private static void CheckOthers()
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 1f;
            foreach (var player in Player.GetAllPlayers())
            {
                if (!player || player == Player.m_localPlayer) continue;
                var zdo = CharacterView(player)?.GetZDO();
                if (zdo == null) continue;
                string text = zdo.GetString(ZdoKey);
                if (Seen.TryGetValue(zdo.m_uid, out var was) && was == text) continue;
                Seen[zdo.m_uid] = text;
                var vis = player.GetComponent<VisEquipment>();
                if (vis) ApplyAll(vis);
            }
        }

        // ---------------------------------------------------------------- the keys
        private static readonly KeyCode[] Keys = { KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.PageUp, KeyCode.PageDown, KeyCode.Home, KeyCode.End };
        private static KeyCode held = KeyCode.None;
        private static float nextRepeat;
        private static int picked;
        private static bool published, broken;

        internal static void Update()
        {
            try
            {
                var player = Player.m_localPlayer;
                if (broken || !player) { published = false; return; }
                if (!published) { published = true; Publish(player); }
                CheckOthers();
                if (!Enabled.Value || !Input.GetKey(AdjustKey.Value) || (Chat.instance && Chat.instance.HasFocus()) || Console.IsVisible() ||
                    InventoryGui.IsVisible() || Menu.IsVisible()) { held = KeyCode.None; return; }
                KeyCode pressed = KeyCode.None;
                foreach (var key in Keys)
                    if (Input.GetKeyDown(key)) { pressed = key; held = key; nextRepeat = Time.time + RepeatDelay; break; }
                if (pressed == KeyCode.None && held != KeyCode.None && held != KeyCode.Home && held != KeyCode.End)
                {
                    if (!Input.GetKey(held)) { held = KeyCode.None; return; }
                    if (Time.time < nextRepeat) return;
                    nextRepeat = Time.time + RepeatEvery;
                    pressed = held;
                }
                if (pressed != KeyCode.None) Press(player, pressed, Input.GetKey(KeyCode.RightShift));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Holster adjusting is off until the game restarts: {e.GetBaseException().Message}");
                broken = true;
            }
        }

        // What's on your back right now: the right-hand item, the left-hand item, and Better Archery's quiver.
        private sealed class Target
        {
            internal string Name, Shown;
            internal GameObject Item;
            internal bool Quiver;
        }

        private static List<Target> Targets(Player player)
        {
            var list = new List<Target>();
            var vis = player.GetComponent<VisEquipment>();
            if (vis)
            {
                foreach (var (item, name) in new[] { (RightInstance(vis), PrefabName(RightHash(vis))), (LeftInstance(vis), PrefabName(LeftHash(vis))) })
                    if (item && !string.IsNullOrEmpty(name)) list.Add(new Target { Name = name, Shown = Shown(name), Item = item });
            }
            var quiver = Quiver(player);
            if (quiver && QuiverSettings()) list.Add(new Target { Name = "quiver", Shown = "Quiver", Item = quiver, Quiver = true });
            return list;
        }

        private static string Shown(string prefab)
        {
            var drop = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            return drop ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : prefab;
        }

        private static void Press(Player player, KeyCode key, bool tilt)
        {
            var targets = Targets(player);
            if (targets.Count == 0) { player.Message(MessageHud.MessageType.TopLeft, "Nothing on your back to move"); return; }
            if (key == KeyCode.Home) picked++;
            var target = targets[((picked % targets.Count) + targets.Count) % targets.Count];
            if (key == KeyCode.Home) { player.Message(MessageHud.MessageType.TopLeft, $"Moving: {target.Shown}"); return; }

            var joint = target.Item.transform.parent;
            if (!joint) return;
            if (key == KeyCode.End)
            {
                if (target.Quiver) SetQuiver(QuiverDefault(true), QuiverDefault(false), target.Item);
                else { var mine = Mine(player); mine.Remove(target.Name); SaveMine(player, mine); ApplyAll(player.GetComponent<VisEquipment>()); }
                player.Message(MessageHud.MessageType.TopLeft, $"{target.Shown}: back where the game puts it");
                return;
            }

            var body = player.transform;
            Vector3 dir;
            switch (key)
            {
                case KeyCode.UpArrow: dir = body.up; break;
                case KeyCode.DownArrow: dir = -body.up; break;
                case KeyCode.LeftArrow: dir = -body.right; break;
                case KeyCode.RightArrow: dir = body.right; break;
                case KeyCode.PageUp: dir = -body.forward; break;
                default: dir = body.forward; break;
            }
            // Tilting turns around the axis across the movement: up and down pitch, left and right turn, page keys roll.
            Vector3 axis = key == KeyCode.UpArrow || key == KeyCode.DownArrow ? Vector3.Cross(dir, body.forward)
                : key == KeyCode.LeftArrow || key == KeyCode.RightArrow ? body.up * (key == KeyCode.LeftArrow ? 1f : -1f)
                : body.forward * (key == KeyCode.PageUp ? 1f : -1f);
            Vector3 move = tilt ? Vector3.zero : joint.InverseTransformVector(dir * Step);
            // The same turn, in the hanging point's terms.
            Quaternion turn = tilt ? Quaternion.Inverse(joint.rotation) * Quaternion.AngleAxis(TiltStep, axis.normalized) * joint.rotation : Quaternion.identity;

            if (target.Quiver)
            {
                SetQuiver(quiverPosition.Value + move, (turn * Quaternion.Euler(quiverRotation.Value)).eulerAngles, target.Item);
                player.Message(MessageHud.MessageType.TopLeft, $"Quiver {(tilt ? "tilted" : "moved")}");
                return;
            }
            var places = Mine(player);
            var place = places.TryGetValue(target.Name, out var was) ? was : new Place { Turn = Quaternion.identity };
            place.Move += move;
            place.Turn = turn * place.Turn;
            places[target.Name] = place;
            SaveMine(player, places);
            ApplyAll(player.GetComponent<VisEquipment>());
            player.Message(MessageHud.MessageType.TopLeft, $"{target.Shown} {(tilt ? "tilted" : "moved")}");
        }

        // ---------------------------------------------------------------- Better Archery's quiver
        // Better Archery hangs its quiver on the bow's hanging point and places it from its own settings when it's put on;
        // the same settings are changed here, and the quiver on your back is moved at once.
        private static ConfigEntry<Vector3> quiverPosition, quiverRotation;
        private static bool quiverLooked;

        private static bool QuiverSettings()
        {
            if (quiverLooked) return quiverPosition != null && quiverRotation != null;
            quiverLooked = true;
            if (!Chainloader.PluginInfos.TryGetValue("ishid4.mods.betterarchery", out var archery) || !archery.Instance) return false;
            const BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var type = archery.Instance.GetType();
            quiverPosition = type.GetField("QuiverModelPosition", any)?.GetValue(null) as ConfigEntry<Vector3>;
            quiverRotation = type.GetField("QuiverModelRotation", any)?.GetValue(null) as ConfigEntry<Vector3>;
            if (quiverPosition == null || quiverRotation == null) Plugin.Log.LogWarning("Better Archery's quiver settings changed, so the quiver can't be moved from here.");
            return quiverPosition != null && quiverRotation != null;
        }

        private static GameObject Quiver(Player player)
        {
            var point = Utils.FindChild(player.transform, "BackBow_attach");
            if (!point) return null;
            foreach (Transform child in point)
                if (child.name.EndsWith("_QuiverInstance", StringComparison.Ordinal)) return child.gameObject;
            return null;
        }

        private static Vector3 QuiverDefault(bool position) => (Vector3)(position ? quiverPosition : quiverRotation).DefaultValue;

        private static void SetQuiver(Vector3 position, Vector3 rotation, GameObject quiver)
        {
            quiverPosition.Value = position;
            quiverRotation.Value = rotation;
            quiver.transform.localPosition = position;
            quiver.transform.localEulerAngles = rotation;
        }
    }
}
