using System.Collections.Concurrent;
using InvectorModKit.ChartEditor;

namespace InvectorModKit.ChartEditor.Tests;

/// <summary>One editor window at a time: a second start hands its song to the first one.</summary>
public class EditorInstanceTests
{
    private static string UniqueName() => "InvectorLevelEditorTest-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void TheSongIsTheFirstArgumentThatIsNotAnOption()
    {
        Assert.Null(EditorInstance.SongArgument(new string[0]));
        Assert.Equal(@"C:\Songs\a song.mp3", EditorInstance.SongArgument(new[] { @"C:\Songs\a song.mp3" }));
        Assert.Equal("b.mp3", EditorInstance.SongArgument(new[] { "--verbose", " ", "b.mp3" }));
    }

    [Fact]
    public void OnlyTheFirstStartClaimsTheEditor()
    {
        string name = UniqueName();
        using (var first = EditorInstance.TryClaim(name))
        {
            Assert.NotNull(first);
            Assert.Null(EditorInstance.TryClaim(name));
        }
        using (var again = EditorInstance.TryClaim(name)) Assert.NotNull(again); // free once the first window is gone
    }

    [Fact]
    public void ASecondStartHandsItsSongsToTheFirst()
    {
        string name = UniqueName();
        var received = new BlockingCollection<string>();
        using (var first = EditorInstance.TryClaim(name))
        {
            first.Listen(received.Add);
            Assert.True(EditorInstance.Send(name, @"C:\Songs\Creep.mp3", 3000));
            Assert.True(received.TryTake(out var song, 3000));
            Assert.Equal(@"C:\Songs\Creep.mp3", song);
            Assert.True(EditorInstance.Send(name, "", 3000)); // no song: just come to the front
            Assert.True(received.TryTake(out var none, 3000));
            Assert.Equal("", none);
        }
    }

    [Fact]
    public void NobodyListensWhenNoEditorIsOpen()
    {
        Assert.False(EditorInstance.Send(UniqueName(), "x.mp3", 200));
    }
}
