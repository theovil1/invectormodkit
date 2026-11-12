using InvectorModKit.CustomSongs;

namespace InvectorModKit.ChartEditor.Tests
{
    /// <summary>Where the game plugin copies a sprite's pixels from, for the level editor's skin.</summary>
    public class EditorSkinRegionTests
    {
        [Fact]
        public void ARectanglePackedSpriteIsCopiedFromItsWholePixelRectangle()
        {
            // "Startgate Stripe-Thin" as the atlas stores it: float noise around whole pixels.
            Assert.True(EditorSkinRegion.TryGet(false, 7.6293945E-06f, 0f, 177.99998f, 256f, 512, 512, out var x, out var y, out var w, out var h));
            Assert.Equal((0, 0, 178, 256), (x, y, w, h));
        }

        [Fact]
        public void ATightlyPackedSpriteHasNoRectangleToCopy()
        {
            // Its pixels follow its mesh in the atlas: the rectangle Unity gives back is not where they are.
            Assert.False(EditorSkinRegion.TryGet(true, 221f, 219f, 178f, 256f, 512, 512, out _, out _, out _, out _));
        }

        [Fact]
        public void AnEmptyRectangleHasNothingToCopy()
        {
            Assert.False(EditorSkinRegion.TryGet(false, 0f, 0f, 0f, 0f, 512, 512, out _, out _, out _, out _));
            Assert.False(EditorSkinRegion.TryGet(false, 10f, 10f, 0.2f, 40f, 512, 512, out _, out _, out _, out _));
        }

        [Fact]
        public void ARectangleOutsideItsTextureHasNothingToCopy()
        {
            Assert.False(EditorSkinRegion.TryGet(false, 400f, 0f, 128f, 128f, 512, 512, out _, out _, out _, out _));
            Assert.False(EditorSkinRegion.TryGet(false, -2f, 0f, 16f, 16f, 512, 512, out _, out _, out _, out _));
        }
    }
}
