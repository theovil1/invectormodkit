using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using InvectorModKit.Charting;
using InvectorModKit.Charting.Audio;
using InvectorModKit.Marketplace.Client;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>
    /// Everything the editor's interface asks the C# side for, as JSON in and out ("method" + params). Charts travel
    /// in the *.chart.json format (ChartJson), so the interface edits exactly what is on disk.
    /// </summary>
    public sealed class EditorBackend
    {
        private readonly EditorSettings _settings;
        private readonly Action<string> _openUrl;
        private MarketplaceApi _api;
        private (string Key, Chart Chart) _generated;

        public EditorBackend(EditorSettings settings, Action<string> openUrl, IHttpTransport http = null)
        {
            _settings = settings;
            _openUrl = openUrl;
            _api = new MarketplaceApi(settings.MarketplaceUrl, http ?? new HttpClientTransport()) { Token = settings.Token };
        }

        public EditorSettings Settings => _settings;

        public async Task<JToken> Handle(string method, JObject p)
        {
            p = p ?? new JObject();
            switch (method)
            {
                case "settings": return SettingsInfo();
                case "setGameDir": return SetGameDir((string)p["path"]);
                case "setLang":
                    _settings.Lang = (string)p["lang"];
                    _settings.Save();
                    return SettingsInfo();
                case "songs":
                    return new JArray(SongFiles.Enumerate(_settings.CustomSongsDir).Select(s => new JObject
                    {
                        ["audioPath"] = s.AudioPath, ["title"] = s.Title, ["artist"] = s.Artist, ["hasChart"] = s.HasChart,
                    }));
                case "open": return Open(Required(p, "audioPath"));
                case "newChart":
                {
                    string audio = Required(p, "audioPath");
                    var chart = (string)p["mode"] == "generate" ? await GenerateCached(audio).ConfigureAwait(false) : await Task.Run(() => Generator.Blank(audio)).ConfigureAwait(false);
                    return ToJson(Handmade(chart));
                }
                case "regenerate": return ToJson(await GenerateCached(Required(p, "audioPath")).ConfigureAwait(false));
                case "save": return Save(Required(p, "audioPath"), ReadChart(p), (string)p["title"], (string)p["artist"]);
                case "validate": return Validate(ReadChart(p));
                case "sendToGame": return SendToGame(Required(p, "audioPath"), ReadChart(p), (string)p["title"], (string)p["artist"]);
                case "me": return await Me().ConfigureAwait(false);
                case "signIn": return await SignIn().ConfigureAwait(false);
                case "signOut":
                    _settings.Token = _api.Token = null;
                    _settings.Save();
                    return new JObject { ["signedIn"] = false };
                case "publish":
                    return await Publish(Required(p, "audioPath"), ReadChart(p), (string)p["title"], (string)p["artist"], (string)p["youtubeUrl"], (bool?)p["acceptRules"] ?? false).ConfigureAwait(false);
                case "rulesUrl": return new JValue(_api.RulesUrl((string)p["lang"] ?? _settings.Lang));
                case "openUrl":
                {
                    string url = Required(p, "url");
                    if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("only https links can be opened");
                    _openUrl(url);
                    return JValue.CreateNull();
                }
            }
            throw new ArgumentException("unknown method " + method);
        }

        // ----- settings

        private JObject SettingsInfo() => new JObject
        {
            ["gameDir"] = _settings.GameDir,
            ["customSongsDir"] = _settings.CustomSongsDir,
            ["gameFound"] = GameLocator.IsGameDir(_settings.GameDir),
            ["modGenerator"] = new Generator(_settings.GameDir).UsesModGenerator,
            ["lang"] = _settings.Lang,
            ["hasToken"] = !string.IsNullOrEmpty(_settings.Token),
        };

        private JObject SetGameDir(string path)
        {
            if (!GameLocator.IsGameDir(path)) throw new ArgumentException("not the game's folder: " + path);
            _settings.GameDir = Path.GetFullPath(path);
            _settings.Save();
            _generated = default;
            return SettingsInfo();
        }

        // ----- charts

        private JObject Open(string audioPath)
        {
            if (!File.Exists(audioPath)) throw new FileNotFoundException("audio not found", audioPath);
            var song = SongFiles.Read(audioPath);
            JToken chart = JValue.CreateNull();
            if (song.HasChart) chart = ToJson(ChartJson.Deserialize(File.ReadAllText(song.ChartPath)));
            return new JObject
            {
                ["audioPath"] = Path.GetFullPath(audioPath),
                ["chartPath"] = song.ChartPath,
                ["title"] = song.Title,
                ["artist"] = song.Artist,
                ["inCustomSongs"] = SongFiles.IsInside(_settings.CustomSongsDir, audioPath),
                ["chart"] = chart,
            };
        }

        /// <summary>Writes the chart next to its audio. It becomes hand-made: never regenerated, not a marketplace chart.</summary>
        private JObject Save(string audioPath, Chart chart, string title, string artist)
        {
            string chartPath = SongFiles.ChartPathFor(audioPath);
            WriteChart(chartPath, Handmade(chart));
            WriteSongInfo(audioPath, title, artist);
            return new JObject { ["audioPath"] = audioPath, ["chartPath"] = chartPath };
        }

        private static Chart Handmade(Chart chart)
        {
            chart.Generator = null;
            chart.Source = null;
            return chart;
        }

        private static void WriteChart(string chartPath, Chart chart)
        {
            string tmp = chartPath + ".tmp";
            File.WriteAllText(tmp, ChartJson.Serialize(chart));
            File.Copy(tmp, chartPath, true);
            File.Delete(tmp);
        }

        /// <summary>Title and artist go to song.json, only in a song's own folder inside CustomSongs (the root is shared).</summary>
        private void WriteSongInfo(string audioPath, string title, string artist)
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist)) return;
            string root = _settings.CustomSongsDir, folder = Path.GetDirectoryName(Path.GetFullPath(audioPath));
            if (!SongFiles.IsInside(root, audioPath) || string.Equals(folder, Path.GetFullPath(root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
            var current = SongFiles.Read(audioPath);
            if (current.Title == title && current.Artist == artist) return;
            File.WriteAllText(Path.Combine(folder, "song.json"), new JObject { ["title"] = title, ["artist"] = artist }.ToString());
        }

        private static JObject Validate(Chart chart)
        {
            var result = new JObject();
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
                result[d.ToString()] = new JArray(Playability.Issues(chart, d).Select(i => new JObject
                {
                    ["time"] = Math.Round(i.Time, 4), ["code"] = i.Code, ["mode"] = i.Mode?.ToString(), ["gap"] = Math.Round(i.Gap, 3), ["detail"] = i.Detail, ["text"] = i.ToString(),
                }));
            return result;
        }

        /// <summary>
        /// Puts the song in the game's CustomSongs: saved in place when it is already there, otherwise copied into a
        /// new "Artist - Title" folder with its chart and song.json. The game picks it up (and reloads charts) by itself.
        /// </summary>
        private JObject SendToGame(string audioPath, Chart chart, string title, string artist)
        {
            string root = _settings.CustomSongsDir;
            if (root == null || !GameLocator.IsGameDir(_settings.GameDir)) throw new InvalidOperationException("game not found");
            if (SongFiles.IsInside(root, audioPath))
            {
                var saved = Save(audioPath, chart, title, artist);
                saved["copied"] = false;
                return saved;
            }
            Directory.CreateDirectory(root);
            string name = SongFiles.FolderName(artist, title), folder = Path.Combine(root, name);
            for (int i = 2; Directory.Exists(folder); i++) folder = Path.Combine(root, $"{name} ({i})");
            Directory.CreateDirectory(folder);
            string copy = Path.Combine(folder, Path.GetFileName(audioPath));
            File.Copy(audioPath, copy);
            WriteChart(SongFiles.ChartPathFor(copy), Handmade(chart));
            File.WriteAllText(Path.Combine(folder, "song.json"), new JObject { ["title"] = title ?? "", ["artist"] = artist ?? "" }.ToString());
            return new JObject { ["audioPath"] = copy, ["chartPath"] = SongFiles.ChartPathFor(copy), ["copied"] = true };
        }

        /// <summary>The generated chart is kept for the last audio file: regenerating several zones runs the generator once.</summary>
        private async Task<Chart> GenerateCached(string audioPath)
        {
            string key = Path.GetFullPath(audioPath) + "|" + File.GetLastWriteTimeUtc(audioPath).Ticks + "|" + _settings.GameDir;
            if (_generated.Key == key) return Clone(_generated.Chart);
            var chart = await Task.Run(() => new Generator(_settings.GameDir).Generate(audioPath)).ConfigureAwait(false);
            _generated = (key, chart);
            return Clone(chart);
        }

        private static Chart Clone(Chart chart) => ChartJson.Deserialize(ChartJson.Serialize(chart));

        // ----- marketplace

        private async Task<JToken> Me()
        {
            if (string.IsNullOrEmpty(_api.Token)) return JValue.CreateNull();
            try
            {
                var me = await _api.MeAsync().ConfigureAwait(false);
                return new JObject { ["name"] = me.Name, ["role"] = me.Role };
            }
            catch (MarketplaceException e) when (e.Status == 401)
            {
                _settings.Token = _api.Token = null;
                _settings.Save();
                return JValue.CreateNull();
            }
        }

        private async Task<JToken> SignIn()
        {
            var login = _api.StartBrowserLogin(_settings.Lang);
            _openUrl(login.Url);
            using (var giveUp = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(10)))
            {
                var session = await _api.WaitForBrowserLoginAsync(login, TimeSpan.FromSeconds(2), giveUp.Token).ConfigureAwait(false);
                _settings.Token = session.Token;
                _settings.Save();
                return new JObject { ["name"] = session.Name, ["role"] = session.Role };
            }
        }

        private async Task<JToken> Publish(string audioPath, Chart chart, string title, string artist, string youtubeUrl, bool acceptRules)
        {
            if (string.IsNullOrEmpty(_api.Token)) throw new InvalidOperationException("sign in first");
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("title is required");
            var (fingerprint, duration) = await Task.Run(() =>
            {
                var audio = AudioFile.Decode(audioPath);
                return (Fingerprint.Of(audio).ToJson(), audio.Duration);
            }).ConfigureAwait(false);
            Handmade(chart);
            ChartSummary published;
            try
            {
                published = await _api.PublishAsync(new PublishRequest
                {
                    Title = title, Artist = string.IsNullOrWhiteSpace(artist) ? "Unknown artist" : artist, YoutubeUrl = youtubeUrl,
                    Duration = duration, ChartJson = ChartJson.Serialize(chart), FingerprintJson = fingerprint, AcceptRules = acceptRules,
                }).ConfigureAwait(false);
            }
            catch (MarketplaceException e) when (e.Status == 428)
            {
                return new JObject { ["needRules"] = true };
            }
            // A song in CustomSongs keeps playing exactly the published chart, with its leaderboard.
            if (SongFiles.IsInside(_settings.CustomSongsDir, audioPath))
            {
                chart.Source = new ChartSource { MarketplaceId = published.Id, Version = published.Version, Author = published.Author };
                WriteChart(SongFiles.ChartPathFor(audioPath), chart);
            }
            return new JObject { ["id"] = published.Id, ["version"] = published.Version, ["author"] = published.Author };
        }

        // ----- helpers

        private static string Required(JObject p, string key) =>
            (string)p[key] is string s && s.Length > 0 ? s : throw new ArgumentException(key + " is required");

        private static Chart ReadChart(JObject p) =>
            p["chart"] is JObject o ? ChartJson.Deserialize(o.ToString()) : throw new ArgumentException("chart is required");

        private static JObject ToJson(Chart chart) => JObject.Parse(ChartJson.Serialize(chart));
    }
}
