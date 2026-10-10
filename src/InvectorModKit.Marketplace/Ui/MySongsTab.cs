using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using InvectorModKit.CustomSongs;
using InvectorModKit.Marketplace.Client;
using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>"Mes morceaux": your custom songs, to publish (with the link of their video) or withdraw.</summary>
    internal sealed class MySongsTab : ITab
    {
        private readonly MarketplaceModal _m;
        private readonly GameObject _root;
        private readonly ScrollRect _scroll;
        private readonly RectTransform _content;
        private readonly Text _title, _artist, _info, _status, _empty;
        private readonly Image _cover;
        private readonly ButtonWithEvents _publish, _withdraw, _folder;
        private readonly List<SongRow> _rows = new List<SongRow>();
        private List<LocalSong> _songs = new List<LocalSong>();
        private LocalSong _selected;
        private bool _busy;
        private float _nextRefresh;

        public bool Typing => false;
        public GameObject FirstItem => _rows.Count > 0 ? _rows[0].Root : _publish.gameObject;
        public GameObject LastRow => _lastRow ? _lastRow : FirstItem;
        private GameObject _lastRow;

        public MySongsTab(MarketplaceModal m)
        {
            _m = m;
            _root = new GameObject("Mine", typeof(RectTransform));
            _root.transform.SetParent(m.Box, false);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;

            var help = GameUi.Text(_root.transform, "Help", m.Regular, 22, TextAnchor.MiddleLeft, GameUi.Muted);
            help.text = Loc.T("mine_help");
            GameUi.Place(help, -310, 212, 780, 60);
            (_scroll, _content) = MarketplaceModal.MakeList(_root.transform, -310, -70, 780, 500);
            _empty = GameUi.Text(_scroll.transform, "Empty", m.Regular, 28, TextAnchor.MiddleCenter, GameUi.Muted);
            GameUi.Place(_empty, 0, 0, 700, 200);

            _cover = GameUi.Image(_root.transform, "Cover", Color.white);
            GameUi.Place(_cover, 250, 120, 200, 200);
            _title = GameUi.Text(_root.transform, "Title", m.Bold, 34, TextAnchor.UpperLeft, Color.white);
            GameUi.Place(_title, 535, 175, 330, 90);
            _artist = GameUi.Text(_root.transform, "Artist", m.Regular, 26, TextAnchor.UpperLeft, GameUi.Muted);
            GameUi.Place(_artist, 535, 90, 330, 70);
            _info = GameUi.Text(_root.transform, "Info", m.Regular, 24, TextAnchor.UpperLeft, GameUi.Muted);
            GameUi.Place(_info, 415, -60, 550, 120);
            _status = GameUi.Text(_root.transform, "Status", m.Regular, 24, TextAnchor.UpperLeft, Color.white);
            GameUi.Place(_status, 415, -170, 550, 100);
            _publish = m.MakeButton(_root.transform, Loc.T("publish"), 280, -255, 270, 64, () => Publish(_selected));
            _withdraw = m.MakeButton(_root.transform, Loc.T("withdraw"), 560, -255, 270, 64, () => Withdraw(_selected), 24);
            _folder = m.MakeButton(_root.transform, Loc.T("open_folder"), 280, -325, 270, 56, BrowseTab.OpenFolder, 24);
            _publish.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = _withdraw, selectOnDown = _folder };
            _withdraw.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _publish };
            _folder.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = _publish };
            _root.SetActive(false);
        }

        public void Show()
        {
            _root.SetActive(true);
            Rebuild();
            SyncWithServer();
        }

        /// <summary>Publications withdrawn elsewhere (another tab, another PC, an admin) stop showing as published.</summary>
        private async void SyncWithServer()
        {
            if (!MarketplacePlugin.Session.SignedIn) return;
            try
            {
                var live = await MarketplacePlugin.Api.MyChartsAsync();
                if (!_root) return;
                bool changed = MarketplacePlugin.Session.KeepOnlyPublished(new HashSet<string>(live.Select(c => c.Id)));
                _hidden = new HashSet<string>(live.Where(c => c.Hidden).Select(c => c.Id));
                changed |= PinPublished(live);
                RefreshNotes();
                if (changed && _selected != null) ShowDetails(_selected, keepStatus: true);
            }
            catch (Exception) { } // offline: keep what we know
        }

        private HashSet<string> _hidden = new HashSet<string>();

        /// <summary>
        /// Songs published before charts were pinned get their marketplace source now, when the local chart
        /// still has the published note counts (so its leaderboard compares like with like).
        /// </summary>
        private bool PinPublished(List<ChartSummary> live)
        {
            bool any = false;
            foreach (var s in _songs)
            {
                if (s.MarketplaceId != null) continue;
                string id = MarketplacePlugin.Session.PublishedChart(s.Id);
                var c = id == null ? null : live.FirstOrDefault(x => x.Id == id);
                if (c == null || s.Notes.Length != 4) continue;
                if (s.Notes[0] != c.Notes.Casual || s.Notes[1] != c.Notes.Normal || s.Notes[2] != c.Notes.Hard || s.Notes[3] != c.Notes.Ultra) continue;
                try
                {
                    Pin(s, c.Id, c.Version, c.Author);
                    any = true;
                }
                catch (Exception e) { MarketplacePlugin.Log.LogWarning($"Could not pin {s.Title}: {e.Message}"); }
            }
            return any;
        }

        /// <summary>The local chart becomes (or stops being, with a null id) the marketplace chart, with its leaderboard.</summary>
        private static void Pin(LocalSong s, string chartId, int version, string author)
        {
            CustomSongsApi.SetMarketplaceSource(s.Id, chartId, version, author);
            s.MarketplaceId = chartId;
            s.MarketplaceVersion = chartId == null ? 0 : version;
            s.MarketplaceAuthor = author;
            Scores.MarketplaceLeaderboard.Instance.Forget();
        }

        public void Hide() => _root.SetActive(false);

        public void SetStatus(string text) => _status.text = text;

        public void Tick()
        {
            var selected = GameUi.Selected;
            var row = _rows.FirstOrDefault(r => r.Root == selected);
            if (row != null) { MarketplaceModal.KeepVisible(_scroll, (RectTransform)row.Root.transform); _lastRow = row.Root; }
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 2f;
                if (_selected != null) ShowDetails(_selected, keepStatus: true);
            }
        }

        private void Rebuild()
        {
            foreach (var r in _rows) UnityEngine.Object.Destroy(r.Root);
            _rows.Clear();
            _songs = CustomSongsApi.Songs().Where(s => s.Ready).OrderBy(s => s.Artist).ThenBy(s => s.Title).ToList();
            foreach (var song in _songs)
            {
                var s = song;
                var row = _m.MakeRow(_content, () => GameUi.Select(_publish.gameObject), () => ShowDetails(s, keepStatus: false));
                row.Set(s.Title, s.Artist, Note(s), s.Duration, s.Cover);
                _rows.Add(row);
            }
            MarketplaceModal.ChainNavigation(_rows.Select(r => (Selectable)r.Button).ToList(), _publish);
            _empty.text = _songs.Count == 0 ? Loc.T("no_songs") : "";
            if (_rows.Count > 0) GameUi.Select(_rows[0].Root);
            else ShowDetails(null, false);
        }

        private string Note(LocalSong s)
        {
            if (!IsOwn(s)) return Loc.T("from_marketplace");
            string published = MarketplacePlugin.Session.PublishedChart(s.Id);
            return published == null ? "" : _hidden.Contains(published) ? Loc.T("hidden_note") : Loc.T("published");
        }

        private void ShowDetails(LocalSong s, bool keepStatus)
        {
            _selected = s;
            foreach (var go in new GameObject[] { _cover.gameObject, _title.gameObject, _artist.gameObject, _info.gameObject, _publish.gameObject, _withdraw.gameObject })
                go.SetActive(s != null);
            if (!keepStatus) _status.text = "";
            if (s == null) return;
            _title.text = s.Title;
            _artist.text = s.Artist;
            _cover.sprite = s.Cover;
            _cover.color = s.Cover ? Color.white : new Color(1, 1, 1, 0.1f);
            string published = MarketplacePlugin.Session.PublishedChart(s.Id);
            bool own = IsOwn(s);
            _info.text = $"{(int)s.Duration / 60}:{(int)s.Duration % 60:00}  ·  {Path.GetFileName(s.AudioPath)}\n" +
                         (!own ? Loc.T("installed_from", s.MarketplaceAuthor) : published != null ? Loc.T("published_long") : Loc.T("not_published"));
            _publish.gameObject.SetActive(own);
            MarketplaceModal.SetLabel(_publish, published != null ? Loc.T("update") : Loc.T("publish"));
            _publish.interactable = !_busy;
            _withdraw.gameObject.SetActive(own && published != null);
        }

        /// <summary>Charted by me: auto-generated, hand-made, or my own chart reinstalled from the marketplace.</summary>
        private static bool IsOwn(LocalSong s)
        {
            if (s.MarketplaceId == null) return true;
            var me = MarketplacePlugin.Session.Current;
            return MarketplacePlugin.Session.IsPublishedByMe(s.MarketplaceId) || (me != null && s.MarketplaceAuthor == me.Name);
        }

        private async void Publish(LocalSong s)
        {
            if (s == null || _busy || !IsOwn(s)) return;
            if (!await _m.EnsureSignedIn() || !_root) return;
            string initial = MarketplacePlugin.Session.PublishedLink(s.Id) ?? YoutubeLink.Normalize(GUIUtility.systemCopyBuffer) ?? "";
            _m.Prompt(Loc.T("link_question", s.Title),
                Loc.T("link_hint"),
                initial,
                text => YoutubeLink.Normalize(text) == null ? Loc.T("link_invalid") : null,
                text => Send(s, YoutubeLink.Normalize(text)));
        }

        private async void Send(LocalSong s, string link, bool acceptRules = false)
        {
            _busy = true;
            try
            {
                _status.text = Loc.T("fingerprinting");
                string fingerprint = await Task.Run(() => CustomSongsApi.FingerprintJson(s.Id));
                string chart = File.ReadAllText(s.ChartPath);
                _status.text = Loc.T("sending");
                var published = await MarketplacePlugin.Api.PublishAsync(new PublishRequest
                {
                    Title = s.Title, Artist = s.Artist, YoutubeUrl = link, Duration = s.Duration, ChartJson = chart, FingerprintJson = fingerprint,
                    AcceptRules = acceptRules,
                });
                MarketplacePlugin.Session.RememberPublished(s.Id, published.Id, link);
                try { Pin(s, published.Id, published.Version, published.Author); }
                catch (Exception e) { MarketplacePlugin.Log.LogWarning($"Published, but the local chart could not be pinned: {e.Message}"); }
                if (!_root) return;
                _status.text = published.Version > 1 ? Loc.T("updated", published.Version) : Loc.T("published_done");
                RefreshNotes();
            }
            catch (MarketplaceException e) when (e.Status == 428 && !acceptRules)
            {
                // First publication: the rules must be accepted once.
                if (_root) _status.text = "";
                _m.Confirm(Loc.T("rules_confirm"), () => Send(s, link, acceptRules: true));
            }
            catch (Exception e)
            {
                if (_root) _status.text = Loc.T("publish_failed", e.GetBaseException().Message);
            }
            finally
            {
                _busy = false;
                if (_root) ShowDetails(s, keepStatus: true);
            }
        }

        private void Withdraw(LocalSong s)
        {
            string chartId = s == null ? null : MarketplacePlugin.Session.PublishedChart(s.Id);
            if (chartId == null) return;
            _m.Confirm(Loc.T("withdraw_confirm", s.Title), async () =>
            {
                try
                {
                    await MarketplacePlugin.Api.DeleteAsync(chartId);
                }
                catch (MarketplaceException e) when (e.Status == 404) { }
                catch (Exception e)
                {
                    if (_root) _status.text = Loc.T("remove_failed", e.GetBaseException().Message);
                    return;
                }
                MarketplacePlugin.Session.ForgetPublished(s.Id);
                if (s.MarketplaceId == chartId)
                {
                    try { Pin(s, null, 0, null); }
                    catch (Exception e) { MarketplacePlugin.Log.LogWarning($"Could not unpin {s.Title}: {e.Message}"); }
                }
                if (!_root) return;
                _status.text = Loc.T("withdrawn");
                RefreshNotes();
                ShowDetails(s, keepStatus: true);
            });
        }

        private void RefreshNotes()
        {
            for (int i = 0; i < _rows.Count && i < _songs.Count; i++)
                if (_rows[i].Note) _rows[i].Note.text = Note(_songs[i]);
        }
    }
}
