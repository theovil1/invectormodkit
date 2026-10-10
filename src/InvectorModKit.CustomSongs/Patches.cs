using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameSystems;
using HarmonyLib;
using HelloThere.Avicii;
using HelloThere.Modules.AudioModules;
using HelloThere.Modules.MenuModules;
using UnityEngine.AddressableAssets;

namespace InvectorModKit.CustomSongs
{
    /// <summary>Hooks custom songs into the game's song list, audio loading and score lookups.</summary>
    internal static class Patches
    {
        private static IEnumerable<CustomSong> Built => CustomSongsPlugin.Library.Songs.Where(s => s.Level != null);
        private static CustomSong ByLevel(LevelDefinition level) => level == null ? null : Built.FirstOrDefault(s => s.Level == level);
        private static CustomSong ByMusic(MusicDefinition music) => music == null ? null : Built.FirstOrDefault(s => s.Music == music);

        /// <summary>The song lists of single and multiplayer song select both come from here.</summary>
        [HarmonyPatch(typeof(LevelDefinition), nameof(LevelDefinition.GetLevels)), HarmonyPostfix]
        private static void AddCustomLevels(List<LevelDefinition> __result)
        {
            if (__result == null) return;
            foreach (var song in Built)
                if (song.State != SongState.Failed && !__result.Contains(song.Level)) __result.Add(song.Level);
        }

        /// <summary>Normally "is this DLC installed on Steam"; the custom package always is.</summary>
        [HarmonyPatch(typeof(SteamGameSystem), nameof(SteamGameSystem.HasSongPackage)), HarmonyPostfix]
        private static void OwnCustomPackage(SongPackage songPackage, ref bool __result)
        {
            if (songPackage != null && songPackage == GameAssets.Package) __result = true;
        }

        /// <summary>
        /// Custom songs never get an entry in the game's save (it would outlive the mod): their bests come
        /// from ModKit's modded-scores file, which is also where their results are recorded.
        /// </summary>
        [HarmonyPatch(typeof(UserData), nameof(UserData.GetLevelScore)), HarmonyPrefix]
        private static bool DetachedScores(string levelId, global::Difficulty difficulty, ref LevelScores __result)
        {
            if (levelId == null || !levelId.StartsWith("custom.")) return true;
            __result = ModKit.GetModdedBest(levelId, difficulty) ?? new LevelScores { levelId = levelId };
            return false;
        }

        /// <summary>
        /// Song select only shows a song's grade once its progression says "completed" (75%+ in any
        /// difficulty). Custom songs never touch the game's progression, so answer from the modded scores.
        /// </summary>
        [HarmonyPatch(typeof(HelloThere.Modules.ProgressionModules.Progression), nameof(HelloThere.Modules.ProgressionModules.Progression.GetLevelProgress)), HarmonyPrefix]
        private static bool DetachedProgress(string levelId, ref int __result)
        {
            if (levelId == null || !levelId.StartsWith("custom.")) return true;
            __result = 0;
            foreach (global::Difficulty d in System.Enum.GetValues(typeof(global::Difficulty)))
            {
                var best = ModKit.GetModdedBest(levelId, d);
                if (best != null) __result = System.Math.Max(__result, best.progressPoint);
            }
            return false;
        }

        /// <summary>Load the decoded song before the game looks for its audio.</summary>
        [HarmonyPatch(typeof(StateGame), nameof(StateGame.PreloadRoutine)), HarmonyPostfix]
        private static void LoadCustomAudio(StateGame __instance, ref IEnumerator __result) => __result = WithCustomAudio(__instance, __result);

        private static IEnumerator WithCustomAudio(StateGame state, IEnumerator original)
        {
            var song = ByLevel(state.gameSession?.levelDefinition);
            if (song != null)
            {
                yield return CustomSongsPlugin.WaitUntilPrepared(song); // chart still being generated
                if (song.State == SongState.Ready) yield return SongAudio.EnsureGameplay(song);
                else CustomSongsPlugin.Log.LogError($"{song} cannot be played: {song.Error}");
            }
            while (original.MoveNext()) yield return original.Current;
        }

        /// <summary>The game releases audio through Addressables, which knows nothing about our clips.</summary>
        [HarmonyPatch(typeof(MusicDefinition), nameof(MusicDefinition.ReleaseGameplayAudio)), HarmonyPrefix]
        private static bool ReleaseCustomAudio(MusicDefinition __instance)
        {
            var song = ByMusic(__instance);
            if (song == null) return true;
            const float fade = 1.5f;
            int generation = song.AudioGeneration;
            AudioModule.AudioProvider.Stop(song.GameplayItem.nameHash, fade);
            DelayedAction.unscaledTime.Delay(fade, () =>
            {
                if (song.AudioGeneration != generation) return; // reloaded meanwhile
                if (__instance.loadedGameplayAudio == song.GameplayItem) __instance.loadedGameplayAudio = null;
                SongAudio.Release(song.GameplayItem);
            });
            return false;
        }

        private static AssetReferenceT<AudioItem> _pendingPreview;

        /// <summary>Song select: load the custom preview excerpt first, then let the game play it.</summary>
        [HarmonyPatch(typeof(MenuSceneRoot), nameof(MenuSceneRoot.PlayPreviewMusic), typeof(AssetReferenceT<AudioItem>), typeof(float)), HarmonyPrefix]
        private static bool PlayCustomPreview(MenuSceneRoot __instance, AssetReferenceT<AudioItem> audioRef, float fadeTime)
        {
            var song = Built.FirstOrDefault(s => s.Music.PreviewAudio == audioRef);
            if (song == null) { _pendingPreview = null; return true; }
            SongAudio.Register(song);
            if (SongAudio.HasClip(song.PreviewItem)) return true;
            _pendingPreview = audioRef;
            __instance.StartCoroutine(PreviewWhenLoaded(__instance, song, audioRef, fadeTime));
            return false;
        }

        private static IEnumerator PreviewWhenLoaded(MenuSceneRoot menu, CustomSong song, AssetReferenceT<AudioItem> audioRef, float fadeTime)
        {
            yield return CustomSongsPlugin.WaitUntilPrepared(song);
            if (song.State != SongState.Ready || _pendingPreview != audioRef) yield break;
            yield return SongAudio.EnsurePreview(song);
            if (_pendingPreview == audioRef && SongAudio.HasClip(song.PreviewItem)) menu.PlayPreviewMusic(audioRef, fadeTime);
        }
    }
}
