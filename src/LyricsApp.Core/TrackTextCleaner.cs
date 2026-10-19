using System.Text.RegularExpressions;

namespace LyricsApp.Core;

/// <summary>
/// Cleans up what players put into the media session, especially video titles from browsers
/// ("Artist - Song (Official Video)" posted by "ArtistVEVO").
/// </summary>
public static partial class TrackTextCleaner
{
    private const string NoiseWords =
        @"official\s*(?:music\s*)?(?:lyrics?\s*)?(?:video|audio|visualizer)|(?:music\s*)?video|lyrics?\s*video|" +
        @"official|audio(?:\s*only)?|with\s*lyrics?|lyrics?|visualizer|mv|m/v|hd|hq|4k|klip|resmi\s*klip|official\s*clip|" +
        @"(?:\d{4}\s*)?remaster(?:ed)?(?:\s*\d{4})?|sözleri|şarkı\s*sözleri";

    [GeneratedRegex(@"\s*[\(\[\{（【]\s*(?:" + NoiseWords + @")[^\)\]\}）】]*[\)\]\}）】]", RegexOptions.IgnoreCase)]
    private static partial Regex BracketNoiseRegex();

    [GeneratedRegex(@"\s*(?:[-|–—]\s*)?(?:" + NoiseWords + @")\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingNoiseRegex();

    [GeneratedRegex(@"\s*-\s*Topic\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TopicSuffixRegex();

    [GeneratedRegex(@"\s*VEVO\s*$")]
    private static partial Regex VevoSuffixRegex();

    [GeneratedRegex(@"(?<=\p{Ll})(?=\p{Lu})")]
    private static partial Regex CamelCaseRegex();

    [GeneratedRegex(@"[​-‍﻿⁠]")]
    private static partial Regex InvisibleCharsRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpaceRegex();

    // "Artist - Title" with any dash variant surrounded by spaces.
    [GeneratedRegex(@"^(.+?)\s+[-‐-―−－]+\s+(.+)$")]
    private static partial Regex ArtistTitleSplitRegex();

    public static string CleanArtist(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;
        var s = InvisibleCharsRegex().Replace(artist, string.Empty).Trim();
        s = TopicSuffixRegex().Replace(s, string.Empty);
        if (VevoSuffixRegex().IsMatch(s))
        {
            s = VevoSuffixRegex().Replace(s, string.Empty);
            if (!s.Contains(' ')) s = CamelCaseRegex().Replace(s, " ");
        }
        return MultiSpaceRegex().Replace(s, " ").Trim();
    }

    public static string CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var s = InvisibleCharsRegex().Replace(title, string.Empty).Trim();
        string previous;
        do
        {
            previous = s;
            s = BracketNoiseRegex().Replace(s, string.Empty);
            s = TrailingNoiseRegex().Replace(s, string.Empty).Trim();
        } while (s != previous && s.Length > 0);
        s = MultiSpaceRegex().Replace(s, " ").Trim();
        return s.Length > 0 ? s : title.Trim();
    }

    /// <summary>Cleans both fields and pulls the artist out of "Artist - Title" titles when that is more accurate.</summary>
    public static TrackInfo Clean(TrackInfo track)
    {
        var artist = CleanArtist(track.Artist);
        var title = CleanTitle(track.Title);

        var split = ArtistTitleSplitRegex().Match(title);
        if (split.Success)
        {
            var left = split.Groups[1].Value.Trim();
            var right = split.Groups[2].Value.Trim();
            var normArtist = TrackMatcher.Normalize(artist);
            var normLeft = TrackMatcher.Normalize(left);
            if (normArtist.Length == 0 || normLeft.Contains(normArtist) || normArtist.Contains(normLeft))
            {
                artist = left;
                title = CleanTitle(right);
            }
        }

        return track with { Artist = artist, Title = title };
    }
}
