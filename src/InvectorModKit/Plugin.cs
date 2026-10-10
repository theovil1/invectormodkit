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
        private ConfigEntry<bool> _fullResolution;

        private void Awake()
        {
            ModKit.Log = Logger;
            _trusted = Config.Bind("Integrity", "TrustedPlugins", "",
                "Comma-separated GUIDs of plugins without ModKit metadata that you trust not to affect scores. " +
                "Listing a plugin here keeps Steam leaderboards and achievements on while it is installed: your responsibility.");
            _overlay = Config.Bind("Interface", "ShowStatus", true, "Show the ModKit / modded status in the bottom-left corner.");
            _backups = Config.Bind("Save", "BackupsToKeep", 10, "How many copies of the game save to keep (one is made at every launch).");
            _fullResolution = Config.Bind("Graphics", "KeepFullResolution", true,
                "The game lowers its render resolution whenever a few frames run late, which makes the picture blurry " +
                "until it recovers. True keeps it at full resolution all the time (purely visual).");

            SaveBackup.Run(_backups.Value);

            var harmony = new Harmony(ModKit.Guid);
            Try("online guard", () => OnlineGuard.Apply(harmony));
            Try("score store", () => ScoreStore.Apply(harmony));
            Try("strings", () => StringOverrides.Apply(harmony));
            Try("lifecycle", () => LifecyclePatches.Apply(harmony));
            Try("graphics", () => GraphicsTweaks.Apply(harmony));

            ApplyResolutionSetting();
            _fullResolution.SettingChanged += (_, __) => ApplyResolutionSetting();
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

        private void ApplyResolutionSetting()
        {
            GraphicsTweaks.KeepFullResolution = _fullResolution.Value;
            if (!_fullResolution.Value) DynamicRenderScale.forceMax = false;
        }

        private void Try(string what, Action action)
        {
            try { action(); }
            catch (Exception e) { Logger.LogError($"Failed to install {what} patches: {e}"); }
        }
    }
}
