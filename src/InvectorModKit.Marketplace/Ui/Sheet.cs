using System;
using System.Collections.Generic;
using System.Linq;
using InvectorModKit.Marketplace.Client;
using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// The right side of My library and My publications, in Discover's look (<see cref="ChartSheet"/>): the cover (click
    /// to hear the song), title, artist and facts; <see cref="Middle"/>, the tab's own content; a note line; the one main
    /// button with the line under it and its progress; the actions row and, under "More…", the rarer ones.
    /// </summary>
    internal sealed class Sheet
    {
        private const float Pad = 36, Cover = 260, MainHeight = 110, RowHeight = 60, NoteHeight = 56;

        public readonly RectTransform Root;
        /// <summary>Between the facts and the note line, for the tab's own content.</summary>
        public readonly RectTransform Middle;
        public readonly ButtonWithEvents Main, CoverButton;

        private readonly Image _cover, _fill;
        private readonly Text _title, _artist, _info, _excerpt, _note, _mainLabel, _mainSub;
        private readonly RectTransform _actionsRow, _moreRow;
        private readonly List<ButtonWithEvents> _actions = new List<ButtonWithEvents>(), _more = new List<ButtonWithEvents>();
        private ButtonWithEvents _moreButton;
        // The buttons are built again only when their labels change; a click runs the action of the item shown now.
        private readonly ActionRow _actionActs = new ActionRow(), _moreActs = new ActionRow();
        private string _moreKey;
        private bool _moreOpen;

        public Sheet(Transform parent, Action main, Action listen)
        {
            Root = (RectTransform)UiKit.Panel(parent, "Sheet").transform;

            CoverButton = UiKit.Button(Root, "", ButtonStyle.Secondary, listen);
            UiKit.At(CoverButton, Pad, Pad, Cover, Cover);
            _cover = GameUi.Image(CoverButton.transform, "Cover", Color.white);
            _cover.preserveAspect = true;
            UiKit.Fill(_cover, 6, 6, 6, 6);
            _excerpt = UiKit.Text(Root, "Excerpt", TextRole.Small);
            UiKit.At(_excerpt, Pad, Pad + Cover + 6, Cover, 40);

            float x = Pad + Cover + 32;
            _title = UiKit.Text(Root, "Title", TextRole.Title);
            Top(_title, x, Pad, 74);
            _artist = UiKit.Text(Root, "Artist", TextRole.Subtitle);
            Top(_artist, x, Pad + 78, 46);
            _info = UiKit.Text(Root, "Info", TextRole.Muted);
            _info.lineSpacing = 1.05f;
            Top(_info, x, Pad + 130, Cover - 130 + 40);

            float mainBottom = Pad + RowHeight + 16, noteBottom = mainBottom + MainHeight + 8;
            Middle = new GameObject("Middle", typeof(RectTransform)).GetComponent<RectTransform>();
            Middle.SetParent(Root, false);
            Middle.anchorMin = Vector2.zero;
            Middle.anchorMax = Vector2.one;
            Middle.offsetMin = new Vector2(Pad, noteBottom + NoteHeight + 8);
            Middle.offsetMax = new Vector2(-Pad, -(Pad + Cover + 52));

            _note = UiKit.Text(Root, "Note", TextRole.Small);
            _note.color = Color.white;
            Bottom(_note, noteBottom, NoteHeight);

            Main = UiKit.Button(Root, "", ButtonStyle.Main, main);
            Bottom(Main, mainBottom, MainHeight);
            _fill = GameUi.Image(Main.transform, "Progress", new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.55f));
            UiKit.Fill(_fill, 4, 4, 4, 4);
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Horizontal;
            _fill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            _fill.transform.SetSiblingIndex(1);
            _mainLabel = UiKit.Label(Main);
            UiKit.Fill(_mainLabel, UiKit.Margin, 6, UiKit.Margin, MainHeight * 0.42f);
            _mainSub = UiKit.Text(Main.transform, "Sub", TextRole.Small);
            _mainSub.color = Color.white;
            _mainSub.alignment = TextAnchor.MiddleCenter;
            UiKit.Fill(_mainSub, UiKit.Margin, MainHeight * 0.58f, UiKit.Margin, 6);

            _actionsRow = Row("Actions", Pad);
            _moreRow = Row("More", noteBottom);
            _moreRow.gameObject.SetActive(false);
        }

        private RectTransform Row(string name, float bottom)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            row.SetParent(Root, false);
            Bottom(row, bottom, RowHeight);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            return row;
        }

        private static void Top(Component c, float left, float top, float height)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(left, -top - height);
            rt.offsetMax = new Vector2(-Pad, -top);
        }

        private static void Bottom(Component c, float bottom, float height)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(Pad, bottom);
            rt.offsetMax = new Vector2(-Pad, bottom + height);
        }

        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);

        public void SetSong(string title, string artist, Sprite cover, string info)
        {
            _title.text = title ?? "";
            _artist.text = artist ?? "";
            _info.text = info ?? "";
            SetCover(cover);
        }

        public void SetCover(Sprite cover)
        {
            _cover.sprite = cover;
            _cover.color = cover ? Color.white : new Color(1, 1, 1, 0.08f);
        }

        public void SetInfo(string info) => _info.text = info ?? "";
        public void SetExcerpt(string text) => _excerpt.text = text ?? "";

        /// <summary>The line above the main button: a status, or a warning in yellow.</summary>
        public void SetNote(string text, bool warning = false)
        {
            _note.text = text ?? "";
            _note.color = warning ? UiKit.Warning : Color.white;
        }

        /// <summary>The main button for this state (<paramref name="label"/> and <paramref name="sub"/> already in the player's language).</summary>
        public void SetMain(SheetButton b, string label, string sub)
        {
            _mainLabel.text = label ?? "";
            _mainSub.text = sub ?? "";
            _fill.gameObject.SetActive(b.Percent != null);
            _fill.fillAmount = (b.Percent ?? 0) / 100f;
            Main.interactable = b.Enabled;
            UiKit.SetColors(Main, b.Accent && b.Enabled ? UiKit.Accent : new Color32(0x18, 0x16, 0x19, 0xFF));
            _mainLabel.color = b.Warning ? UiKit.Warning : b.Enabled ? Color.white : UiKit.AccentLight;
        }

        /// <summary>The actions row; "More…" is added at its end when <see cref="SetMore"/> gave any.</summary>
        public void SetActions(params (string label, Action act, bool danger)[] buttons)
        {
            if (!_actionActs.Set(buttons.Select(b => b.label), buttons.Select(b => b.act), _moreKey ?? "")) return;
            Rebuild(_actionsRow, _actions, buttons, _actionActs);
            _moreButton = null;
            if (!string.IsNullOrEmpty(_moreKey))
            {
                _moreButton = UiKit.Button(_actionsRow, Loc.T("more"), ButtonStyle.Secondary, ToggleMore);
                _actions.Add(_moreButton);
            }
        }

        /// <summary>The rarer actions, shown above the main button by "More…" (none: no "More…").</summary>
        public void SetMore(params (string label, Action act, bool danger)[] buttons)
        {
            if (!_moreActs.Set(buttons.Select(b => b.label), buttons.Select(b => b.act))) return;
            _moreKey = string.Join("\n", buttons.Select(b => b.label));
            Rebuild(_moreRow, _more, buttons, _moreActs);
            if (buttons.Length == 0) SetMoreOpen(false);
            _actionActs.Reset(); // "More…" comes or goes on the next SetActions
        }

        private static void Rebuild(RectTransform row, List<ButtonWithEvents> list, (string label, Action act, bool danger)[] buttons, ActionRow acts)
        {
            foreach (var b in list) UnityEngine.Object.Destroy(b.gameObject);
            list.Clear();
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                list.Add(UiKit.Button(row, buttons[i].label, buttons[i].danger ? ButtonStyle.Danger : ButtonStyle.Secondary, () => acts.Invoke(index)));
            }
        }

        public void ToggleMore() => SetMoreOpen(!_moreOpen);

        public void SetMoreOpen(bool open)
        {
            _moreOpen = open && _more.Count > 0;
            _moreRow.gameObject.SetActive(_moreOpen);
            _note.gameObject.SetActive(!_moreOpen);
        }

        public List<Selectable> ActionButtons() => _actions.Where(b => b && b.gameObject.activeSelf).Cast<Selectable>().ToList();

        public List<Selectable> MoreButtons() => _moreOpen ? _more.Where(b => b).Cast<Selectable>().ToList() : new List<Selectable>();
    }
}
