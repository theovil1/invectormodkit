using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using HelloThere.Avicii;
using HelloThere.Modules.AudioModules;
using InvectorModKit.Charting;
using UnityEngine;
using UnityEngine.AddressableAssets;
using ChartDifficulty = InvectorModKit.Charting.Difficulty;

namespace InvectorModKit.CustomSongs
{
    /// <summary>
    /// Builds the game's own ScriptableObjects (LevelDefinition, MusicDefinition, ModesInTimeContainer,
    /// AudioItems, SongPackage) for custom songs, borrowing what cannot be made from scratch (mixer
    /// routing, sound effects, header art) from an official level.
    /// </summary>
    internal static class GameAssets
    {
        /// <summary>The game shows its intro until ~5.5 s; notes start no earlier than this (clip time).</summary>
        private const double FirstNoteAt = 6.0;
        private const double GameStartTime = 5.5;

        private static LevelDefinition _template;
        private static AudioItem _templateGameplay, _templatePreview;
        public static SongPackage Package { get; private set; }
        public static bool Ready { get; private set; }

        public static IEnumerator LoadTemplates(ManualLogSource log)
        {
            while ((_template = FindTemplate()) == null || !AudioModule.IsLoaded || AudioModule.AudioManager == null)
                yield return new WaitForSecondsRealtime(0.25f);

            var gameplay = Addressables.LoadAssetAsync<AudioItem>(_template.MusicDefinition.GameplayAudio.RuntimeKey);
            var preview = Addressables.LoadAssetAsync<AudioItem>(_template.MusicDefinition.PreviewAudio.RuntimeKey);
            yield return gameplay;
            yield return preview;
            _templateGameplay = gameplay.Result;
            _templatePreview = preview.Result;
            if (_templateGameplay == null || _templatePreview == null)
            {
                log.LogError("Could not load template audio items; custom songs disabled.");
                yield break;
            }
            yield return ResolveMixerGroup(_templateGameplay);
            yield return ResolveMixerGroup(_templatePreview);

            Package = ScriptableObject.CreateInstance<SongPackage>();
            Package.name = "Custom Songs";
            Package.HeaderBg = _template.songPackage.HeaderBg;
            Package.HeaderColor = new Color(0.45f, 0.12f, 0.55f, 0.75f);
            Package.SupportsHighscore = false; // never touch Steam leaderboards
            Keep(Package);
            Ready = true;
            log.LogInfo($"Templates from '{_template.name}': gameplay item '{_templateGameplay.name}' -> {_templateGameplay.DefaultOutputGroup?.name}, preview '{_templatePreview.name}' -> {_templatePreview.DefaultOutputGroup?.name}");
        }

        /// <summary>Same lookup AudioModule does when it loads audio items: mixer group by its Addressables location key.</summary>
        private static IEnumerator ResolveMixerGroup(AudioItem item)
        {
            if (item.DefaultOutputGroup != null) yield break;
            var log = CustomSongsPlugin.Log;
            if (item.DefaultOutputGroupRef == null || !item.DefaultOutputGroupRef.RuntimeKeyIsValid())
            {
                log.LogInfo($"{item.name}: no mixer group reference (the game plays it unrouted too)");
                yield break;
            }
            var locations = Addressables.LoadResourceLocationsAsync(item.DefaultOutputGroupRef, typeof(UnityEngine.Audio.AudioMixerGroup));
            yield return locations;
            foreach (var location in locations.Result)
            {
                if (AudioModule.mixerLookup.TryGetValue(location.PrimaryKey, out var group)) item.DefaultOutputGroup = group;
                log.LogInfo($"{item.name}: mixer location {location.PrimaryKey} -> {(group != null ? group.name : "not in lookup (" + AudioModule.mixerLookup.Count + " known)")}");
            }
            Addressables.Release(locations);
        }

        private static LevelDefinition FindTemplate() =>
            LevelDefinition.levels.FirstOrDefault(l => l != null && l.InSongSelect && l.songPackage != null && l.MusicDefinition != null && l.Modes != null
                && l.MusicDefinition.GameplayAudio != null && l.MusicDefinition.GameplayAudio.RuntimeKeyIsValid()
                && l.MusicDefinition.PreviewAudio != null && l.MusicDefinition.PreviewAudio.RuntimeKeyIsValid());

