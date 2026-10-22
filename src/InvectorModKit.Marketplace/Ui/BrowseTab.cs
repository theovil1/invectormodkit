using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InvectorModKit.CustomSongs;
using InvectorModKit.Marketplace.Client;
using InvectorModKit.Marketplace.Scores;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>"Parcourir": search the catalogue, see a chart's details, install it, open its video.</summary>
    internal sealed class BrowseTab : ITab
    {
        private readonly MarketplaceModal _m;
        private readonly GameObject _root;
        private readonly ButtonWithEvents _search, _sort, _more, _install, _youtube, _folder, _remove, _report;
        private readonly ScrollRect _scroll;
        private readonly RectTransform _content;
        private readonly Text _title, _artist, _info, _status, _empty;
        private readonly Image _cover, _audioPanel, _boardPanel;
        private readonly Text _video, _board;
        private string _boardKey;
        private float _boardAt;
        private readonly List<SongRow> _rows = new List<SongRow>();
        private readonly List<ChartSummary> _items = new List<ChartSummary>();

        private string _query = "";
        private ChartSort _sortMode = ChartSort.Recent;
        private int _page, _generation;
        private bool _typing;
        private float _searchAt = -1, _nextRefresh;
        private ChartSummary _selected;
        private HashSet<string> _installed = new HashSet<string>();
        private readonly HashSet<string> _fetching = new HashSet<string>();
        private bool _justDownloaded;

        public bool Typing => _typing;
        private string _statusKey = "external";
        public GameObject FirstItem => _rows.Count > 0 ? _rows[0].Root : _search.gameObject;
        public GameObject LastRow => _lastRow ? _lastRow : FirstItem;
        private GameObject _lastRow;

        public BrowseTab(MarketplaceModal m)
        {
            _m = m;
            _root = new GameObject("Browse", typeof(RectTransform));
            _root.transform.SetParent(m.Box, false);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;

            _search = m.MakeButton(_root.transform, "", -420, 205, 560, 58, StartTyping, 26);
            _sort = m.MakeButton(_root.transform, Loc.T("sort_recent"), -20, 205, 200, 58, ToggleSort, 26);
            (_scroll, _content) = MarketplaceModal.MakeList(_root.transform, -310, -70, 780, 500);
            _empty = GameUi.Text(_scroll.transform, "Empty", m.Regular, 28, TextAnchor.MiddleCenter, GameUi.Muted);
            GameUi.Place(_empty, 0, 0, 700, 200);
            _more = m.MakeButton(_root.transform, Loc.T("more_results"), -310, -350, 320, 44, () => Search(append: true), 24);

            _cover = GameUi.Image(_root.transform, "Cover", Color.white);
            GameUi.Place(_cover, 225, 160, 160, 160);
            _title = GameUi.Text(_root.transform, "Title", m.Bold, 32, TextAnchor.UpperLeft, Color.white);
            GameUi.Place(_title, 505, 200, 350, 80);
            _artist = GameUi.Text(_root.transform, "Artist", m.Regular, 26, TextAnchor.UpperLeft, GameUi.Muted);
            GameUi.Place(_artist, 505, 125, 350, 60);
            _info = GameUi.Text(_root.transform, "Info", m.Regular, 22, TextAnchor.UpperLeft, GameUi.Muted);
            GameUi.Place(_info, 415, 25, 550, 90);

            // Where the audio comes from: the chart is timed on the soundtrack of the attached video.
            _audioPanel = GameUi.Image(_root.transform, "Audio", new Color(1, 1, 1, 0.06f));
            GameUi.Place(_audioPanel, 415, -100, 560, 150);
            var audioHeader = GameUi.Text(_audioPanel.transform, "Header", m.Bold, 22, TextAnchor.UpperLeft, Color.white);
            audioHeader.text = Loc.T("audio_header");
            GameUi.Place(audioHeader, 0, 52, 520, 30);
            _video = GameUi.Text(_audioPanel.transform, "Video", m.Regular, 22, TextAnchor.UpperLeft, Color.white);
            GameUi.Place(_video, 0, 8, 520, 56);
            var audioNote = GameUi.Text(_audioPanel.transform, "Note", m.Regular, 19, TextAnchor.UpperLeft, GameUi.Muted);
            audioNote.text = Loc.T("audio_note");
            audioNote.resizeTextForBestFit = true;
            audioNote.resizeTextMinSize = 14;
            audioNote.resizeTextMaxSize = 19;
            GameUi.Place(audioNote, 0, -46, 520, 50);

            // Once the chart is installed (or is mine), its leaderboard takes the audio panel's place.
            _boardPanel = GameUi.Image(_root.transform, "Leaderboard", new Color(1, 1, 1, 0.06f));
            GameUi.Place(_boardPanel, 415, -100, 560, 150);
            _board = GameUi.Text(_boardPanel.transform, "Lines", m.Regular, 21, TextAnchor.UpperLeft, Color.white);
            _board.supportRichText = true;
            _board.lineSpacing = 1.05f;
            GameUi.Place(_board, 0, 0, 520, 136);

            _status = GameUi.Text(_root.transform, "Status", m.Regular, 21, TextAnchor.UpperLeft, Color.white);
            GameUi.Place(_status, 415, -202, 550, 44);
            _install = m.MakeButton(_root.transform, Loc.T("install"), 280, -258, 270, 58, () => Primary(_selected));
            _youtube = m.MakeButton(_root.transform, Loc.T("open_youtube"), 560, -258, 270, 58, () => { if (_selected != null) Application.OpenURL(_selected.YoutubeUrl); });
            _folder = m.MakeButton(_root.transform, Loc.T("open_folder"), 280, -324, 270, 52, OpenFolder, 24);
            _remove = m.MakeButton(_root.transform, Loc.T("remove"), 560, -324, 270, 52, RemoveSelected, 24);
            _report = m.MakeButton(_root.transform, Loc.T("report"), 560, -324, 270, 52, ReportSelected, 24);
            var actions = new Selectable[] { _install, _youtube, _folder, _remove };
            for (int i = 0; i < actions.Length; i++)
                actions[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnRight = i % 2 == 0 ? actions[i + 1] : null,
                    selectOnLeft = i % 2 == 1 ? actions[i - 1] : null,
                    selectOnDown = i + 2 < actions.Length ? actions[i + 2] : null,
                    selectOnUp = i >= 2 ? actions[i - 2] : null,
                };
            _report.navigation = _remove.navigation;

            ShowDetails(null);
            UpdateSearchLabel();
            _root.SetActive(false);
            Search();
        }

        public void Show()
        {
            _root.SetActive(true);
            if (_rows.Count > 0) GameUi.Select(_rows[0].Root);
        }

        public void Hide()
        {
            StopTyping(search: false);
            _root.SetActive(false);
        }

        public void SetStatus(string text) => SetStatus(text, "external");
        /// <summary>Named status lines, so RefreshLocalState can drop the stale ones (waiting for a fetch that ended).</summary>
        private void SetStatus(string text, string key)
        {
            _statusKey = key;
            _status.text = text;
        }

        public void Tick()
        {
            if (_typing) ReadTyping();
            else if (Input.GetKeyDown(KeyCode.F)) StartTyping();
            if (_searchAt > 0 && Time.unscaledTime >= _searchAt) { _searchAt = -1; Search(); }

            var selected = GameUi.Selected;
            var row = _rows.FirstOrDefault(r => r.Root == selected);
            if (row != null) { MarketplaceModal.KeepVisible(_scroll, (RectTransform)row.Root.transform); _lastRow = row.Root; }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 1f;
                RefreshLocalState();
            }
        }

        // ----- search

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
            if (search) { _searchAt = -1; Search(); }
        }

        private void ReadTyping()
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                StopTyping(search: true);
                return;
            }
            bool changed = false;
            foreach (char c in Input.inputString)
            {
                if (c == '\b') { if (_query.Length > 0) { _query = _query.Substring(0, _query.Length - 1); changed = true; } }
                else if (!char.IsControl(c) && _query.Length < 60) { _query += c; changed = true; }
            }
            if (changed)
            {
                _searchAt = Time.unscaledTime + 0.4f;
                UpdateSearchLabel();
            }
        }

        private void UpdateSearchLabel() =>
            MarketplaceModal.SetLabel(_search, _typing ? _query + "▌" : _query.Length > 0 ? Loc.T("search_query", _query.ToUpperInvariant()) : Loc.T("search_hint"));

        private void ToggleSort()
        {
            _sortMode = _sortMode == ChartSort.Recent ? ChartSort.Popular : ChartSort.Recent;
            MarketplaceModal.SetLabel(_sort, _sortMode == ChartSort.Recent ? Loc.T("sort_recent") : Loc.T("sort_popular"));
            Search();
        }

        private async void Search(bool append = false)
        {
            int generation = ++_generation;
            int page = append ? _page + 1 : 0;
            _empty.text = append ? "" : Loc.T("searching");
            try
            {
                var result = await MarketplacePlugin.Api.SearchAsync(_query, _sortMode, page);
                if (!_root || generation != _generation) return;
                if (!append) _items.Clear();
                _items.AddRange(result.Items);
                _page = page;
                _more.gameObject.SetActive(result.More);
                Rebuild();
                _empty.text = _items.Count == 0 ? (_query.Length > 0 ? Loc.T("no_match") : Loc.T("no_charts")) : "";
            }
            catch (Exception e)
            {
                if (!_root || generation != _generation) return;
                _empty.text = Loc.T("unreachable", e.GetBaseException().Message);
            }
        }

        private void Rebuild()
        {
            foreach (var r in _rows) UnityEngine.Object.Destroy(r.Root);
            _rows.Clear();
            foreach (var item in _items)
            {
                var chart = item;
                SongRow row = null;
                row = _m.MakeRow(_content, () => Primary(chart), () => ShowDetails(chart));
                row.Set(chart.Title, Loc.T("by_author", chart.Artist, chart.Author), Note(chart), chart.Duration, null);
                Thumbnails.Get(chart.YoutubeId, sprite => { if (row.Root) row.Set(chart.Title, Loc.T("by_author", chart.Artist, chart.Author), Note(chart), chart.Duration, sprite); });
                _rows.Add(row);
            }
            MarketplaceModal.ChainNavigation(_rows.Select(r => (Selectable)r.Button).ToList(), _install);
            Selectable first = _rows.Count > 0 ? _rows[0].Button : null;
            if (first) { var nav = first.navigation; nav.selectOnUp = _search; first.navigation = nav; }
            _search.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = _sort, selectOnDown = first };
            _sort.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _search, selectOnDown = first };
            if (_rows.Count > 0 && !_typing && _root.activeInHierarchy) GameUi.Select(_rows[0].Root);
            if (_rows.Count == 0) ShowDetails(null);
        }

        // ----- local state: what is installed or waiting for its audio

        private void RefreshLocalState()
        {
            _installed = new HashSet<string>(CustomSongsApi.Songs().Where(s => s.MarketplaceId != null && s.Ready).Select(s => s.MarketplaceId));
            for (int i = 0; i < _rows.Count && i < _items.Count; i++)
                if (_rows[i].Note) _rows[i].Note.text = Note(_items[i]);
            if (_selected != null) UpdateActions(_selected);
        }

        private string Note(ChartSummary c) =>
            MarketplacePlugin.Session.IsPublishedByMe(c.Id) ? Loc.T("mine") : _installed.Contains(c.Id) ? Loc.T("installed") : MarketplacePlugin.Installs.IsDownloaded(c.Id) ? Loc.T("waiting") : "";

        // ----- details and actions

        private void ShowDetails(ChartSummary c)
        {
            _selected = c;
            bool any = c != null;
            foreach (var go in new GameObject[] { _cover.gameObject, _title.gameObject, _artist.gameObject, _info.gameObject, _install.gameObject, _youtube.gameObject })
                go.SetActive(any);
            _boardKey = null;
            if (!any)
            {
                foreach (var go in new GameObject[] { _audioPanel.gameObject, _boardPanel.gameObject, _remove.gameObject, _report.gameObject }) go.SetActive(false);
                SetStatus("", "");
                return;
            }
            _title.text = c.Title;
            _artist.text = c.Artist;
            _info.text = Loc.T("chart_by", c.Author, c.Version, c.Bpm.ToString("0.#"), $"{(int)c.Duration / 60}:{(int)c.Duration % 60:00}") + "\n" +
                         Loc.T("notes_line", c.Notes.Casual, c.Notes.Normal, c.Notes.Hard, c.Notes.Ultra) + "\n" +
                         Loc.Downloads(c.Downloads);
            _video.text = "youtu.be/" + c.YoutubeId;
            VideoInfo.Get(c.YoutubeId, (title, channel) =>
            {
                if (_selected == c && _video) _video.text = Loc.T("quoted", title) + (string.IsNullOrEmpty(channel) ? "" : $"  ·  {channel}");
            });
            _cover.sprite = null;
            _cover.color = new Color(1, 1, 1, 0.1f);
            Thumbnails.Get(c.YoutubeId, sprite => { if (_selected == c && _cover) { _cover.sprite = sprite; _cover.color = Color.white; } });
            SetStatus("", "");
            _justDownloaded = false;
            UpdateActions(c);
        }

        private void UpdateActions(ChartSummary c)
        {
            bool mine = MarketplacePlugin.Session.IsPublishedByMe(c.Id);
            bool installed = _installed.Contains(c.Id), waiting = !mine && !installed && MarketplacePlugin.Installs.IsDownloaded(c.Id);
            MarketplaceModal.SetLabel(_install, mine ? Loc.T("mine") : installed ? Loc.T("remove_game") : waiting ? Loc.T("waiting_audio") : Loc.T("install"));
            _install.interactable = !mine && (installed || !waiting);
            if (waiting)
            {
                // While a fetch runs (audio_fetching) the drop hint only shows once it's quiet, and a stale
                // hint or failure from an earlier attempt disappears once the chart is installed.
                if (_statusKey != "audio_fetching" && (_statusKey == "" || _statusKey == "downloading" || _statusKey == "downloaded"))
                    SetStatus(Loc.T("drop_hint"), "drop_hint");
                if (installed && _justDownloaded) { SetStatus(Loc.T("installed_done"), "installed_done"); _justDownloaded = false; }
            }
            else if (_statusKey == "drop_hint" || _statusKey == "audio_failed")
                SetStatus("", "");
            var session = MarketplacePlugin.Session.Current;
            bool canRemove = session != null && (session.Role == "admin" || session.Name == c.Author);
            _remove.gameObject.SetActive(canRemove);
            _report.gameObject.SetActive(!canRemove && !mine);
            var corner = canRemove ? _remove : _report.gameObject.activeSelf ? _report : null;
            var nav = _folder.navigation; nav.selectOnRight = corner; _folder.navigation = nav;
            nav = _youtube.navigation; nav.selectOnDown = corner; _youtube.navigation = nav;

            bool showBoard = mine || installed;
            _audioPanel.gameObject.SetActive(!showBoard);
            _boardPanel.gameObject.SetActive(showBoard);
            if (showBoard) LoadBoard(c);
        }

        /// <summary>Top 3 of the difficulty chosen in the game, for the installed version (else the latest), and my rank.</summary>
        private async void LoadBoard(ChartSummary c)
        {
            var difficulty = ApplicationSession.OnlineUserData != null ? ApplicationSession.OnlineUserData.difficulty : global::Difficulty.Hard;
            var local = CustomSongsApi.Songs().FirstOrDefault(s => s.MarketplaceId == c.Id && s.MarketplaceVersion > 0);
            int? version = local?.MarketplaceVersion;
            string key = c.Id + "|" + version + "|" + difficulty;
            if (key == _boardKey && Time.unscaledTime - _boardAt < 30f) return;
            _boardKey = key;
            _boardAt = Time.unscaledTime;
            string header = "<b>" + Loc.T("leaderboard", difficulty.ToString().ToUpperInvariant()) + "</b>";
            _board.text = header + "\n…";
            try
            {
                var board = await MarketplaceLeaderboard.Top(c.Id, version, difficulty, 3);
                if (!_root || _boardKey != key) return;
                var lines = new List<string> { header };
                if (board.Entries.Count == 0) lines.Add(Loc.T("board_empty"));
                foreach (var e in board.Entries)
                {
                    string line = Loc.T("board_line", e.Rank, e.Name, e.Score, e.Percent);
                    lines.Add(board.Me != null && e.SteamId == board.Me.SteamId ? "<color=#CB33FF>" + line + "</color>" : line);
                }
                if (board.Me != null && board.Entries.All(e => e.SteamId != board.Me.SteamId))
                    lines.Add("<color=#CB33FF>" + Loc.T("board_me", board.Me.Rank, board.Me.Score) + "</color>");
                _board.text = string.Join("\n", lines);
            }
            catch (Exception e)
            {
                if (_root && _boardKey == key) _board.text = header + "\n" + Loc.T("unreachable", e.GetBaseException().Message);
            }
        }

        private async void ReportSelected()
        {
            var c = _selected;
            if (c == null) return;
            if (!await _m.EnsureSignedIn() || !_root) return;
            _m.Prompt(Loc.T("report_question", c.Title), Loc.T("report_hint"), "",
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

        private void Primary(ChartSummary c)
        {
            if (c == null) return;
            if (MarketplacePlugin.Session.IsPublishedByMe(c.Id)) return;
            if (_installed.Contains(c.Id)) { _m.Confirm(Loc.T("remove_game_confirm", c.Title, c.Artist), () => RemoveFromGame(c)); return; }
            if (MarketplacePlugin.Installs.IsDownloaded(c.Id)) { FetchAudio(c); return; }
            Install(c);
        }

        /// <summary>The player's copy of the song goes away (audio, chart, cover, cached excerpts); the chart stays available.</summary>
        private void RemoveFromGame(ChartSummary c)
        {
            CustomSongsApi.RemoveMarketplaceSong(c.Id);
            MarketplacePlugin.Installs.Remove(c.Id);
            RefreshLocalState();
            SetStatus(Loc.T("removed_game"), "removed_game");
        }

        private async void Install(ChartSummary c)
        {
            SetStatus(Loc.T("downloading"), "downloading");
            try
            {
                var download = await MarketplacePlugin.Api.DownloadAsync(c.Id);
                MarketplacePlugin.Installs.Install(download);
                CustomSongsApi.RequestMarketplaceMatch();
                if (!_root) return;
                SetStatus(Loc.T("downloaded"), "downloaded");
                _justDownloaded = true;
                RefreshLocalState();
                FetchAudio(c);
            }
            catch (Exception e)
            {
                if (_root) SetStatus(Loc.T("download_failed", e.GetBaseException().Message), "download_failed");
            }
        }

        /// <summary>
        /// Fetches the song itself with yt-dlp (bundled, or the player's own on the PATH), into CustomSongs:
        /// the folder watcher then matches it with the chart, nothing else to do. A failure leaves the
        /// download waiting, and the old advice (drop the file by hand) keeps it playable.
        /// </summary>
        private async void FetchAudio(ChartSummary c)
        {
            if (c == null || _fetching.Contains(c.Id)) return;
            _fetching.Add(c.Id);
            try
            {
                var (ytDlp, ffmpegDir) = AudioFetch.Resolve(MarketplacePlugin.SharedDir);
                if (ytDlp == null ||
                    AudioFetch.Existing(CustomSongsApi.SongsFolder, c.Artist, c.Title) != null)
                {
                    if (_root) { SetStatus(Loc.T("drop_hint"), "drop_hint"); if (ytDlp == null) Application.OpenURL(c.YoutubeUrl); }
                    return;
                }
                string tmpBase = Path.Combine(Path.GetTempPath(), "invectormodkit-" + c.Id);
                string args = AudioFetch.BuildArgs(c.YoutubeId, tmpBase, ffmpegDir);
                MarketplacePlugin.Log.LogInfo($"Audio fetch: {ytDlp} {args}");
                if (_root) SetStatus(Loc.T("audio_fetching"), "audio_fetching");
                await System.Threading.Tasks.Task.Run(() =>
                    AudioFetch.Download(ytDlp, args, line => MarketplacePlugin.Log.LogInfo(line)));
                string produced = tmpBase + ".ogg";
                if (!File.Exists(produced))
                    throw new FileNotFoundException("yt-dlp left nothing at " + produced);
                string target = AudioFetch.Target(CustomSongsApi.SongsFolder, c.Artist, c.Title);
                AudioFetch.Move(produced, target);
                MarketplacePlugin.Log.LogInfo("Audio fetched: " + target);
                Thumbnails.Save(c.YoutubeId, Path.ChangeExtension(target, ".jpg")); // song select's cover
                CustomSongsApi.RequestMetadataReload();
                CustomSongsApi.RequestMarketplaceMatch();
                SetStatus(Loc.T("audio_ready"), "audio_ready");
            }
            catch (Exception e)
            {
                MarketplacePlugin.Log.LogError("Audio fetch failed: " + e);
                if (_root && _selected == c)
                    SetStatus(Loc.T("audio_failed", e.GetBaseException().Message), "audio_failed");
            }
            finally { _fetching.Remove(c.Id); }
        }

        private void RemoveSelected()
        {
            var c = _selected;
            if (c == null) return;
            _m.Confirm(Loc.T("remove_confirm", c.Title, c.Artist), async () =>
            {
                try
                {
                    await MarketplacePlugin.Api.DeleteAsync(c.Id);
                    MarketplacePlugin.Session.ForgetPublishedChart(c.Id);
                    Search();
                }
                catch (Exception e)
                {
                    if (_root) SetStatus(Loc.T("remove_failed", e.GetBaseException().Message));
                }
            });
        }

        public static void OpenFolder()
        {
            string folder = CustomSongsApi.SongsFolder;
            if (!string.IsNullOrEmpty(folder)) Application.OpenURL("file:///" + folder.Replace('\\', '/'));
        }
    }
}


