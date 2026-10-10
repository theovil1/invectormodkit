using System.Collections.Generic;
using HarmonyLib;

namespace InvectorModKit.Internal
{
    /// <summary>Lets mods add or replace localized strings: every lookup goes through StringDatabase.GetFirstMatch.</summary>
    internal static class StringOverrides
    {
        private static readonly Dictionary<string, string> _overrides = new Dictionary<string, string>();

        public static void Set(string key, string text) => _overrides[key] = text;

        public static void Apply(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(StringDatabase), "GetFirstMatch");
            if (target == null) { ModKit.Log.LogWarning("StringDatabase.GetFirstMatch not found; string overrides disabled."); return; }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(StringOverrides), nameof(Prefix)));
        }

        private static bool Prefix(object[] __args, ref string __result)
        {
            if (_overrides.Count == 0 || __args.Length == 0 || !(__args[0] is string key)) return true;
            if (!_overrides.TryGetValue(key, out var text)) return true;
            __result = text;
            return false;
        }
    }
}
