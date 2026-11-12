using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InvectorModKit.CustomSongs;
using InvectorModKit.Marketplace.Client;
using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// "My library": every song on this PC. On the left, adding a song from a YouTube link (its progress in the list),
    /// the filters and the songs; on the right the selected song's sheet. The player's own unpublished chart is
    /// published from its sheet: the video link field, checked as typed, and PUBLISH.
    /// </summary>
    internal sealed class LibraryTab : ITab
    {
        private const float ListWidth = 840, Gap = 48, FieldHeight = 58;

        /// <summary>A row: a song on this PC, or a song being added from a link.</summary>
        private sealed class Entry
        {
            public LocalSong Song;
            public LinkDownload Download;
            public string Key => Song != null ? Song.Id : "link:" + Download.VideoId;
        }

        private readonly MarketplaceScreen _s;
        private readonly RectTransform _root;
        private readonly ButtonWithEvents _add;
        private readonly ButtonWithEvents[] _chips = new ButtonWithEvents[4];
        private readonly ScrollRect _scroll;
        private readonly RectTransform _content;
        private readonly Text _empty, _linkCheck;
        private readonly Sheet _sheet;
        private readonly ButtonWithEvents _linkField;
        private readonly List<SongRow> _rows = new List<SongRow>();
        private readonly List<Entry> _entries = new List<Entry>();

        private LibraryShown _filter = LibraryShown.All;
        private Entry _selected;
        private string _link = "", _checkedId, _videoTitle;
        private float _checkGiveUp = -1, _nextRefresh, _selectedAt;
        private bool _typing, _excerptStopped, _mainEnabled = true;
        private string _publishingSong, _publishStep, _status, _excerptFor, _mainLabel, _signature;
        private GameObject _lastRow;

        public bool Typing => _typing;
        public GameObject FirstItem => _rows.Count > 0 ? _rows[0].Root : _add.gameObject;
        public GameObject LastRow => _lastRow ? _lastRow : FirstItem;

        public LibraryTab(MarketplaceScreen screen)
        {
            _s = screen;
            _root = new GameObject("Library", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(screen.Body, false);
            UiKit.Fill(_root);

            _add = UiKit.Button(_root, Loc.T("add_from_link"), ButtonStyle.Secondary, AddFromLink);
            UiKit.Label(_add).alignment = TextAnchor.MiddleLeft;
            UiKit.At(_add, 0, 0, ListWidth, 66);
            var chips = new GameObject("Filters", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            chips.SetParent(_root, false);
            UiKit.At(chips, 0, 80, ListWidth, 56);
            var layout = chips.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            string[] labels = { "filter_all", "filter_mine", "filter_from_marketplace", "filter_published" };
            for (int i = 0; i < 4; i++)
            {
                var shown = (LibraryShown)i;
                _chips[i] = UiKit.Button(chips, Loc.T(labels[i]), ButtonStyle.Chip, () => SetFilter(shown));
            }

            (_scroll, _content) = MarketplaceScreen.MakeList(_root);
            var listRt = (RectTransform)_scroll.transform;
            listRt.anchorMin = new Vector2(0, 0);
            listRt.anchorMax = new Vector2(0, 1);
            listRt.pivot = new Vector2(0, 1);
            listRt.offsetMin = new Vector2(0, 0);
            listRt.offsetMax = new Vector2(ListWidth, -150);
            _empty = UiKit.Text(_scroll.transform, "Empty", TextRole.Muted);
            _empty.alignment = TextAnchor.MiddleCenter;
            UiKit.Fill(_empty, 40, 40, 40, 160);

            _sheet = new Sheet(_root, Main, Listen);
            var sheetRt = _sheet.Root;
            sheetRt.anchorMin = Vector2.zero;
            sheetRt.anchorMax = Vector2.one;
            sheetRt.offsetMin = new Vector2(ListWidth + Gap, 0);
            sheetRt.offsetMax = Vector2.zero;

            // The publish form, in the sheet: the video link field and what was found at that link.
            _linkField = UiKit.Button(_sheet.Middle, "", ButtonStyle.Secondary, StartTyping);
            UiKit.Label(_linkField).alignment = TextAnchor.MiddleLeft;
            var field = (RectTransform)_linkField.transform;
            field.anchorMin = new Vector2(0, 1);
            field.anchorMax = new Vector2(1, 1);
            field.pivot = new Vector2(0.5f, 1);
            field.offsetMin = new Vector2(0, -FieldHeight);
            field.offsetMax = Vector2.zero;
            _linkCheck = UiKit.Text(_sheet.Middle, "Check", TextRole.Small);
            var check = _linkCheck.rectTransform;
            check.anchorMin = Vector2.zero;
            check.anchorMax = Vector2.one;
            check.offsetMin = Vector2.zero;
            check.offsetMax = new Vector2(0, -FieldHeight - 8);

            UpdateChips();
            _root.gameObject.SetActive(false);
        }

        // ----- tab

        public void Show()
        {
            _root.gameObject.SetActive(true);
            LinkDownloads.Changed += Rebuild;
            _mainLabel = null;
            Rebuild();
            UpdateKeys();
        }

        public void Hide()
        {
            LinkDownloads.Changed -= Rebuild;
            StopTyping();
            StopExcerpt();
            _root.gameObject.SetActive(false);
        }

        public void SetStatus(string text) { _status = text; RefreshSheet(); }

        public void Search() { }

        public void Listen()
        {
            var song = _selected?.Song;
            if (song == null) return;
            if (_s.Player.Playing == song.Id || _excerptFor == song.Id && !_excerptStopped) { StopExcerpt(); _excerptStopped = true; _sheet.SetExcerpt(""); }
            else { _excerptStopped = false; PlayExcerpt(song); }
        }

        public void Tick()
        {
            if (_typing) { ReadTyping(); return; }
            var selected = GameUi.Selected;
            var row = _rows.FirstOrDefault(r => r.Root == selected);
            if (row != null) { MarketplaceScreen.KeepVisible(_scroll, (RectTransform)row.Root.transform); _lastRow = row.Root; }

            var song = _selected?.Song;
            if (song != null && !_excerptStopped && _excerptFor != song.Id && Time.unscaledTime - _selectedAt >= ExcerptWindow.Dwell) PlayExcerpt(song);
            if (_checkGiveUp > 0 && Time.unscaledTime >= _checkGiveUp) { _checkGiveUp = -1; UpdateCheck(); }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.5f;
                if (Signature() != _signature) Rebuild(); // a song arrived or went (the game's folder watcher)
                else { RefreshRows(); RefreshSheet(); }
            }
        }

        private void UpdateKeys()
        {
            var hints = new List<Hint>();
            if (_mainEnabled && _mainLabel != null) hints.Add(new Hint("Keyboard_Return", ScreenInput.ActionOk, _mainLabel));
            if (_selected?.Song != null) hints.Add(new Hint("Keyboard_Space", ScreenInput.ActionListen, Loc.T("hint_listen")));
            hints.Add(new Hint("Keyboard_Tab", ScreenInput.ActionNext, Loc.T("hint_tabs")));
            hints.Add(new Hint("Keyboard_Escape", ScreenInput.ActionCancel, Loc.T("hint_back")));
            _s.Keys.Set(hints.ToArray());
        }

        // ----- the list

        private void SetFilter(LibraryShown filter)
        {
            _filter = filter;
            UpdateChips();
            Rebuild();
        }

        private void UpdateChips()
        {
            for (int i = 0; i < 4; i++) UiKit.SetSelected(_chips[i], (int)_filter == i);
        }

        /// <summary>What the list shows: rebuilt when it changes.</summary>
        private string Signature() =>
            string.Join("|", LinkDownloads.All.Select(d => d.VideoId + (d.ErrorKey != null ? "!" : ""))) + "#" +
            string.Join("|", CustomSongsApi.Songs().Where(s => s.Ready).Select(s => s.Id + ":" + s.MarketplaceId));

        private void Rebuild()
        {
            if (!_root) return;
            string keep = _selected?.Key;
            foreach (var r in _rows) UnityEngine.Object.Destroy(r.Root);
            _rows.Clear();
            _entries.Clear();
            _signature = Signature();

            var songs = CustomSongsApi.Songs().Where(s => s.Ready).ToDictionary(s => s.Id);
            _entries.AddRange(LinkDownloads.All.Select(d => new Entry { Download = d }));
            var items = songs.Values.Select(s => new LibraryItem
            {
                Id = s.Id, Title = s.Title, Artist = s.Artist, Mine = Publishing.IsOwn(s),
                Published = MarketplacePlugin.Session.PublishedChart(s.Id) != null,
            });
            _entries.AddRange(CatalogueFilter.Library(items, _filter).Select(i => new Entry { Song = songs[i.Id] }));

            foreach (var entry in _entries)
            {
                var e = entry;
                var row = _s.MakeRow(_content, Main, () => Select(e));
                _rows.Add(row);
                SetRow(row, e);
                if (e.Download != null) Thumbnails.Get(e.Download.VideoId, sprite => { if (row.Root) row.Set(e.Download.Title, "YouTube", Tag(e), 0, sprite); });
            }
            _empty.text = _entries.Count > 0 ? "" : _filter == LibraryShown.All ? Loc.T("no_songs") : Loc.T("no_match");
            Wire();
            var again = _entries.FindIndex(e => e.Key == keep);
            if (again >= 0)
            {
                // The same song, read again (pinned, published, its chart regenerated): the sheet follows.
                _selected = _entries[again];
                if (_selected.Song != null) _sheet.SetInfo(Info(_selected.Song));
                RefreshSheet();
            }
            else _selected = null;
            if (_root.gameObject.activeInHierarchy && !_typing && _rows.Count > 0) GameUi.Select(_rows[Math.Max(0, again)].Root);
            if (_rows.Count == 0) ShowSheet(null);
        }

        private void SetRow(SongRow row, Entry e)
        {
            if (e.Song != null) row.Set(e.Song.Title, e.Song.Artist, Tag(e), e.Song.Duration, e.Song.Cover);
            else row.Set(e.Download.Title, "YouTube", Tag(e), 0, row.Cover ? row.Cover.sprite : null);
        }

        private void RefreshRows()
        {
            for (int i = 0; i < _rows.Count && i < _entries.Count; i++)
                if (_entries[i].Download != null && _rows[i].Note) _rows[i].Note.text = Tag(_entries[i]);
        }

        private static string Tag(Entry e)
        {
            if (e.Download != null)
                return e.Download.ErrorKey != null ? Loc.T("main_add_failed") : e.Download.Preparing ? Loc.T("waiting") : $"{(int)(e.Download.Progress * 100)} %";
            if (MarketplacePlugin.Session.PublishedChart(e.Song.Id) != null) return Loc.T("tag_published");
            return Publishing.IsOwn(e.Song) ? Loc.T("tag_mine") : Loc.T("tag_marketplace");
        }

        // ----- navigation

        private void Wire()
        {
            var rows = _rows.Select(r => (Selectable)r.Button).ToList();
            MarketplaceScreen.ChainNavigation(rows, _linkField.gameObject.activeInHierarchy ? (Selectable)_linkField : _sheet.Main);
            Selectable first = rows.FirstOrDefault();
            if (first) { var nav = first.navigation; nav.selectOnUp = _chips[0]; first.navigation = nav; }
            _add.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = _chips[0], selectOnRight = _sheet.Main };
            for (int i = 0; i < _chips.Length; i++)
                _chips[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = i > 0 ? _chips[i - 1] : null,
                    selectOnRight = i + 1 < _chips.Length ? _chips[i + 1] : (Selectable)_sheet.Main,
                    selectOnUp = _add,
                    selectOnDown = first,
                };
            WireSheet();
        }

        private void WireSheet()
        {
            var actions = _sheet.ActionButtons();
            var more = _sheet.MoreButtons();
            bool form = _linkField.gameObject.activeInHierarchy;
            Selectable back = _lastRow ? _lastRow.GetComponent<Selectable>() : _rows.Count > 0 ? _rows[0].Button : null;
            Selectable aboveMain = more.Count > 0 ? more[0] : form ? (Selectable)_linkField : _sheet.CoverButton;
            _sheet.CoverButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = back, selectOnDown = form ? (Selectable)_linkField : more.Count > 0 ? more[0] : _sheet.Main };
            _linkField.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = back, selectOnUp = _sheet.CoverButton, selectOnDown = more.Count > 0 ? more[0] : _sheet.Main };
            Chain(more, back, form ? (Selectable)_linkField : _sheet.CoverButton, _sheet.Main);
            _sheet.Main.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = back, selectOnUp = aboveMain, selectOnDown = actions.Count > 0 ? actions[0] : null };
            Chain(actions, back, _sheet.Main, null);
        }

        private static void Chain(List<Selectable> row, Selectable left, Selectable up, Selectable down)
        {
            for (int i = 0; i < row.Count; i++)
                row[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = i > 0 ? row[i - 1] : left,
                    selectOnRight = i + 1 < row.Count ? row[i + 1] : null,
                    selectOnUp = up,
                    selectOnDown = down,
                };
        }

        // ----- the selected song

        private void Select(Entry e)
        {
            if (_selected != null && e != null && _selected.Key == e.Key) return;
            StopTyping();
            StopExcerpt();
            _excerptStopped = false;
            _selectedAt = Time.unscaledTime;
            ShowSheet(e);
        }

        private void ShowSheet(Entry e)
        {
            _selected = e;
            _status = null;
            _sheet.SetMoreOpen(false);
            _sheet.SetVisible(e != null);
            _sheet.SetExcerpt("");
            if (e == null) return;
            if (e.Song != null)
            {
                var s = e.Song;
                _sheet.SetSong(s.Title, s.Artist, s.Cover, Info(s));
                if (CanPublish(s))
                {
                    _link = MarketplacePlugin.Session.PublishedLink(s.Id) ?? YoutubeLink.Normalize(GUIUtility.systemCopyBuffer) ?? "";
                    _checkedId = null;
                    UpdateCheck();
                }
            }
            else
            {
                _sheet.SetSong(e.Download.Title, "YouTube", null, e.Download.Link);
                Thumbnails.Get(e.Download.VideoId, sprite => { if (_selected == e) _sheet.SetCover(sprite); });
            }
            RefreshSheet();
        }

        private static string Info(LocalSong s)
        {
            string origin = !Publishing.IsOwn(s) ? Loc.T("installed_from", s.MarketplaceAuthor)
                          : MarketplacePlugin.Session.PublishedChart(s.Id) != null ? Loc.T("published_long") : Loc.T("not_published");
            string notes = s.Notes.Length == 4 ? Loc.T("notes_line", s.Notes[0], s.Notes[1], s.Notes[2], s.Notes[3]) : "";
            return origin + "\n" + notes + "\n" + $"{(int)s.Duration / 60}:{(int)s.Duration % 60:00}  ·  {Path.GetFileName(s.AudioPath)}";
        }

        /// <summary>The player's own chart, not published yet: its sheet holds the publish form.</summary>
        private static bool CanPublish(LocalSong s) => Publishing.IsOwn(s) && MarketplacePlugin.Session.PublishedChart(s.Id) == null;

        private void RefreshSheet()
        {
            var e = _selected;
            if (e == null) return;
            SheetButton b;
            string sub;
            bool form = false;
            if (e.Song != null)
            {
                var s = e.Song;
                string published = MarketplacePlugin.Session.PublishedChart(s.Id);
                form = CanPublish(s) || _publishingSong == s.Id;
                b = LibraryState.For(new LibraryFacts
                {
                    Mine = Publishing.IsOwn(s), Published = published != null && _publishingSong != s.Id, PublishedVersion = s.MarketplaceVersion,
                    LinkValid = YoutubeLink.Normalize(_link) != null, Publishing = _publishingSong == s.Id, PublishStep = _publishStep,
                    Author = s.MarketplaceAuthor,
                });
                sub = b.SubKey == null ? null : Loc.T(b.SubKey, b.SubArgs);
                _sheet.SetMore(Publishing.IsOwn(s) || s.MarketplaceId == null
                    ? new[] { (Loc.T("open_folder"), (Action)MarketplaceScreen.OpenFolder, false) }
                    : new[] { (Loc.T("open_folder"), (Action)MarketplaceScreen.OpenFolder, false), (Loc.T("remove_game"), (Action)(() => RemoveFromGame(s)), true) });
                _sheet.SetActions((Loc.T("level_editor"), () => Publishing.OpenInEditor(s, SetStatus), false));
            }
            else
            {
                var d = e.Download;
                b = LibraryState.For(new LibraryFacts { Adding = d.ErrorKey == null, Preparing = d.Preparing, Progress = d.Progress, AddError = d.ErrorKey });
                sub = d.ErrorKey == null ? Loc.T(b.SubKey)
                    : d.ErrorKey.StartsWith("reason_", StringComparison.Ordinal) ? Loc.T("link_failed", Loc.T(d.ErrorKey))
                    : d.ErrorKey == "link_failed" ? Loc.T("link_failed", d.ErrorDetail ?? "") : Loc.T(d.ErrorKey);
                _sheet.SetMore();
                _sheet.SetActions((Loc.T("watch_video"), () => Application.OpenURL(d.Link), false));
            }
            bool wasForm = _linkField.gameObject.activeSelf;
            _linkField.gameObject.SetActive(form);
            _linkCheck.gameObject.SetActive(form);
            if (form) UpdateLinkField();
            string label = Loc.T(b.LabelKey, b.LabelArgs);
            _sheet.SetMain(b, label, sub);
            _sheet.SetNote(_status);
            if (label != _mainLabel || b.Enabled != _mainEnabled || wasForm != form)
            {
                _mainLabel = label;
                _mainEnabled = b.Enabled;
                UpdateKeys();
                Wire();
            }
        }

        // ----- the link field

        private void StartTyping()
        {
            if (_selected?.Song == null || !CanPublish(_selected.Song)) return;
            _typing = true;
            UpdateLinkField();
        }

        private void StopTyping()
        {
            if (!_typing) return;
            _typing = false;
            UpdateLinkField();
            UpdateCheck();
        }

        private void ReadTyping()
        {
            if (_s.Input.Ok || _s.Input.Back) { StopTyping(); RefreshSheet(); return; }
            bool changed = false;
            foreach (char c in _s.Input.Typed)
            {
                if (c == '\b') { if (_link.Length > 0) { _link = _link.Substring(0, _link.Length - 1); changed = true; } }
                else if (!char.IsControl(c) && _link.Length < 200) { _link += c; changed = true; }
            }
            if (!changed) return;
            UpdateLinkField();
            UpdateCheck();
            RefreshSheet();
        }

        private void UpdateLinkField() =>
            UiKit.SetLabel(_linkField, _typing ? _link + "▌" : _link.Length > 0 ? _link : Loc.T("link_field"));

        /// <summary>What is at the link: being checked, the video's title, not found, or not a video link.</summary>
        private void UpdateCheck()
        {
            string link = YoutubeLink.Normalize(_link);
            if (link == null)
            {
                _checkedId = null;
                _linkCheck.color = UiKit.Warning;
                _linkCheck.text = _link.Trim().Length == 0 ? "" : Loc.T("link_invalid");
                return;
            }
            string id = link.Substring(link.IndexOf("v=", StringComparison.Ordinal) + 2);
            if (id != _checkedId)
            {
                _checkedId = id;
                _videoTitle = null;
                _checkGiveUp = Time.unscaledTime + 8f;
                VideoInfo.Get(id, (title, channel) => { if (_checkedId == id) { _videoTitle = title; _checkGiveUp = -1; UpdateCheck(); } });
            }
            if (_videoTitle != null) { _linkCheck.color = new Color32(0x9B, 0xE7, 0xB0, 0xFF); _linkCheck.text = Loc.T("video_found", _videoTitle); }
            else if (_checkGiveUp > 0) { _linkCheck.color = UiKit.Muted; _linkCheck.text = Loc.T("link_checking"); }
            else { _linkCheck.color = UiKit.Warning; _linkCheck.text = Loc.T("video_not_found"); }
        }

        // ----- actions

        private void Main()
        {
            var s = _selected?.Song;
            if (s == null || !CanPublish(s) || _publishingSong != null) return;
            string link = YoutubeLink.Normalize(_link);
            if (link == null) { StartTyping(); return; }
            _publishingSong = s.Id;
            _publishStep = null;
            _status = null;
            RefreshSheet();
            Publishing.Publish(_s, s, link,
                step => { _publishStep = step; if (_root) RefreshSheet(); },
                published =>
                {
                    _publishingSong = null;
                    if (!_root) return;
                    _status = published.Version > 1 ? Loc.T("updated", published.Version) : Loc.T("published_done");
                    Rebuild();
                },
                why =>
                {
                    _publishingSong = null;
                    if (!_root) return;
                    if (why != null) _status = why;
                    RefreshSheet();
                });
        }

        private void AddFromLink()
        {
            string initial = YoutubeLink.Normalize(GUIUtility.systemCopyBuffer) ?? "";
            _s.Prompt(Loc.T("add_link_question"), Loc.T("link_hint"), initial,
                text => YoutubeLink.Normalize(text) == null ? Loc.T("link_invalid") : null,
                text =>
                {
                    LinkDownloads.Start(text);
                    if (_filter != LibraryShown.All) SetFilter(LibraryShown.All);
                    if (_rows.Count > 0) { GameUi.Select(_rows[0].Root); Select(_entries[0]); }
                });
        }

        /// <summary>A marketplace song's audio and chart go away; the chart stays on the marketplace.</summary>
        private void RemoveFromGame(LocalSong s)
        {
            _s.Confirm(Loc.T("remove_game_confirm", s.Title, s.Artist), () =>
            {
                CustomSongsApi.RemoveMarketplaceSong(s.MarketplaceId);
                MarketplacePlugin.Installs.Remove(s.MarketplaceId);
                if (!_root) return;
                _status = Loc.T("removed_game");
                Rebuild();
            });
        }

        // ----- excerpts

        private void PlayExcerpt(LocalSong s)
        {
            _excerptFor = s.Id;
            _s.Excerpts.GetLocal(s.Id, s.PreviewPath,
                (clip, start, end) =>
                {
                    if (_selected?.Song != s || _excerptStopped || !_root.gameObject.activeInHierarchy) return;
                    _s.Player.Play(s.Id, clip, start, end);
                    _sheet.SetExcerpt(Loc.T("excerpt_playing"));
                },
                () => { if (_selected?.Song == s) _sheet.SetExcerpt(Loc.T("excerpt_unavailable")); });
        }

        private void StopExcerpt()
        {
            _excerptFor = null;
            _s.Excerpts.Forget();
            _s.Player.Stop();
        }
    }
}
