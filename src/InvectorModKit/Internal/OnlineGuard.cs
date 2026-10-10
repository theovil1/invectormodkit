using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using HelloThere.Avicii;

namespace InvectorModKit.Internal
{
    /// <summary>
    /// While the session is modded, nothing reaches Steam: leaderboards report themselves disabled
    /// (which also hides the in-game highscore UI), submissions and achievement/stat writes are
    /// dropped, and the achievement counters stored in the save are not incremented (they would be
    /// pushed to Steam later, in a clean session).
    /// </summary>
    internal static class OnlineGuard
    {
        public static void Apply(Harmony harmony)
        {
            var skip = new HarmonyMethod(typeof(OnlineGuard), nameof(SkipWhenModded));

            harmony.Patch(AccessTools.Method(typeof(SteamLeaderboardService), nameof(SteamLeaderboardService.IsEnabled)),
                postfix: new HarmonyMethod(typeof(OnlineGuard), nameof(DisableWhenModded)));
            PatchAll(harmony, typeof(SteamLeaderboardService), skip, "Submit");
            // A board claimed by a ModKit leaderboard provider never reaches Steam (see LeaderboardRouter).
            PatchAll(harmony, typeof(HighScoreManager), new HarmonyMethod(typeof(OnlineGuard), nameof(SubmitScorePrefix)), "SubmitScore");

            PatchAll(harmony, typeof(SteamAchievementService), skip, "Award", "Progress", "SubmitAchievement", "SetStat", "SetStatForAchievement", "Reset");
            harmony.Patch(AccessTools.Method(typeof(SteamAchievementService), "SaveChanges"),
                prefix: new HarmonyMethod(typeof(OnlineGuard), nameof(SaveChangesPrefix)));

            PatchAll(harmony, typeof(AchievementTracker), skip,
                "SetProgress", "EvaluateGameSession", "AddExitOrRestart", "AddPerfect", "AddTobleroneSpin", "AddBounce", "PassedFreeflyGate", "NewProgress", "NewAward");
            PatchAll(harmony, typeof(UserData), skip, "AddExitOrRestart", "AddPerfect", "AddTobleroneSpin", "AddBounce", "AddFinish");
        }

        private static void PatchAll(Harmony harmony, Type type, HarmonyMethod prefix, params string[] names)
        {
            foreach (string name in names)
            {
                var methods = AccessTools.GetDeclaredMethods(type).Where(m => m.Name == name && !m.IsAbstract).ToList();
                if (methods.Count == 0)
                {
                    ModKit.Log.LogWarning($"OnlineGuard: {type.Name}.{name} not found (game updated?)");
                    continue;
                }
                foreach (MethodInfo m in methods) harmony.Patch(m, prefix: prefix);
            }
        }

        private static bool SkipWhenModded() => !Integrity.IsModded;

        private static bool SubmitScorePrefix(LevelDefinition level) => !Integrity.IsModded || Leaderboards.Claims(level);

        private static void DisableWhenModded(ref bool __result)
        {
            if (Integrity.IsModded) __result = false;
        }

        private static bool SaveChangesPrefix(Action<bool> onComplete)
        {
            if (!Integrity.IsModded) return true;
            onComplete?.Invoke(true); // callers wait for this callback
            return false;
        }
    }
}
