using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

public class ReleasesTests
{
    // The shape of api.github.com/repos/{owner}/{repo}/releases/latest (trimmed).
    private const string Latest = """
    {
      "tag_name": "v0.2.0",
      "name": "InvectorModKit 0.2.0",
      "assets": [
        { "name": "InvectorModKit-0.2.0.zip", "browser_download_url": "https://github.com/theovil1/InvectorModKit/releases/download/v0.2.0/InvectorModKit-0.2.0.zip" },
        { "name": "InvectorModKitSetup.exe", "browser_download_url": "https://github.com/theovil1/InvectorModKit/releases/download/v0.2.0/InvectorModKitSetup.exe" }
      ]
    }
    """;

    [Fact]
    public void Parse_ReadsVersionAndSetupUrl()
    {
        var release = Releases.Parse(Latest);
        Assert.Equal("0.2.0", release.Version);
        Assert.Equal("https://github.com/theovil1/InvectorModKit/releases/download/v0.2.0/InvectorModKitSetup.exe", release.SetupUrl);
    }

    [Fact]
    public void Parse_ErrorOrMissingAsset_IsNull()
    {
        Assert.Null(Releases.Parse("{\"message\":\"API rate limit exceeded\"}"));
        Assert.Null(Releases.Parse("{\"tag_name\":\"v0.2.0\",\"assets\":[]}"));
        Assert.Null(Releases.Parse("not json"));
        Assert.Null(Releases.Parse(null));
    }

    [Theory]
    [InlineData("v0.10.0", "0.9.1", true)]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("v1.0", "0.9.9", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("v0.1.0", "0.2.0", false)]
    [InlineData("garbage", "0.1.0", false)]
    [InlineData("0.2.0", "", false)]
    public void IsNewer(string candidate, string current, bool expected) => Assert.Equal(expected, Releases.IsNewer(candidate, current));
}
