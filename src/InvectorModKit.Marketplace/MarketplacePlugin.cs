using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using InvectorModKit.CustomSongs;
using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace
{
    /// <summary>
    /// In-game marketplace: browse and install charts shared by other players (the audio comes from each
    /// player's own copy of the song), and publish your own.
    /// </summary>
    [BepInPlugin(Guid, "Marketplace", ModKit.Version)]
    [BepInDependency(ModKit.Guid)]
    [BepInDependency(CustomSongsPlugin.Guid)]
    [ModKitMod(AffectsGameplay = false)]
    public sealed class MarketplacePlugin : BaseUnityPlugin
    {
        public const string Guid = "com.invectormodkit.marketplace";

        internal static ManualLogSource Log;
        internal static MarketplacePlugin Instance;
        internal static MarketplaceApi Api;
        internal static MarketplaceSession Session;
        internal static InstallStore Installs;

        private ConfigEntry<string> _server;
        private ConfigEntry<bool> _checkUpdates;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            _server = Config.Bind("Server", "Url", "https://invector-marketplace.invector-marketplace.workers.dev", "Marketplace server.");
            Api = new MarketplaceApi(_server.Value, new UnityWebRequestTransport());
            Session = new MarketplaceSession(Api, Path.Combine(Paths.ConfigPath, "InvectorModKit"));
            Installs = new InstallStore(CustomSongsApi.SongsFolder);
            new HarmonyLib.Harmony(Guid).PatchAll(typeof(Ui.EntryPoints));
            new HarmonyLib.Harmony(Guid).PatchAll(typeof(Scores.MarketplaceLeaderboard));
            Leaderboards.Register(Scores.MarketplaceLeaderboard.Instance);
            _checkUpdates = Config.Bind("Updates", "Check", true, "Look for a newer version of the mod at start (GitHub Releases) and offer it in the main menu.");
            if (_checkUpdates.Value) Updates.Check();
            Startup();
        }

        private async void Startup()
        {
            try
            {
                await Session.RestoreAsync();
                var page = await Api.SearchAsync("", ChartSort.Recent, 0);
                Logger.LogInfo($"Marketplace online: {page.Items.Count}{(page.More ? "+" : "")} chart(s); {(Session.SignedIn ? "signed in as " + Session.Current.Name : "not signed in")}");
            }
            catch (Exception e)
            {
                Logger.LogWarning("Marketplace unavailable for now: " + e.GetBaseException().Message);
            }
        }

        private void Update()
        {
            MainThread.Pump();
            try
            {
                if (!Ui.MarketplaceModal.IsOpen && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.M) && Ui.EntryPoints.CanOpenHere)
                    Ui.MarketplaceModal.Open(this);
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }
    }
}
