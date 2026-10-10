using InvectorModKit.Marketplace.Client;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Marketplace.Client.Tests;

public class LeaderboardApiTests
{
    private static MarketplaceApi Api(FakeTransport http) => new("https://m.test", http) { Token = "t0k3n" };

    [Fact]
    public async Task StartPlay_And_SubmitScore()
    {
        var http = new FakeTransport()
            .Reply(200, """{"playId":"p1"}""")
            .Reply(200, """{"rank":2,"best":{"score":2000,"percent":97},"total":5}""");
        var api = Api(http);
        Assert.Equal("p1", await api.StartPlayAsync("abc", 3, 2));
        Assert.Equal(JToken.Parse("""{"chartId":"abc","version":3,"difficulty":2}"""), JToken.Parse(http.Sent[0].Body), JToken.EqualityComparer);

        var result = await api.SubmitScoreAsync(new ScoreSubmission { PlayId = "p1", Score = 2000, Percent = 97, Misses = 1, Streak = 25, Perfect = 20, Notes = 30 });
        Assert.Equal(("https://m.test/scores", "t0k3n"), (http.Sent[1].Url, http.Sent[1].Bearer));
        Assert.Equal(JToken.Parse("""{"playId":"p1","score":2000,"percent":97,"misses":1,"streak":25,"perfect":20,"notes":30}"""), JToken.Parse(http.Sent[1].Body), JToken.EqualityComparer);
        Assert.Equal((2, 2000, 97, 5), (result.Rank, result.BestScore, result.BestPercent, result.Total));
    }

    [Fact]
    public async Task Scores_Global_And_Friends()
    {
        const string board = """{"version":3,"total":2,"entries":[{"rank":1,"steamId":"1","name":"Bob","score":3000,"percent":99},{"rank":2,"steamId":"2","name":"Alice","score":2000,"percent":97}],"me":{"rank":2,"steamId":"2","name":"Alice","score":2000,"percent":97}}""";
        var http = new FakeTransport().Reply(200, board).Reply(200, """{"version":3,"total":0,"entries":[],"me":null}""");
        var api = Api(http);
        var global = await api.ScoresAsync("abc", null, 2, 0, 10);
        Assert.Equal("https://m.test/charts/abc/scores?difficulty=2&start=0&count=10", http.Sent[0].Url);
        Assert.Equal(new[] { "Bob", "Alice" }, global.Entries.Select(e => e.Name));
        Assert.Equal((2, 2000), (global.Me.Rank, global.Me.Score));

        var friends = await api.FriendScoresAsync("abc", 3, 1, new[] { "76561190000000005" });
        Assert.Equal("https://m.test/charts/abc/scores/friends", http.Sent[1].Url);
        Assert.Equal(JToken.Parse("""{"version":3,"difficulty":1,"ids":["76561190000000005"]}"""), JToken.Parse(http.Sent[1].Body), JToken.EqualityComparer);
        Assert.Null(friends.Me);
        Assert.Empty(friends.Entries);
    }

    [Fact]
    public async Task Report_Rules_Language()
    {
        var http = new FakeTransport().Reply(200, """{"reports":3,"hidden":true}""");
        var api = Api(http);
        Assert.True(await api.ReportAsync("abc", "wrong video"));
        Assert.Equal(JToken.Parse("""{"reason":"wrong video"}"""), JToken.Parse(http.Sent[0].Body), JToken.EqualityComparer);
        Assert.Equal("https://m.test/rules?lang=pt-BR", api.RulesUrl("pt-BR"));
        Assert.Equal("https://m.test/rules", api.RulesUrl());
        Assert.EndsWith("&lang=ja", api.StartBrowserLogin("ja").Url);
    }

    [Fact]
    public async Task Publish_SendsAcceptRules_And_Rules428IsReported()
    {
        var http = new FakeTransport().Reply(428, """{"error":"rules"}""");
        var e = await Assert.ThrowsAsync<MarketplaceException>(() => Api(http).PublishAsync(new PublishRequest
        {
            Title = "T", Artist = "A", YoutubeUrl = "https://youtu.be/dvgZkm1xWPE", Duration = 200, ChartJson = "{}", FingerprintJson = "{}", AcceptRules = true,
        }));
        Assert.Equal((428, "rules"), (e.Status, e.Message));
        Assert.True((bool)JObject.Parse(http.Sent[0].Body)["acceptRules"]!);
    }
}
