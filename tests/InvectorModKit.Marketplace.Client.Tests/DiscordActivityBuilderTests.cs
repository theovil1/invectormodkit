using InvectorModKit.Marketplace.Client.Discord;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>What the player's Discord profile shows for each moment of the game and the editor.</summary>
public class DiscordActivityBuilderTests
{
    private static readonly DateTime Start = new DateTime(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new DateTime(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc);

    private static long Ms(DateTime t) => new DateTimeOffset(t).ToUnixTimeMilliseconds();

    private static PresenceTexts Texts(string lang = "en") => new PresenceTexts
    {
        Menu = "In the main menu", SongSelect = "Choosing a song", Marketplace = "Browsing the marketplace",
        Editor = "Making a level", Paused = "Paused", Results = "Looking at the results",
        ScoreLine = "{0} pts · combo {1}", NotesOne = "{0} note", NotesMany = "{0} notes", CustomSong = "Custom song",
        GetMod = "Get InvectorModKit", JoinDiscord = "Join the Discord", ModVersion = "InvectorModKit 0.4.0",
        LanguageCode = lang, ModUrl = "https://github.com/theovil1/invectormodkit/releases/latest",
        DiscordUrl = "https://discord.gg/9wXNm5NM26",
    };

    private static PresenceSnapshot Level() => new PresenceSnapshot
    {
        Screen = PresenceScreen.Playing, Title = "Blinding Lights", Artist = "The Weeknd", Difficulty = "Hard",
        Score = 412300, Combo = 48, RemainingSeconds = 107,
    };

    private static JObject Build(PresenceSnapshot s, PresenceTexts t = null) => ActivityBuilder.Build(s, t ?? Texts(), Start, Now);

    [Fact]
    public void Level_ShowsSongDifficultyScoreAndCountdown()
    {
        var a = Build(Level());
        Assert.Equal(0, (int)a["type"]);
        Assert.Equal("Blinding Lights · The Weeknd", (string)a["details"]);
        Assert.Equal("Hard · 412300 pts · combo 48", (string)a["state"]);
        Assert.Equal(Ms(Now) + 107000, (long)a["timestamps"]["end"]);
        Assert.Null(a["timestamps"]["start"]);
        Assert.Equal("logo", (string)a["assets"]["large_image"]);
        Assert.Equal("InvectorModKit 0.4.0", (string)a["assets"]["large_text"]);
        Assert.Equal("hard", (string)a["assets"]["small_image"]);
        Assert.Equal("Hard", (string)a["assets"]["small_text"]);
    }

    [Fact]
    public void Level_NoArtist_ShowsTitleAlone()
    {
        var s = Level();
        s.Artist = "";
        Assert.Equal("Blinding Lights", (string)Build(s)["details"]);
    }

    [Fact]
    public void CustomSong_MarkedInLargeText()
    {
        var s = Level();
        s.CustomSong = true;
        Assert.Equal("InvectorModKit 0.4.0 · Custom song", (string)Build(s)["assets"]["large_text"]);
    }

    [Fact]
    public void Paused_HasNoTimestamps()
    {
        var s = Level();
        s.Screen = PresenceScreen.Paused;
        var a = Build(s);
        Assert.Equal("Blinding Lights · The Weeknd", (string)a["details"]);
        Assert.Equal("Paused", (string)a["state"]);
        Assert.Null(a["timestamps"]);
    }

    [Fact]
    public void Menu_ShowsScreenAndElapsed()
    {
        var a = Build(new PresenceSnapshot { Screen = PresenceScreen.Menu });
        Assert.Equal("In the main menu", (string)a["details"]);
        Assert.Equal(Ms(Start), (long)a["timestamps"]["start"]);
        Assert.Null(a["state"]);
        Assert.Null(a["assets"]["small_image"]);
    }

    [Theory]
    [InlineData(PresenceScreen.SongSelect, "Choosing a song")]
    [InlineData(PresenceScreen.Marketplace, "Browsing the marketplace")]
    [InlineData(PresenceScreen.Results, "Looking at the results")]
    public void OtherScreens_ShowTheirText(PresenceScreen screen, string details) =>
        Assert.Equal(details, (string)Build(new PresenceSnapshot { Screen = screen })["details"]);

    [Fact]
    public void Editor_ShowsSongDifficultyAndNotes()
    {
        var a = Build(new PresenceSnapshot { Screen = PresenceScreen.Editor, Title = "Blinding Lights", Difficulty = "Hard", Notes = 412 }, Texts("fr"));
        Assert.Equal("Making a level", (string)a["details"]);
        Assert.Equal("Blinding Lights · Hard · 412 notes", (string)a["state"]);
        Assert.Equal("hard", (string)a["assets"]["small_image"]);
        Assert.Equal(Ms(Start), (long)a["timestamps"]["start"]);
    }

    [Theory]
    [InlineData(1, "en", "1 note")]
    [InlineData(0, "fr", "0 note")]
    [InlineData(0, "en", "0 notes")]
    public void Editor_OneNote_UsesSingular(int notes, string lang, string expected)
    {
        var a = Build(new PresenceSnapshot { Screen = PresenceScreen.Editor, Title = "X", Difficulty = "Hard", Notes = notes }, Texts(lang));
        Assert.Equal("X · Hard · " + expected, (string)a["state"]);
    }

    [Fact]
    public void Editor_NoSong_HasNoState()
    {
        var a = Build(new PresenceSnapshot { Screen = PresenceScreen.Editor });
        Assert.Equal("Making a level", (string)a["details"]);
        Assert.Null(a["state"]);
        Assert.Null(a["assets"]["small_image"]);
    }

    [Fact]
    public void Hidden_BuildsNull() => Assert.Null(Build(new PresenceSnapshot { Screen = PresenceScreen.Hidden }));

    [Fact]
    public void Buttons_ModThenDiscord()
    {
        var b = (JArray)Build(Level())["buttons"];
        Assert.Equal(2, b.Count);
        Assert.Equal("Get InvectorModKit", (string)b[0]["label"]);
        Assert.Equal("https://github.com/theovil1/invectormodkit/releases/latest", (string)b[0]["url"]);
        Assert.Equal("Join the Discord", (string)b[1]["label"]);
        Assert.Equal("https://discord.gg/9wXNm5NM26", (string)b[1]["url"]);
    }

    [Fact]
    public void EmptyInvite_DropsDiscordButton()
    {
        var t = Texts();
        t.DiscordUrl = "";
        Assert.Single((JArray)Build(Level(), t)["buttons"]);
    }

    [Fact]
    public void LongTexts_AreTruncated()
    {
        var s = Level();
        s.Title = new string('a', 300);
        var t = Texts();
        t.GetMod = new string('b', 40);
        var a = Build(s, t);
        Assert.Equal(128, ((string)a["details"]).Length);
        Assert.Equal(32, ((string)a["buttons"][0]["label"]).Length);
    }

    [Fact]
    public void Truncation_NeverSplitsAnEmoji()
    {
        var s = Level();
        s.Artist = "";
        s.Title = new string('a', 127) + "😀";
        var details = (string)Build(s)["details"];
        Assert.Equal(127, details.Length);
    }
}
