using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>The game's menu navigation, held off while the marketplace is open and given back on close.</summary>
public class HeldFlagTests
{
    [Fact]
    public void Release_GivesBackTheValueSeenOnHold()
    {
        var flag = new HeldFlag();
        flag.Hold(current: true, forced: false);
        Assert.False(flag.Watch(false));
        Assert.True(flag.Release());
    }

    [Fact]
    public void GameTurnsItOnWhileHeld_OnIsGivenBack()
    {
        // Opened during song select's intro (navigation off), which turns it on 1.5 s later behind the marketplace.
        var flag = new HeldFlag();
        flag.Hold(current: false, forced: false);
        Assert.False(flag.Watch(true));
        Assert.True(flag.Release());
    }

    [Fact]
    public void GameTurnsItOnThenOffWhileHeld_TheLastOneSeenIsGivenBack()
    {
        var flag = new HeldFlag();
        flag.Hold(current: false, forced: false);
        flag.Watch(true);
        flag.Watch(false); // the forced value again: either ours or the game's, the last change seen stays
        Assert.True(flag.Release());
    }

    [Fact]
    public void Release_OnceOnly()
    {
        var flag = new HeldFlag();
        Assert.Null(flag.Release());
        flag.Hold(current: true, forced: false);
        flag.Hold(current: false, forced: false); // a second hold keeps the first value
        Assert.True(flag.Held);
        Assert.True(flag.Release());
        Assert.False(flag.Held);
        Assert.Null(flag.Release());
    }

    [Fact]
    public void Watch_NotHeld_LeavesTheValue()
    {
        var flag = new HeldFlag();
        Assert.True(flag.Watch(true));
        Assert.False(flag.Watch(false));
    }
}
