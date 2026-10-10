using System;
using System.Collections.Generic;
using System.Linq;
using InvectorModKit.CustomSongs;
using InvectorModKit.Marketplace.Client;
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
        private readonly ButtonWithEvents _search, _sort, _more, _install, _youtube, _folder, _remove;
        private readonly ScrollRect _scroll;
        private readonly RectTransform _content;
        private readonly Text _title, _artist, _info, _status, _empty;
        private readonly Image _cover, _audioPanel;
        private readonly Text _video;
        private readonly List<SongRow> _rows = new List<SongRow>();
        private readonly List<ChartSummary> _items = new List<ChartSummary>();

        private string _query = "";
        private ChartSort _sortMode = ChartSort.Recent;
        private int _page, _generation;
        private bool _typing;
        private float _searchAt = -1, _nextRefresh;
        private ChartSummary _selected;
        private HashSet<string> _installed = new HashSet<string>();

        public bool Typing => _typing;

        public BrowseTab(MarketplaceModal m)
        {
            _m = m;
            _root = new GameObject("Browse", typeof(RectTransform));
            _root.transform.SetParent(m.Box, false);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;

            _search = m.MakeButton(_root.transform, "", -420, 205, 560, 58, StartTyping, 26);
            _sort = m.MakeButton(_root.transform, "RÉCENTS", -20, 205, 200, 58, ToggleSort, 26);
            (_scroll, _content) = MarketplaceModal.MakeList(_root.transform, -310, -70, 780, 500);
            _empty = GameUi.Text(_scroll.transform, "Empty", m.Regular, 28, TextAnchor.MiddleCenter, GameUi.Muted);
            GameUi.Place(_empty, 0, 0, 700, 200);
            _more = m.MakeButton(_root.transform, "PLUS DE RÉSULTATS", -310, -350, 320, 44, () => Search(append: true), 24);

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
            GameUi.Place(_audioPanel, 415, -95, 560, 130);
            var audioHeader = GameUi.Text(_audioPanel.transform, "Header", m.Bold, 22, TextAnchor.UpperLeft, Color.white);
            audioHeader.text = "AUDIO : LA VIDÉO YOUTUBE LIÉE";
            GameUi.Place(audioHeader, 0, 42, 520, 30);
            _video = GameUi.Text(_audioPanel.transform, "Video", m.Regular, 22, TextAnchor.UpperLeft, Color.white);
            GameUi.Place(_video, 0, 3, 520, 50);
            var audioNote = GameUi.Text(_audioPanel.transform, "Note", m.Regular, 19, TextAnchor.UpperLeft, GameUi.Muted);
            audioNote.text = "Le chart est calé sur la bande-son de cette vidéo : c'est cette version du morceau qu'il te faut.";
            GameUi.Place(audioNote, 0, -42, 520, 44);

            _status = GameUi.Text(_root.transform, "Status", m.Regular, 21, TextAnchor.UpperLeft, Color.white);
            GameUi.Place(_status, 415, -190, 550, 56);
            _install = m.MakeButton(_root.transform, "INSTALLER", 280, -258, 270, 58, () => Primary(_selected));
            _youtube = m.MakeButton(_root.transform, "OUVRIR SUR YOUTUBE", 560, -258, 270, 58, () => { if (_selected != null) Application.OpenURL(_selected.YoutubeUrl); });
            _folder = m.MakeButton(_root.transform, "DOSSIER CUSTOMSONGS", 280, -324, 270, 52, OpenFolder, 24);
            _remove = m.MakeButton(_root.transform, "RETIRER", 560, -324, 270, 52, RemoveSelected, 24);
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

        public void SetStatus(string text) => _status.text = text;

        public void Tick()
        {
            if (_typing) ReadTyping();
            else if (Input.GetKeyDown(KeyCode.F)) StartTyping();
            if (_searchAt > 0 && Time.unscaledTime >= _searchAt) { _searchAt = -1; Search(); }

            var selected = GameUi.Selected;
            var row = _rows.FirstOrDefault(r => r.Root == selected);
            if (row != null) MarketplaceModal.KeepVisible(_scroll, (RectTransform)row.Root.transform);

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
            if (EventSystem.current) EventSystem.current.sendNavigationEvents = false;
            UpdateSearchLabel();
        }

        private void StopTyping(bool search)
        {
            if (!_typing) return;
            _typing = false;
            if (EventSystem.current) EventSystem.current.sendNavigationEvents = true;
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
            MarketplaceModal.SetLabel(_search, _typing ? _query + "▌" : _query.Length > 0 ? "RECHERCHE : " + _query.ToUpperInvariant() : "F  RECHERCHER UN TITRE, UN ARTISTE, UN AUTEUR");

        private void ToggleSort()
        {
            _sortMode = _sortMode == ChartSort.Recent ? ChartSort.Popular : ChartSort.Recent;
            MarketplaceModal.SetLabel(_sort, _sortMode == ChartSort.Recent ? "RÉCENTS" : "POPULAIRES");
            Search();
        }

        private async void Search(bool append = false)
        {
            int generation = ++_generation;
            int page = append ? _page + 1 : 0;
            _empty.text = append ? "" : "Recherche…";
            try
            {
                var result = await MarketplacePlugin.Api.SearchAsync(_query, _sortMode, page);
                if (!_root || generation != _generation) return;
                if (!append) _items.Clear();
                _items.AddRange(result.Items);
                _page = page;
                _more.gameObject.SetActive(result.More);
                Rebuild();
                _empty.text = _items.Count == 0 ? (_query.Length > 0 ? "Aucun chart ne correspond." : "Aucun chart publié pour l'instant.") : "";
            }
            catch (Exception e)
            {
                if (!_root || generation != _generation) return;
                _empty.text = "Marketplace injoignable : " + e.GetBaseException().Message;
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
                row.Set(chart.Title, $"{chart.Artist}  ·  par {chart.Author}", Note(chart), chart.Duration, null);
                Thumbnails.Get(chart.YoutubeId, sprite => { if (row.Root) row.Set(chart.Title, $"{chart.Artist}  ·  par {chart.Author}", Note(chart), chart.Duration, sprite); });
                _rows.Add(row);
            }
            MarketplaceModal.ChainNavigation(_rows.Select(r => (Selectable)r.Button).ToList(), _install);
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
            MarketplacePlugin.Session.IsPublishedByMe(c.Id) ? "TON CHART" : _installed.Contains(c.Id) ? "INSTALLÉ" : MarketplacePlugin.Installs.IsDownloaded(c.Id) ? "EN ATTENTE" : "";

        // ----- details and actions

        private void ShowDetails(ChartSummary c)
        {
            _selected = c;
            bool any = c != null;
            foreach (var go in new GameObject[] { _cover.gameObject, _title.gameObject, _artist.gameObject, _info.gameObject, _audioPanel.gameObject, _install.gameObject, _youtube.gameObject, _remove.gameObject })
                go.SetActive(any);
            if (!any) { _status.text = ""; return; }
            _title.text = c.Title;
            _artist.text = c.Artist;
            _info.text = $"Chart de {c.Author}  ·  version {c.Version}  ·  {c.Bpm:0.#} BPM  ·  {(int)c.Duration / 60}:{(int)c.Duration % 60:00}\n" +
                         $"Notes : Casual {c.Notes.Casual}  ·  Normal {c.Notes.Normal}  ·  Hard {c.Notes.Hard}  ·  Ultra {c.Notes.Ultra}\n" +
                         $"{c.Downloads} téléchargement{(c.Downloads > 1 ? "s" : "")}";
            _video.text = "youtu.be/" + c.YoutubeId;
            VideoInfo.Get(c.YoutubeId, (title, channel) =>
            {
                if (_selected == c && _video) _video.text = $"« {title} »" + (string.IsNullOrEmpty(channel) ? "" : $"  ·  {channel}");
            });
            _cover.sprite = null;
            _cover.color = new Color(1, 1, 1, 0.1f);
            Thumbnails.Get(c.YoutubeId, sprite => { if (_selected == c && _cover) { _cover.sprite = sprite; _cover.color = Color.white; } });
            _status.text = "";
            UpdateActions(c);
        }

        private void UpdateActions(ChartSummary c)
        {
            bool mine = MarketplacePlugin.Session.IsPublishedByMe(c.Id);
            bool installed = _installed.Contains(c.Id), waiting = !mine && !installed && MarketplacePlugin.Installs.IsDownloaded(c.Id);
            MarketplaceModal.SetLabel(_install, mine ? "TON CHART" : installed ? "INSTALLÉ" : waiting ? "EN ATTENTE DE L'AUDIO" : "INSTALLER");
            _install.interactable = !mine && !installed && !waiting;
            if (waiting && string.IsNullOrEmpty(_status.text))
                _status.text = "Dépose ce morceau (la version de la vidéo liée) dans le dossier CustomSongs : le chart s'y calera tout seul.";
            if (installed && _status.text.StartsWith("Téléchargé")) _status.text = "Installé ! Il est dans la sélection, section Custom Songs.";
            var session = MarketplacePlugin.Session.Current;
            _remove.gameObject.SetActive(session != null && (session.Role == "admin" || session.Name == c.Author));
        }

        private void Primary(ChartSummary c)
        {
            if (c == null) return;
            if (_installed.Contains(c.Id) || MarketplacePlugin.Session.IsPublishedByMe(c.Id)) return;
            if (MarketplacePlugin.Installs.IsDownloaded(c.Id)) { Application.OpenURL(c.YoutubeUrl); return; }
            Install(c);
        }

        private async void Install(ChartSummary c)
        {
            _status.text = "Téléchargement du chart…";
            try
            {
                var download = await MarketplacePlugin.Api.DownloadAsync(c.Id);
                MarketplacePlugin.Installs.Install(download);
                CustomSongsApi.RequestMarketplaceMatch();
                if (!_root) return;
                _status.text = "Téléchargé. Je cherche ce morceau parmi tes fichiers… S'il n'y est pas, dépose-le (la version de la vidéo liée) dans CustomSongs.";
                RefreshLocalState();
            }
            catch (Exception e)
            {
                if (_root) _status.text = "Échec du téléchargement : " + e.GetBaseException().Message;
            }
        }

        private void RemoveSelected()
        {
            var c = _selected;
            if (c == null) return;
            _m.Confirm($"Retirer « {c.Title} » ({c.Artist}) du marketplace ?\nLes joueurs qui l'ont installé le gardent.", async () =>
            {
                try
                {
                    await MarketplacePlugin.Api.DeleteAsync(c.Id);
                    MarketplacePlugin.Session.ForgetPublishedChart(c.Id);
                    Search();
                }
                catch (Exception e)
                {
                    if (_root) _status.text = "Impossible de retirer : " + e.GetBaseException().Message;
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
