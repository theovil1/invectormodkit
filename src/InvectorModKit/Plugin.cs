using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using InvectorModKit.Internal;

namespace InvectorModKit
{
    [BepInPlugin(ModKit.Guid, ModKit.Name, ModKit.Version)]
    [BepInProcess("Invector Rhythm Galaxy.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private ConfigEntry<string> _trusted;
        private ConfigEntry<bool> _overlay;
        private ConfigEntry<int> _backups;

        private void Awake()
        {
            ModKit.Log = Logger;
            _trusted = Config.Bind("Integrity", "TrustedPlugins", "",
                "Comma-separated GUIDs of plugins without ModKit metadata that you trust not to affect scores. " +
                "Listing a plugin here keeps Steam leaderboards and achievements on while it is installed: your responsibility.");
            _overlay = Config.Bind("Interface", "ShowStatus", true, "Show the ModKit / modded status in the bottom-left corner.");
            _backups = Config.Bind("Save", "BackupsToKeep", 10, "How many copies of the game save to keep (one is made at every launch).");

            SaveBackup.Run(_backups.Value);

            var harmony = new Harmony(ModKit.Guid);
            Try("online guard", () => OnlineGuard.Apply(harmony));
            Try("score store", () => ScoreStore.Apply(harmony));
            Try("strings", () => StringOverrides.Apply(harmony));
            Try("lifecycle", () => LifecyclePatches.Apply(harmony));

            StatusOverlay.Visible = _overlay.Value;
            _overlay.SettingChanged += (_, __) => StatusOverlay.Visible = _overlay.Value;
            gameObject.AddComponent<StatusOverlay>();
            Logger.LogInfo($"{ModKit.Name} {ModKit.Version} loaded");
        }

        private void Start()
        {
            // Every plugin has run Awake by now.
            Integrity.ClassifyPlugins(_trusted.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private void Try(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { Logger.LogError($"Failed to install {what} patches: {e}"); }
        }
    }
}
