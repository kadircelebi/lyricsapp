using LyricsApp.Core;

namespace LyricsApp.Core.Tests;

public sealed class TrackOffsetsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "LyricsAppTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(folder, "offsets.json");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void Unknown_song_has_no_offset()
    {
        var offsets = TrackOffsets.Load(FilePath);
        Assert.Equal(0, offsets.Get(new TrackInfo("Some Artist", "Some Song")));
        Assert.Equal(0, offsets.Get(null));
    }

    [Fact]
    public void Offset_belongs_to_one_song_only()
    {
        var offsets = TrackOffsets.Load(FilePath);
        offsets.Set(new TrackInfo("Artist", "Song A"), -10);

        Assert.Equal(-10, offsets.Get(new TrackInfo("Artist", "Song A")));
        Assert.Equal(0, offsets.Get(new TrackInfo("Artist", "Song B")));
        Assert.Equal(0, offsets.Get(new TrackInfo("Other Artist", "Song A")));
    }

    [Fact]
    public void Survives_a_restart()
    {
        TrackOffsets.Load(FilePath).Set(new TrackInfo("Artist", "Song"), 1.5);

        Assert.Equal(1.5, TrackOffsets.Load(FilePath).Get(new TrackInfo("Artist", "Song")));
    }

    [Fact]
    public void Same_song_under_slightly_different_names()
    {
        var offsets = TrackOffsets.Load(FilePath);
        offsets.Set(new TrackInfo("Şarkıcı Adı", "GİTME"), 2.5);

        Assert.Equal(2.5, offsets.Get(new TrackInfo("sarkici adi", "Gitme", "Some Album", TimeSpan.FromMinutes(3))));
    }

    [Fact]
    public void Zero_removes_the_entry()
    {
        var offsets = TrackOffsets.Load(FilePath);
        var song = new TrackInfo("Artist", "Song");
        offsets.Set(song, -3);
        offsets.Set(song, 0);

        Assert.Equal(0, offsets.Count);
        Assert.Equal(0, TrackOffsets.Load(FilePath).Count);
    }

    [Fact]
    public void Song_without_a_title_is_not_stored()
    {
        var offsets = TrackOffsets.Load(FilePath);
        offsets.Set(new TrackInfo("Artist", "  "), 4);
        offsets.Set(null, 4);

        Assert.Equal(0, offsets.Count);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Broken_file_starts_empty()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(FilePath, "{ not json");

        var offsets = TrackOffsets.Load(FilePath);
        Assert.Equal(0, offsets.Count);

        offsets.Set(new TrackInfo("Artist", "Song"), 1);
        Assert.Equal(1, TrackOffsets.Load(FilePath).Get(new TrackInfo("Artist", "Song")));
    }

    [Fact]
    public void In_memory_store_writes_nothing()
    {
        var offsets = TrackOffsets.InMemory();
        offsets.Set(new TrackInfo("Artist", "Song"), 2);

        Assert.Equal(2, offsets.Get(new TrackInfo("Artist", "Song")));
        Assert.False(Directory.Exists(folder));
    }
}
