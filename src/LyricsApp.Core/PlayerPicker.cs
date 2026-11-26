namespace LyricsApp.Core;

/// <summary>One player the system reports, as far as choosing between players needs it.</summary>
/// <param name="App">The player's application id.</param>
/// <param name="Track">What it reports as playing, before any cleanup.</param>
/// <param name="IsSystemChoice">The player Windows itself would control with the media keys.</param>
/// <param name="HasNoLyrics">Its current song was searched already and nothing was found.</param>
public sealed record PlayerSnapshot(string App, TrackInfo Track, bool IsPlaying, bool IsSystemChoice, bool HasNoLyrics = false);

/// <summary>
/// Decides which player the lyrics follow when several report something. Windows hands its own choice to whatever
/// started last, so a video opened next to the music would take the window over; here a playing song keeps it.
/// </summary>
public static class PlayerPicker
{
    /// <summary>From this length on, something without an album is taken for a talk, a mix or a film.</summary>
    public static readonly TimeSpan LongProgramme = TimeSpan.FromMinutes(15);

    /// <summary>False for long recordings that belong to no album: podcasts, talks, mixes, films. Unknown length counts as a song (radio).</summary>
    public static bool IsSong(TrackInfo track) =>
        !(track.Duration is { } length && length >= LongProgramme && string.IsNullOrWhiteSpace(track.Album));

    /// <summary>
    /// Index of the player to follow, or -1 for none.
    /// Songs come before long programmes and playing before paused; a song whose lyrics were not found gives way to
    /// another playing song. Among equals the followed player stays, then Windows' own choice, then the first.
    /// </summary>
    /// <param name="followed">Index of the player followed until now, or -1.</param>
    /// <param name="pinnedApp">The app the user picked by hand; it wins while it reports a song title.</param>
    /// <param name="ignoredApps">Apps the user never wants followed.</param>
    public static int Pick(IReadOnlyList<PlayerSnapshot> players, int followed, string? pinnedApp, IReadOnlySet<string> ignoredApps)
    {
        var usable = Enumerable.Range(0, players.Count)
            .Where(i => !players[i].Track.IsEmpty && !ignoredApps.Contains(players[i].App))
            .ToList();

        if (pinnedApp is not null)
        {
            var pinned = usable.Where(i => players[i].App == pinnedApp).ToList();
            if (pinned.Count > 0) return Preferred(pinned, players, followed);
        }

        return usable
            .GroupBy(i => Rank(players[i]))
            .OrderBy(group => group.Key)
            .Select(group => Preferred([.. group], players, followed))
            .DefaultIfEmpty(-1)
            .First();
    }

    private static int Rank(PlayerSnapshot player)
    {
        if (!IsSong(player.Track)) return player.IsPlaying ? 4 : 5;
        if (player.IsPlaying) return player.HasNoLyrics ? 1 : 0;
        return player.HasNoLyrics ? 3 : 2;
    }

    private static int Preferred(List<int> candidates, IReadOnlyList<PlayerSnapshot> players, int followed)
    {
        // A pinned app may have a playing and a paused player; the playing one is the one meant.
        var playing = candidates.Where(i => players[i].IsPlaying).ToList();
        if (playing.Count > 0) candidates = playing;

        if (candidates.Contains(followed)) return followed;
        var systemChoice = candidates.FindIndex(i => players[i].IsSystemChoice);
        return candidates[Math.Max(systemChoice, 0)];
    }
}
