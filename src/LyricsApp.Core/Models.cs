namespace LyricsApp.Core;

/// <summary>What the media session reports about the current song.</summary>
public sealed record TrackInfo(string Artist, string Title, string? Album = null, TimeSpan? Duration = null)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Title);

    public override string ToString() => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
}

public sealed record LyricLine(TimeSpan Time, string Text);

public abstract record Lyrics(string Source)
{
    public abstract string ToText();
}

public sealed record SyncedLyrics(IReadOnlyList<LyricLine> Lines, string Source) : Lyrics(Source)
{
    public override string ToText() => LrcParser.Format(Lines);
}

public sealed record PlainLyrics(string Text, string Source) : Lyrics(Source)
{
    public override string ToText() => Text;
}

public sealed record InstrumentalLyrics(string Source) : Lyrics(Source)
{
    public override string ToText() => string.Empty;
}

/// <summary>Lyrics plus the identity the provider actually matched (may differ in spelling from the query).</summary>
public sealed record LyricsResult(Lyrics Lyrics, TrackInfo MatchedTrack);
