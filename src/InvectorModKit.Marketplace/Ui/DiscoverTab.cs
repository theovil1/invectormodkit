using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using InvectorModKit.CustomSongs;
using InvectorModKit.Marketplace.Client;
using InvectorModKit.Marketplace.Scores;
using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// "Discover": the catalogue on the left (search, one-click filters, the charts), the selected chart's sheet on the
    /// right. Resting on a chart plays its excerpt; the sheet's main button installs it (chart, then its music fetched
    /// from its video) and then says where it is, or what to do when the music could not be fetched.
    /// </summary>
    internal sealed class DiscoverTab : ITab
    {
        private const float ListWidth = 840, Gap = 48;

        /// <summary>The sheet's difficulties, Casual to Ultra, as the game numbers them (its enum starts at Normal).</summary>
        private static readonly global::Difficulty[] GameLevels = { global::Difficulty.Casual, global::Difficulty.Normal, global::Difficulty.Hard, global::Difficulty.Ultra };

        private readonly MarketplaceScreen _s;
        private readonly RectTransform _root;
        private readonly ButtonWithEvents _search, _more;
        private readonly ButtonWithEvents[] _chips = new ButtonWithEvents[5];
        private readonly ScrollRect _scroll;
        private readonly RectTransform _content;
        private readonly Text _empty;
        private readonly ButtonWithEvents _retryList;
        private readonly ChartSheet _sheet;
        private readonly List<SongRow> _rows = new List<SongRow>();
        private readonly List<ChartSummary> _loaded = new List<ChartSummary>();
        private List<ChartSummary> _shown = new List<ChartSummary>();

        private string _query = "";
        private ChartSort _sort = ChartSort.Popular;
        private Shown _filter = Shown.All;
        private int? _difficulty;
        private int _boardLevel;
        private int _page, _generation;
        private bool _typing;
        private float _searchAt = -1, _nextRefresh, _selectedAt;
        private ChartSummary _selected;
        private string _excerptFor;
        private bool _excerptStopped;
        private string _boardKey, _mainLabel;
        private bool _mainEnabled = true;
        private float _boardAt;
        private HashSet<string> _installed = new HashSet<string>();
        private readonly HashSet<string> _downloading = new HashSet<string>(), _fetching = new HashSet<string>();
        /// <summary>Charts the marketplace said are not there any more, until a search lists them again.</summary>
        private readonly HashSet<string> _gone = new HashSet<string>();
        // Written by yt-dlp's output thread while the screen reads it.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> _progress = new System.Collections.Concurrent.ConcurrentDictionary<string, double>();
        private readonly Dictionary<string, string> _errors = new Dictionary<string, string>();
        private string _status;
        private GameObject _lastRow;

        public bool Typing => _typing;
        public GameObject FirstItem => _rows.Count > 0 ? _rows[0].Root : _search.gameObject;
        public GameObject LastRow => _lastRow ? _lastRow : FirstItem;

        public DiscoverTab(MarketplaceScreen screen)
        {
            _s = screen;
            _root = new GameObject("Discover", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(screen.Body, false);
            UiKit.Fill(_root);

            // ----- left: search, filters, list
            _search = UiKit.Button(_root, "", ButtonStyle.Secondary, StartTyping);
            UiKit.Label(_search).alignment = TextAnchor.MiddleLeft;
            UiKit.At(_search, 0, 0, ListWidth, 66);
            var chips = new GameObject("Filters", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            chips.SetParent(_root, false);
            UiKit.At(chips, 0, 80, ListWidth, 56);
            var layout = chips.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            _chips[0] = UiKit.Button(chips, Loc.T("filter_all"), ButtonStyle.Chip, () => SetFilter(Shown.All));
            _chips[1] = UiKit.Button(chips, Loc.T("filter_not_installed"), ButtonStyle.Chip, () => SetFilter(Shown.NotInstalled));
            _chips[2] = UiKit.Button(chips, Loc.T("filter_installed"), ButtonStyle.Chip, () => SetFilter(Shown.Installed));
            _chips[3] = UiKit.Button(chips, "", ButtonStyle.Chip, ToggleSort);
            _chips[4] = UiKit.Button(chips, "", ButtonStyle.Chip, NextDifficulty);

            (_scroll, _content) = MarketplaceScreen.MakeList(_root);
            var listRt = (RectTransform)_scroll.transform;
            listRt.anchorMin = new Vector2(0, 0);
            listRt.anchorMax = new Vector2(0, 1);
            listRt.pivot = new Vector2(0, 1);
            listRt.offsetMin = new Vector2(0, 70);
            listRt.offsetMax = new Vector2(ListWidth, -150);
            _empty = UiKit.Text(_scroll.transform, "Empty", TextRole.Muted);
            _empty.alignment = TextAnchor.MiddleCenter;
            UiKit.Fill(_empty, 40, 40, 40, 160);
            _retryList = UiKit.Button(_scroll.transform, Loc.T("retry"), ButtonStyle.Secondary, () => Query());
            GameUi.Place(_retryList, 0, -80, 300, 62);
            _more = UiKit.Button(_root, Loc.T("more_results"), ButtonStyle.Secondary, () => Query(append: true));
            var moreRt = (RectTransform)_more.transform;
            moreRt.anchorMin = moreRt.anchorMax = moreRt.pivot = new Vector2(0, 0);
            moreRt.anchoredPosition = new Vector2(ListWidth / 2 - 180, 0);
            moreRt.sizeDelta = new Vector2(360, 58);

            // ----- right: the sheet
            _sheet = new ChartSheet(_root, Main, Listen, () => { if (_selected != null) Application.OpenURL(_selected.YoutubeUrl); },
                ReportSelected, MarketplaceScreen.OpenFolder, () => { if (_selected != null) _s.Confirm(Loc.T("remove_game_confirm", _selected.Title, _selected.Artist), () => RemoveFromGame(_selected)); },
                DeleteSelected, SetBoardLevel);
            var sheetRt = _sheet.Root;
            sheetRt.anchorMin = Vector2.zero;
            sheetRt.anchorMax = Vector2.one;
            sheetRt.offsetMin = new Vector2(ListWidth + Gap, 0);
            sheetRt.offsetMax = Vector2.zero;

            try { _boardLevel = Math.Max(0, Array.IndexOf(GameLevels, ApplicationSession.OnlineUserData != null ? ApplicationSession.OnlineUserData.difficulty : global::Difficulty.Hard)); }
            catch (Exception) { _boardLevel = 2; }
            UpdateChips();
            UpdateSearchLabel();
            ShowSheet(null);
            _root.gameObject.SetActive(false);
            Query();
        }

        // ----- tab

        public void Show()
        {
            _root.gameObject.SetActive(true);
            _mainLabel = null;
            if (_rows.Count > 0) GameUi.Select(_rows[0].Root);
            UpdateKeys();
        }

        public void Hide()
        {
            StopTyping(search: false);
            StopExcerpt();
            _root.gameObject.SetActive(false);
        }

        public void SetStatus(string text) { _status = text; RefreshSheet(); }

        public void Search() => StartTyping();

        public void Listen()
        {
            if (_selected == null) return;
            if (_s.Player.Playing == _selected.Id || _excerptFor == _selected.Id && !_excerptStopped) { StopExcerpt(); _excerptStopped = true; _sheet.SetExcerpt(""); }
            else { _excerptStopped = false; PlayExcerpt(_selected); }
        }

        public void Tick()
        {
            if (_typing) { ReadTyping(); return; }
            if (_searchAt > 0 && Time.unscaledTime >= _searchAt) { _searchAt = -1; Query(append: false); }

            var selected = GameUi.Selected;
            var row = _rows.FirstOrDefault(r => r.Root == selected);
            if (row != null) { MarketplaceScreen.KeepVisible(_scroll, (RectTransform)row.Root.transform); _lastRow = row.Root; }

            if (_selected != null && !_excerptStopped && _excerptFor != _selected.Id && Time.unscaledTime - _selectedAt >= ExcerptWindow.Dwell)
                PlayExcerpt(_selected);

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.5f;
                RefreshLocalState();
            }
        }

        private void UpdateKeys()
        {
            string main = _selected != null ? _mainLabel ?? Loc.T("main_install") : Loc.T("main_install");
            var hints = new List<Hint>();
            if (_mainEnabled) hints.Add(new Hint("Keyboard_Return", ScreenInput.ActionOk, main)); // nothing to press when installed or yours
            hints.Add(new Hint("Keyboard_Space", ScreenInput.ActionListen, Loc.T("hint_listen")));
            hints.Add(new Hint("Keyboard_F", ScreenInput.ActionSearch, Loc.T("hint_search")));
            hints.Add(new Hint("Keyboard_Tab", ScreenInput.ActionNext, Loc.T("hint_tabs")));
            hints.Add(new Hint("Keyboard_Escape", ScreenInput.ActionCancel, Loc.T("hint_back")));
            _s.Keys.Set(hints.ToArray());
        }

        // ----- search and filters

        private void StartTyping()
        {
            _typing = true;
            UpdateSearchLabel();
        }

        private void StopTyping(bool search)
        {
            if (!_typing) return;
            _typing = false;
            UpdateSearchLabel();
            if (search) { _searchAt = -1; Query(append: false); }
        }

        private void ReadTyping()
        {
            if (_s.Input.Ok || _s.Input.Back) { StopTyping(search: true); return; }
            bool changed = false;
            foreach (char c in _s.Input.Typed)
            {
                if (c == '\b') { if (_query.Length > 0) { _query = _query.Substring(0, _query.Length - 1); changed = true; } }
                else if (!char.IsControl(c) && _query.Length < 60) { _query += c; changed = true; }
            }
            if (changed)
            {
                _searchAt = Time.unscaledTime + 0.4f;
                UpdateSearchLabel();
            }
            if (_searchAt > 0 && Time.unscaledTime >= _searchAt) { _searchAt = -1; Query(append: false); }
        }

        private void UpdateSearchLabel() =>
            UiKit.SetLabel(_search, _typing ? _query + "▌" : _query.Length > 0 ? Loc.T("search_query", _query.ToUpperInvariant()) : Loc.T("search_hint"));

        private void SetFilter(Shown filter)
        {
            _filter = filter;
            UpdateChips();
            Rebuild();
        }

        private void ToggleSort()
        {
            _sort = _sort == ChartSort.Recent ? ChartSort.Popular : ChartSort.Recent;
            UpdateChips();
            Query(append: false);
        }

        private void NextDifficulty()
        {
            _difficulty = _difficulty == null ? 0 : _difficulty == 3 ? (int?)null : _difficulty + 1;
            UpdateChips();
            Rebuild();
        }

        private void UpdateChips()
        {
            for (int i = 0; i < 3; i++) UiKit.SetSelected(_chips[i], (int)_filter == i);
            UiKit.SetLabel(_chips[3], _sort == ChartSort.Recent ? Loc.T("sort_recent") : Loc.T("sort_popular"));
            UiKit.SetLabel(_chips[4], _difficulty == null ? Loc.T("difficulty_any") : new[] { "CASUAL", "NORMAL", "HARD", "ULTRA" }[_difficulty.Value]);
            UiKit.SetSelected(_chips[4], _difficulty != null);
        }

        private async void Query(bool append = false)
        {
            int generation = ++_generation;
            int page = append ? _page + 1 : 0;
            _retryList.gameObject.SetActive(false);
            if (!append) _empty.text = Loc.T("searching");
            try
            {
                var result = await MarketplacePlugin.Api.SearchAsync(_query, _sort, page);
                if (!_root || generation != _generation) return;
                if (!append) _loaded.Clear();
                _loaded.AddRange(result.Items);
                foreach (var c in result.Items) _gone.Remove(c.Id);
                _page = page;
                _more.gameObject.SetActive(result.More);
                Rebuild();
            }
            catch (Exception e)
            {
                if (!_root || generation != _generation) return;
                _empty.text = Loc.T("unreachable", e.GetBaseException().Message);
                _retryList.gameObject.SetActive(true);
                _more.gameObject.SetActive(false);
            }
        }

        private void Rebuild()
        {
            var keep = _selected?.Id;
            foreach (var r in _rows) UnityEngine.Object.Destroy(r.Root);
            _rows.Clear();
            _installed = InstalledIds();
            _shown = CatalogueFilter.Apply(_loaded, _installed, _filter, _difficulty);
            foreach (var item in _shown)
            {
                var chart = item;
                SongRow row = null;
                row = _s.MakeRow(_content, () => Main(), () => Select(chart));
                row.Set(chart.Title, Loc.T("by_author", chart.Artist, chart.Author), Tag(chart), chart.Duration, null);
                Thumbnails.Get(chart.YoutubeId, sprite => { if (row.Root) row.Set(chart.Title, Loc.T("by_author", chart.Artist, chart.Author), Tag(chart), chart.Duration, sprite); });
                _rows.Add(row);
            }
            _empty.text = _shown.Count == 0 ? (_query.Length > 0 || _filter != Shown.All || _difficulty != null ? Loc.T("no_match") : Loc.T("no_charts")) : "";
            Wire();
            // The chart shown came back with other facts (shown again or hidden by an admin, a new version): show them.
            var fresh = keep == null ? null : _shown.FirstOrDefault(c => c.Id == keep);
            if (fresh != null && fresh != _selected && Changed(fresh, _selected)) ShowSheet(fresh);
            var again = _rows.FirstOrDefault(r => _shown[_rows.IndexOf(r)].Id == keep);
            if (_root.gameObject.activeInHierarchy && !_typing && _rows.Count > 0) GameUi.Select((again ?? _rows[0]).Root);
            if (_rows.Count == 0) ShowSheet(null);
        }

        private static bool Changed(ChartSummary a, ChartSummary b) =>
            a.Hidden != b.Hidden || a.Version != b.Version || a.Downloads != b.Downloads || a.Title != b.Title || a.Artist != b.Artist || a.YoutubeId != b.YoutubeId;

        // ----- navigation: search, filters, rows, the sheet

        private void Wire()
        {
            var rows = _rows.Select(r => (Selectable)r.Button).ToList();
            MarketplaceScreen.ChainNavigation(rows, _sheet.Main);
            Selectable first = rows.FirstOrDefault();
            if (first) { var nav = first.navigation; nav.selectOnUp = _chips[0]; first.navigation = nav; }
            _search.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = _chips[0], selectOnRight = _sheet.Main };
            for (int i = 0; i < _chips.Length; i++)
                _chips[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = i > 0 ? _chips[i - 1] : null,
                    selectOnRight = i + 1 < _chips.Length ? _chips[i + 1] : (Selectable)_sheet.Main,
                    selectOnUp = _search,
                    selectOnDown = first,
                };
            WireSheet();
        }

        private void WireSheet()
        {
            var actions = _sheet.ActionButtons();
            var more = _sheet.MoreButtons();
            var entries = _sheet.EntryButtons();
            var entryActions = _sheet.EntryActionButtons();
            Selectable level = _sheet.Levels[_boardLevel];
            Selectable back = _lastRow ? _lastRow.GetComponent<Selectable>() : _rows.Count > 0 ? _rows[0].Button : null;
            // Down from the difficulties: an admin's board lines, the actions on the chosen line, More…'s row, the main button.
            Selectable belowBoard = entryActions.Count > 0 ? entryActions[0] : more.Count > 0 ? more[0] : _sheet.Main;
            Selectable belowLevels = entries.Count > 0 ? entries[0] : belowBoard;
            for (int i = 0; i < 4; i++)
                _sheet.Levels[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = i > 0 ? _sheet.Levels[i - 1] : back,
                    selectOnRight = i < 3 ? _sheet.Levels[i + 1] : null,
                    selectOnUp = _sheet.CoverButton,
                    selectOnDown = belowLevels,
                };
            for (int i = 0; i < entries.Count; i++)
                entries[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = back,
                    selectOnUp = i > 0 ? entries[i - 1] : level,
                    selectOnDown = i + 1 < entries.Count ? entries[i + 1] : belowBoard,
                };
            Selectable aboveActions = entries.Count > 0 ? entries[entries.Count - 1] : level;
            Chain(entryActions, back, aboveActions, more.Count > 0 ? more[0] : _sheet.Main);
            _sheet.CoverButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = level, selectOnLeft = back };
            Chain(more, back, entryActions.Count > 0 ? entryActions[0] : aboveActions, _sheet.Main);
            _sheet.Main.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = back,
                selectOnUp = more.Count > 0 ? more[0] : entryActions.Count > 0 ? entryActions[0] : aboveActions,
                selectOnDown = actions.Count > 0 ? actions[0] : null,
            };
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

        // ----- the selected chart

        private void Select(ChartSummary c)
        {
            if (_selected == c) return;
            StopExcerpt();
            _excerptStopped = false;
            _selectedAt = Time.unscaledTime;
            ShowSheet(c);
        }

        private void ShowSheet(ChartSummary c)
        {
            _selected = c;
            _status = null;
            _boardKey = null;
            _sheet.SetBoardEntries(null);
            _sheet.SetEntryActions(null, null);
            _sheet.SetVisible(c != null);
            if (c == null) return;
            _sheet.SetChart(c, Info(c, null));
            _sheet.SetLevel(_boardLevel);
            _sheet.SetExcerpt("");
            VideoInfo.Get(c.YoutubeId, (title, channel) => { if (_selected == c) _sheet.SetInfo(Info(c, Loc.T("quoted", title))); });
            Thumbnails.Get(c.YoutubeId, sprite => { if (_selected == c) _sheet.SetCover(sprite); });
            RefreshSheet();
            LoadBoard(c);
        }

        private static string Info(ChartSummary c, string video) =>
            Loc.T("chart_by", c.Author, c.Version, c.Bpm.ToString("0.#"), $"{(int)c.Duration / 60}:{(int)c.Duration % 60:00}") + "\n" +
            Loc.Downloads(c.Downloads) + (video != null ? "\n" + video : "");

        private void SetBoardLevel(int level)
        {
            _boardLevel = level;
            _sheet.SetLevel(level);
            WireSheet();
            if (_selected != null) LoadBoard(_selected);
        }

        private ChartFacts Facts(ChartSummary c)
        {
            var local = CustomSongsApi.Songs().FirstOrDefault(s => s.MarketplaceId == c.Id && s.Ready);
            bool installed = local != null, downloaded = MarketplacePlugin.Installs.IsDownloaded(c.Id), fetching = _fetching.Contains(c.Id);
            _progress.TryGetValue(c.Id, out double progress);
            _errors.TryGetValue(c.Id, out string error);
            bool otherVersion = downloaded && !installed && !fetching && error == null && IsOtherVersion(c);
            // the music is here (fetched, or the player's own copy) and the game is still preparing the song
            bool matching = downloaded && !installed && !fetching && error == null && !otherVersion
                && (CustomSongsApi.MarketplaceMatchPending || AudioFetch.Existing(CustomSongsApi.SongsFolder, c.Artist, c.Title) != null);
            return new ChartFacts
            {
                Mine = MarketplacePlugin.Session.IsPublishedByMe(c.Id),
                Installed = installed,
                Downloaded = downloaded,
                Downloading = _downloading.Contains(c.Id),
                Fetching = fetching,
                OtherVersion = otherVersion,
                Matching = matching,
                Progress = progress,
                FetchError = error,
                InstalledVersion = local?.MarketplaceVersion ?? 0,
                LatestVersion = c.Version,
                Gone = _gone.Contains(c.Id),
            };
        }

        private void RefreshSheet()
        {
            var c = _selected;
            if (c == null) return;
            var facts = Facts(c);
            var b = MainButton.For(facts);
            string label = b.Kind == MainKind.Installing ? Loc.T(b.LabelKey, b.Percent) : Loc.T(b.LabelKey);
            string sub = b.SubKey == null ? null
                : b.SubKey == "main_retry_sub" ? (b.Reason != null ? Loc.T("main_retry_sub", b.Reason) : Loc.T("main_retry_missing"))
                : b.SubKey == "main_update_sub" ? Loc.T("main_update_sub", c.Version)
                : b.SubKey == "other_version" ? Loc.T("other_version", c.Author)
                : Loc.T(b.SubKey);
            _sheet.SetMain(b, label, sub);
            bool retry = b.Kind == MainKind.Retry;
            _sheet.SetNote(retry ? Loc.T("folder_advice") : _status ?? (c.Hidden ? Loc.T("hidden_admin_note") : null), warning: false);
            var session = MarketplacePlugin.Session.Current;
            bool canDelete = session != null && (session.Role == "admin" || session.Name == c.Author);
            _sheet.SetActions(folder: retry, report: !facts.Mine && !canDelete, removeGame: facts.Installed && !facts.Mine, delete: canDelete);
            if (label != _mainLabel || b.Enabled != _mainEnabled)
            {
                _mainLabel = label;
                _mainEnabled = b.Enabled;
                UpdateKeys();
                WireSheet();
            }
        }

        private void RefreshLocalState()
        {
            _installed = InstalledIds();
            for (int i = 0; i < _rows.Count && i < _shown.Count; i++)
                if (_rows[i].Note) _rows[i].Note.text = Tag(_shown[i]);
            RefreshSheet();
            if (_selected != null && _excerptFor == _selected.Id && _s.Player.Playing == _selected.Id) _sheet.SetExcerpt(Loc.T("excerpt_playing"));
        }

        private static HashSet<string> InstalledIds() =>
            new HashSet<string>(CustomSongsApi.Songs().Where(s => s.MarketplaceId != null && s.Ready).Select(s => s.MarketplaceId));

        private string Tag(ChartSummary c)
        {
            if (c.Hidden) return Loc.T("tag_hidden"); // only admins are sent hidden charts
            if (MarketplacePlugin.Session.IsPublishedByMe(c.Id)) return Loc.T("mine");
            if (_downloading.Contains(c.Id) || _fetching.Contains(c.Id)) return Loc.T("waiting");
            var local = CustomSongsApi.Songs().FirstOrDefault(s => s.MarketplaceId == c.Id && s.Ready);
            if (local != null) return local.MarketplaceVersion > 0 && local.MarketplaceVersion < c.Version ? Loc.T("tag_update") : Loc.T("installed");
            if (MarketplacePlugin.Installs.IsDownloaded(c.Id)) return Loc.T("waiting");
            return (DateTime.UtcNow - c.Updated).TotalDays < 7 ? Loc.T("tag_new") : "";
        }

        /// <summary>
        /// The song file this chart goes with is charted, every check against the waiting charts is done, and it still
        /// doesn't carry this chart: its audio is another version than the author's.
        /// </summary>
        private static bool IsOtherVersion(ChartSummary c)
        {
            if (CustomSongsApi.MarketplaceMatchPending) return false;
            string file = AudioFetch.Existing(CustomSongsApi.SongsFolder, c.Artist, c.Title);
            if (file == null) return false;
            string path = Path.GetFullPath(file);
            return CustomSongsApi.Songs().Any(s => s.Ready && s.MarketplaceId != c.Id && string.Equals(Path.GetFullPath(s.AudioPath), path, StringComparison.OrdinalIgnoreCase));
        }

        // ----- excerpts

        private void PlayExcerpt(ChartSummary c)
        {
            _excerptFor = c.Id;
            _sheet.SetExcerpt(Loc.T("excerpt_loading"));
            _s.Excerpts.Get(c,
                (clip, start, end) =>
                {
                    if (_selected != c || _excerptStopped || !_root.gameObject.activeInHierarchy) return;
                    _s.Player.Play(c.Id, clip, start, end);
                    _sheet.SetExcerpt(Loc.T("excerpt_playing"));
                },
                () => { if (_selected == c) _sheet.SetExcerpt(Loc.T("excerpt_unavailable")); });
        }

        private void StopExcerpt()
        {
            _excerptFor = null;
            _s.Excerpts.Forget();
            _s.Player.Stop();
        }

        // ----- leaderboard

        private async void LoadBoard(ChartSummary c)
        {
            var difficulty = GameLevels[_boardLevel];
            var local = CustomSongsApi.Songs().FirstOrDefault(s => s.MarketplaceId == c.Id && s.MarketplaceVersion > 0);
            int? version = local?.MarketplaceVersion;
            string key = c.Id + "|" + version + "|" + difficulty;
            if (key == _boardKey && Time.unscaledTime - _boardAt < 30f) return;
            _boardKey = key;
            _boardAt = Time.unscaledTime;
            string header = "<b>" + Loc.T("leaderboard", difficulty.ToString().ToUpperInvariant()) + "</b>";
            _sheet.SetBoard(header + "\n…");
            try
            {
                var board = await MarketplaceLeaderboard.Top(c.Id, version, difficulty, 5);
                if (!_root || _boardKey != key) return;
                var lines = new List<string> { header };
                if (board.Entries.Count == 0) lines.Add(Loc.T("board_empty"));
                foreach (var e in board.Entries)
                {
                    string line = Loc.T("board_line", e.Rank, e.Name, e.Score, e.Percent);
                    lines.Add(board.Me != null && e.SteamId == board.Me.SteamId ? "<color=#E9B8FF>" + line + "</color>" : line);
                }
                if (board.Me != null && board.Entries.All(e => e.SteamId != board.Me.SteamId))
                    lines.Add("<color=#E9B8FF>" + Loc.T("board_me", board.Me.Rank, board.Me.Score) + "</color>");
                if (MarketplacePlugin.Session.Current?.CanModerate == true && board.Entries.Count > 0)
                {
                    // Admins: each line is a button; choosing one offers to delete that score or ban that player.
                    int serverDifficulty = MarketplaceLeaderboard.ServerDifficulty(difficulty);
                    _sheet.SetBoard(header);
                    _sheet.SetBoardEntries(board.Entries.Select(e => (Loc.T("board_line", e.Rank, e.Name, e.Score, e.Percent),
                        (Action)(() => ChooseEntry(c, board.Version, serverDifficulty, e)))).ToList());
                    WireSheet();
                    return;
                }
                _sheet.SetBoardEntries(null);
                _sheet.SetBoard(string.Join("\n", lines));
            }
            catch (Exception e)
            {
                if (!_root || _boardKey != key) return;
                if (MainButton.IsGone(e))
                {
                    // removed, or hidden for this player, while its sheet was open
                    _gone.Add(c.Id);
                    _sheet.SetBoard(header + "\n" + Loc.T("main_gone_sub"));
                    RefreshSheet();
                }
                else _sheet.SetBoard(header + "\n" + Loc.T("unreachable", e.GetBaseException().Message));
            }
        }

        // ----- an admin's actions on a board line

        private void ChooseEntry(ChartSummary c, int version, int serverDifficulty, BoardEntry e)
        {
            _sheet.SetEntryActions(
                () => _s.Confirm(Loc.T("mod_confirm_score", e.Name, e.Score),
                    () => Moderate(() => MarketplacePlugin.Api.DeleteScoreAsync(e.SteamId, c.Id, version, serverDifficulty))),
                ModerationState.CanBan(e.SteamId, MarketplacePlugin.Session.Current?.SteamId)
                    ? () => _s.Confirm(Loc.T("mod_confirm_ban", e.Name), () => Moderate(() => MarketplacePlugin.Api.SetRoleAsync(e.SteamId, "banned")))
                    : (Action)null);
            WireSheet();
        }

        /// <summary>Runs an admin call; once the server agreed, the board reloads. A refusal says why on the note line.</summary>
        private async void Moderate(Func<System.Threading.Tasks.Task> call)
        {
            var c = _selected;
            try
            {
                await call();
                if (!_root) return;
                _sheet.SetEntryActions(null, null);
                SetStatus(Loc.T("mod_done"));
                _boardKey = null;
                if (c != null && _selected == c) LoadBoard(c);
            }
            catch (Exception ex)
            {
                if (!_root) return;
                string refused = ex.GetBaseException() is MarketplaceException m ? ModerationState.Refused(m.Status) : null;
                SetStatus(refused != null ? Loc.T(refused) : Loc.T("unreachable", ex.GetBaseException().Message));
            }
        }

        // ----- actions

        private void Main()
        {
            var c = _selected;
            if (c == null) return;
            var b = MainButton.For(Facts(c));
            if (!b.Enabled) return;
            if (b.Kind == MainKind.Install || b.Kind == MainKind.Update) Install(c);
            else if (b.Kind == MainKind.Retry) { _errors.Remove(c.Id); CustomSongsApi.RequestMarketplaceMatch(); FetchAudio(c); }
        }

        private async void Install(ChartSummary c)
        {
            _downloading.Add(c.Id);
            _errors.Remove(c.Id);
            _progress[c.Id] = 0;
            _status = null;
            RefreshSheet();
            try
            {
                var download = await MarketplacePlugin.Api.DownloadAsync(c.Id);
                MarketplacePlugin.Installs.Install(download);
                CustomSongsApi.RequestMarketplaceMatch();
                _downloading.Remove(c.Id);
                if (!_root) return;
                FetchAudio(c);
            }
            catch (Exception e)
            {
                _downloading.Remove(c.Id);
                if (MainButton.IsGone(e)) _gone.Add(c.Id);
                if (!_root || _selected != c) return;
                if (_gone.Contains(c.Id)) RefreshSheet();
                else SetStatus(Loc.T("download_failed", e.GetBaseException().Message));
            }
        }

        /// <summary>
        /// Fetches the song itself with yt-dlp into CustomSongs: the folder watcher then matches it with the chart. A
        /// failure keeps the chart waiting and its reason, and the sheet offers RETRY and the folder advice.
        /// </summary>
        private async void FetchAudio(ChartSummary c)
        {
            if (c == null || _fetching.Contains(c.Id)) return;
            var (ytDlp, ffmpegDir) = AudioFetch.Resolve(MarketplacePlugin.SharedDir);
            if (ytDlp == null)
            {
                _errors[c.Id] = Loc.T("drop_hint");
                Application.OpenURL(c.YoutubeUrl);
                RefreshSheet();
                return;
            }
            if (AudioFetch.Existing(CustomSongsApi.SongsFolder, c.Artist, c.Title) != null)
            {
                CustomSongsApi.RequestMarketplaceMatch(); // the player's own copy: matched, or another version
                RefreshSheet();
                return;
            }
            _fetching.Add(c.Id);
            _progress[c.Id] = 0;
            RefreshSheet();
            try
            {
                string tmpBase = Path.Combine(Path.GetTempPath(), "invectormodkit-" + c.Id);
                string args = AudioFetch.BuildArgs(c.YoutubeId, tmpBase, ffmpegDir);
                MarketplacePlugin.Log.LogInfo($"Audio fetch: {ytDlp} {args}");
                await System.Threading.Tasks.Task.Run(() => AudioFetch.Download(ytDlp, args, line: line =>
                {
                    if (AudioFetch.ProgressOf(line) is double p) _progress[c.Id] = p;
                }));
                string produced = tmpBase + ".ogg";
                if (!File.Exists(produced)) throw new FileNotFoundException("yt-dlp left nothing at " + produced);
                string target = AudioFetch.Target(CustomSongsApi.SongsFolder, c.Artist, c.Title);
                AudioFetch.Move(produced, target);
                MarketplacePlugin.Log.LogInfo("Audio fetched: " + target);
                Thumbnails.Save(c.YoutubeId, Path.ChangeExtension(target, ".jpg")); // song select's cover
                CustomSongsApi.RequestMetadataReload();
                CustomSongsApi.RequestMarketplaceMatch();
            }
            catch (Exception e)
            {
                MarketplacePlugin.Log.LogError("Audio fetch failed: " + e);
                _errors[c.Id] = e.GetBaseException() is AudioFetchException f && f.Reason != null ? Loc.T(f.Reason) : Loc.T("reason_unknown");
            }
            finally
            {
                _fetching.Remove(c.Id);
                if (_root) RefreshSheet();
            }
        }

        /// <summary>The player's copy of the song goes away (audio, chart, cover, cached excerpts); the chart stays available.</summary>
        private void RemoveFromGame(ChartSummary c)
        {
            CustomSongsApi.RemoveMarketplaceSong(c.Id);
            MarketplacePlugin.Installs.Remove(c.Id);
            _sheet.SetMoreOpen(false);
            SetStatus(Loc.T("removed_game"));
            RefreshLocalState();
        }

        private async void ReportSelected()
        {
            var c = _selected;
            if (c == null) return;
            if (!await _s.EnsureSignedIn() || !_root) return;
            _s.Prompt(Loc.T("report_question", c.Title), Loc.T("report_hint"), "",
                text => text.Trim().Length < 5 || text.Trim().Length > 300 ? Loc.T("report_invalid") : null,
                async text =>
                {
                    try
                    {
                        bool hidden = await MarketplacePlugin.Api.ReportAsync(c.Id, text.Trim());
                        if (_root) SetStatus(Loc.T(hidden ? "reported_hidden" : "reported"));
                    }
                    catch (Exception e)
                    {
                        if (_root) SetStatus(Loc.T("report_failed", e.GetBaseException().Message));
                    }
                });
        }

        private void DeleteSelected()
        {
            var c = _selected;
            if (c == null) return;
            _s.Confirm(Loc.T("remove_confirm", c.Title, c.Artist), async () =>
            {
                try
                {
                    await MarketplacePlugin.Api.DeleteAsync(c.Id);
                    MarketplacePlugin.Session.ForgetPublishedChart(c.Id);
                    Query(append: false);
                }
                catch (Exception e)
                {
                    if (_root) SetStatus(Loc.T("remove_failed", e.GetBaseException().Message));
                }
            });
        }
    }
}
