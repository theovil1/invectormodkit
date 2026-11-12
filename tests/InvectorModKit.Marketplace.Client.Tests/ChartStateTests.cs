using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>What the sheet's main button says and does, from what is known about the chart.</summary>
public class ChartStateTests
{
    [Fact]
    public void State_NotInstalledOffersInstall()
    {
        var b = MainButton.For(new ChartFacts { LatestVersion = 1 });
        Assert.Equal((MainKind.Install, true, "main_install", "main_install_sub"), (b.Kind, b.Enabled, b.LabelKey, b.SubKey));
    }

    [Fact]
    public void State_GoneSaysSo()
    {
        var b = MainButton.For(new ChartFacts { Gone = true, LatestVersion = 1 });
        Assert.Equal((MainKind.Gone, false, "main_gone", "main_gone_sub"), (b.Kind, b.Enabled, b.LabelKey, b.SubKey));
    }

    [Fact]
    public void State_GoneKeepsWhatIsInTheGame()
    {
        // Installed: still playable, but no update any more. Music missing: it comes from YouTube, not the marketplace.
        Assert.Equal(MainKind.Installed, MainButton.For(new ChartFacts { Gone = true, Installed = true, InstalledVersion = 1, LatestVersion = 2 }).Kind);
        Assert.Equal(MainKind.Retry, MainButton.For(new ChartFacts { Gone = true, Downloaded = true, LatestVersion = 1 }).Kind);
    }

    [Fact]
    public void State_GoneOnlyForAMissingChart()
    {
        Assert.True(MainButton.IsGone(new MarketplaceException(404, "chart not found")));
        Assert.True(MainButton.IsGone(new System.AggregateException(new MarketplaceException(404, "chart not found"))));
        Assert.False(MainButton.IsGone(new MarketplaceException(404, "version not found")));
        Assert.False(MainButton.IsGone(new MarketplaceException(0, "unreachable")));
    }

    [Fact]
    public void State_DownloadingShowsChartStep()
    {
        var b = MainButton.For(new ChartFacts { Downloading = true, Progress = 0.42, LatestVersion = 1 });
        Assert.Equal((MainKind.Installing, false, 42, "main_installing", "main_step_chart"), (b.Kind, b.Enabled, b.Percent, b.LabelKey, b.SubKey));
    }

    [Fact]
    public void State_FetchingShowsMusicStep()
    {
        var b = MainButton.For(new ChartFacts { Downloaded = true, Fetching = true, Progress = 0.7, LatestVersion = 1 });
        Assert.Equal((MainKind.Installing, 70, "main_step_music"), (b.Kind, b.Percent, b.SubKey));
    }

    [Fact]
    public void State_MusicOnDiskShowsPreparing()
    {
        // The music is fetched; the game is still preparing the song (matching it with the chart, charting it).
        var b = MainButton.For(new ChartFacts { Downloaded = true, Matching = true, Progress = 1, LatestVersion = 1 });
        Assert.Equal((MainKind.Installing, false, 100, "main_step_prepare"), (b.Kind, b.Enabled, b.Percent, b.SubKey));
    }

    [Fact]
    public void State_FailedFetchOffersRetryWithReason()
    {
        var b = MainButton.For(new ChartFacts { Downloaded = true, FetchError = "Video unavailable", LatestVersion = 1 });
        Assert.Equal((MainKind.Retry, true, "main_retry", "main_retry_sub", "Video unavailable"), (b.Kind, b.Enabled, b.LabelKey, b.SubKey, b.Reason));
    }

    [Fact]
    public void State_OtherVersionOffersRetry()
    {
        var b = MainButton.For(new ChartFacts { Downloaded = true, OtherVersion = true, LatestVersion = 1 });
        Assert.Equal((MainKind.Retry, "other_version", null), (b.Kind, b.SubKey, b.Reason));
    }

    [Fact]
    public void State_InstalledSaysWhere()
    {
        var b = MainButton.For(new ChartFacts { Installed = true, InstalledVersion = 1, LatestVersion = 1 });
        Assert.Equal((MainKind.Installed, false, "main_installed", "main_installed_sub"), (b.Kind, b.Enabled, b.LabelKey, b.SubKey));
    }

    [Fact]
    public void State_NewerVersionOffersUpdate()
    {
        var b = MainButton.For(new ChartFacts { Installed = true, InstalledVersion = 1, LatestVersion = 2 });
        Assert.Equal((MainKind.Update, true, "main_update", "main_update_sub"), (b.Kind, b.Enabled, b.LabelKey, b.SubKey));
    }

    [Fact]
    public void State_MineWinsOverInstalled()
    {
        var b = MainButton.For(new ChartFacts { Mine = true, Installed = true, InstalledVersion = 1, LatestVersion = 2 });
        Assert.Equal((MainKind.Mine, false, "main_mine", null), (b.Kind, b.Enabled, b.LabelKey, b.SubKey));
    }
}
