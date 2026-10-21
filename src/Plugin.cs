// Potato Dye - cape colors at the barber, written from scratch.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PotatoDye
{
    // Sit in the barber's chair and a Cape color panel sits beside the hair settings: red, green and blue sliders that
    // tint the cape you're wearing as you move them. Apply keeps the color on that cape (it stays with the cape through
    // chests, trades and death), Cancel puts it back. It's free, like changing a hair style. The color multiplies the
    // cape's own texture, so light capes take it best. Other players with Potato Dye see your colors.
    [BepInPlugin(Id, "Potato Dye", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Id = "potato.dye", Version = "1.0.0";
        internal const string ItemKey = "PotatoDye.Color";
        internal static readonly int ZdoKey = "PotatoDye.Cape".GetStableHashCode();

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> ShowColors, BarberPanel;

        private void Awake()
        {
            Log = Logger;
            ShowColors = Config.Bind("General", "Show cape colors", true, "Show dyed capes in their colors, yours and other players'.");
            BarberPanel = Config.Bind("General", "Barber panel", true, "Show the Cape color panel at the barber.");
            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
        }

        // ---------------------------------------------------------------- colors
        // A color is kept as "RRGGBB" on the cape and as 0x1RRGGBB in the wearer's shared data (0 = not dyed).
        internal static bool TryParse(string text, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(text) || text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)) return false;
            color = FromRgb(rgb);
            return true;
        }

        internal static string ToHex(Color color) => (ToRgb(color) & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

        internal static int ToRgb(Color color) =>
            (Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f) << 16) | (Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f) << 8) | Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f);

        internal static Color FromRgb(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        internal static bool IsPlain(Color color) => ToRgb(color) == 0xFFFFFF;

        internal static int Packed(Color? color) => color.HasValue && !IsPlain(color.Value) ? (1 << 24) | ToRgb(color.Value) : 0;

        internal static Color? ColorOf(ItemDrop.ItemData item) =>
            item?.m_customData != null && item.m_customData.TryGetValue(ItemKey, out var text) && TryParse(text, out var color) ? color : (Color?)null;

        internal static void SetColor(ItemDrop.ItemData item, Color? color)
        {
            if (item == null) return;
            if (!color.HasValue || IsPlain(color.Value)) item.m_customData?.Remove(ItemKey);
            else
            {
                if (item.m_customData == null) item.m_customData = new Dictionary<string, string>();
                item.m_customData[ItemKey] = ToHex(color.Value);
            }
        }

        // ---------------------------------------------------------------- your cape, shared with everyone
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> Shoulder = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_shoulderItem");
        private static readonly AccessTools.FieldRef<Character, ZNetView> CharacterView = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        internal static ItemDrop.ItemData WornCape(Player player) => player ? Shoulder(player) : null;

        // While the barber sliders move, the cape shows the slider color instead of its own.
        internal static Color? Preview;
        internal static bool Previewing;

        [HarmonyPatch(typeof(Player), "Update")]
        internal static class Publish
        {
            private static float next;

            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || (!Previewing && Time.time < next)) return;
                // Left the chair without Apply or Cancel: back to the cape's own color.
                if (Previewing && !__instance.IsAttached()) { Previewing = false; Preview = null; }
                next = Time.time + 0.25f;
                var zdo = CharacterView(__instance)?.GetZDO();
                if (zdo == null || !zdo.IsOwner()) return;
                int want = Packed(Previewing ? Preview : ColorOf(WornCape(__instance)));
                if (zdo.GetInt(ZdoKey) != want) zdo.Set(ZdoKey, want);
            }
        }

        // ---------------------------------------------------------------- drawing
        // Every worn cape is tinted with its wearer's color through the game's own material settings (the way cape
        // styles are set), multiplied with the cape's own color. Set again now and then, since the game's snow and wet
        // effects can refresh the same settings.
        [HarmonyPatch(typeof(VisEquipment), "UpdateVisuals")]
        internal static class Draw
        {
            private sealed class State
            {
                internal List<GameObject> Instances;
                internal int Packed;
                internal float Next;
                internal readonly List<GameObject> Tinted = new List<GameObject>();
            }

            private static readonly ConditionalWeakTable<VisEquipment, State> States = new ConditionalWeakTable<VisEquipment, State>();
            private static readonly AccessTools.FieldRef<VisEquipment, ZNetView> View = AccessTools.FieldRefAccess<VisEquipment, ZNetView>("m_nview");
            private static readonly AccessTools.FieldRef<VisEquipment, List<GameObject>> Instances = AccessTools.FieldRefAccess<VisEquipment, List<GameObject>>("m_shoulderItemInstances");
            private static readonly int ColorId = Shader.PropertyToID("_Color");

            private static void Postfix(VisEquipment __instance)
            {
                if (!__instance.m_isPlayer || __instance.m_isArmorStand || MaterialMan.instance == null) return;
                var zdo = View(__instance)?.GetZDO();
                int packed = Plugin.ShowColors.Value && zdo != null ? zdo.GetInt(ZdoKey) : 0;
                var instances = Instances(__instance);
                var state = States.GetOrCreateValue(__instance);
                if (state.Instances == instances && state.Packed == packed && (packed == 0 || Time.time < state.Next)) return;
                if (state.Instances != instances) state.Tinted.Clear();
                state.Instances = instances;
                state.Packed = packed;
                state.Next = Time.time + 2f;
                try { Apply(instances, packed, state); }
                catch (Exception e) { Log.LogWarning("Couldn't color a cape: " + e.Message); }
            }

            private static void Apply(List<GameObject> instances, int packed, State state)
            {
                if (packed == 0)
                {
                    foreach (var target in state.Tinted)
                        if (target) MaterialMan.instance.ResetValue(target, ColorId);
                    state.Tinted.Clear();
                    return;
                }
                if (instances == null) return;
                var dye = FromRgb(packed & 0xFFFFFF);
                foreach (var instance in instances)
                {
                    if (!instance) continue;
                    // Share the settings holder of the cape's style, so neither undoes the other.
                    var style = instance.GetComponentInChildren<ItemStyle>(true);
                    var target = style ? style.gameObject : instance;
                    var own = Color.white;
                    foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                    {
                        var material = renderer.sharedMaterial;
                        if (material && material.HasProperty(ColorId)) { own = material.GetColor(ColorId); break; }
                    }
                    MaterialMan.instance.SetValue(target, ColorId, own * dye, true);
                    if (!state.Tinted.Contains(target)) state.Tinted.Add(target);
                }
            }
        }

        // ---------------------------------------------------------------- the color in the tooltip
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        internal static class Tooltip
        {
            private static void Postfix(ItemDrop.ItemData item, ref string __result)
            {
                var color = ColorOf(item);
                if (color.HasValue) __result += $"\n<color=#{ToHex(color.Value)}>■■■</color> Dyed #{ToHex(color.Value)}";
            }
        }
    }

    // ---------------------------------------------------------------- the barber panel
    // A panel beside the barber window, built from the window's own parts (background, sliders, text, buttons) so it
    // looks like the rest of it. It opens and closes with the window.
    internal static class BarberPanel
    {
        private static GameObject panel;
        private static PlayerCustomizaton owner;
        private static readonly Slider[] Sliders = new Slider[3];
        private static readonly TMP_Text[] Values = new TMP_Text[3];
        private static TMP_Text note;
        private static Image swatch;
        private static bool syncing;
        private static readonly string[] Names = { "Red", "Green", "Blue" };
        private static readonly Color[] Tints = { new Color(1f, 0.45f, 0.45f), new Color(0.5f, 1f, 0.5f), new Color(0.55f, 0.7f, 1f) };

        private static readonly AccessTools.FieldRef<bool> WasHidden = AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(PlayerCustomizaton), "m_barberWasHidden"));

        [HarmonyPatch(typeof(PlayerCustomizaton), nameof(PlayerCustomizaton.ShowBarberGui))]
        internal static class Show
        {
            private static void Prefix(out bool __state) => __state = WasHidden();

            private static void Postfix(bool __state)
            {
                var barber = PlayerCustomizaton.m_barberInstance;
                if (!Plugin.BarberPanel.Value || !barber || !barber.m_rootPanel || !Player.m_localPlayer) return;
                try
                {
                    if (!panel || owner != barber) Build(barber);
                    if (!panel) return;
                    panel.SetActive(true);
                    Fit();
                    // Coming back from the inventory keeps the sliders where they were.
                    if (!__state || !Plugin.Previewing) Load();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Couldn't open the cape color panel: " + e);
                    if (panel) panel.SetActive(false);
                }
            }
        }

        [HarmonyPatch(typeof(PlayerCustomizaton), nameof(PlayerCustomizaton.OnApply))]
        internal static class Apply
        {
            private static void Prefix()
            {
                if (!Plugin.Previewing) return;
                var cape = Plugin.WornCape(Player.m_localPlayer);
                if (cape != null) Plugin.SetColor(cape, Plugin.Preview);
                Stop();
            }
        }

        [HarmonyPatch(typeof(PlayerCustomizaton), nameof(PlayerCustomizaton.OnCancel))]
        internal static class Cancel
        {
            private static void Prefix() => Stop();
        }

        private static void Stop()
        {
            Plugin.Previewing = false;
            Plugin.Preview = null;
        }

        // Sliders from the cape you're wearing (white = its own colors).
        private static void Load()
        {
            var cape = Plugin.WornCape(Player.m_localPlayer);
            var color = Plugin.ColorOf(cape) ?? Color.white;
            syncing = true;
            Sliders[0].value = color.r;
            Sliders[1].value = color.g;
            Sliders[2].value = color.b;
            syncing = false;
            bool wearing = cape != null;
            foreach (var slider in Sliders) slider.interactable = wearing;
            note.text = wearing ? Localization.instance.Localize(cape.m_shared.m_name) : "Wear a cape to dye it";
            Stop();
            Refresh();
        }

        private static void Changed()
        {
            if (syncing || Plugin.WornCape(Player.m_localPlayer) == null) return;
            Plugin.Preview = Current();
            Plugin.Previewing = true;
            Refresh();
        }

        private static Color Current() => new Color(Sliders[0].value, Sliders[1].value, Sliders[2].value, 1f);

        private static void Refresh()
        {
            var color = Current();
            for (int i = 0; i < 3; i++) Values[i].text = Mathf.RoundToInt(Sliders[i].value * 255f).ToString(CultureInfo.InvariantCulture);
            swatch.color = color;
        }

        private static void Reset()
        {
            syncing = true;
            foreach (var slider in Sliders) slider.value = 1f;
            syncing = false;
            Changed();
        }

        private static void Build(PlayerCustomizaton barber)
        {
            if (panel) UnityEngine.Object.Destroy(panel);
            owner = barber;
            var root = barber.m_rootPanel.transform as RectTransform;
            var sliderSource = barber.m_hairLevel ? barber.m_hairLevel : barber.m_hairTone;
            var textSource = barber.m_selectedHair;
            if (!root || !sliderSource || !textSource || !barber.m_cancel)
            {
                Plugin.Log.LogWarning("The barber window changed; the cape color panel stays off.");
                return;
            }

            const float width = 300f, height = 250f, pad = 16f;
            // Beside the window's background (its largest picture), unless that fills the screen.
            var source = root.GetComponent<Image>() ?? root.GetComponentsInChildren<Image>(true)
                .Where(image => image.sprite && image.transform != root && !image.GetComponentInParent<Selectable>())
                .OrderByDescending(image => ((RectTransform)image.transform).rect.width * ((RectTransform)image.transform).rect.height).FirstOrDefault();
            var canvas = root.GetComponentInParent<Canvas>()?.rootCanvas;
            var host = root;
            if (source && canvas)
            {
                var size = ((RectTransform)source.transform).rect.size;
                var full = ((RectTransform)canvas.transform).rect.size;
                if (size.x < full.x * 0.8f) host = (RectTransform)source.transform;
            }
            panel = new GameObject("PotatoDye_CapePanel", typeof(RectTransform));
            var rect = (RectTransform)panel.transform;
            rect.SetParent(host, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(12f, 0f);
            rect.sizeDelta = new Vector2(width, height);

            // The window's own background, or a plain dark one.
            var background = panel.AddComponent<Image>();
            if (source)
            {
                background.sprite = source.sprite;
                background.type = source.type;
                background.color = source.color;
                background.material = source.material;
            }
            else background.color = new Color(0f, 0f, 0f, 0.75f);

            float y = pad;
            var title = Text(textSource, "Title", pad, y, width - pad * 2f, 30f);
            title.text = "Cape color";
            title.alignment = TextAlignmentOptions.Center;
            y += 32f;
            note = Text(textSource, "Cape", pad, y, width - pad * 2f, 22f);
            note.alignment = TextAlignmentOptions.Center;
            note.fontSize = Mathf.Max(12f, textSource.fontSize * 0.8f);
            y += 30f;

            for (int i = 0; i < 3; i++)
            {
                var label = Text(textSource, Names[i], pad, y, 60f, 26f);
                label.text = Names[i];
                label.alignment = TextAlignmentOptions.Left;
                label.color = Tints[i];
                label.fontSize = Mathf.Max(12f, textSource.fontSize * 0.85f);

                var copy = UnityEngine.Object.Instantiate(sliderSource.gameObject, rect, false);
                copy.name = "PotatoDye_" + Names[i];
                Clean(copy);
                var slider = copy.GetComponent<Slider>();
                slider.onValueChanged = new Slider.SliderEvent();
                slider.minValue = 0f;
                slider.maxValue = 1f;
                slider.wholeNumbers = false;
                slider.onValueChanged.AddListener(_ => Changed());
                Place((RectTransform)copy.transform, pad + 64f, y + 3f, width - pad * 2f - 64f - 44f, 20f);
                foreach (var image in copy.GetComponentsInChildren<Image>(true))
                    if (image.transform != copy.transform && !image.GetComponent<Selectable>()) image.color *= Tints[i];
                Sliders[i] = slider;

                var value = Text(textSource, Names[i] + "Value", width - pad - 40f, y, 40f, 26f);
                value.alignment = TextAlignmentOptions.Right;
                value.fontSize = Mathf.Max(12f, textSource.fontSize * 0.8f);
                Values[i] = value;
                y += 34f;
            }

            var swatchObject = new GameObject("PotatoDye_Swatch", typeof(RectTransform));
            swatchObject.transform.SetParent(rect, false);
            Place((RectTransform)swatchObject.transform, pad, y + 4f, 40f, 32f);
            swatch = swatchObject.AddComponent<Image>();

            var reset = UnityEngine.Object.Instantiate(barber.m_cancel.gameObject, rect, false);
            reset.name = "PotatoDye_Reset";
            Clean(reset);
            var button = reset.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(Reset);
            var buttonText = reset.GetComponentInChildren<TMP_Text>(true);
            if (buttonText) buttonText.text = "No dye";
            Place((RectTransform)reset.transform, width - pad - 140f, y, 140f, 40f);
            panel.SetActive(false);
        }

        // Keep the whole panel on screen.
        private static void Fit()
        {
            var rect = (RectTransform)panel.transform;
            var canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
            if (!canvas) return;
            Canvas.ForceUpdateCanvases();
            var space = (RectTransform)canvas.transform;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var corner in corners)
            {
                var local = space.InverseTransformPoint(corner);
                minX = Mathf.Min(minX, local.x); maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y); maxY = Mathf.Max(maxY, local.y);
            }
            var bounds = space.rect;
            float dx = 0f, dy = 0f;
            if (maxX > bounds.xMax) dx = bounds.xMax - maxX;
            if (minX + dx < bounds.xMin) dx = bounds.xMin - minX;
            if (minY < bounds.yMin) dy = bounds.yMin - minY;
            if (maxY + dy > bounds.yMax) dy = bounds.yMax - maxY;
            if (dx == 0f && dy == 0f) return;
            var shift = rect.parent.InverseTransformVector(space.TransformVector(new Vector3(dx, dy, 0f)));
            rect.anchoredPosition += new Vector2(shift.x, shift.y);
        }

        private static TMP_Text Text(TMP_Text source, string name, float x, float y, float w, float h)
        {
            var copy = UnityEngine.Object.Instantiate(source.gameObject, panel.transform, false);
            copy.name = "PotatoDye_" + name;
            Clean(copy);
            foreach (Transform child in copy.transform) UnityEngine.Object.Destroy(child.gameObject);
            var text = copy.GetComponent<TMP_Text>();
            text.text = "";
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            Place((RectTransform)copy.transform, x, y, w, h);
            return text;
        }

        // No translation, gamepad shortcut or layout parts carried over from the copied control.
        private static void Clean(GameObject copy)
        {
            foreach (var localize in copy.GetComponentsInChildren<Localize>(true)) UnityEngine.Object.Destroy(localize);
            foreach (var shortcut in copy.GetComponentsInChildren<UIGamePad>(true)) UnityEngine.Object.Destroy(shortcut);
            foreach (var layout in copy.GetComponents<LayoutElement>()) UnityEngine.Object.Destroy(layout);
            foreach (var fitter in copy.GetComponents<ContentSizeFitter>()) UnityEngine.Object.Destroy(fitter);
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }
    }
}
