using InvectorModKit.CustomSongs;
using InvectorModKit.Marketplace.Client;
using UnityEngine;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// Plays a chart's excerpt through the game's preview mixer group (so it follows the music volume): fades in over
    /// <see cref="ExcerptWindow.FadeIn"/>, out over <see cref="ExcerptWindow.FadeOut"/> before its end, then starts again.
    /// </summary>
    internal sealed class PreviewPlayer : MonoBehaviour
    {
        private const float StopFade = 0.3f;

        private AudioSource _source;
        private double _start, _end;
        private float _stopping = -1, _stopFrom;

        /// <summary>What is playing (a chart id), or null.</summary>
        public string Playing { get; private set; }

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0;
            _source.ignoreListenerPause = true;
        }

        public void Play(string id, AudioClip clip, double startInClip, double endInClip)
        {
            if (clip == null) return;
            _source.Stop();
            _source.outputAudioMixerGroup = CustomSongsApi.PreviewMixerGroup;
            _source.clip = clip;
            _start = Mathf.Clamp((float)startInClip, 0, clip.length);
            _end = Mathf.Clamp((float)endInClip, (float)_start, clip.length);
            _source.time = (float)_start;
            _source.volume = 0;
            _stopping = -1;
            _source.Play();
            Playing = id;
        }

        /// <summary>Fades out (a short fade, or at once).</summary>
        public void Stop(bool immediately = false)
        {
            Playing = null;
            if (_source == null || !_source.isPlaying) return;
            if (immediately) { _source.Stop(); return; }
            _stopping = StopFade;
            _stopFrom = _source.volume;
        }

        private void Update()
        {
            if (_source == null || _source.clip == null) return;
            if (_stopping >= 0)
            {
                if (!_source.isPlaying) { _stopping = -1; return; }
                _stopping -= Time.unscaledDeltaTime;
                _source.volume = Mathf.Max(0, _stopping / StopFade) * _stopFrom;
                if (_stopping <= 0) { _source.Stop(); _stopping = -1; }
                return;
            }
            if (Playing == null) return; // stopped
            double t = _source.time;
            if (ExcerptWindow.StartAgain(_source.isPlaying, t, _end))
            {
                _source.time = (float)_start; // again from the start, faded in
                if (!_source.isPlaying) _source.Play(); // the clip ran out (an excerpt ending at the song's end)
                t = _start;
            }
            float fadeIn = Mathf.Clamp01((float)((t - _start) / ExcerptWindow.FadeIn));
            float fadeOut = Mathf.Clamp01((float)((_end - t) / ExcerptWindow.FadeOut));
            _source.volume = Mathf.Min(fadeIn, fadeOut);
        }
    }
}
