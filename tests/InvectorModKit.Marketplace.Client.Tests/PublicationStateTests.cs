using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>A published chart's state in My publications: up to date, changed since, hidden, not on this PC.</summary>
public class PublicationStateTests
{
    private static PublicationFacts Facts(string local = "aaa", string published = "aaa") => new PublicationFacts
    {
        ServerVersion = 2, LocalSongFound = true, LocalHash = local, PublishedHash = published, NotesMatch = true,
    };

    [Fact]
    public void Publication_UpToDate()
    {
        var v = PublicationState.For(Facts());
        Assert.Equal((PublicationRow.UpToDate, false, "main_up_to_date", false), (v.Row, v.Changed, v.Main.LabelKey, v.Main.Enabled));
    }

    [Fact]
    public void Publication_ChangedOffersNextVersion()
    {
        var v = PublicationState.For(Facts(local: "bbb"));
        Assert.Equal((PublicationRow.Changed, true, "main_publish_version", true, true, "changed_warning"),
                     (v.Row, v.Changed, v.Main.LabelKey, v.Main.Enabled, v.Main.Accent, v.Main.SubKey));
        Assert.Equal(new object[] { 3 }, v.Main.LabelArgs);
        Assert.Equal(new object[] { 2 }, v.Main.SubArgs);
    }

    [Fact]
    public void Publication_NoHashComparesNotes()
    {
        var same = Facts(published: null);
        Assert.Equal(PublicationRow.UpToDate, PublicationState.For(same).Row);
        var other = Facts(published: null);
        other.NotesMatch = false;
        Assert.Equal(PublicationRow.Changed, PublicationState.For(other).Row);
    }

    [Fact]
    public void Publication_NoLocalSongIsNotOnThisPc()
    {
        var f = Facts(local: null);
        f.LocalSongFound = false;
        var v = PublicationState.For(f);
        Assert.Equal((PublicationRow.NotOnThisPc, false, "main_not_on_pc", false, "not_on_pc_sub"),
                     (v.Row, v.Changed, v.Main.LabelKey, v.Main.Enabled, v.Main.SubKey));
    }

    [Fact]
    public void Publication_HiddenRowEvenWhenChanged()
    {
        var f = Facts(local: "bbb");
        f.Hidden = true;
        f.Reports = 4;
        var v = PublicationState.For(f);
        Assert.Equal((PublicationRow.Hidden, true, "main_publish_version"), (v.Row, v.Changed, v.Main.LabelKey));
    }

    [Fact]
    public void Publication_PublishingWaits()
    {
        var f = Facts(local: "bbb");
        f.Publishing = true;
        var v = PublicationState.For(f);
        Assert.Equal(("main_publishing", false), (v.Main.LabelKey, v.Main.Enabled));
    }
}
