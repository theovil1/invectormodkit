using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>A sheet's buttons act on the item shown now, even when the buttons are not built again.</summary>
public class ActionRowTests
{
    [Fact]
    public void ActionRow_SameLabelsActOnTheNewItem()
    {
        var row = new ActionRow();
        string done = null;
        Assert.True(row.Set(new[] { "Delete" }, new System.Action[] { () => done = "A" }));
        Assert.False(row.Set(new[] { "Delete" }, new System.Action[] { () => done = "B" }));
        row.Invoke(0);
        Assert.Equal("B", done);
    }

    [Fact]
    public void ActionRow_NewLabelsOrResetRebuild()
    {
        var row = new ActionRow();
        row.Set(new[] { "Delete" }, new System.Action[] { () => { } });
        Assert.True(row.Set(new[] { "Delete", "Ban" }, new System.Action[] { () => { }, () => { } }));
        Assert.True(row.Set(new[] { "Delete", "Ban" }, new System.Action[] { () => { }, () => { } }, extraKey: "more"));
        row.Reset();
        Assert.True(row.Set(new[] { "Delete", "Ban" }, new System.Action[] { () => { }, () => { } }, extraKey: "more"));
    }

    [Fact]
    public void ActionRow_UnknownButtonDoesNothing()
    {
        var row = new ActionRow();
        row.Set(new string[0], new System.Action[0]);
        row.Invoke(3);
    }
}
