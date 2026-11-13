using System;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Marketplace.Client.Discord
{
    /// <summary>
    /// Keeps the player's Discord activity in line with the latest snapshot, from a background thread: connects to the
    /// local Discord client, paces updates (Discord accepts about one every few seconds) and reconnects quietly when
    /// Discord is closed or restarted.
    /// </summary>
    public sealed class DiscordPresence : IDisposable
    {
        /// <summary>The "Invector: Rhythm Galaxy" application on Discord's developer portal: its name and art assets.</summary>
        public const string DefaultApplicationId = "1570718340719186024";

        private static readonly int[] BackoffSeconds = { 5, 10, 20, 40, 60 };
        // Discord accepts 5 activity updates per 20 s: one every 4 s at most
        private const double MinGap = 4, EndTolerance = 2000;

        private readonly string _applicationId;
        private readonly int _pid;
        private readonly Func<IDiscordPipe> _pipeFactory;
        private readonly Action<string> _log;
        private readonly DateTime _sessionStart;
        private readonly object _lock = new object();
        private readonly byte[] _buffer = new byte[64 * 1024], _chunk = new byte[16 * 1024];

        private PresenceSnapshot _wish;
        private PresenceTexts _wishTexts;
        private IDiscordPipe _pipe;
        private int _received;
        private DateTime _nextOpen = DateTime.MinValue;
        private int _backoff;
        private bool _down;
        private bool _resend;
        private bool _hasSent;
        private JToken _sentActivity;
        private DateTime _lastSend;
        private long _nonce;
        private Thread _thread;
        private volatile bool _stopping;

        public DiscordPresence(string applicationId, int pid, Func<IDiscordPipe> pipeFactory, Action<string> log, DateTime sessionStartUtc)
        {
            _applicationId = applicationId;
            _pid = pid;
            _pipeFactory = pipeFactory;
            _log = log ?? (_ => { });
            _sessionStart = sessionStartUtc;
        }

        /// <summary>The latest state to show; safe from any thread.</summary>
        public void Update(PresenceSnapshot snapshot, PresenceTexts texts)
        {
            lock (_lock)
            {
                _wish = snapshot?.Copy();
                _wishTexts = texts;
            }
        }

        /// <summary>One step: connect when due, answer Discord, send the wish when the pacing allows.</summary>
        public void Tick(DateTime nowUtc)
        {
            try
            {
                if (_pipe == null || !_pipe.IsOpen)
                {
                    if (nowUtc < _nextOpen) return;
                    Connect(nowUtc);
                    if (_pipe == null) return;
                }
                if (!Drain(nowUtc)) return;
                Send(nowUtc);
            }
            catch (Exception e)
            {
                Drop(nowUtc, e.Message);
            }
        }

        private void Connect(DateTime now)
        {
            _pipe?.Dispose();
            _pipe = _pipeFactory();
            _received = 0;
            if (!_pipe.TryOpen())
            {
                Drop(now, "Discord is not running");
                return;
            }
            _pipe.Write(IpcFrame.Encode(IpcOpcode.Handshake, new JObject { ["v"] = 1, ["client_id"] = _applicationId }.ToString(Formatting.None)));
            _backoff = 0;
            _down = false;
            _resend = true;
            _log("Connected to Discord");
        }

        /// <summary>Reads what Discord sent: answers pings, drops the pipe on Close. False when the pipe was dropped.</summary>
        private bool Drain(DateTime now)
        {
            int n;
            while ((n = _pipe.ReadAvailable(_chunk)) > 0)
            {
                if (_received + n > _buffer.Length) _received = 0; // more than a frame of ours could be: start over
                Buffer.BlockCopy(_chunk, 0, _buffer, _received, n);
                _received += n;
            }
            while (IpcFrame.TryDecode(_buffer, _received, out var op, out var json, out int consumed))
            {
                Buffer.BlockCopy(_buffer, consumed, _buffer, 0, _received - consumed);
                _received -= consumed;
                if (op == IpcOpcode.Ping) _pipe.Write(IpcFrame.Encode(IpcOpcode.Pong, json));
                else if (op == IpcOpcode.Close)
                {
                    Drop(now, "Discord closed the connection: " + json);
                    return false;
                }
            }
            return true;
        }

        private void Send(DateTime now)
        {
            PresenceSnapshot wish;
            PresenceTexts texts;
            lock (_lock)
            {
                wish = _wish;
                texts = _wishTexts;
            }
            if (wish == null || texts == null) return;
            JToken activity = (JToken)ActivityBuilder.Build(wish, texts, _sessionStart, now) ?? JValue.CreateNull();
            double gap = _hasSent ? (now - _lastSend).TotalSeconds : double.MaxValue;
            if (!_resend && (!Changed(_sentActivity, activity) || gap < MinGap)) return;
            var frame = new JObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = new JObject { ["pid"] = _pid, ["activity"] = activity },
                ["nonce"] = (++_nonce).ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            _pipe.Write(IpcFrame.Encode(IpcOpcode.Frame, frame.ToString(Formatting.None)));
            _sentActivity = activity;
            _lastSend = now;
            _hasSent = true;
            _resend = false;
        }

        /// <summary>Anything shown differs, except a countdown end that only drifted by less than 2 s.</summary>
        private static bool Changed(JToken sentActivity, JToken activity)
        {
            if (sentActivity == null || sentActivity.Type != activity.Type) return true;
            if (activity.Type == JTokenType.Null) return false;
            var a = (JObject)activity.DeepClone();
            var b = (JObject)sentActivity.DeepClone();
            long? endA = (long?)a["timestamps"]?["end"], endB = (long?)b["timestamps"]?["end"];
            if (endA.HasValue != endB.HasValue) return true;
            if (endA.HasValue && Math.Abs(endA.Value - endB.Value) >= EndTolerance) return true;
            if (endA.HasValue) { a["timestamps"]["end"] = 0; b["timestamps"]["end"] = 0; }
            return !JToken.DeepEquals(a, b);
        }

        private void Drop(DateTime now, string why)
        {
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
            _received = 0;
            _nextOpen = now.AddSeconds(BackoffSeconds[_backoff]);
            _backoff = Math.Min(_backoff + 1, BackoffSeconds.Length - 1);
            if (!_down) _log("Discord presence paused: " + why);
            _down = true;
        }

        /// <summary>Runs <see cref="Tick"/> twice a second on a background thread until disposed.</summary>
        public void Start()
        {
            if (_thread != null) return;
            _thread = new Thread(() =>
            {
                while (!_stopping)
                {
                    Tick(DateTime.UtcNow);
                    Thread.Sleep(500);
                }
            }) { IsBackground = true, Name = "Discord presence" };
            _thread.Start();
        }

        public void Dispose()
        {
            _stopping = true;
            _thread?.Join(1000);
            _thread = null;
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
        }
    }
}
