using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;

namespace InvectorModKit.Internal
{
    /// <summary>
    /// Decides whether this session is "modded" (see <see cref="ModKit.IsModdedSession"/>):
    /// any loaded plugin that declares <see cref="ModKitModAttribute.AffectsGameplay"/>, or that
    /// does not use ModKit at all and is not on the trusted list, makes it modded.
    /// </summary>
    internal static class Integrity
    {
        private static readonly List<string> _reasons = new List<string>();
        private static bool _classified;

        public static bool IsModded { get; private set; }
        public static IReadOnlyList<string> Reasons => _reasons;

        /// <summary>Plugins known to be unable to affect scores (GUIDs).</summary>
        public static readonly HashSet<string> BuiltInTrusted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "com.bepis.bepinex.configurationmanager",
        };

        public static void MarkModded(string reason)
        {
            if (!_reasons.Contains(reason)) _reasons.Add(reason);
            if (!IsModded) ModKit.Log?.LogWarning($"Session is now MODDED ({reason}): Steam leaderboards, achievements and stats are off; results go to {ScoreStore.FileName}.");
            IsModded = true;
        }

        /// <summary>Classifies every loaded plugin. Call once all plugins have loaded.</summary>
        public static void ClassifyPlugins(IEnumerable<string> extraTrusted)
        {
            if (_classified) return;
            _classified = true;
            var trusted = new HashSet<string>(BuiltInTrusted, StringComparer.OrdinalIgnoreCase);
            foreach (var t in extraTrusted) trusted.Add(t.Trim());

            foreach (var info in Chainloader.PluginInfos.Values.OrderBy(p => p.Metadata.GUID))
            {
                string guid = info.Metadata.GUID;
                if (guid == ModKit.Guid) continue;
                var attr = info.Instance == null ? null
                    : (ModKitModAttribute)Attribute.GetCustomAttribute(info.Instance.GetType(), typeof(ModKitModAttribute));
                string verdict;
                if (attr != null)
                {
                    verdict = attr.AffectsGameplay ? "affects gameplay" : "cosmetic";
                    if (attr.AffectsGameplay) MarkModded($"{info.Metadata.Name} {info.Metadata.Version} affects gameplay");
                }
                else if (trusted.Contains(guid)) verdict = "trusted";
                else
                {
                    verdict = "unknown plugin, treated as gameplay-affecting";
                    MarkModded($"{info.Metadata.Name} {info.Metadata.Version} ({guid}) is not a ModKit mod");
                }
                ModKit.Log.LogInfo($"  plugin {guid} {info.Metadata.Version}: {verdict}");
            }
            ModKit.Log.LogMessage(IsModded
                ? "Modded session: Steam leaderboards/achievements/stats disabled."
                : "Clean session: only cosmetic or trusted mods, Steam features untouched.");
        }
    }
}
