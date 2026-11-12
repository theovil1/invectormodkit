using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>A YouTube link on the clipboard is offered for a song only when its video looks like that song.</summary>
public class LinkGuessTests
{
    [Fact]
    public void Guess_SongTitleInTheVideoTitle() =>
        Assert.True(LinkGuess.IsThisSong("Kyiv", "Apashe & Alina Pash - Kyiv (Official Video)"));

    [Fact]
    public void Guess_FileNameAgainstTheVideoTitle() =>
        Assert.True(LinkGuess.IsThisSong("RetroVision - Hope _ Future Trap _ NCS - Copyright Free Music", "RetroVision - Hope | Future Trap | NCS - Copyright Free Music"));

    [Fact]
    public void Guess_AnotherSongIsRefused() =>
        Assert.False(LinkGuess.IsThisSong("Imagine Dragons - On Top Of The World", "RetroVision - Hope | Future Trap | NCS - Copyright Free Music"));

    [Fact]
    public void Guess_IgnoresCaseAndAccents() =>
        Assert.True(LinkGuess.IsThisSong("Déjà Vu", "DEJA VU (Official Audio)"));

    [Fact]
    public void Guess_NothingToCompare()
    {
        Assert.False(LinkGuess.IsThisSong("", "Kyiv"));
        Assert.False(LinkGuess.IsThisSong(null, "Kyiv"));
        Assert.False(LinkGuess.IsThisSong("Kyiv", null));
    }
}
