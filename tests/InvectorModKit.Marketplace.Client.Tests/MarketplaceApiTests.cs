using InvectorModKit.Charting.Marketplace;
using InvectorModKit.Marketplace.Client;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>Answers every request with the next queued response and remembers what was sent.</summary>
internal sealed class FakeTransport : IHttpTransport
{
    public readonly Queue<HttpResult> Responses = new();
    public readonly List<(string Method, string Url, string Body, string Bearer)> Sent = new();

    public Task<HttpResult> SendAsync(string method, string url, string jsonBody, string bearer)
    {
        Sent.Add((method, url, jsonBody, bearer));
        return Task.FromResult(Responses.Dequeue());
    }

    public FakeTransport Reply(int status, string body)
    {
        Responses.Enqueue(new HttpResult { Status = status, Body = body });
        return this;
    }
}

public class MarketplaceApiTests
{
    private const string Summary = """
        {"id":"abc","title":"Viva La Vida","artist":"Coldplay","author":"Alice","youtubeId":"dvgZkm1xWPE","duration":242,"bpm":138,
         "notes":{"casual":100,"normal":200,"hard":300,"ultra":400},"version":2,"updatedAt":1760000000000,"downloads":7}
        """;

    [Fact]
    public async Task Search_BuildsQueryAndParses()
    {
        var http = new FakeTransport().Reply(200, $$"""{"items":[{{Summary}}],"page":1,"more":true}""");
        var page = await new MarketplaceApi("https://m.test/", http).SearchAsync("viva la", ChartSort.Popular, 1);
        Assert.Equal("GET", http.Sent[0].Method);
        Assert.Equal("https://m.test/charts?q=viva%20la&sort=popular&page=1", http.Sent[0].Url);
        Assert.True(page.More);
        var c = Assert.Single(page.Items);
        Assert.Equal(("abc", "Viva La Vida", "Coldplay", "Alice", "dvgZkm1xWPE"), (c.Id, c.Title, c.Artist, c.Author, c.YoutubeId));
        Assert.Equal((242.0, 138.0, 400, 2, 7), (c.Duration, c.Bpm, c.Notes.Ultra, c.Version, c.Downloads));
        Assert.Equal("https://www.youtube.com/watch?v=dvgZkm1xWPE", c.YoutubeUrl);
    }

    [Fact]
    public async Task Login_StoresToken()
    {
        var http = new FakeTransport()
            .Reply(200, """{"token":"t0k3n","steamId":"7656","name":"Theo","role":"admin"}""")
            .Reply(200, "[]");
        var api = new MarketplaceApi("https://m.test", http);
        var session = await api.LoginAsync("0a0b");
        Assert.Equal(("7656", "Theo", "admin"), (session.SteamId, session.Name, session.Role));
        Assert.Equal("t0k3n", api.Token);
        Assert.Equal("ticket", Assert.Single(JObject.Parse(http.Sent[0].Body).Properties()).Name);
        await api.MyChartsAsync();
        Assert.Equal(("https://m.test/me/charts", "t0k3n"), (http.Sent[1].Url, http.Sent[1].Bearer));
    }

    [Fact]
    public async Task Publish_SendsBearer()
    {
        var http = new FakeTransport().Reply(200, Summary);
        var api = new MarketplaceApi("https://m.test", http) { Token = "t0k3n" };
        var published = await api.PublishAsync(new PublishRequest
        {
            Title = "Viva La Vida", Artist = "Coldplay", YoutubeUrl = "https://youtu.be/dvgZkm1xWPE", Duration = 242,
            ChartJson = """{"bpm":138,"notes":{}}""", FingerprintJson = """{"rate":100,"duration":242,"data":""}""",
        });
        Assert.Equal("abc", published.Id);
        var (method, url, body, bearer) = http.Sent[0];
        Assert.Equal(("POST", "https://m.test/charts", "t0k3n"), (method, url, bearer));
        var o = JObject.Parse(body);
        Assert.Equal(138, (int)o["chart"]["bpm"]); // sent as an object, not a string
        Assert.Equal(100, (int)o["fingerprint"]["rate"]);
        Assert.Equal("https://youtu.be/dvgZkm1xWPE", (string)o["youtubeUrl"]);
    }

    [Fact]
    public async Task Api_ServerError_ThrowsMarketplaceException()
    {
        var api = new MarketplaceApi("https://m.test", new FakeTransport()
            .Reply(403, """{"error":"publishing is invite-only for now"}""")
            .Reply(502, "<html>Bad gateway</html>")
            .Reply(200, "not json"));
        var e = await Assert.ThrowsAsync<MarketplaceException>(() => api.MyChartsAsync());
        Assert.Equal((403, "publishing is invite-only for now"), (e.Status, e.Message));
        e = await Assert.ThrowsAsync<MarketplaceException>(() => api.SearchAsync("", ChartSort.Recent, 0));
        Assert.Equal(502, e.Status);
        await Assert.ThrowsAsync<MarketplaceException>(() => api.DownloadAsync("abc"));
    }

    [Fact]
    public async Task Install_WritesPendingLoadable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "imk-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var http = new FakeTransport().Reply(200, $$$"""
                {"summary":{{{Summary}}},
                 "chart":{"format":1,"bpm":138,"end":240,"sections":[{"t":0,"mode":"List"}],"notes":{"Casual":[],"Normal":[],"Hard":[],"Ultra":[{"t":10}]}},
                 "fingerprint":{"rate":100,"duration":242,"data":"AAEC"}}
                """);
            var download = await new MarketplaceApi("https://m.test", http).DownloadAsync("abc");
            var store = new InstallStore(dir);
            store.Install(download);
            var pending = Assert.Single(PendingInstall.Load(store.MarketplaceDir));
            Assert.Equal(("abc", "Viva La Vida", "Coldplay", "Alice", 2), (pending.Id, pending.Title, pending.Artist, pending.Author, pending.Version));
            Assert.Equal(new byte[] { 0, 1, 2 }, pending.Fingerprint.Envelope);
            Assert.Equal(10, pending.ChartFor(new Charting.Audio.FingerprintMatch { Offset = 0 })[Charting.Difficulty.Ultra][0].Time);
            Assert.True(store.IsDownloaded("abc"));

            store.Remove("abc");
            Assert.Empty(PendingInstall.Load(store.MarketplaceDir));
            Assert.False(store.IsDownloaded("abc"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
