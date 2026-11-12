using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>Discover's one-click filters, on the page the server sent (its order kept).</summary>
public class CatalogueFilterTests
{
    private static ChartSummary Chart(string id, int casual = 10, int normal = 20, int hard = 30, int ultra = 40) =>
        new ChartSummary { Id = id, Notes = new NoteCounts { Casual = casual, Normal = normal, Hard = hard, Ultra = ultra } };

    private static readonly ChartSummary[] Page = { Chart("c"), Chart("a"), Chart("b") };
    private static readonly HashSet<string> Installed = new() { "a" };

    private static string[] Ids(IEnumerable<ChartSummary> items) => items.Select(c => c.Id).ToArray();

    [Fact]
    public void Filter_AllKeepsOrder() =>
        Assert.Equal(new[] { "c", "a", "b" }, Ids(CatalogueFilter.Apply(Page, Installed, Shown.All, null)));

    [Fact]
    public void Filter_NotInstalledHidesInstalled() =>
        Assert.Equal(new[] { "c", "b" }, Ids(CatalogueFilter.Apply(Page, Installed, Shown.NotInstalled, null)));

    [Fact]
    public void Filter_InstalledOnly() =>
        Assert.Equal(new[] { "a" }, Ids(CatalogueFilter.Apply(Page, Installed, Shown.Installed, null)));

    [Fact]
    public void Filter_DifficultyKeepsChartsWithNotesThere()
    {
        var ultraOnly = Chart("u", 0, 0, 0, 120);
        Assert.Equal(new[] { "u" }, Ids(CatalogueFilter.Apply(new[] { ultraOnly }, Installed, Shown.All, 3)));
        Assert.Empty(CatalogueFilter.Apply(new[] { ultraOnly }, Installed, Shown.All, 2));
    }
}
