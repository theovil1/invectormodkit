namespace InvectorModKit.Charting.Tests
{
    public class SongMetadataTests
    {
        [Theory]
        [InlineData("Coldplay", "Coldplay - Viva La Vida (Official Video)", "Coldplay", "Viva La Vida")]
        [InlineData("GENER8ION", "GENER8ION Yung Lean — STORM II Official Music Video 4K Subtitled", "GENER8ION Yung Lean", "STORM II")]
        [InlineData(null, "Daft Punk – One More Time [HD]", "Daft Punk", "One More Time")]
        [InlineData("Lana Del Rey", "Video Games", "Lana Del Rey", "Video Games")]
        [InlineData("The Buggles", "Video Killed the Radio Star (Lyrics)", "The Buggles", "Video Killed the Radio Star")]
        [InlineData("Artist", "Song Title", "Artist", "Song Title")]
        public void NormalizesDownloadedTitles(string artist, string title, string expectedArtist, string expectedTitle)
        {
            var (a, t) = SongMetadata.Normalize(artist, title);
            Assert.Equal(expectedArtist, a);
            Assert.Equal(expectedTitle, t);
        }

        [Theory]
        [InlineData("Coldplay - Viva La Vida", "Coldplay", "Viva La Vida")]
        [InlineData("Yung Lean — Ginseng Strip 2002", "Yung Lean", "Ginseng Strip 2002")]
        [InlineData("JustATitle", null, "JustATitle")]
        public void SplitsFileNames(string name, string artist, string title)
        {
            Assert.Equal((artist, title), SongMetadata.SplitFileName(name));
        }
    }
}
