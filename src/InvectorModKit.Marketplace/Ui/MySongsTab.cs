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

        public MySongsTab(MarketplaceModal m)
        {
            _m = m;
            _root = new GameObject("Mine", typeof(RectTransform));
            _root.transform.SetParent(m.Box, false);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;

            var help = GameUi.Text(_root.transform, "Help", m.Regular, 24, TextAnchor.MiddleLeft, GameUi.Muted);
            help.text = "Pour publier : copie le lien de la vidéo YouTube du morceau (Ctrl+C), puis « Publier ». Seul le chart est envoyé, jamais l'audio.";
            GameUi.Place(help, 0, 205, 1380, 60);
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
            _publish = m.MakeButton(_root.transform, "PUBLIER", 280, -255, 270, 64, () => Publish(_selected));
            _withdraw = m.MakeButton(_root.transform, "RETIRER LA PUBLICATION", 560, -255, 270, 64, () => Withdraw(_selected), 24);
            _folder = m.MakeButton(_root.transform, "DOSSIER CUSTOMSONGS", 280, -325, 270, 56, BrowseTab.OpenFolder, 24);
            _publish.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = _withdraw, selectOnDown = _folder };
            _withdraw.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = _publish };
            _folder.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = _publish };
            _root.SetActive(false);
        }

        public void Show()
        {
            _root.SetActive(true);
            Rebuild();
        }

        public void Hide() => _root.SetActive(false);

        public void SetStatus(string text) => _status.text = text;

        public void Tick()
        {
            var selected = GameUi.Selected;
            var row = _rows.FirstOrDefault(r => r.Root == selected);
            if (row != null) MarketplaceModal.KeepVisible(_scroll, (RectTransform)row.Root.transform);
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
            _empty.text = _songs.Count == 0 ? "Aucun morceau custom prêt. Dépose des mp3 dans le dossier CustomSongs." : "";
            if (_rows.Count > 0) GameUi.Select(_rows[0].Root);
            else ShowDetails(null, false);
        }

        private static string Note(LocalSong s) =>
            s.MarketplaceId != null ? "MARKETPLACE" : MarketplacePlugin.Session.PublishedChart(s.Id) != null ? "PUBLIÉ" : "";

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
            _info.text = $"{(int)s.Duration / 60}:{(int)s.Duration % 60:00}  ·  {Path.GetFileName(s.AudioPath)}\n" +
                         (s.MarketplaceId != null ? "Chart installé depuis le marketplace." : published != null ? "Publié sur le marketplace." : "Pas encore publié.");
            bool own = s.MarketplaceId == null;
            _publish.gameObject.SetActive(own);
            MarketplaceModal.SetLabel(_publish, published != null ? "METTRE À JOUR" : "PUBLIER");
            _publish.interactable = !_busy;
            _withdraw.gameObject.SetActive(own && published != null);
        }

        private async void Publish(LocalSong s)
        {
            if (s == null || _busy || s.MarketplaceId != null) return;
            string link = YoutubeLink.Normalize(GUIUtility.systemCopyBuffer);
            if (link == null)
            {
                _status.text = "Copie d'abord le lien de la vidéo YouTube de ce morceau (Ctrl+C dans ton navigateur), puis recommence.";
                return;
            }
            if (!await _m.EnsureSignedIn() || !_root) return;
            _m.Confirm($"Publier « {s.Title} » de {s.Artist} ?\nVidéo : {link}\nSeul le chart est envoyé, pas l'audio.", async () =>
            {
                _busy = true;
                try
                {
                    _status.text = "Analyse de l'audio (empreinte)…";
                    string fingerprint = await Task.Run(() => CustomSongsApi.FingerprintJson(s.Id));
                    string chart = File.ReadAllText(s.ChartPath);
                    _status.text = "Envoi…";
                    var published = await MarketplacePlugin.Api.PublishAsync(new PublishRequest
                    {
                        Title = s.Title, Artist = s.Artist, YoutubeUrl = link, Duration = s.Duration, ChartJson = chart, FingerprintJson = fingerprint,
                    });
                    MarketplacePlugin.Session.RememberPublished(s.Id, published.Id);
                    if (!_root) return;
                    _status.text = published.Version > 1 ? $"Mis à jour (version {published.Version})." : "Publié ! Les autres joueurs peuvent maintenant l'installer.";
                    RefreshNotes();
                }
                catch (Exception e)
                {
                    if (_root) _status.text = "Publication impossible : " + e.GetBaseException().Message;
                }
                finally
                {
                    _busy = false;
                    if (_root) ShowDetails(s, keepStatus: true);
                }
            });
        }

        private void Withdraw(LocalSong s)
        {
            string chartId = s == null ? null : MarketplacePlugin.Session.PublishedChart(s.Id);
            if (chartId == null) return;
            _m.Confirm($"Retirer « {s.Title} » du marketplace ?\nLes joueurs qui l'ont installé le gardent.", async () =>
            {
                try
                {
                    await MarketplacePlugin.Api.DeleteAsync(chartId);
                }
                catch (MarketplaceException e) when (e.Status == 404) { }
                catch (Exception e)
                {
                    if (_root) _status.text = "Impossible de retirer : " + e.GetBaseException().Message;
                    return;
                }
                MarketplacePlugin.Session.ForgetPublished(s.Id);
                if (!_root) return;
                _status.text = "Retiré du marketplace.";
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
