using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>One key of the bottom bar: its keyboard key, the game's UI action for controllers, and what it does.</summary>
    internal struct Hint
    {
        public string KeyboardSprite;
        public int? UiActionId;
        public string Label;

        public Hint(string keyboardSprite, int? uiActionId, string label)
        {
            KeyboardSprite = keyboardSprite;
            UiActionId = uiActionId;
            Label = label;
        }
    }

    /// <summary>
    /// The screen's bottom bar: for each key, the picture of the key the player uses (the keyboard key, or the button of
    /// the controller they last touched, as the game draws its own hints) followed by what it does.
    /// </summary>
    internal sealed class KeyBar
    {
        public const float Height = 78;
        private const float Icon = 44;

        private readonly RectTransform _row;
        private readonly List<(Hint hint, Image icon, Text label)> _items = new List<(Hint, Image, Text)>();
        private ControlType _shownFor = ControlType.None;

        public KeyBar(Transform parent)
        {
            var bar = UiKit.Panel(parent, "Key bar", new Color(0, 0, 0, 0.35f));
            var rt = (RectTransform)bar.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0, Height);
            _row = new GameObject("Keys", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            _row.SetParent(bar.transform, false);
            UiKit.Fill(_row, 60, 0, 60, 0);
            var layout = _row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 44;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        }

        public void Set(params Hint[] hints)
        {
            foreach (var item in _items) UnityEngine.Object.Destroy(item.icon.transform.parent.gameObject);
            _items.Clear();
            foreach (var hint in hints)
            {
                var group = new GameObject("Hint", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
                group.SetParent(_row, false);
                var layout = group.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = 12;
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                var icon = GameUi.Image(group, "Icon", Color.white);
                icon.preserveAspect = true;
                var iconLayout = icon.gameObject.AddComponent<LayoutElement>();
                iconLayout.preferredWidth = iconLayout.preferredHeight = Icon;
                var label = UiKit.Text(group, "Label", TextRole.Body);
                label.resizeTextForBestFit = false;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.alignment = TextAnchor.MiddleLeft;
                label.text = hint.Label;
                _items.Add((hint, icon, label));
            }
            _shownFor = ControlType.None;
            Tick();
        }

        /// <summary>Redraws the pictures when the player switches between keyboard and controller.</summary>
        public void Tick()
        {
            ControlType now;
            try { now = ControlTypeHelper.GetLastControllerDontChangePreviousIfMouse(_shownFor == ControlType.None ? ControlType.Keyboard : _shownFor); }
            catch (Exception) { now = ControlType.Keyboard; }
            if (now == _shownFor && _items.Count > 0 && _items[0].icon.sprite != null) return;
            _shownFor = now;
            Rewired.Player player = null;
            try { player = RewiredInputHelper.GetLastUsedControllerAndPlayer(out _); } catch (Exception) { }
            foreach (var (hint, icon, label) in _items)
            {
                Sprite sprite = null;
                if (now != ControlType.Keyboard && hint.UiActionId is int action && player != null && ControllerIconMapCollection.Instance != null)
                {
                    try { sprite = ControllerIconMapCollection.Instance.GetIcon(player, now, 1 /* UI */, action); } catch (Exception) { }
                }
                if (sprite == null && hint.KeyboardSprite != null) sprite = GameUi.KeySprite(hint.KeyboardSprite);
                icon.sprite = sprite;
                icon.gameObject.SetActive(sprite != null);
            }
        }
    }
}