        public static void Build(CustomSong song, int order)
        {
            var chart = song.Chart;
            song.LeadIn = chart.LeadIn(Math.Max(0.5, FirstNoteAt - chart.StartTime));
            double pad = song.LeadIn;

            song.GameplayItem = MakeItem(_templateGameplay, "CustomSong " + song.Id);
            song.PreviewItem = MakeItem(_templatePreview, "CustomSongPreview " + song.Id);

            var tm = _template.MusicDefinition;
            var music = Keep(ScriptableObject.CreateInstance<MusicDefinition>());
            music.name = "Song - " + song;
            music.ArtistName = song.Artist;
            music.SongName = song.Title;
            music.songId = song.LevelId;
            music.ArtistImage = Covers.Make(song);
            music.manualBpm = (float)chart.Bpm;
            music.PickupSfxDefinition = CopySfx(tm.PickupSfxDefinition);
            music.OverrideSpectogramSignalValue = tm.OverrideSpectogramSignalValue;
            music.SpectogramSignalValue = tm.SpectogramSignalValue;
            music.GameplayAudio = new AssetReferenceT<AudioItem>("");
            music.PreviewAudio = new AssetReferenceT<AudioItem>("");
            SongAudio.Inject(music.GameplayAudio, song.GameplayItem);
            SongAudio.Inject(music.PreviewAudio, song.PreviewItem);
            song.Music = music;

            var modes = Keep(ScriptableObject.CreateInstance<ModesInTimeContainer>());
            modes.name = "Modes - " + song;
            modes.TierMode = new ModeInTime { mode = new ModeSet { modeType = ModeType.List }, startTime = new TimeField(0) };
            modes.PickupSfxDefinition = CopySfx(_template.Modes.PickupSfxDefinition);
            modes.ModesInTime = chart.Sections
                .Select((s, i) => new ModeInTime { mode = new ModeSet { modeType = (ModeType)(int)s.Mode }, startTime = new TimeField(i == 0 ? 0 : s.Start + pad) })
                .ToList();
            var pattern = new StrumPattern();
            pattern.MakeEmpty();
            foreach (ChartDifficulty d in Enum.GetValues(typeof(ChartDifficulty)))
                pattern.SetStrums(ToGame(d), chart[d].Select(n => new Strum
                {
                    time = n.Time + pad,
                    length = n.Length,
                    type = (StrumType)(int)n.Kind,
                    lane = n.Lane,
                    color = n.Color,
                    starData = new StarPickupData(n.StarSet, n.LastInSet),
                }).ToList());
            modes.StrumPattern = pattern;
            modes.TransitionIntoSongAt = new TimeField(0);
            modes.StartTime = GameStartTime;
            modes.EndTime = chart.EndTime + pad;

            var level = Keep(ScriptableObject.CreateInstance<LevelDefinition>());
            level.name = "Level - " + song;
            level.levelId = song.LevelId;
            level.SaveName = song.LevelId;
            level.difficulty = 1000 + order;
            level.seed = 1000;
            level.InSongSelect = true;
            level.InitiallyLocked = false;
            level.MusicDefinition = music;
            level.Modes = modes;
            level.highscoreTablePrefix = 0;
            level.songPackage = Package;
            song.Level = level;
        }

        private static global::Difficulty ToGame(ChartDifficulty d) => (global::Difficulty)Enum.Parse(typeof(global::Difficulty), d.ToString());

        private static AudioItem MakeItem(AudioItem template, string name)
        {
            var item = Keep(UnityEngine.Object.Instantiate(template));
            ((UnityEngine.Object)item).name = name;
            item._name = null;
            item._nameHash = int.MinValue; // recomputed from the new name
            item.AudioClipReference = new AssetReferenceT<AudioClip>("");
            item.loadingHandles = new List<UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle>();
            item.DefaultOutputGroup = template.DefaultOutputGroup;
            item.LoadAtStart = false;
            item.clipLength = 0f;
            return item;
        }

        private static PickupSfxDefinition CopySfx(PickupSfxDefinition source)
        {
            var copy = new PickupSfxDefinition();
            if (source != null) copy.SetDefaults(source);
            return copy;
        }

        internal static T Keep<T>(T obj) where T : UnityEngine.Object
        {
            obj.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return obj;
        }
    }
}
