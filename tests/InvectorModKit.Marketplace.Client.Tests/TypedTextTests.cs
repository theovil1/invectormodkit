using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>The marketplace's one-line text fields: typing, Backspace, Ctrl+A.</summary>
public class TypedTextTests
{
    private static (string text, bool selected, bool changed) Type(string text, bool selected, string typed, int max = 60)
    {
        bool changed = TypedText.Apply(ref text, ref selected, typed, max);
        return (text, selected, changed);
    }

    [Fact]
    public void Typed_AddsAndErases()
    {
        Assert.Equal(("kyi", false, true), Type("ky", false, "iv\b"));
        Assert.Equal(("", false, false), Type("", false, "\b"));
        Assert.Equal(("ab", false, true), Type("a", false, "bc", max: 2));
    }

    [Fact]
    public void Typed_SelectAllThenTypeReplaces()
    {
        Assert.Equal(("kyiv", true, false), Type("kyiv", false, TypedText.SelectAll.ToString()));
        Assert.Equal(("m", false, true), Type("kyiv", true, "m"));
        Assert.Equal(("metallica", false, true), Type("kyiv", false, TypedText.SelectAll + "metallica"));
    }

    [Fact]
    public void Typed_SelectAllThenBackspaceClears()
    {
        Assert.Equal(("", false, true), Type("kyiv", false, TypedText.SelectAll + "\b"));
        Assert.Equal(("", false, false), Type("", false, TypedText.SelectAll.ToString()));
    }

    [Fact]
    public void Typed_ShowsTheSelection()
    {
        Assert.Equal("kyiv▌", TypedText.Show("kyiv", false));
        Assert.Equal("<b><color=#E9B8FF>kyiv</color></b>▌", TypedText.Show("kyiv", true));
    }
}
