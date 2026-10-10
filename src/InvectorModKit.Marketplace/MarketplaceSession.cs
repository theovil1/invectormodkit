using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InvectorModKit.Marketplace.Client;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InvectorModKit.Marketplace
{
    /// <summary>
    /// The player's marketplace session and publications, kept in BepInEx/config/InvectorModKit/marketplace.json
    /// (never in the game's save): token checked on start, Steam sign-in page when an action needs it.
    /// </summary>
    internal sealed class MarketplaceSession
    {
        private readonly MarketplaceApi _api;
        private readonly string _file;
        private readonly JObject _state;

        public Session Current { get; private set; }
        public bool SignedIn => Current != null;
        /// <summary>Set while waiting for the player to sign in on Steam's page.</summary>
        public bool SigningIn { get; private set; }

        public MarketplaceSession(MarketplaceApi api, string configDir)
        {
            _api = api;
            _file = Path.Combine(configDir, "marketplace.json");
            try { _state = File.Exists(_file) ? JObject.Parse(File.ReadAllText(_file)) : new JObject(); }
            catch (Exception) { _state = new JObject(); }
        }

        /// <summary>Restores the saved session if the server still accepts it.</summary>
        public async Task RestoreAsync()
        {
            string token = (string)_state["token"];
            if (string.IsNullOrEmpty(token)) return;
            _api.Token = token;
            try
            {
                Current = await _api.MeAsync();
            }
            catch (MarketplaceException e) when (e.Status == 401)
            {
                _api.Token = null;
                _state.Remove("token");
                Save();
            }
        }

        /// <summary>Opens Steam's sign-in page and waits (10 minutes at most) for the player to confirm.</summary>
        public async Task<Session> SignInAsync()
        {
            if (SignedIn) return Current;
            SigningIn = true;
            try
            {
                var login = _api.StartBrowserLogin();
                Application.OpenURL(login.Url);
                using (var giveUp = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
                    Current = await _api.WaitForBrowserLoginAsync(login, TimeSpan.FromSeconds(2), giveUp.Token);
                _state["token"] = Current.Token;
                Save();
                MarketplacePlugin.Log.LogInfo($"Signed in to the marketplace as {Current.Name} ({Current.Role})");
                return Current;
            }
            finally
            {
                SigningIn = false;
            }
        }

        /// <summary>The marketplace chart id this custom song was published as, or null.</summary>
        public string PublishedChart(string songId)
        {
            var record = _state["published"]?[songId];
            return record is JObject o ? (string)o["chart"] : (string)record; // older records: just the id
        }

        /// <summary>The video link this custom song was last published with, or null.</summary>
        public string PublishedLink(string songId) => (string)(_state["published"]?[songId] as JObject)?["link"];

        /// <summary>Is this marketplace chart one of mine (published from this game)?</summary>
        public bool IsPublishedByMe(string chartId) =>
            _state["published"] is JObject published && published.Properties().Any(p => (p.Value is JObject o ? (string)o["chart"] : (string)p.Value) == chartId);

        public void RememberPublished(string songId, string chartId, string link)
        {
            if (!(_state["published"] is JObject published)) _state["published"] = published = new JObject();
            published[songId] = new JObject { ["chart"] = chartId, ["link"] = link };
            Save();
        }

        public void ForgetPublished(string songId)
        {
            (_state["published"] as JObject)?.Remove(songId);
            Save();
        }

        /// <summary>Forgets publications whose chart is no longer on the server (withdrawn anywhere). True if any changed.</summary>
        public bool KeepOnlyPublished(ICollection<string> liveChartIds)
        {
            if (!(_state["published"] is JObject published)) return false;
            var gone = published.Properties().Where(p => !liveChartIds.Contains(p.Value is JObject o ? (string)o["chart"] : (string)p.Value)).Select(p => p.Name).ToList();
            foreach (var songId in gone) published.Remove(songId);
            if (gone.Count > 0) Save();
            return gone.Count > 0;
        }

        public void ForgetPublishedChart(string chartId)
        {
            if (!(_state["published"] is JObject published)) return;
            var gone = published.Properties().Where(p => (p.Value is JObject o ? (string)o["chart"] : (string)p.Value) == chartId).Select(p => p.Name).ToList();
            foreach (var songId in gone) published.Remove(songId);
            if (gone.Count > 0) Save();
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file));
            File.WriteAllText(_file, _state.ToString());
        }
    }
}
