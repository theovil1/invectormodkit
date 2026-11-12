using System;
using System.Collections.Generic;
using System.Linq;
using InvectorModKit.Marketplace.Client;
using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// Discover's right side: the selected chart's cover (click to hear it), title, artist and facts, its four
    /// difficulties (each choosing the leaderboard shown), the leaderboard, the one main button that says what happens
    /// next, a note line, and the secondary actions (video, report, more).
    /// </summary>
    internal sealed class ChartSheet
    {
        private const float Pad = 36, Cover = 260, MainHeight = 110, RowHeight = 60;

        public readonly RectTransform Root;
        public readonly ButtonWithEvents Main, Video, Report, More, Folder, RemoveGame, Delete;
        public readonly ButtonWithEvents CoverButton;
        public readonly ButtonWithEvents[] Levels = new ButtonWithEvents[4];

        private readonly Image _cover, _fill;
        private readonly Text _title, _artist, _info, _excerpt, _board, _note, _mainLabel, _mainSub;
        private readonly RectTransform _moreRow, _entries, _entryRow;
        private readonly List<ButtonWithEvents> _entryButtons = new List<ButtonWithEvents>();
        private readonly ButtonWithEvents _deleteScore, _banPlayer;
        private Action _onDeleteScore, _onBanPlayer;
        private bool _moreOpen;

        public ChartSheet(Transform parent, Action main, Action listen, Action video, Action report, Action folder,
                          Action removeGame, Action delete, Action<int> level)
        {
            var panel = UiKit.Panel(parent, "Sheet");
            Root = (RectTransform)panel.transform;

            CoverButton = UiKit.Button(Root, "", ButtonStyle.Secondary, listen);
            UiKit.At(CoverButton, Pad, Pad, Cover, Cover);
            _cover = GameUi.Image(CoverButton.transform, "Cover", Color.white);
            _cover.preserveAspect = true;
            UiKit.Fill(_cover, 6, 6, 6, 6);
            _excerpt = UiKit.Text(Root, "Excerpt", TextRole.Small);
            UiKit.At(_excerpt, Pad, Pad + Cover + 6, Cover, 56);

            float x = Pad + Cover + 32;
            _title = UiKit.Text(Root, "Title", TextRole.Title);
            Stretch(_title, x, Pad, Pad, 74);
            _artist = UiKit.Text(Root, "Artist", TextRole.Subtitle);
            Stretch(_artist, x, Pad + 78, Pad, 46);
            _info = UiKit.Text(Root, "Info", TextRole.Muted);
            _info.lineSpacing = 1.05f;
            Stretch(_info, x, Pad + 130, Pad, 140);

            var levels = new GameObject("Levels", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            levels.SetParent(Root, false);
            Stretch(levels, Pad, Pad + Cover + 70, Pad, 72);
            var layout = levels.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            for (int i = 0; i < 4; i++)
            {
                int d = i;
                Levels[i] = UiKit.Button(levels, "", ButtonStyle.Chip, () => level(d));
            }

            _board = UiKit.Text(Root, "Leaderboard", TextRole.Body);
            _board.supportRichText = true;
            _board.resizeTextForBestFit = false;
            _board.fontSize = 26;
            _board.lineSpacing = 1.08f;
            var boardRt = (RectTransform)_board.transform;
            boardRt.anchorMin = new Vector2(0, 0);
            boardRt.anchorMax = new Vector2(1, 1);
            boardRt.offsetMin = new Vector2(Pad, Pad + RowHeight + 16 + MainHeight + 16 + 64);
            boardRt.offsetMax = new Vector2(-Pad, -(Pad + Cover + 70 + 72 + 20));

            // Admins: the board's lines as buttons under its header, and the actions on the chosen line under them.
            float boardBottom = Pad + RowHeight + 16 + MainHeight + 16 + 64;
            _entries = new GameObject("Entries", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            _entries.SetParent(Root, false);
            _entries.anchorMin = new Vector2(0, 0);
            _entries.anchorMax = new Vector2(1, 1);
            _entries.offsetMin = new Vector2(Pad, boardBottom + 58);
            _entries.offsetMax = new Vector2(-Pad, -(Pad + Cover + 70 + 72 + 20 + 40));
            var entriesLayout = _entries.GetComponent<VerticalLayoutGroup>();
            entriesLayout.spacing = 6;
            entriesLayout.childControlWidth = entriesLayout.childControlHeight = entriesLayout.childForceExpandWidth = true;
            entriesLayout.childForceExpandHeight = false;
            _entries.gameObject.SetActive(false);
            _entryRow = Row("Entry actions", boardBottom);
            _deleteScore = UiKit.Button(_entryRow, Loc.T("mod_delete_score"), ButtonStyle.Danger, () => _onDeleteScore?.Invoke());
            _banPlayer = UiKit.Button(_entryRow, Loc.T("mod_ban_player"), ButtonStyle.Danger, () => _onBanPlayer?.Invoke());
            _entryRow.gameObject.SetActive(false);

            _note = UiKit.Text(Root, "Note", TextRole.Small);
            _note.color = Color.white;
            Bottom(_note, Pad, Pad + RowHeight + 16 + MainHeight + 8, 56);

            Main = UiKit.Button(Root, "", ButtonStyle.Main, main);
            Bottom(Main, Pad, Pad + RowHeight + 16, MainHeight);
            _fill = GameUi.Image(Main.transform, "Progress", new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, 0.55f));
            UiKit.Fill(_fill, 4, 4, 4, 4);
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Horizontal;
            _fill.sprite = Texture2D.whiteTexture != null ? Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f) : null;
            _fill.transform.SetSiblingIndex(1);
            _mainLabel = UiKit.Label(Main);
            UiKit.Fill(_mainLabel, UiKit.Margin, 6, UiKit.Margin, MainHeight * 0.42f);
            _mainSub = UiKit.Text(Main.transform, "Sub", TextRole.Small);
            _mainSub.color = Color.white;
            _mainSub.alignment = TextAnchor.MiddleCenter;
            UiKit.Fill(_mainSub, UiKit.Margin, MainHeight * 0.58f, UiKit.Margin, 6);

            var row = Row("Actions", Pad);
            Video = UiKit.Button(row, Loc.T("watch_video"), ButtonStyle.Secondary, video);
            Folder = UiKit.Button(row, Loc.T("open_folder"), ButtonStyle.Secondary, folder);
            Report = UiKit.Button(row, Loc.T("report"), ButtonStyle.Secondary, report);
            More = UiKit.Button(row, Loc.T("more"), ButtonStyle.Secondary, ToggleMore);

            _moreRow = Row("More", Pad + RowHeight + 16 + MainHeight + 16);
            RemoveGame = UiKit.Button(_moreRow, Loc.T("remove_game"), ButtonStyle.Secondary, removeGame);
            Delete = UiKit.Button(_moreRow, Loc.T("delete_chart"), ButtonStyle.Danger, delete);
            _moreRow.gameObject.SetActive(false);
        }

        private RectTransform Row(string name, float bottom)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            row.SetParent(Root, false);
            Bottom(row, Pad, bottom, RowHeight);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            return row;
        }

        private static void Stretch(Component c, float left, float top, float right, float height)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(left, -top - height);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void Bottom(Component c, float side, float bottom, float height)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(side, bottom);
            rt.offsetMax = new Vector2(-side, bottom + height);
        }

        public void ToggleMore() => SetMoreOpen(!_moreOpen);

        public void SetMoreOpen(bool open)
        {
            _moreOpen = open && (RemoveGame.gameObject.activeSelf || Delete.gameObject.activeSelf);
            _moreRow.gameObject.SetActive(_moreOpen);
            _board.gameObject.SetActive(!_moreOpen);
            _entries.gameObject.SetActive(!_moreOpen && _entryButtons.Count > 0);
            if (_moreOpen) _entryRow.gameObject.SetActive(false);
        }

        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);

        public void SetChart(ChartSummary c, string info)
        {
            _title.text = c.Title;
            _artist.text = c.Artist;
            _info.text = info;
            _cover.sprite = null;
            _cover.color = new Color(1, 1, 1, 0.08f);
            string[] names = { "CASUAL", "NORMAL", "HARD", "ULTRA" };
            int[] notes = { c.Notes.Casual, c.Notes.Normal, c.Notes.Hard, c.Notes.Ultra };
            for (int i = 0; i < 4; i++) UiKit.SetLabel(Levels[i], names[i] + "\n" + Loc.T("notes_count", notes[i]));
            SetMoreOpen(false);
        }

        public void SetInfo(string info) => _info.text = info;

        public void SetCover(Sprite sprite)
        {
            _cover.sprite = sprite;
            _cover.color = sprite ? Color.white : new Color(1, 1, 1, 0.08f);
        }

        public void SetLevel(int difficulty)
        {
            for (int i = 0; i < 4; i++) UiKit.SetSelected(Levels[i], i == difficulty);
        }

        public void SetExcerpt(string text) => _excerpt.text = text ?? "";

        /// <summary>
        /// Admins: the board's lines as buttons (<paramref name="entries"/> null: the text board, as players see it).
        /// The board's text then keeps only its header.
        /// </summary>
        public void SetBoardEntries(IList<(string line, Action select)> entries)
        {
            foreach (var b in _entryButtons) UnityEngine.Object.Destroy(b.gameObject);
            _entryButtons.Clear();
            _entries.gameObject.SetActive(entries != null && entries.Count > 0);
            if (entries == null) return;
            foreach (var (line, select) in entries)
            {
                var button = UiKit.Button(_entries, line, ButtonStyle.Secondary, select);
                UiKit.Label(button).alignment = TextAnchor.MiddleLeft;
                var size = button.gameObject.AddComponent<LayoutElement>();
                size.minHeight = size.preferredHeight = 44;
                _entryButtons.Add(button);
            }
        }

        /// <summary>
        /// The actions on the board line an admin chose (<paramref name="deleteScore"/> null: none chosen, the row goes;
        /// <paramref name="banPlayer"/> null: that player cannot be banned, the admin's own line).
        /// </summary>
        public void SetEntryActions(Action deleteScore, Action banPlayer)
        {
            _onDeleteScore = deleteScore;
            _onBanPlayer = banPlayer;
            _entryRow.gameObject.SetActive(deleteScore != null);
            _banPlayer.gameObject.SetActive(banPlayer != null);
        }

        public List<Selectable> EntryButtons() => _entryButtons.Where(b => b).Cast<Selectable>().ToList();

        public List<Selectable> EntryActionButtons() => _entryRow.gameObject.activeSelf
            ? new[] { _deleteScore, _banPlayer }.Where(b => b.gameObject.activeSelf).Cast<Selectable>().ToList()
            : new List<Selectable>();
        public void SetBoard(string text) => _board.text = text ?? "";
        public void SetNote(string text, bool warning = false)
        {
            _note.text = text ?? "";
            _note.color = warning ? UiKit.Warning : Color.white;
        }

        /// <summary>The main button for this state: its label, the line under it, the progress bar, its colours.</summary>
        public void SetMain(MainButton b, string label, string sub)
        {
            _mainLabel.text = label;
            _mainSub.text = sub ?? "";
            _fill.gameObject.SetActive(b.Kind == MainKind.Installing);
            _fill.fillAmount = b.Percent / 100f;
            Main.interactable = b.Enabled;
            bool accent = b.Kind == MainKind.Install || b.Kind == MainKind.Update;
            UiKit.SetColors(Main, accent ? UiKit.Accent : new Color32(0x18, 0x16, 0x19, 0xFF));
            _mainLabel.color = b.Kind == MainKind.Retry ? UiKit.Warning : b.Kind == MainKind.Installed ? UiKit.AccentLight : Color.white;
        }

        /// <summary>Which secondary actions this chart has; the row keeps only those.</summary>
        public void SetActions(bool folder, bool report, bool removeGame, bool delete)
        {
            Folder.gameObject.SetActive(folder);
            Report.gameObject.SetActive(report);
            RemoveGame.gameObject.SetActive(removeGame);
            Delete.gameObject.SetActive(delete);
            More.gameObject.SetActive(removeGame || delete);
            if (!removeGame && !delete) SetMoreOpen(false);
        }

        /// <summary>The visible buttons of the actions row, left to right (for navigation).</summary>
        public List<Selectable> ActionButtons()
        {
            var list = new List<Selectable>();
            foreach (var b in new[] { Video, Folder, Report, More }) if (b.gameObject.activeSelf) list.Add(b);
            return list;
        }

        public List<Selectable> MoreButtons()
        {
            var list = new List<Selectable>();
            if (_moreOpen) foreach (var b in new[] { RemoveGame, Delete }) if (b.gameObject.activeSelf) list.Add(b);
            return list;
        }
    }
}
