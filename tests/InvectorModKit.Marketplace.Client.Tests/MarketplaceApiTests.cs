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
    public async Task Reports_ReadsChartsAndDetails()
    {
        var http = new FakeTransport().Reply(200, """
            [{"id":"abc","title":"Kyiv","artist":"Apashe","author":"Bob","authorSteamId":"76561190000000020","hidden":1,"version":2,"reports":3,"last":1760000000000,
              "details":[{"chartId":"abc","reason":"wrong video","at":1760000000000,"name":"Alice","steamId":"76561190000000011"}]}]
            """);
        var reports = await new MarketplaceApi("https://m.test", http) { Token = "t" }.ReportsAsync();
        var r = reports[0];
        Assert.Equal(("abc", "Bob", "76561190000000020", true, 2, 3), (r.Id, r.Author, r.AuthorSteamId, r.Hidden, r.Version, r.Reports));
        Assert.Equal(("wrong video", "Alice", "76561190000000011", 1760000000000L), (r.Details[0].Reason, r.Details[0].Name, r.Details[0].SteamId, r.Details[0].At));
        Assert.Equal(("GET", "https://m.test/admin/api/reports", "t"), (http.Sent[0].Method, http.Sent[0].Url, http.Sent[0].Bearer));
    }

    [Fact]
    public async Task ChartAction_SendsIdAndAction()
    {
        var http = new FakeTransport().Reply(200, """{"ok":true}""");
        await new MarketplaceApi("https://m.test", http) { Token = "t" }.ChartActionAsync("abc", "hide");
        var o = JObject.Parse(http.Sent[0].Body);
        Assert.Equal(("POST", "https://m.test/admin/api/chart", "abc", "hide"), (http.Sent[0].Method, http.Sent[0].Url, (string)o["id"], (string)o["action"]));
    }

    [Fact]
    public async Task DeleteScore_SendsBoardNumbers()
    {
        var http = new FakeTransport().Reply(200, """{"ok":true}""");
        await new MarketplaceApi("https://m.test", http) { Token = "t" }.DeleteScoreAsync("76561190000000011", "abc", 3, 2);
        var o = JObject.Parse(http.Sent[0].Body);
        Assert.Equal("https://m.test/admin/api/score", http.Sent[0].Url);
        Assert.Equal(JTokenType.Integer, o["version"].Type);
        Assert.Equal(("76561190000000011", "abc", 3, 2), ((string)o["steamId"], (string)o["chartId"], (int)o["version"], (int)o["difficulty"]));
    }

    [Fact]
    public async Task SetRole_SendsSteamIdAndRole()
    {
        var http = new FakeTransport().Reply(200, """{"ok":true}""");
        await new MarketplaceApi("https://m.test", http) { Token = "t" }.SetRoleAsync("76561190000000011", "banned");
        var o = JObject.Parse(http.Sent[0].Body);
        Assert.Equal(("https://m.test/admin/api/user", "76561190000000011", "banned"), (http.Sent[0].Url, (string)o["steamId"], (string)o["role"]));
    }

    [Fact]
    public async Task Banned_AsksBannedOnly()
    {
        var http = new FakeTransport().Reply(200, """[{"steamId":"76561190000000021","name":"Mallory","role":"banned","createdAt":1,"bannedAt":1760000000000,"charts":2,"scores":5}]""");
        var users = await new MarketplaceApi("https://m.test", http) { Token = "t" }.BannedAsync();
        Assert.Equal("https://m.test/admin/api/users?banned=1", http.Sent[0].Url);
        Assert.Equal(("Mallory", (long?)1760000000000L, 2, 5), (users[0].Name, users[0].BannedAt, users[0].Charts, users[0].Scores));
    }

    [Fact]
    public async Task AdminCharts_SearchesAndReads()
    {
        var http = new FakeTransport().Reply(200, """[{"id":"abc","title":"Kyiv","artist":"Apashe","hidden":0,"version":1,"downloads":4,"updatedAt":5,"author":"Bob","authorSteamId":"76561190000000020","reports":1}]""");
        var charts = await new MarketplaceApi("https://m.test", http) { Token = "t" }.AdminChartsAsync("ky iv");
        Assert.Equal("https://m.test/admin/api/charts?q=ky%20iv", http.Sent[0].Url);
        Assert.Equal(("abc", false, 4, "76561190000000020", 1), (charts[0].Id, charts[0].Hidden, charts[0].Downloads, charts[0].AuthorSteamId, charts[0].Reports));
    }

    [Fact]
    public async Task MyCharts_ReadsStats()
    {
        string mine = Summary.Replace("\"downloads\":7", "\"downloads\":7,\"players\":3,\"reports\":1");
        var http = new FakeTransport().Reply(200, $"[{mine}]");
        var charts = await new MarketplaceApi("https://m.test", http) { Token = "t" }.MyChartsAsync();
        Assert.Equal((3, 1), (charts[0].Players, charts[0].Reports));
    }

    [Fact]
    public async Task MyCharts_StatsMissingAreNull()
    {
        var http = new FakeTransport().Reply(200, $"[{Summary}]");
        var charts = await new MarketplaceApi("https://m.test", http) { Token = "t" }.MyChartsAsync();
        Assert.Equal(((int?)null, (int?)null), (charts[0].Players, charts[0].Reports));
    }

    [Fact]
    public async Task ChangeVideo_SendsPatchWithBearer()
    {
        var http = new FakeTransport().Reply(200, Summary);
        var changed = await new MarketplaceApi("https://m.test", http) { Token = "t0k3n" }.ChangeVideoAsync("abc", "https://youtu.be/abcdefghijk");
        Assert.Equal("abc", changed.Id);
        var (method, url, body, bearer) = http.Sent[0];
        Assert.Equal(("PATCH", "https://m.test/charts/abc", "t0k3n"), (method, url, bearer));
        Assert.Equal("https://youtu.be/abcdefghijk", (string)JObject.Parse(body)["youtubeUrl"]);
    }

    [Fact]
    public async Task Search_ReadsPreviewTime()
    {
        string withPreview = Summary.Replace("\"downloads\":7", "\"downloads\":7,\"preview\":61.5");
        var http = new FakeTransport().Reply(200, $$"""{"items":[{{withPreview}}],"page":0,"more":false}""");
        var page = await new MarketplaceApi("https://m.test/", http).SearchAsync("", ChartSort.Recent, 0);
        Assert.Equal(61.5, page.Items[0].Preview);
    }

    [Fact]
    public async Task Search_PreviewMissingIsNull()
    {
        var http = new FakeTransport().Reply(200, $$"""{"items":[{{Summary}}],"page":0,"more":false}""");
        var page = await new MarketplaceApi("https://m.test/", http).SearchAsync("", ChartSort.Recent, 0);
        Assert.Null(page.Items[0].Preview);
    }

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
    public async Task BrowserLogin_PollsUntilSignedIn()
    {
        var http = new FakeTransport()
            .Reply(202, """{"pending":true}""")
            .Reply(202, """{"pending":true}""")
            .Reply(200, """{"token":"t0k3n","steamId":"7656","name":"Theo","role":"admin"}""");
        var api = new MarketplaceApi("https://m.test", http);
        var login = api.StartBrowserLogin();
        Assert.Matches("^[0-9a-f]{64}$", login.Code);
        Assert.Equal($"https://m.test/auth/steam/openid/start?code={login.Code}", login.Url);
        Assert.NotEqual(login.Code, api.StartBrowserLogin().Code);

        var session = await api.WaitForBrowserLoginAsync(login, TimeSpan.FromMilliseconds(1));
        Assert.Equal(("Theo", "t0k3n"), (session.Name, api.Token));
        Assert.All(http.Sent, s => Assert.Equal($"https://m.test/auth/steam/openid/poll?code={login.Code}", s.Url));
        Assert.Equal(3, http.Sent.Count);
    }

    [Fact]
    public async Task Me_KeepsToken()
    {
        var http = new FakeTransport().Reply(200, """{"steamId":"7656","name":"Teyko","role":"admin"}""");
        var me = await new MarketplaceApi("https://m.test", http) { Token = "t0k3n" }.MeAsync();
        Assert.Equal(("7656", "Teyko", "admin", "t0k3n"), (me.SteamId, me.Name, me.Role, me.Token));
        Assert.Equal(("https://m.test/me", "t0k3n"), (http.Sent[0].Url, http.Sent[0].Bearer));
    }

    [Fact]
    public async Task BrowserLogin_Expired_Throws()
    {
        var api = new MarketplaceApi("https://m.test", new FakeTransport().Reply(404, """{"error":"sign-in expired"}"""));
        var e = await Assert.ThrowsAsync<MarketplaceException>(() => api.WaitForBrowserLoginAsync(api.StartBrowserLogin(), TimeSpan.FromMilliseconds(1)));
        Assert.Equal(404, e.Status);
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
