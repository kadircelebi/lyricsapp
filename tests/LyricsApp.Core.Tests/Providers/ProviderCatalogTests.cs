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
        Assert.Equal(["LrcLib", "Musixmatch", "Genius", "lyrics.ovh"], chain.Select(p => p.Name));
        Assert.True(ProviderCatalog.IsKnown("LyricsOvh"));
        Assert.False(ProviderCatalog.IsKnown("shazam"));
    }
}
