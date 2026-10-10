// The song: decoded once for the waveform, played by an <audio> element (slower speeds keep the pitch), with
// claps on the notes and a metronome scheduled a little ahead on a WebAudio clock.

const BUCKET = 128; // samples per waveform bucket at the finest level

export class AudioEngine {
  constructor() {
    this.ctx = new AudioContext({ latencyHint: "interactive" });
    this.el = new Audio();
    this.el.preservesPitch = true;
    this.el.preload = "auto";
    this.duration = 0;
    this.levels = []; // waveform pyramid: [{ size, min: Float32Array, max: Float32Array }]
    this.sampleRate = 44100;
    this._anchor = { el: 0, at: 0 };
    this._scheduledUntil = -1;
    this._sources = [];
    this.clapVolume = 0.6;
    this.clap = this._makeClick(2200, 0.03, 0.6);
    this.tick = this._makeClick(1200, 0.025, 0.5);
    this.tickBar = this._makeClick(1800, 0.03, 0.6);
    this.el.addEventListener("ended", () => this.onEnded && this.onEnded());
  }

  /** Loads the song: playback source and decoded samples for the waveform. */
  async load(url) {
    this.pause();
    const response = await fetch(url);
    if (!response.ok) throw new Error("audio: HTTP " + response.status);
    const data = await response.arrayBuffer();
    // Played from memory: seeking needs no range requests.
    if (this._blobUrl) URL.revokeObjectURL(this._blobUrl);
    this._blobUrl = URL.createObjectURL(new Blob([data]));
    this.el.src = this._blobUrl;
    const buffer = await this.ctx.decodeAudioData(data.slice(0));
    this.duration = buffer.duration;
    this.sampleRate = buffer.sampleRate;
    this.levels = buildPeaks(buffer);
    this.seek(0);
  }

  get playing() { return !this.el.paused; }

  async play() {
    if (this.ctx.state === "suspended") await this.ctx.resume();
    this._scheduledUntil = this.time;
    await this.el.play();
    this._resync();
  }

  pause() {
    this.el.pause();
    for (const s of this._sources) try { s.stop(); } catch { /* already done */ }
    this._sources = [];
    this._resync();
  }

  seek(t) {
    this.el.currentTime = Math.max(0, Math.min(this.duration || 0, t));
    this._scheduledUntil = this.el.currentTime;
    for (const s of this._sources) try { s.stop(); } catch { }
    this._sources = [];
    this._resync();
  }

  set rate(r) { this.el.playbackRate = r; this._resync(); }
  get rate() { return this.el.playbackRate; }

  /** Smooth song time: the element's clock moves in steps, interpolate between them. */
  get time() {
    if (this.el.paused) return this.el.currentTime;
    const t = this.el.currentTime;
    if (t !== this._anchor.el) this._resync();
    return this._anchor.el + ((performance.now() - this._anchor.at) / 1000) * this.el.playbackRate;
  }

  _resync() { this._anchor = { el: this.el.currentTime, at: performance.now() }; }

  /**
   * Called every frame while playing: schedules claps at `noteTimes` and metronome clicks at `beats`
   * ({ t, bar }) that fall within the next 150 ms of song time.
   */
  schedule(noteTimes, beats, claps, metronome) {
    if (!this.playing) return;
    const now = this.time, until = now + 0.15 * this.rate, from = Math.max(this._scheduledUntil, now);
    if (until <= from) return;
    const at = (t) => this.ctx.currentTime + (t - now) / this.rate;
    if (claps) for (const t of noteTimes) if (t >= from && t < until) this._play(this.clap, at(t), this.clapVolume);
    if (metronome) for (const b of beats) if (b.t >= from && b.t < until) this._play(b.bar ? this.tickBar : this.tick, at(b.t), 0.5);
    this._scheduledUntil = until;
    this._sources = this._sources.filter((s) => !s._done);
  }

  _play(buffer, when, volume) {
    const src = this.ctx.createBufferSource();
    const gain = this.ctx.createGain();
    gain.gain.value = volume;
    src.buffer = buffer;
    src.connect(gain).connect(this.ctx.destination);
    src.onended = () => { src._done = true; };
    src.start(Math.max(this.ctx.currentTime, when));
    this._sources.push(src);
  }

  _makeClick(freq, seconds, decay) {
    const rate = this.ctx.sampleRate, n = Math.floor(rate * seconds);
    const buffer = this.ctx.createBuffer(1, n, rate), data = buffer.getChannelData(0);
    for (let i = 0; i < n; i++) data[i] = Math.sin((2 * Math.PI * freq * i) / rate) * Math.exp((-i / n) * 6 * decay) * (i < 32 ? i / 32 : 1);
    return buffer;
  }

  /**
   * Waveform for a pixel range: min/max of the samples under each pixel, from the coarsest level that still has
   * at least one bucket per pixel.
   */
  peaks(t0, secondsPerPixel, width) {
    if (!this.levels.length) return null;
    const samplesPerPixel = secondsPerPixel * this.sampleRate;
    let level = this.levels[0];
    for (const l of this.levels) if (l.size <= samplesPerPixel) level = l;
    const min = new Float32Array(width), max = new Float32Array(width);
    for (let x = 0; x < width; x++) {
      const a = Math.floor(((t0 + x * secondsPerPixel) * this.sampleRate) / level.size);
      const b = Math.max(a + 1, Math.floor(((t0 + (x + 1) * secondsPerPixel) * this.sampleRate) / level.size));
      let lo = 0, hi = 0;
      for (let i = Math.max(0, a); i < Math.min(b, level.min.length); i++) {
        if (level.min[i] < lo) lo = level.min[i];
        if (level.max[i] > hi) hi = level.max[i];
      }
      min[x] = lo;
      max[x] = hi;
    }
    return { min, max };
  }
}

/** Min/max of the mono mix per BUCKET samples, then halved level by level. */
function buildPeaks(buffer) {
  const channels = [];
  for (let c = 0; c < buffer.numberOfChannels; c++) channels.push(buffer.getChannelData(c));
  const frames = buffer.length, count = Math.ceil(frames / BUCKET);
  let min = new Float32Array(count), max = new Float32Array(count);
  for (let b = 0; b < count; b++) {
    let lo = 0, hi = 0;
    const end = Math.min(frames, (b + 1) * BUCKET);
    for (let i = b * BUCKET; i < end; i++) {
      let v = 0;
      for (const ch of channels) v += ch[i];
      v /= channels.length;
      if (v < lo) lo = v;
      if (v > hi) hi = v;
    }
    min[b] = lo;
    max[b] = hi;
  }
  const levels = [{ size: BUCKET, min, max }];
  while (min.length > 512) {
    const n = Math.ceil(min.length / 2), nmin = new Float32Array(n), nmax = new Float32Array(n);
    for (let i = 0; i < n; i++) {
      nmin[i] = Math.min(min[2 * i], min[2 * i + 1] ?? 0);
      nmax[i] = Math.max(max[2 * i], max[2 * i + 1] ?? 0);
    }
    min = nmin;
    max = nmax;
    levels.push({ size: levels[levels.length - 1].size * 2, min, max });
  }
  return levels;
}
