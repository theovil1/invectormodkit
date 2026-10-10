using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>The marketplace server's JSON API (see server/src/index.ts). Every failure is a <see cref="MarketplaceException"/>.</summary>
    public sealed class MarketplaceApi
    {
        private readonly string _base;
        private readonly IHttpTransport _http;

        /// <summary>Session token from <see cref="LoginAsync"/> (kept by the caller between sessions).</summary>
        public string Token { get; set; }

        public MarketplaceApi(string baseUrl, IHttpTransport http)
        {
            _base = baseUrl.TrimEnd('/');
            _http = http;
        }

        public async Task<SearchPage> SearchAsync(string query, ChartSort sort, int page)
        {
            var o = (JObject)await SendAsync("GET", $"/charts?q={Uri.EscapeDataString(query ?? "")}&sort={(sort == ChartSort.Popular ? "popular" : "recent")}&page={page}").ConfigureAwait(false);
            return new SearchPage { Items = ((JArray)o["items"]).Select(i => Summary((JObject)i)).ToList(), Page = (int)o["page"], More = (bool)o["more"] };
        }

        /// <summary>The latest version of a chart with its fingerprint (counts as a download).</summary>
        public async Task<ChartDownload> DownloadAsync(string id)
        {
            var o = (JObject)await SendAsync("GET", "/charts/" + Uri.EscapeDataString(id)).ConfigureAwait(false);
            return new ChartDownload
            {
                Summary = Summary((JObject)o["summary"]),
                ChartJson = o["chart"].ToString(Formatting.None),
                FingerprintJson = o["fingerprint"].ToString(Formatting.None),
            };
        }

        /// <summary>Signs in with a Steam session ticket (hex) and keeps the returned token.</summary>
        public async Task<Session> LoginAsync(string ticketHex)
        {
            var o = (JObject)await SendAsync("POST", "/auth/steam", new JObject { ["ticket"] = ticketHex }).ConfigureAwait(false);
            var session = new Session { Token = (string)o["token"], SteamId = (string)o["steamId"], Name = (string)o["name"], Role = (string)o["role"] };
            Token = session.Token;
            return session;
        }

        /// <summary>
        /// Starts a sign-in on Steam's own page: open <see cref="BrowserLogin.Url"/> in the player's browser,
        /// then wait with <see cref="WaitForBrowserLoginAsync"/>. The code is a secret only this game knows.
        /// </summary>
        /// <param name="lang">The game's language (Loc code), so the page the player lands on speaks it.</param>
        public BrowserLogin StartBrowserLogin(string lang = null)
        {
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            string code = string.Concat(bytes.Select(b => b.ToString("x2")));
            return new BrowserLogin { Code = code, Url = $"{_base}/auth/steam/openid/start?code={code}" + LangQuery(lang, "&") };
        }

        /// <summary>Polls until the player signed in on Steam's page (keeps the token), or throws when it expires.</summary>
        public async Task<Session> WaitForBrowserLoginAsync(BrowserLogin login, TimeSpan pollEvery, System.Threading.CancellationToken cancel = default)
        {
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                var o = (JObject)await SendAsync("GET", "/auth/steam/openid/poll?code=" + login.Code).ConfigureAwait(false);
                if (o["token"] != null)
                {
                    var session = new Session { Token = (string)o["token"], SteamId = (string)o["steamId"], Name = (string)o["name"], Role = (string)o["role"] };
                    Token = session.Token;
                    return session;
                }
                await Task.Delay(pollEvery, cancel).ConfigureAwait(false);
            }
        }

        /// <summary>The signed-in player (checks that <see cref="Token"/> is still valid; the token is not returned).</summary>
        public async Task<Session> MeAsync()
        {
            var o = (JObject)await SendAsync("GET", "/me").ConfigureAwait(false);
            return new Session { Token = Token, SteamId = (string)o["steamId"], Name = (string)o["name"], Role = (string)o["role"] };
        }

        public async Task<List<ChartSummary>> MyChartsAsync()
        {
            var a = (JArray)await SendAsync("GET", "/me/charts").ConfigureAwait(false);
            return a.Select(i => Summary((JObject)i)).ToList();
        }

        public async Task<ChartSummary> PublishAsync(PublishRequest r)
        {
            var body = new JObject
            {
                ["title"] = r.Title,
                ["artist"] = r.Artist,
                ["youtubeUrl"] = r.YoutubeUrl,
                ["duration"] = Math.Round(r.Duration, 3),
                ["chart"] = JToken.Parse(r.ChartJson),
                ["fingerprint"] = JToken.Parse(r.FingerprintJson),
            };
            if (r.AcceptRules) body["acceptRules"] = true;
            return Summary((JObject)await SendAsync("POST", "/charts", body).ConfigureAwait(false));
        }

        public Task DeleteAsync(string id) => SendAsync("DELETE", "/charts/" + Uri.EscapeDataString(id));

        /// <summary>The publishing rules page, in the game's language.</summary>
        public string RulesUrl(string lang = null) => _base + "/rules" + LangQuery(lang, "?");

        private static string LangQuery(string lang, string separator) =>
            string.IsNullOrEmpty(lang) ? "" : separator + "lang=" + Uri.EscapeDataString(lang);

        /// <summary>The song starts: opens a play on that board; its id goes with the score.</summary>
        public async Task<string> StartPlayAsync(string chartId, int version, int difficulty)
        {
            var o = (JObject)await SendAsync("POST", "/plays", new JObject { ["chartId"] = chartId, ["version"] = version, ["difficulty"] = difficulty }).ConfigureAwait(false);
            return (string)o["playId"];
        }

        /// <summary>The song ended: the server checks the score against the chart and keeps the player's best.</summary>
        public async Task<ScoreResult> SubmitScoreAsync(ScoreSubmission s)
        {
            var o = (JObject)await SendAsync("POST", "/scores", new JObject
            {
                ["playId"] = s.PlayId,
                ["score"] = s.Score,
                ["percent"] = s.Percent,
                ["misses"] = s.Misses,
                ["streak"] = s.Streak,
                ["perfect"] = s.Perfect,
                ["notes"] = s.Notes,
            }).ConfigureAwait(false);
            return new ScoreResult { Rank = (int)o["rank"], BestScore = (int)o["best"]["score"], BestPercent = (int)o["best"]["percent"], Total = (int)o["total"] };
        }

        /// <param name="version">null: the chart's latest version.</param>
        public async Task<BoardList> ScoresAsync(string chartId, int? version, int difficulty, int start, int count)
        {
            string query = $"?difficulty={difficulty}&start={start}&count={count}" + (version.HasValue ? "&version=" + version.Value : "");
            return Board((JObject)await SendAsync("GET", "/charts/" + Uri.EscapeDataString(chartId) + "/scores" + query).ConfigureAwait(false));
        }

        /// <summary>Entries of these Steam friends (and of the signed-in player), with their global rank.</summary>
        public async Task<BoardList> FriendScoresAsync(string chartId, int version, int difficulty, IEnumerable<string> steamIds)
        {
            var body = new JObject { ["version"] = version, ["difficulty"] = difficulty, ["ids"] = new JArray(steamIds.Take(500).ToArray()) };
            return Board((JObject)await SendAsync("POST", "/charts/" + Uri.EscapeDataString(chartId) + "/scores/friends", body).ConfigureAwait(false));
        }

        /// <summary>Reports a chart (5 to 300 characters); true when it got hidden by this report.</summary>
        public async Task<bool> ReportAsync(string chartId, string reason)
        {
            var o = (JObject)await SendAsync("POST", "/charts/" + Uri.EscapeDataString(chartId) + "/report", new JObject { ["reason"] = reason }).ConfigureAwait(false);
            return (bool?)o["hidden"] ?? false;
        }

        private static BoardList Board(JObject o) => new BoardList
        {
            Version = (int?)o["version"] ?? 1,
            Total = (int?)o["total"] ?? 0,
            Entries = ((JArray)o["entries"] ?? new JArray()).Select(e => Entry((JObject)e)).ToList(),
            Me = o["me"] is JObject me ? Entry(me) : null,
        };

        private static BoardEntry Entry(JObject e) => new BoardEntry
        {
            Rank = (int?)e["rank"] ?? 0,
            SteamId = (string)e["steamId"],
            Name = (string)e["name"],
            Score = (int?)e["score"] ?? 0,
            Percent = (int?)e["percent"] ?? 0,
        };

        private async Task<JToken> SendAsync(string method, string path, JObject body = null)
        {
            HttpResult result;
            try
            {
                result = await _http.SendAsync(method, _base + path, body?.ToString(Formatting.None), Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new MarketplaceException(0, "marketplace unreachable: " + e.GetBaseException().Message, e);
            }
            JToken json = null;
            if (!string.IsNullOrWhiteSpace(result.Body))
            {
                try { json = JToken.Parse(result.Body); }
                catch (JsonException) { }
            }
            if (result.Status < 200 || result.Status >= 300)
                throw new MarketplaceException(result.Status, (json as JObject)?["error"]?.ToString() ?? $"marketplace answered HTTP {result.Status}");
            if (json == null && result.Status != 204) throw new MarketplaceException(result.Status, "marketplace sent an unreadable answer");
            return json;
        }

        private static ChartSummary Summary(JObject o)
        {
            var notes = (JObject)o["notes"];
            return new ChartSummary
            {
                Id = (string)o["id"],
                Title = (string)o["title"],
                Artist = (string)o["artist"],
                Author = (string)o["author"],
                YoutubeId = (string)o["youtubeId"],
                Duration = (double?)o["duration"] ?? 0,
                Bpm = (double?)o["bpm"] ?? 0,
                Notes = new NoteCounts { Casual = (int?)notes?["casual"] ?? 0, Normal = (int?)notes?["normal"] ?? 0, Hard = (int?)notes?["hard"] ?? 0, Ultra = (int?)notes?["ultra"] ?? 0 },
                Version = (int?)o["version"] ?? 1,
                UpdatedAt = (long?)o["updatedAt"] ?? 0,
                Downloads = (int?)o["downloads"] ?? 0,
                Hidden = (bool?)o["hidden"] ?? false,
            };
        }
    }
}
