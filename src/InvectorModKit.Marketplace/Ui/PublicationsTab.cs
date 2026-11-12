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
    /// "My publications": the charts the player published, as the server lists them, each with its song on this PC.
    /// The sheet gives installs, ranked players and reports, says when the chart changed since its published version
    /// (PUBLISH VERSION N), and changes the video link or withdraws the publication.
    /// </summary>
    internal sealed class PublicationsTab : ITab
    {
        private const float ListWidth = 840, Gap = 48;

        private sealed class Pub
        {
            public ChartSummary Chart;
            public LocalSong Song;
            public PublicationView View;
        }

        private readonly MarketplaceScreen _s;
        private readonly RectTransform _root;
        private readonly ScrollRect _scroll;
        private readonly RectTransform _content;
        private readonly Text _empty;
        private readonly ButtonWithEvents _action;
        private readonly Sheet _sheet;
        private readonly List<SongRow> _rows = new List<SongRow>();
        private readonly List<Pub> _pubs = new List<Pub>();
        private readonly Dictionary<string, string> _hashes = new Dictionary<string, string>();

        private Pub _selected;
        private int _generation;
        private bool _signInAction, _excerptStopped, _mainEnabled = true;
        private string _publishing, _publishStep, _status, _excerptFor, _mainLabel, _videoTitle;
        private float _nextRefresh, _selectedAt;
        private GameObject _lastRow;

        public bool Typing => false;
        public GameObject FirstItem => _rows.Count > 0 ? _rows[0].Root : _action.gameObject.activeInHierarchy ? _action.gameObject : null;
        public GameObject LastRow => _lastRow ? _lastRow : FirstItem;

        public PublicationsTab(MarketplaceScreen screen)
        {
            _s = screen;
            _root = new GameObject("Publications", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(screen.Body, false);
            UiKit.Fill(_root);

            (_scroll, _content) = MarketplaceScreen.MakeList(_root);
            var listRt = (RectTransform)_scroll.transform;
            listRt.anchorMin = new Vector2(0, 0);
            listRt.anchorMax = new Vector2(0, 1);
            listRt.pivot = new Vector2(0, 1);
            listRt.offsetMin = Vector2.zero;
            listRt.offsetMax = new Vector2(ListWidth, 0);
            _empty = UiKit.Text(_scroll.transform, "Empty", TextRole.Muted);
            _empty.alignment = TextAnchor.MiddleCenter;
            UiKit.Fill(_empty, 40, 40, 40, 160);
            _action = UiKit.Button(_scroll.transform, "", ButtonStyle.Main, ListAction);
            GameUi.Place(_action, 0, -80, 360, 70);
            _action.gameObject.SetActive(false);

            _sheet = new Sheet(_root, Main, Listen);
            var sheetRt = _sheet.Root;
            sheetRt.anchorMin = Vector2.zero;
            sheetRt.anchorMax = Vector2.one;
            sheetRt.offsetMin = new Vector2(ListWidth + Gap, 0);
            sheetRt.offsetMax = Vector2.zero;
            _sheet.SetVisible(false);
            _root.gameObject.SetActive(false);
        }

        // ----- tab

        public void Show()
        {
            _root.gameObject.SetActive(true);
            _mainLabel = null;
            UpdateKeys();
            Load();
        }

        public void Hide()
        {
            StopExcerpt();
            _root.gameObject.SetActive(false);
        }

        public void SetStatus(string text) { _status = text; RefreshSheet(); }

        public void Search() { }

        public void Listen()
        {
            var p = _selected;
            if (p == null) return;
            if (_s.Player.Playing == p.Chart.Id || _excerptFor == p.Chart.Id && !_excerptStopped) { StopExcerpt(); _excerptStopped = true; _sheet.SetExcerpt(""); }
            else { _excerptStopped = false; PlayExcerpt(p); }
        }

        public void Tick()
        {
            var selected = GameUi.Selected;
            var row = _rows.FirstOrDefault(r => r.Root == selected);
            if (row != null) { MarketplaceScreen.KeepVisible(_scroll, (RectTransform)row.Root.transform); _lastRow = row.Root; }
            if (_selected != null && !_excerptStopped && _excerptFor != _selected.Chart.Id && Time.unscaledTime - _selectedAt >= ExcerptWindow.Dwell) PlayExcerpt(_selected);
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 2f; // the level editor may have saved the chart
                _hashes.Clear();
                foreach (var p in _pubs) p.View = PublicationState.For(Facts(p));
                RefreshRows();
                RefreshSheet();
            }
        }

        private void UpdateKeys()
        {
            var hints = new List<Hint>();
            if (_mainEnabled && _mainLabel != null) hints.Add(new Hint("Keyboard_Return", ScreenInput.ActionOk, _mainLabel));
            if (_selected != null) hints.Add(new Hint("Keyboard_Space", ScreenInput.ActionListen, Loc.T("hint_listen")));
            hints.Add(new Hint("Keyboard_Tab", ScreenInput.ActionNext, Loc.T("hint_tabs")));
            hints.Add(new Hint("Keyboard_Escape", ScreenInput.ActionCancel, Loc.T("hint_back")));
            _s.Keys.Set(hints.ToArray());
        }

        // ----- loading

        /// <summary>
        /// The player's charts from the server, each with its song here; publications withdrawn elsewhere are forgotten.
        /// <paramref name="keepStatus"/>: after an action, its result stays on the sheet.
        /// </summary>
        private async void Load(bool keepStatus = false)
        {
            int generation = ++_generation;
            var session = MarketplacePlugin.Session;
            if (!session.SignedIn)
            {
                Fill(new List<Pub>(), Loc.T("sign_in_publications"), Loc.T("sign_in"), signIn: true);
                return;
            }
            if (_pubs.Count == 0) Fill(new List<Pub>(), Loc.T("searching"), null, signIn: false); // a reload keeps the list meanwhile
            try
            {
                var live = await MarketplacePlugin.Api.MyChartsAsync();
                if (!_root || generation != _generation) return;
                session.KeepOnlyPublished(new HashSet<string>(live.Select(c => c.Id)));
                var songs = CustomSongsApi.Songs().Where(s => s.Ready).ToList();
                PinPublished(songs, live);
                var pubs = live.Select(c => new Pub
                {
                    Chart = c,
                    Song = songs.FirstOrDefault(s => s.MarketplaceId == c.Id) ?? songs.FirstOrDefault(s => session.PublishedChart(s.Id) == c.Id),
                }).ToList();
                _hashes.Clear();
                foreach (var p in pubs) p.View = PublicationState.For(Facts(p));
                Fill(pubs, pubs.Count == 0 ? Loc.T("no_publications") : "", null, signIn: false, keepStatus);
            }
            catch (Exception e)
            {
                if (!_root || generation != _generation) return;
                Fill(new List<Pub>(), Loc.T("unreachable", e.GetBaseException().Message), Loc.T("retry"), signIn: false);
            }
        }

        private void ListAction()
        {
            if (!_signInAction) { Load(); return; }
            SignInThenLoad();
        }

        private async void SignInThenLoad()
        {
            if (await _s.EnsureSignedIn() && _root) Load();
        }

        /// <summary>
        /// Keeps my songs' marketplace source in step with my live publications: a song published before charts were
        /// pinned, or pinned to an earlier publication since withdrawn, points at the live chart when its note counts
        /// still match it (its leaderboard compares like with like); a song pinned to a publication of mine that no
        /// longer exists is unpinned.
        /// </summary>
        private static void PinPublished(List<LocalSong> songs, List<ChartSummary> live)
        {
            var me = MarketplacePlugin.Session.Current;
            foreach (var s in songs)
            {
                if (s.MarketplaceId != null && !Publishing.IsOwn(s)) continue; // someone else's chart
                string id = MarketplacePlugin.Session.PublishedChart(s.Id);
                var c = id == null ? null : live.FirstOrDefault(x => x.Id == id);
                try
                {
                    if (c != null)
                    {
                        if (s.MarketplaceId == c.Id && s.MarketplaceVersion == c.Version) continue;
                        if (!NotesMatch(s, c)) continue;
                        Publishing.Pin(s, c.Id, c.Version, c.Author);
                    }
                    else if (s.MarketplaceId != null && me != null && s.MarketplaceAuthor == me.Name && live.All(x => x.Id != s.MarketplaceId))
                        Publishing.Pin(s, null, 0, null);
                }
                catch (Exception e) { MarketplacePlugin.Log.LogWarning($"Could not update the marketplace source of {s.Title}: {e.Message}"); }
            }
        }

        private static bool NotesMatch(LocalSong s, ChartSummary c) =>
            s.Notes.Length == 4 && s.Notes[0] == c.Notes.Casual && s.Notes[1] == c.Notes.Normal && s.Notes[2] == c.Notes.Hard && s.Notes[3] == c.Notes.Ultra;

        private PublicationFacts Facts(Pub p)
        {
            var f = new PublicationFacts
            {
                ServerVersion = p.Chart.Version, Hidden = p.Chart.Hidden, Reports = p.Chart.Reports ?? 0,
                LocalSongFound = p.Song != null && File.Exists(p.Song.ChartPath), Publishing = _publishing == p.Chart.Id,
            };
            if (!f.LocalSongFound) return f;
            f.LocalHash = Hash(p.Song);
            f.PublishedHash = MarketplacePlugin.Session.PublishedHash(p.Song.Id);
            f.NotesMatch = NotesMatch(p.Song, p.Chart);
            // Published before hashes were kept: while its notes still match, this chart is the published one.
            if (f.PublishedHash == null && f.NotesMatch && f.LocalHash != null && MarketplacePlugin.Session.PublishedChart(p.Song.Id) == p.Chart.Id)
            {
                MarketplacePlugin.Session.RememberHash(p.Song.Id, f.LocalHash);
                f.PublishedHash = f.LocalHash;
            }
            return f;
        }

        private string Hash(LocalSong s)
        {
            if (_hashes.TryGetValue(s.Id, out var known)) return known;
            string hash;
            try { hash = ChartContent.Hash(File.ReadAllText(s.ChartPath)); }
            catch (Exception) { hash = null; }
            return _hashes[s.Id] = hash;
        }

        // ----- the list

        private void Fill(List<Pub> pubs, string empty, string action, bool signIn, bool keepStatus = false)
        {
            string status = _status;
            string keep = _selected?.Chart.Id;
            foreach (var r in _rows) UnityEngine.Object.Destroy(r.Root);
            _rows.Clear();
            _pubs.Clear();
            _pubs.AddRange(pubs);
            _empty.text = empty ?? "";
            _signInAction = signIn;
            _action.gameObject.SetActive(action != null);
            if (action != null) UiKit.SetLabel(_action, action);
            foreach (var pub in _pubs)
            {
                var p = pub;
                var row = _s.MakeRow(_content, Main, () => Select(p));
                row.Set(p.Chart.Title, RowLine(p), "", p.Chart.Duration, p.Song?.Cover);
                if (p.Song?.Cover == null) Thumbnails.Get(p.Chart.YoutubeId, sprite => { if (row.Root) row.Set(p.Chart.Title, RowLine(p), "", p.Chart.Duration, sprite); });
                _rows.Add(row);
            }
            Wire();
            int again = _pubs.FindIndex(p => p.Chart.Id == keep);
            if (again >= 0) { _selected = _pubs[again]; ShowSheet(_selected, keepStatus: true); }
            else ShowSheet(null, keepStatus: false);
            if (keepStatus) { _status = status; RefreshSheet(); }
            if (_root.gameObject.activeInHierarchy && _rows.Count > 0)
            {
                GameUi.Select(_rows[Math.Max(0, again)].Root);
                if (again < 0 && keepStatus) { ShowSheet(_pubs[0], keepStatus: true); _status = status; RefreshSheet(); }
            }
            else if (_root.gameObject.activeInHierarchy && _action.gameObject.activeSelf) GameUi.Select(_action.gameObject);
        }

        private static string State(Pub p)
        {
            switch (p.View?.Row ?? PublicationRow.UpToDate)
            {
                case PublicationRow.Hidden: return Loc.T("pub_hidden", p.Chart.Reports ?? 0);
                case PublicationRow.NotOnThisPc: return Loc.T("pub_not_on_pc");
                case PublicationRow.Changed: return Loc.T("pub_changed");
                default: return Loc.T("pub_up_to_date");
            }
        }

        private static string RowLine(Pub p)
        {
            string line = Loc.T("pub_row", p.Chart.Version, State(p)) + "  ·  " + Loc.T("pub_installs", p.Chart.Downloads);
            return p.Chart.Players == null ? line : line + "  ·  " + Loc.T("pub_players", p.Chart.Players.Value);
        }

        private void RefreshRows()
        {
            for (int i = 0; i < _rows.Count && i < _pubs.Count; i++)
                if (_rows[i].Artist) _rows[i].Artist.text = RowLine(_pubs[i]);
        }

        private void Wire()
        {
            MarketplaceScreen.ChainNavigation(_rows.Select(r => (Selectable)r.Button).ToList(), _sheet.Main);
            WireSheet();
        }

        private void WireSheet()
        {
            var actions = _sheet.ActionButtons();
            var more = _sheet.MoreButtons();
            Selectable back = _lastRow ? _lastRow.GetComponent<Selectable>() : _rows.Count > 0 ? _rows[0].Button : null;
            _sheet.CoverButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = back, selectOnDown = more.Count > 0 ? more[0] : _sheet.Main };
            Chain(more, back, _sheet.CoverButton, _sheet.Main);
            _sheet.Main.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit, selectOnLeft = back,
                selectOnUp = more.Count > 0 ? more[0] : _sheet.CoverButton, selectOnDown = actions.Count > 0 ? actions[0] : null,
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

        // ----- the selected publication

        private void Select(Pub p)
        {
            if (_selected == p) return;
            StopExcerpt();
            _excerptStopped = false;
            _selectedAt = Time.unscaledTime;
            ShowSheet(p, keepStatus: false);
        }

        private void ShowSheet(Pub p, bool keepStatus)
        {
            _selected = p;
            if (!keepStatus) _status = null;
            _sheet.SetMoreOpen(false);
            _sheet.SetVisible(p != null);
            if (p == null) { _mainLabel = null; UpdateKeys(); return; }
            _sheet.SetExcerpt("");
            _videoTitle = null;
            _sheet.SetSong(p.Chart.Title, p.Chart.Artist, p.Song?.Cover, Info(p));
            if (p.Song?.Cover == null) Thumbnails.Get(p.Chart.YoutubeId, sprite => { if (_selected == p) _sheet.SetCover(sprite); });
            VideoInfo.Get(p.Chart.YoutubeId, (title, channel) => { if (_selected == p) { _videoTitle = title; _sheet.SetInfo(Info(p)); } });
            RefreshSheet();
        }

        private string Info(Pub p)
        {
            var c = p.Chart;
            var stats = new List<string> { Loc.T("pub_installs", c.Downloads) };
            if (c.Players != null) stats.Add(Loc.T("pub_players", c.Players.Value));
            if (c.Reports != null) stats.Add(Loc.T("pub_reports", c.Reports.Value));
            string video = _videoTitle != null ? Loc.T("quoted", _videoTitle) : c.YoutubeUrl;
            return Loc.T("pub_row", c.Version, State(p)) + "\n" + string.Join("  ·  ", stats) + "\n" + video;
        }

        private void RefreshSheet()
        {
            var p = _selected;
            if (p == null) return;
            var b = p.View?.Main ?? PublicationState.For(Facts(p)).Main;
            string label = Loc.T(b.LabelKey, b.LabelArgs);
            string sub = _publishing == p.Chart.Id && _publishStep != null ? Loc.T(_publishStep) : b.SubKey == null ? null : Loc.T(b.SubKey, b.SubArgs);
            _sheet.SetInfo(Info(p));
            _sheet.SetMain(b, label, sub);
            _sheet.SetNote(_status);
            _sheet.SetMore((Loc.T("withdraw_more"), () => Withdraw(p), true));
            _sheet.SetActions((Loc.T("watch_video"), () => Application.OpenURL(p.Chart.YoutubeUrl), false),
                              (Loc.T("change_link"), () => ChangeLink(p), false));
            if (label != _mainLabel || b.Enabled != _mainEnabled)
            {
                _mainLabel = label;
                _mainEnabled = b.Enabled;
                UpdateKeys();
            }
            WireSheet();
        }

        // ----- actions

        /// <summary>PUBLISH VERSION N: the local chart goes out as the next version of this chart (same video).</summary>
        private void Main()
        {
            var p = _selected;
            if (p == null || p.Song == null || _publishing != null) return;
            if (!(p.View ?? PublicationState.For(Facts(p))).Main.Enabled) return;
            _publishing = p.Chart.Id;
            _publishStep = null;
            _status = null;
            p.View = PublicationState.For(Facts(p));
            RefreshSheet();
            Publishing.Publish(_s, p.Song, p.Chart.YoutubeUrl,
                step => { _publishStep = step; if (_root) RefreshSheet(); },
                published =>
                {
                    _publishing = null;
                    if (!_root) return;
                    _status = Loc.T("updated", published.Version);
                    Load(keepStatus: true);
                },
                why =>
                {
                    _publishing = null;
                    if (!_root) return;
                    if (why != null) _status = why;
                    p.View = PublicationState.For(Facts(p));
                    RefreshSheet();
                });
        }

        private void ChangeLink(Pub p)
        {
            _s.Prompt(Loc.T("change_link"), Loc.T("link_hint"), p.Chart.YoutubeUrl,
                text => YoutubeLink.Normalize(text) == null ? Loc.T("link_invalid") : null,
                async text =>
                {
                    string link = YoutubeLink.Normalize(text);
                    try
                    {
                        await MarketplacePlugin.Api.ChangeVideoAsync(p.Chart.Id, link);
                        var session = MarketplacePlugin.Session;
                        if (p.Song != null && session.PublishedChart(p.Song.Id) == p.Chart.Id)
                            session.RememberPublished(p.Song.Id, p.Chart.Id, link, session.PublishedHash(p.Song.Id));
                        if (!_root) return;
                        _status = Loc.T("link_changed");
                        Load(keepStatus: true);
                    }
                    catch (MarketplaceException e) when (e.Status == 404 || e.Status == 405)
                    {
                        if (_root) SetStatus(Loc.T("server_too_old"));
                    }
                    catch (Exception e)
                    {
                        if (_root) SetStatus(Loc.T("link_change_failed", e.GetBaseException().Message));
                    }
                });
        }

        private void Withdraw(Pub p)
        {
            _s.Confirm(Loc.T("withdraw_confirm", p.Chart.Title), async () =>
            {
                try { await MarketplacePlugin.Api.DeleteAsync(p.Chart.Id); }
                catch (MarketplaceException e) when (e.Status == 404) { }
                catch (Exception e)
                {
                    if (_root) SetStatus(Loc.T("remove_failed", e.GetBaseException().Message));
                    return;
                }
                MarketplacePlugin.Session.ForgetPublishedChart(p.Chart.Id);
                if (p.Song != null && p.Song.MarketplaceId == p.Chart.Id)
                {
                    try { Publishing.Pin(p.Song, null, 0, null); }
                    catch (Exception e) { MarketplacePlugin.Log.LogWarning($"Could not unpin {p.Song.Title}: {e.Message}"); }
                }
                if (!_root) return;
                _status = Loc.T("withdrawn");
                Load(keepStatus: true);
            });
        }

        // ----- excerpts

        private void PlayExcerpt(Pub p)
        {
            _excerptFor = p.Chart.Id;
            void Ready(AudioClip clip, double start, double end)
            {
                if (_selected != p || _excerptStopped || !_root.gameObject.activeInHierarchy) return;
                _s.Player.Play(p.Chart.Id, clip, start, end);
                _sheet.SetExcerpt(Loc.T("excerpt_playing"));
            }
            void Failed() { if (_selected == p) _sheet.SetExcerpt(Loc.T("excerpt_unavailable")); }
            if (p.Song != null && p.Song.PreviewPath != null) _s.Excerpts.GetLocal(p.Chart.Id, p.Song.PreviewPath, Ready, Failed);
            else { _sheet.SetExcerpt(Loc.T("excerpt_loading")); _s.Excerpts.Get(p.Chart, Ready, Failed); }
        }

        private void StopExcerpt()
        {
            _excerptFor = null;
            _s.Excerpts.Forget();
            _s.Player.Stop();
        }
    }
}
