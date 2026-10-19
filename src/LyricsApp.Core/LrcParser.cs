using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LyricsApp.Core;

public static partial class LrcParser
{
    // [mm:ss], [mm:ss.xx], [mm:ss.xxx], [mm:ss:xx]; minutes may have 1-3 digits.
    [GeneratedRegex(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimestampRegex();

    // [ar:...], [ti:...], [offset:+250], [by:...] etc.
    [GeneratedRegex(@"^\s*\[([a-zA-Z#]+):(.*)\]\s*$")]
    private static partial Regex MetadataRegex();

    public static bool IsLrc(string? text) => !string.IsNullOrWhiteSpace(text) && TimestampRegex().IsMatch(text);

    public static IReadOnlyList<LyricLine> Parse(string text)
    {
        var lines = new List<LyricLine>();
        var offset = TimeSpan.Zero;

        foreach (var raw in text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None))
        {
            var matches = TimestampRegex().Matches(raw);
            if (matches.Count == 0)
            {
                var meta = MetadataRegex().Match(raw);
                if (meta.Success && meta.Groups[1].Value.Equals("offset", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(meta.Groups[2].Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms))
                {
                    // Positive offset means lyrics should appear earlier.
                    offset = TimeSpan.FromMilliseconds(-ms);
                }
                continue;
            }

            // Text follows the last timestamp; a line may carry several timestamps (repeated chorus).
            var last = matches[^1];
            var lyric = raw[(last.Index + last.Length)..].Trim();

            foreach (Match m in matches)
            {
                var minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                var fraction = m.Groups[3].Success ? m.Groups[3].Value : "0";
                var millis = int.Parse(fraction.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
                var time = new TimeSpan(0, 0, minutes, seconds, millis) + offset;
                lines.Add(new LyricLine(time < TimeSpan.Zero ? TimeSpan.Zero : time, lyric));
            }
        }

        // Stable sort keeps original order for equal timestamps.
        return lines.OrderBy(l => l.Time).ToList();
    }

    public static string Format(IEnumerable<LyricLine> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            sb.Append('[')
              .Append(((int)line.Time.TotalMinutes).ToString("D2", CultureInfo.InvariantCulture)).Append(':')
              .Append(line.Time.Seconds.ToString("D2", CultureInfo.InvariantCulture)).Append('.')
              .Append((line.Time.Milliseconds / 10).ToString("D2", CultureInfo.InvariantCulture)).Append(']')
              .AppendLine(line.Text);
        }
        return sb.ToString();
    }

    /// <summary>Index of the line active at <paramref name="position"/>, or -1 before the first line.</summary>
    public static int FindActiveLine(IReadOnlyList<LyricLine> lines, TimeSpan position)
    {
        int lo = 0, hi = lines.Count - 1, result = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (lines[mid].Time <= position)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return result;
    }
}
