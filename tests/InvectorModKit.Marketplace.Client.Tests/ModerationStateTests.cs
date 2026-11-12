using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>The Moderation tab's main buttons and what a refused action says.</summary>
public class ModerationStateTests
{
    [Fact]
    public void Moderation_HiddenOffersShow()
    {
        var b = ModerationState.ForChart(hidden: true, busy: false);
        Assert.Equal(("mod_show", true, true), (b.LabelKey, b.Enabled, b.Accent));
    }

    [Fact]
    public void Moderation_VisibleOffersHide()
    {
        var b = ModerationState.ForChart(hidden: false, busy: false);
        Assert.Equal(("mod_hide", true, true), (b.LabelKey, b.Enabled, b.Accent));
    }

    [Fact]
    public void Moderation_BusyWaits()
    {
        Assert.False(ModerationState.ForChart(hidden: false, busy: true).Enabled);
        Assert.False(ModerationState.ForPlayer(busy: true).Enabled);
    }

    [Fact]
    public void Moderation_PlayerOffersLift()
    {
        var b = ModerationState.ForPlayer(busy: false);
        Assert.Equal(("mod_lift_ban", true), (b.LabelKey, b.Enabled));
    }

    [Fact]
    public void Moderation_NoBanOfYourself()
    {
        // The server refuses to change an admin's own role: the game does not offer it.
        Assert.True(ModerationState.CanBan("76561190000000001", "76561190000000002"));
        Assert.False(ModerationState.CanBan("76561190000000001", "76561190000000001"));
        Assert.False(ModerationState.CanBan(null, "76561190000000001"));
    }

    [Fact]
    public void Moderation_RefusedSaysNotAdmin() =>
        Assert.Equal("mod_not_admin", ModerationState.Refused(403));

    [Fact]
    public void Moderation_OldServerSaysSo()
    {
        // A server from before moderation in the game wants its web page's cookie: 401 to the game's token.
        Assert.Equal("server_too_old", ModerationState.Refused(401));
        Assert.Equal("server_too_old", ModerationState.Refused(404));
        Assert.Null(ModerationState.Refused(500));
    }
}
