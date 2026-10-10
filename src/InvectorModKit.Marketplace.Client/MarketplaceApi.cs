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
            return Summary((JObject)await SendAsync("POST", "/charts", body).ConfigureAwait(false));
        }

        public Task DeleteAsync(string id) => SendAsync("DELETE", "/charts/" + Uri.EscapeDataString(id));

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
            };
        }
    }
}
