using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using HelloThere.Modules.AudioModules;
using InvectorModKit.Charting.Audio;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace InvectorModKit.CustomSongs
{
    /// <summary>
    /// Decodes custom audio off the main thread and hands it to the game as if Addressables had loaded
    /// it: the AssetReference gets an already-completed operation whose result is our AudioClip.
    /// </summary>
    internal static class SongAudio
    {
        private const int PreviewClipsKept = 4;
        private static readonly LinkedList<CustomSong> _previews = new LinkedList<CustomSong>();

        public static void Inject(AssetReference reference, UnityEngine.Object asset)
        {
            reference.m_Operation = Addressables.ResourceManager.CreateCompletedOperation(asset, null);
        }

        /// <summary>
        /// The game rebuilds its audio item database when menus load, so register right before every use.
        /// Writes the database directly: AddAdditionalAudioItems would raise OnAdditionalAudioItemsAdded,
        /// which song select treats as "new DLC installed" and reloads itself.
        /// </summary>
        public static void Register(CustomSong song)
        {
            GameAssets.CopyRouting(song);
            var db = AudioModule.AudioManager?.audioItemCollection;
            if (db == null) return;
            foreach (var item in new[] { song.GameplayItem, song.PreviewItem })
                if (item != null && !db.ContainsKey(item.nameHash)) db.Add(item.nameHash, item);
        }

        public static bool HasClip(AudioItem item) => item != null && item.AudioClipReference.m_Operation.IsValid() && item.AudioClipReference.Asset != null;

        /// <summary>Makes sure the full song (with its lead-in silence) is loaded before gameplay starts.</summary>
        public static IEnumerator EnsureGameplay(CustomSong song)
        {
            song.AudioGeneration++;
            Register(song);
            if (song.Music.loadedGameplayAudio == song.GameplayItem && HasClip(song.GameplayItem)) yield break;

            DecodedAudio decoded = null;
            var task = Task.Run(() =>
            {
                decoded = AudioFile.Decode(song.AudioPath);
                Loudness.ApplyGainLimited(decoded, song.Chart.Gain ?? 0); // match the official songs' loudness
            });
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                CustomSongsPlugin.Log.LogError($"{song}: could not decode audio: {task.Exception?.GetBaseException().Message}");
                yield break;
            }
            var clip = MakeClip("CustomSong " + song.Id, decoded, song.LeadIn);
            SetClip(song.GameplayItem, clip);
            song.Music.loadedGameplayAudio = song.GameplayItem;
            CheckAlignment(song, clip, decoded);
        }

        /// <summary>Notes are placed at song time + lead-in: verify the audio really starts after exactly that much silence.</summary>
        private static void CheckAlignment(CustomSong song, AudioClip clip, DecodedAudio decoded)
        {
            int pad = (int)Math.Round(song.LeadIn * decoded.SampleRate);
            int probe = Math.Min(decoded.Frames - 64, decoded.SampleRate * 10); // 10 s into the song
            var got = new float[64 * decoded.Channels];
            clip.GetData(got, pad + probe);
            double err = 0;
            for (int i = 0; i < got.Length; i++) err = Math.Max(err, Math.Abs(got[i] - decoded.Samples[probe * decoded.Channels + i]));
            var log = CustomSongsPlugin.Log;
            if (err > 1e-3) log.LogError($"{song}: audio is misaligned with the chart (max diff {err:0.0000})");
            else log.LogInfo($"{song}: audio loaded, {clip.length:0.0}s incl. {song.LeadIn:0.000}s lead-in, aligned with chart");
        }

        /// <summary>Loads the cached preview excerpt; only a few previews stay in memory.</summary>
        public static IEnumerator EnsurePreview(CustomSong song)
        {
            Register(song);
            if (HasClip(song.PreviewItem)) { Touch(song); yield break; }
            DecodedAudio decoded = null;
            var task = Task.Run(() => decoded = AudioFile.Decode(song.PreviewPath));
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted)
            {
                CustomSongsPlugin.Log.LogError($"{song}: could not load preview: {task.Exception?.GetBaseException().Message}");
                yield break;
            }
            SetClip(song.PreviewItem, MakeClip("CustomSongPreview " + song.Id, decoded, 0));
            Touch(song);
        }

        private static void Touch(CustomSong song)
        {
            _previews.Remove(song);
            _previews.AddFirst(song);
            while (_previews.Count > PreviewClipsKept)
            {
                var old = _previews.Last.Value;
                _previews.RemoveLast();
                if (!AudioModule.AudioProvider.IsInstancePlaying(old.PreviewItem.nameHash)) Release(old.PreviewItem);
            }
        }

        private static AudioClip MakeClip(string name, DecodedAudio audio, double leadIn)
        {
            int pad = (int)Math.Round(leadIn * audio.SampleRate);
            var clip = AudioClip.Create(name, pad + audio.Frames, audio.Channels, audio.SampleRate, false);
            clip.SetData(audio.Samples, pad);
            return GameAssets.Keep(clip);
        }

        private static void SetClip(AudioItem item, AudioClip clip)
        {
            Release(item);
            Inject(item.AudioClipReference, clip);
            item.clipLength = clip.length;
        }

        public static void Release(AudioItem item)
        {
            var op = item.AudioClipReference.m_Operation;
            if (!op.IsValid()) return;
            var clip = op.Result as AudioClip;
            Addressables.Release(op);
            item.AudioClipReference.m_Operation = default;
            if (clip != null) UnityEngine.Object.Destroy(clip);
        }
    }
}
