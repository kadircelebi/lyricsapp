using LyricsApp.Media;

namespace LyricsApp;

/// <summary>One line of the window menu's Source submenu. <see cref="App"/> is null for "Automatic".</summary>
public sealed record SourceMenuEntry(string Header, bool IsChecked, SavedApp? App);

/// <summary>
/// What the Source submenu shows: "Automatic", then the players reporting a song right now, then the hand-picked
/// player when it is not among them (so the choice stays visible and can be taken back).
/// </summary>
public static class SourceMenu
{
    /// <param name="pinned">The player picked by hand, or null while the choice is automatic.</param>
    public static IReadOnlyList<SourceMenuEntry> Entries(IReadOnlyList<MediaPlayerInfo> players, SavedApp? pinned)
    {
        var entries = new List<SourceMenuEntry> { new(Strings.SourceAutomatic, pinned is null, null) };
        foreach (var player in players)
        {
            // On automatic, say which one the rule chose; a hand-picked one carries the tick instead.
            var name = pinned is null && player.IsFollowed ? Strings.SourceFollowedNow(player.Name) : player.Name;
            entries.Add(new SourceMenuEntry(Header(name), player.App == pinned?.Id, new SavedApp(player.App, player.Name)));
        }
        if (pinned is not null && players.All(player => player.App != pinned.Id))
        {
            entries.Add(new SourceMenuEntry(Header(Strings.SourceNotOpen(pinned.Name)), true, pinned));
        }
        return entries;
    }

    // An underscore in a menu header would mark the next letter as its access key.
    private static string Header(string name) => name.Replace("_", "__");
}
