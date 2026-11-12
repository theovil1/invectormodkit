using System;
using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>A value read from a file is read again only once the file changed.</summary>
public class StampedCacheTests
{
    [Fact]
    public void Stamped_ReadsAgainOnlyWhenTheFileChanged()
    {
        var cache = new StampedCache();
        int reads = 0;
        var monday = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal("h1", cache.Get("song", monday, () => { reads++; return "h1"; }));
        Assert.Equal("h1", cache.Get("song", monday, () => { reads++; return "h2"; }));
        Assert.Equal(1, reads);
        Assert.Equal("h2", cache.Get("song", monday.AddSeconds(1), () => { reads++; return "h2"; }));
        Assert.Equal(2, reads);
    }
}
