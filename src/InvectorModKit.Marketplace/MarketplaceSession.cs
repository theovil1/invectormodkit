using System;
using System.IO;
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
        public string PublishedChart(string songId) => (string)_state["published"]?[songId];

        public void RememberPublished(string songId, string chartId)
        {
            if (!(_state["published"] is JObject published)) _state["published"] = published = new JObject();
            published[songId] = chartId;
            Save();
        }

        public void ForgetPublished(string songId)
        {
            (_state["published"] as JObject)?.Remove(songId);
            Save();
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file));
            File.WriteAllText(_file, _state.ToString());
        }
    }
}
