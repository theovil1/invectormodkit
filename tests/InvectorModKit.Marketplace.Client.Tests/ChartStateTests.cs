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
        Assert.Equal((MainKind.Installed, "main_installed", "main_installed_sub"), (b.Kind, b.LabelKey, b.SubKey));
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
