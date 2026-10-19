using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LyricsApp.Core.Providers;

/// <summary>Small JSON and text helpers shared by the online providers.</summary>
internal static partial class ProviderHelpers
{
    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRunRegex();

    public static string? GetString(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    public static long? GetInt64(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n)) return n;
        if (el.ValueKind == JsonValueKind.Number) return (long)el.GetDouble();
        return el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : null;
    }

    /// <summary>True for JSON true or a non-zero number (Musixmatch uses 0/1 flags).</summary>
    public static bool GetFlag(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var el)
        && (el.ValueKind == JsonValueKind.True || (el.ValueKind == JsonValueKind.Number && el.GetDouble() != 0));

    /// <summary>Walks nested objects; returns false when any step is missing or not an object.</summary>
    public static bool TryGetPath(JsonElement root, out JsonElement value, params string[] path)
    {
        value = root;
        foreach (var name in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out value)) return false;
        }
        return true;
    }

    public static TimeSpan? Seconds(long? value) => value is > 0 ? TimeSpan.FromSeconds(value.Value) : null;

    /// <summary>Unix newlines, trimmed lines, at most one blank line between stanzas.</summary>
    public static string NormalizePlainText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim());
        return BlankRunRegex().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    public static string Q(string? value) => Uri.EscapeDataString(value ?? string.Empty);
}
