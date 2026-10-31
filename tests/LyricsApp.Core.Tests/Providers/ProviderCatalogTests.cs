using LyricsApp.Core;
using LyricsApp.Core.Providers;

namespace LyricsApp.Core.Tests;

public class ProviderCatalogTests
{
    [Fact]
    public void Builds_in_given_order_skipping_unknown_and_repeated_ids()
    {
        using var catalog = new ProviderCatalog();
        var local = new LocalFolderProvider(Path.GetTempPath());

        var chain = catalog.Build(["Genius", "nope", "local", "lyrics.ovh", "genius", null], local);

        Assert.Collection(chain,
            p => Assert.IsType<GeniusProvider>(p),
            p => Assert.Same(local, p),
            p => Assert.IsType<LyricsOvhProvider>(p));
    }

    [Fact]
    public void Null_means_default_order_and_missing_local_is_left_out()
    {
        using var catalog = new ProviderCatalog();
        var chain = catalog.Build(null, local: null);
        Assert.Equal(["LrcLib", "Musixmatch", "YouTube Music", "Genius", "lyrics.ovh"], chain.Select(p => p.Name));
        Assert.True(ProviderCatalog.IsKnown("LyricsOvh"));
        Assert.True(ProviderCatalog.IsKnown("YouTube Music"));
        Assert.False(ProviderCatalog.IsKnown("shazam"));
    }

    [Fact]
    public void Older_settings_get_the_new_source_at_its_default_place()
    {
        var list = ProviderCatalog.AddNewSources(["local", "lrclib", "musixmatch", "genius", "lyricsovh"], seen: null);
        Assert.Equal(ProviderCatalog.DefaultOrder, list);
    }

    [Fact]
    public void New_source_keeps_the_users_order_and_their_removals()
    {
        Assert.Equal(["genius", "local", "youtubemusic"], ProviderCatalog.AddNewSources(["genius", "local"], seen: null));
        Assert.Equal(["local", "lrclib", "youtubemusic"], ProviderCatalog.AddNewSources(["local", "lrclib"], seen: null));
        Assert.Equal(["youtubemusic", "genius"], ProviderCatalog.AddNewSources(["genius"], seen: null));
    }

    [Fact]
    public void Source_the_user_saw_and_removed_stays_off()
    {
        var list = ProviderCatalog.AddNewSources(["local", "genius"], ProviderCatalog.DefaultOrder);
        Assert.Equal(["local", "genius"], list);
    }

    [Fact]
    public void Source_already_listed_is_not_added_twice()
    {
        var list = ProviderCatalog.AddNewSources(["YouTube Music", "local"], seen: null);
        Assert.Equal(["YouTube Music", "local"], list);
    }

    [Fact]
    public void No_saved_list_means_the_default_order()
    {
        Assert.Equal(ProviderCatalog.DefaultOrder, ProviderCatalog.AddNewSources(null, null));
    }
}
