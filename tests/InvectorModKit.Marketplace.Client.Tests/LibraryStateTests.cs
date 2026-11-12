using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>What the main button of a My library sheet says and does.</summary>
public class LibraryStateTests
{
    [Fact]
    public void Library_OwnUnpublishedOffersPublish()
    {
        var b = LibraryState.For(new LibraryFacts { Mine = true, LinkValid = true });
        Assert.Equal(("main_publish", true, true, "only_chart_sent"), (b.LabelKey, b.Enabled, b.Accent, b.SubKey));
    }

    [Fact]
    public void Library_NoLinkCannotPublish()
    {
        var b = LibraryState.For(new LibraryFacts { Mine = true, LinkValid = false });
        Assert.Equal(("main_publish", false, "link_needed"), (b.LabelKey, b.Enabled, b.SubKey));
    }

    [Fact]
    public void Library_PublishedSaysWhere()
    {
        var b = LibraryState.For(new LibraryFacts { Mine = true, Published = true, PublishedVersion = 3, LinkValid = true });
        Assert.Equal(("main_published", false, "main_published_sub"), (b.LabelKey, b.Enabled, b.SubKey));
        Assert.Equal(new object[] { 3 }, b.SubArgs);
    }

    [Fact]
    public void Library_MarketplaceSongSaysWhose()
    {
        var b = LibraryState.For(new LibraryFacts { Mine = false, Author = "Alice" });
        Assert.Equal(("main_installed", false, "origin_marketplace"), (b.LabelKey, b.Enabled, b.SubKey));
        Assert.Equal(new object[] { "Alice" }, b.SubArgs);
    }

    [Fact]
    public void Library_PublishingShowsItsStep()
    {
        var b = LibraryState.For(new LibraryFacts { Mine = true, LinkValid = true, Publishing = true, PublishStep = "sending" });
        Assert.Equal(("main_publishing", false, "sending"), (b.LabelKey, b.Enabled, b.SubKey));
    }

    [Fact]
    public void Library_AddingShowsPercent()
    {
        var b = LibraryState.For(new LibraryFacts { Adding = true, Progress = 0.42 });
        Assert.Equal(("main_adding", false, 42, "add_step_download"), (b.LabelKey, b.Enabled, b.Percent, b.SubKey));
        Assert.Equal(new object[] { 42 }, b.LabelArgs);
    }

    [Fact]
    public void Library_PreparingAfterTheDownload()
    {
        var b = LibraryState.For(new LibraryFacts { Adding = true, Preparing = true, Progress = 1 });
        Assert.Equal(("main_adding", 100, "main_step_prepare"), (b.LabelKey, b.Percent, b.SubKey));
    }

    [Fact]
    public void Library_AddFailedShowsReason()
    {
        var b = LibraryState.For(new LibraryFacts { AddError = "link_exists" });
        Assert.Equal(("main_add_failed", false, true, "link_exists"), (b.LabelKey, b.Enabled, b.Warning, b.SubKey));
    }
}
