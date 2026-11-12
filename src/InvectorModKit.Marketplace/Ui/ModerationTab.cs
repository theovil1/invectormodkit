using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InvectorModKit.Marketplace.Client;
using UnityEngine;
using UnityEngine.UI;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// "Moderation", for admins only: reported charts with their reports, hidden charts, every chart (searchable), and
    /// banned players. A chart's sheet hides or shows it again, clears its reports, deletes it or bans its author; a
    /// banned player's sheet lifts the ban. What cannot be undone asks first; nothing changes here until the server agreed.
    /// </summary>
    internal sealed class ModerationTab : ITab
    {
        private const float ListWidth = 840, Gap = 48;

        /// <summary>A row: a chart (reported or not) or a banned player.</summary>
        private sealed class Item
        {
            public AdminChart Chart;
            public ReportedChart Reported;
            public AdminUser Player;
            public string Key => Player != null ? "player:" + Player.SteamId : "chart:" + (Chart?.Id ?? Reported.Id);
        }

        private readonly MarketplaceScreen _s;
        private readonly RectTransform _root;
        private readonly ButtonWithEvents _search;
        private readonly ButtonWithEvents[] _chips = new ButtonWithEvents[4];
        private readonly ScrollRect _scroll;
        private readonly RectTransform _content;
        private readonly Text _empty, _reports;
        private readonly ButtonWithEvents _retry;
        private readonly Sheet _sheet;
        private readonly List<SongRow> _rows = new List<SongRow>();
        private readonly List<Item> _items = new List<Item>();

        private List<ReportedChart> _reported = new List<ReportedChart>();
        private List<AdminChart> _charts = new List<AdminChart>();
        private List<AdminUser> _banned = new List<AdminUser>();
        private ModerationShown _filter = ModerationShown.Reported;
        private string _query = "", _status, _mainLabel, _error;
        private bool _typing, _busy, _loaded, _mainEnabled = true;
        private float _searchAt = -1;
        private int _generation;
        private Item _selected;
        private GameObject _lastRow;

        public bool Typing => _typing;
        public GameObject FirstItem => _rows.Count > 0 ? _rows[0].Root : _chips[0].gameObject;
        public GameObject LastRow => _lastRow ? _lastRow : FirstItem;

        public ModerationTab(MarketplaceScreen screen)
        {
            _s = screen;
            _root = new GameObject("Moderation", typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(screen.Body, false);
            UiKit.Fill(_root);

            _search = UiKit.Button(_root, "", ButtonStyle.Secondary, StartTyping);
            UiKit.Label(_search).alignment = TextAnchor.MiddleLeft;
            UiKit.At(_search, 0, 0, ListWidth, 66);
            var chips = new GameObject("Filters", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            chips.SetParent(_root, false);
            UiKit.At(chips, 0, 80, ListWidth, 56);
            var layout = chips.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlWidth = layout.childControlHeight = layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            for (int i = 0; i < 4; i++)
            {
                var shown = (ModerationShown)i;
                _chips[i] = UiKit.Button(chips, "", ButtonStyle.Chip, () => SetFilter(shown));
            }

            (_scroll, _content) = MarketplaceScreen.MakeList(_root);
            var listRt = (RectTransform)_scroll.transform;
            listRt.anchorMin = new Vector2(0, 0);
            listRt.anchorMax = new Vector2(0, 1);
            listRt.pivot = new Vector2(0, 1);
            listRt.offsetMin = Vector2.zero;
            listRt.offsetMax = new Vector2(ListWidth, -150);
            _empty = UiKit.Text(_scroll.transform, "Empty", TextRole.Muted);
            _empty.alignment = TextAnchor.MiddleCenter;
            UiKit.Fill(_empty, 40, 40, 40, 160);
            _retry = UiKit.Button(_scroll.transform, Loc.T("retry"), ButtonStyle.Secondary, () => Load());
            GameUi.Place(_retry, 0, -80, 300, 62);
            _retry.gameObject.SetActive(false);

            _sheet = new Sheet(_root, Main, () => { });
            var sheetRt = _sheet.Root;
            sheetRt.anchorMin = Vector2.zero;
            sheetRt.anchorMax = Vector2.one;
            sheetRt.offsetMin = new Vector2(ListWidth + Gap, 0);
            sheetRt.offsetMax = Vector2.zero;
            _reports = UiKit.Text(_sheet.Middle, "Reports", TextRole.Small);
            _reports.color = Color.white;
            _reports.lineSpacing = 1.1f;
            UiKit.Fill(_reports);
            _sheet.SetVisible(false);

            UpdateChips();
            UpdateSearchLabel();
            _root.gameObject.SetActive(false);
        }

        // ----- tab

        public void Show()
        {
            _root.gameObject.SetActive(true);
            _mainLabel = null;
            UpdateKeys();
            if (!_loaded) Load();
            else Rebuild();
        }

        public void Hide()
        {
            StopTyping(search: false);
            _root.gameObject.SetActive(false);
        }

        public void SetStatus(string text) { _status = text; RefreshSheet(); }
        public void Listen() { }
        public void Search() { SetFilter(ModerationShown.All); StartTyping(); }

        public void Tick()
        {
            if (_typing) { ReadTyping(); return; }
            if (_searchAt > 0 && Time.unscaledTime >= _searchAt) { _searchAt = -1; LoadCharts(); }
            var selected = GameUi.Selected;
            var row = _rows.FirstOrDefault(r => r.Root == selected);
            if (row != null) { MarketplaceScreen.KeepVisible(_scroll, (RectTransform)row.Root.transform); _lastRow = row.Root; }
        }

        private void UpdateKeys()
        {
            var hints = new List<Hint>();
            if (_mainEnabled && _mainLabel != null) hints.Add(new Hint("Keyboard_Return", ScreenInput.ActionOk, _mainLabel));
            hints.Add(new Hint("Keyboard_F", ScreenInput.ActionSearch, Loc.T("hint_search")));
            hints.Add(new Hint("Keyboard_Tab", ScreenInput.ActionNext, Loc.T("hint_tabs")));
            hints.Add(new Hint("Keyboard_Escape", ScreenInput.ActionCancel, Loc.T("hint_back")));
            _s.Keys.Set(hints.ToArray());
        }

        // ----- loading

        /// <summary>Reported charts, every chart (for the hidden count and the search) and banned players, together.</summary>
        private async void Load(bool keepStatus = false)
        {
            int generation = ++_generation;
            _error = null;
            _retry.gameObject.SetActive(false);
            if (_items.Count == 0) _empty.text = Loc.T("searching");
            string status = keepStatus ? _status : null;
            try
            {
                var api = MarketplacePlugin.Api;
                var reported = api.ReportsAsync();
                var charts = api.AdminChartsAsync(_query);
                var banned = api.BannedAsync();
                await Task.WhenAll(reported, charts, banned);
                if (!_root || generation != _generation) return;
                _reported = reported.Result;
                _charts = charts.Result;
                _banned = banned.Result;
                _loaded = true;
            }
            catch (Exception e)
            {
                if (!_root || generation != _generation) return;
                _reported = new List<ReportedChart>();
                _charts = new List<AdminChart>();
                _banned = new List<AdminUser>();
                _error = Why(e);
                _retry.gameObject.SetActive(true);
            }
            UpdateChips();
            Rebuild();
            if (keepStatus) { _status = status; RefreshSheet(); }
        }

        private async void LoadCharts()
        {
            int generation = ++_generation;
            try
            {
                var charts = await MarketplacePlugin.Api.AdminChartsAsync(_query);
                if (!_root || generation != _generation) return;
                _charts = charts;
                Rebuild();
            }
            catch (Exception e)
            {
                if (!_root || generation != _generation) return;
                _error = Why(e);
                Rebuild();
            }
        }

        /// <summary>In the player's words: not an admin any more, a server from before this, or the server's error.</summary>
        private static string Why(Exception e)
        {
            var refused = e.GetBaseException() is MarketplaceException m ? ModerationState.Refused(m.Status) : null;
            return refused != null ? Loc.T(refused) : Loc.T("unreachable", e.GetBaseException().Message);
        }

        // ----- filters and search

        private void SetFilter(ModerationShown filter)
        {
            if (_filter == filter) return;
            _filter = filter;
            UpdateChips();
            Rebuild();
        }

        private void UpdateChips()
        {
            UiKit.SetLabel(_chips[0], Loc.T("mod_reported", _reported.Count));
            UiKit.SetLabel(_chips[1], Loc.T("mod_hidden_filter", _charts.Count(c => c.Hidden)));
            UiKit.SetLabel(_chips[2], Loc.T("mod_all"));
            UiKit.SetLabel(_chips[3], Loc.T("mod_banned_filter"));
            for (int i = 0; i < 4; i++) UiKit.SetSelected(_chips[i], (int)_filter == i);
            _search.gameObject.SetActive(_filter == ModerationShown.All);
        }

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
            if (search) { _searchAt = -1; LoadCharts(); }
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
            if (!changed) return;
            _searchAt = Time.unscaledTime + 0.4f;
            UpdateSearchLabel();
        }

        private void UpdateSearchLabel() =>
            UiKit.SetLabel(_search, _typing ? _query + "▌" : _query.Length > 0 ? Loc.T("search_query", _query.ToUpperInvariant()) : Loc.T("search_hint"));

        // ----- the list

        private void Rebuild()
        {
            if (!_root) return;
            string keep = _selected?.Key;
            foreach (var r in _rows) UnityEngine.Object.Destroy(r.Root);
            _rows.Clear();
            _items.Clear();
            switch (_filter)
            {
                case ModerationShown.Reported: _items.AddRange(_reported.Select(r => new Item { Reported = r, Chart = _charts.FirstOrDefault(c => c.Id == r.Id) })); break;
                case ModerationShown.Hidden: _items.AddRange(_charts.Where(c => c.Hidden).Select(c => new Item { Chart = c, Reported = _reported.FirstOrDefault(r => r.Id == c.Id) })); break;
                case ModerationShown.All: _items.AddRange(_charts.Select(c => new Item { Chart = c, Reported = _reported.FirstOrDefault(r => r.Id == c.Id) })); break;
                default: _items.AddRange(_banned.Select(u => new Item { Player = u })); break;
            }
            foreach (var item in _items)
            {
                var it = item;
                var row = _s.MakeRow(_content, Main, () => Select(it));
                if (it.Player != null) row.Set(it.Player.Name, Loc.T("mod_banned_since", Date(it.Player.BannedAt)), "", 0, null);
                else row.Set(Title(it), RowLine(it), Hidden(it) ? Loc.T("tag_hidden") : "", 0, null);
                _rows.Add(row);
            }
            _empty.text = _error ?? (_items.Count == 0 ? Loc.T("mod_nothing") : "");
            Wire();
            int again = _items.FindIndex(i => i.Key == keep);
            if (again >= 0) { _selected = _items[again]; ShowSheet(_selected, keepStatus: true); }
            else ShowSheet(null, keepStatus: false);
            if (_root.gameObject.activeInHierarchy && !_typing && _rows.Count > 0) GameUi.Select(_rows[Math.Max(0, again)].Root);
        }

        private static string Title(Item i) => i.Chart?.Title ?? i.Reported.Title;
        private static string Artist(Item i) => i.Chart?.Artist ?? i.Reported.Artist;
        private static string Author(Item i) => i.Chart?.Author ?? i.Reported.Author;
        private static string AuthorSteamId(Item i) => i.Chart?.AuthorSteamId ?? i.Reported.AuthorSteamId;
        private static string ChartId(Item i) => i.Chart?.Id ?? i.Reported.Id;
        private static bool Hidden(Item i) => i.Chart?.Hidden ?? i.Reported.Hidden;
        private static int Reports(Item i) => i.Reported?.Reports ?? i.Chart?.Reports ?? 0;

        private static string RowLine(Item i) =>
            Artist(i) + "  ·  " + Loc.T("by_author_short", Author(i)) + (Reports(i) > 0 ? "  ·  " + Loc.Count("pub_reports", Reports(i)) : "");

        private static string Date(long? ms) =>
            ms == null || ms <= 0 ? "?" : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).LocalDateTime.ToString("yyyy-MM-dd");

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
                    selectOnUp = _search.gameObject.activeSelf ? _search : null,
                    selectOnDown = first,
                };
            WireSheet();
        }

        private void WireSheet()
        {
            var actions = _sheet.ActionButtons();
            var more = _sheet.MoreButtons();
            Selectable back = _lastRow ? _lastRow.GetComponent<Selectable>() : _rows.Count > 0 ? _rows[0].Button : null;
            Chain(more, back, null, _sheet.Main);
            _sheet.Main.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit, selectOnLeft = back,
                selectOnUp = more.Count > 0 ? more[0] : null, selectOnDown = actions.Count > 0 ? actions[0] : null,
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

        // ----- the sheet

        private void Select(Item i)
        {
            if (_selected != null && i != null && _selected.Key == i.Key) return;
            ShowSheet(i, keepStatus: false);
        }

        private void ShowSheet(Item i, bool keepStatus)
        {
            _selected = i;
            if (!keepStatus) _status = null;
            _sheet.SetMoreOpen(false);
            _sheet.SetVisible(i != null);
            if (i == null) { _mainLabel = null; UpdateKeys(); return; }
            if (i.Player != null)
            {
                var u = i.Player;
                _sheet.SetSong(u.Name, "", null, Loc.T("mod_banned_since", Date(u.BannedAt)) + "\n" + Loc.Count("mod_player_charts", u.Charts) + "  ·  " + Loc.Count("mod_player_scores", u.Scores));
                _reports.text = "";
            }
            else
            {
                var lines = new List<string> { Loc.T("by_author_short", Author(i)) + "  ·  v" + (i.Chart?.Version ?? i.Reported.Version) };
                if (Hidden(i)) lines.Add(Loc.T("hidden_admin_note"));
                lines.Add((i.Chart != null ? Loc.Count("pub_installs", i.Chart.Downloads) + "  ·  " : "") + Loc.Count("pub_reports", Reports(i)));
                _sheet.SetSong(Title(i), Artist(i), null, string.Join("\n", lines));
                _reports.text = i.Reported == null ? "" : string.Join("\n", i.Reported.Details.Select(d => Loc.T("mod_report_line", Date(d.At), d.Name, d.Reason)));
            }
            RefreshSheet();
        }

        private void RefreshSheet()
        {
            var i = _selected;
            if (i == null) return;
            SheetButton b;
            if (i.Player != null)
            {
                b = ModerationState.ForPlayer(_busy);
                _sheet.SetMore();
                _sheet.SetActions();
            }
            else
            {
                b = ModerationState.ForChart(Hidden(i), _busy);
                _sheet.SetMore((Loc.T("mod_delete"), () => DeleteChart(i), true), (Loc.T("mod_ban_author"), () => BanAuthor(i), true));
                if (Reports(i) > 0) _sheet.SetActions((Loc.T("mod_clear"), () => ClearReports(i), false));
                else _sheet.SetActions();
            }
            string label = Loc.T(b.LabelKey);
            _sheet.SetMain(b, label, null);
            _sheet.SetNote(_status, warning: _status != null && _status != Loc.T("mod_done"));
            if (label != _mainLabel || b.Enabled != _mainEnabled)
            {
                _mainLabel = label;
                _mainEnabled = b.Enabled;
                UpdateKeys();
            }
            WireSheet();
        }

        // ----- actions (each asks first when it cannot be undone; the lists reload once the server agreed)

        private void Main()
        {
            var i = _selected;
            if (i == null || _busy) return;
            if (i.Player != null)
            {
                _s.Confirm(Loc.T("mod_confirm_lift", i.Player.Name), () => Act(() => MarketplacePlugin.Api.SetRoleAsync(i.Player.SteamId, "")));
                return;
            }
            if (Hidden(i)) Act(() => MarketplacePlugin.Api.ChartActionAsync(ChartId(i), "show"));
            else _s.Confirm(Loc.T("mod_confirm_hide", Title(i)), () => Act(() => MarketplacePlugin.Api.ChartActionAsync(ChartId(i), "hide")));
        }

        private void ClearReports(Item i) => Act(() => MarketplacePlugin.Api.ChartActionAsync(ChartId(i), "clear-reports"));

        private void DeleteChart(Item i) =>
            _s.Confirm(Loc.T("mod_confirm_delete", Title(i)), () => Act(() => MarketplacePlugin.Api.ChartActionAsync(ChartId(i), "delete")));

        private void BanAuthor(Item i) =>
            _s.Confirm(Loc.T("mod_confirm_ban", Author(i)), () => Act(() => MarketplacePlugin.Api.SetRoleAsync(AuthorSteamId(i), "banned")));

        private async void Act(Func<Task> call)
        {
            if (_busy) return;
            _busy = true;
            _status = null;
            RefreshSheet();
            try
            {
                await call();
                if (!_root) return;
                _status = Loc.T("mod_done");
                _busy = false;
                Load(keepStatus: true);
            }
            catch (Exception e)
            {
                _busy = false;
                if (!_root) return;
                _status = Why(e);
                RefreshSheet();
            }
        }
    }
}
