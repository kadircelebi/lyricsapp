using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LyricsApp.Core;

/// <summary>Decides whether a provider's search hit is the song we asked for.</summary>
public static partial class TrackMatcher
{
    [GeneratedRegex(@"\s*[\(\[\{].*?[\)\]\}]")]
    private static partial Regex ParentheticalRegex();

    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring|with)\s+.+$", RegexOptions.IgnoreCase)]
    private static partial Regex FeatRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}\s]+")]
    private static partial Regex PunctuationRegex();

    [GeneratedRegex(@"^the\s+")]
    private static partial Regex LeadingTheRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpaceRegex();

    // Separators between multiple artists: "A & B", "A, B", "A x B", "A ve B", "A feat. B".
    [GeneratedRegex(@"\s*(?:&|,|\+|/|\bx\b|\bve\b|\band\b|\bfeat\.?|\bft\.?|\bfeaturing\b|\bwith\b)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSeparatorRegex();

    public const int MinAcceptableScore = 4;

    /// <summary>Lowercase, accent-free (ş→s, ı→i…), no brackets, no "feat.", no punctuation.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var s = ParentheticalRegex().Replace(value, " ");
        s = FeatRegex().Replace(s, string.Empty);
        s = FoldDiacritics(s.ToLowerInvariant());
        s = PunctuationRegex().Replace(s, " ");
        s = LeadingTheRegex().Replace(s.Trim(), string.Empty);
        return MultiSpaceRegex().Replace(s, " ").Trim();
    }

    private static string FoldDiacritics(string s)
    {
        var decomposed = s.Replace('ı', 'i').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>3 = equal, 2 = one contains the other, 1 = most words shared, 0 = different.</summary>
    internal static int ScoreText(string query, string candidate)
    {
        var a = Normalize(query);
        var b = Normalize(candidate);
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 3;
        // Whole words only: "anne" must not match inside "annees", nor "malik" inside "malika".
        if ($" {a} ".Contains($" {b} ") || $" {b} ".Contains($" {a} ")) return 2;

        var wa = a.Split(' ').ToHashSet();
        var wb = b.Split(' ').ToHashSet();
        var shared = wa.Intersect(wb).Count();
        return shared * 2 >= Math.Min(wa.Count, wb.Count) + 1 ? 1 : 0;
    }

    internal static int ScoreArtist(string query, string candidate)
    {
        var direct = ScoreText(query, candidate);
        if (direct >= 2) return direct;

        // Any single artist in common is good enough ("Semicenk ve Büken" vs "Semicenk").
        var qa = SplitArtists(query);
        var ca = SplitArtists(candidate);
        return qa.Overlaps(ca) ? 2 : direct;
    }

    private static HashSet<string> SplitArtists(string artists) =>
        ArtistSeparatorRegex().Split(artists).Select(Normalize).Where(a => a.Length > 0).ToHashSet();

    /// <summary>Combined score; negative when the durations clearly disagree.</summary>
    public static int Score(TrackInfo query, TrackInfo candidate)
    {
        var title = ScoreText(query.Title, candidate.Title);
        if (title == 0) return 0;

        var artist = string.IsNullOrWhiteSpace(query.Artist) ? 2 : ScoreArtist(query.Artist, candidate.Artist);
        if (artist == 0) return 0;
        var score = title * 2 + artist;

        if (query.Duration is { } qd && candidate.Duration is { } cd && qd > TimeSpan.Zero && cd > TimeSpan.Zero)
        {
            // Music videos can run a bit longer than the album track (intro/outro), so only a big gap is fatal.
            var diff = Math.Abs((qd - cd).TotalSeconds);
            if (diff <= 3) score += 2;
            else if (diff > 30) return 0;
            else if (diff > 15) score -= 3;
        }
        return score;
    }

    public static bool IsAcceptable(TrackInfo query, TrackInfo candidate) => Score(query, candidate) >= MinAcceptableScore;
}
