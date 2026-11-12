using System;
using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    internal enum ButtonStyle { Main, Secondary, Danger, Chip, Tab }

    internal enum TextRole { Title, Subtitle, Body, Muted, Tag, Small }

    /// <summary>
    /// The marketplace screen's building blocks, in the game's look (its dialog buttons, its Exo2 fonts), laid out in a
    /// reference of 1920 units wide. Every button label keeps <see cref="Margin"/> units on each side, shrinks to fit
    /// (down to <see cref="MinFont"/>) and wraps to two lines: it never touches or leaves its button.
    /// </summary>
    internal static class UiKit
    {
        public const float Margin = 24;
        public const int MinFont = 18;

        public static readonly Color Accent = new Color32(0xB2, 0x1F, 0xE0, 0xFF);
        public static readonly Color AccentLight = new Color32(0xE9, 0xB8, 0xFF, 0xFF);
        public static readonly Color PanelColor = new Color32(0x08, 0x04, 0x16, 0xB0);
        public static readonly Color Muted = new Color(1, 1, 1, 0.72f);
        public static readonly Color Warning = new Color32(0xFF, 0xD1, 0x5C, 0xFF);
        public static readonly Color DangerText = new Color32(0xFF, 0xB3, 0xB3, 0xFF);
        private static readonly Color Dark = new Color32(0x18, 0x16, 0x19, 0xFF);

        public static Font Regular, SemiBold, Bold;

        /// <summary>The game's fonts, from the regular one its dialogs use.</summary>
        public static void Init(Font regular)
        {
            Regular = regular;
            SemiBold = GameUi.Font("Exo2-SemiBoldCondensed", regular);
            Bold = GameUi.Font("Exo2-BoldCondensed", regular);
        }

        /// <summary>Anchored to the parent's top left: <paramref name="left"/>, <paramref name="top"/> in units from it.</summary>
        public static RectTransform At(Component c, float left, float top, float width, float height)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }

        /// <summary>Stretched over the parent, inset by the given units.</summary>
        public static RectTransform Fill(Component c, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Text Text(Transform parent, string name, TextRole role)
        {
            int size; Font font; Color color = Color.white;
            switch (role)
            {
                case TextRole.Title: size = 60; font = Bold; break;
                case TextRole.Subtitle: size = 32; font = SemiBold; color = new Color(1, 1, 1, 0.85f); break;
                case TextRole.Muted: size = 25; font = Regular; color = Muted; break;
                case TextRole.Tag: size = 22; font = Bold; break;
                case TextRole.Small: size = 22; font = Regular; color = Muted; break;
                default: size = 27; font = Regular; break;
            }
            var text = GameUi.Text(parent, name, font ?? Regular, size, TextAnchor.UpperLeft, color);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Math.Min(size, MinFont);
            text.resizeTextMaxSize = size;
            return text;
        }

        public static Image Panel(Transform parent, string name, Color? color = null) => GameUi.Image(parent, name, color ?? PanelColor);

        /// <summary>One of the game's dialog buttons, restyled; its label always fits.</summary>
        public static ButtonWithEvents Button(Transform parent, string label, ButtonStyle style, Action onClick)
        {
            var go = GameUi.Clone(GameUi.QuestionButtonTemplate.gameObject, parent, "Button " + label);
            var button = go.GetComponent<ButtonWithEvents>();
            var text = button.GetComponentInChildren<Text>(true);
            foreach (var fitter in text.GetComponents<ContentSizeFitter>()) UnityEngine.Object.Destroy(fitter);
            Fill(text, Margin, 4, Margin, 4);
            int size = style == ButtonStyle.Main ? 40 : style == ButtonStyle.Chip ? 24 : style == ButtonStyle.Tab ? 30 : 26;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.lineSpacing = 0.9f;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = MinFont;
            text.resizeTextMaxSize = size;
            text.fontSize = size;
            if (style == ButtonStyle.Main || style == ButtonStyle.Tab) text.font = Bold ?? text.font;
            text.text = label;
            if (style == ButtonStyle.Danger) text.color = DangerText;
            if (style == ButtonStyle.Main) SetColors(button, Accent);
            button.onClick.AddListener(() =>
            {
                try { onClick?.Invoke(); }
                catch (Exception e) { MarketplacePlugin.Log.LogError(e); }
            });
            return button;
        }

        public static Text Label(ButtonWithEvents button) => button.GetComponentInChildren<Text>(true);

        public static void SetLabel(ButtonWithEvents button, string label) => Label(button).text = label;

        /// <summary>A chip or tab that is on: lighter, like the game's selected tabs.</summary>
        public static void SetSelected(ButtonWithEvents button, bool on) => SetColors(button, on ? new Color(1, 1, 1, 0.35f) : Dark);

        /// <summary>The button's resting colour (selection and hover keep the game's own).</summary>
        public static void SetColors(ButtonWithEvents button, Color normal)
        {
            var colors = button.colors;
            colors.normalColor = normal;
            // ButtonWithEvents puts currentColorBlock back on every select / deselect: it must change too.
            button.colors = button.currentColorBlock = button.originalColors = colors;
        }
    }
}
