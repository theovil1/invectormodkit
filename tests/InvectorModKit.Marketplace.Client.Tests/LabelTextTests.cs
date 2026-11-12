using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>The words of a button label that must stay whole on one line (the label shrinks rather than cut them).</summary>
public class LabelTextTests
{
    [Fact]
    public void Words_OneLongWordStaysWhole() =>
        Assert.Equal(new[] { "Niezainstalowane" }, LabelText.UnbreakableWords("Niezainstalowane"));

    [Fact]
    public void Words_SplitAtSpacesAndLineBreaks() =>
        Assert.Equal(new[] { "CASUAL", "412", "notes", "Не", "установлены" }, LabelText.UnbreakableWords("CASUAL\n412 notes Не  установлены"));

    [Fact]
    public void Words_KoreanKeepsItsWords() =>
        Assert.Equal(new[] { "내", "차트" }, LabelText.UnbreakableWords("내 차트"));

    [Fact]
    public void Words_JapaneseAndChineseBreakAnywhere()
    {
        Assert.Empty(LabelText.UnbreakableWords("すべての難易度"));
        Assert.Empty(LabelText.UnbreakableWords("任意难度"));
        Assert.Equal(new[] { "ULTRA" }, LabelText.UnbreakableWords("ULTRA\n音符"));
    }

    [Fact]
    public void Sentences_JapaneseGetsALineEach() =>
        Assert.Equal("「A」（B）を曲から削除しますか？\nチャートは残ります。\n再インストールします。",
                     LabelText.SentencePerLine("「A」（B）を曲から削除しますか？チャートは残ります。再インストールします。"));

    [Fact]
    public void Sentences_ChineseGetsALineEach() =>
        Assert.Equal("移除「A」吗？\n谱面保留在市场上：重新安装会再次下载这首歌。",
                     LabelText.SentencePerLine("移除「A」吗？谱面保留在市场上：重新安装会再次下载这首歌。"));

    [Fact]
    public void Sentences_OthersUnchanged()
    {
        Assert.Equal("Remove “A” (B)? The chart stays. Really.", LabelText.SentencePerLine("Remove “A” (B)? The chart stays. Really."));
        Assert.Equal("削除しますか？", LabelText.SentencePerLine("削除しますか？"));
        Assert.Equal("", LabelText.SentencePerLine(""));
        Assert.Null(LabelText.SentencePerLine(null));
    }

    [Fact]
    public void Words_NothingForEmpty()
    {
        Assert.Empty(LabelText.UnbreakableWords(""));
        Assert.Empty(LabelText.UnbreakableWords(null));
    }
}
