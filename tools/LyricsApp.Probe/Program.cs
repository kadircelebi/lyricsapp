// Diagnostic: reads the current media session, searches the online providers (no local cache), and
// reports what the app would show. Prints counts and timing only, never the lyrics text.
//   dotnet run --project tools/LyricsApp.Probe              -> use what is playing now
//   dotnet run --project tools/LyricsApp.Probe -- "Artist" "Title" [durationSeconds]
//   ... -- --provider musixmatch "Artist" "Title"           -> ask one provider only
//          (lrclib, musixmatch, genius, lyricsovh)
//   ... -- --recognize                                       -> name what is playing via Shazam, then search for that
using System.Diagnostics;
using System.Globalization;
using LyricsApp.Core;
using LyricsApp.Core.Providers;
using LyricsApp.Core.Recognition;
using LyricsApp.Media;

var positional = new List<string>();
string? onlyProvider = null;
var recognize = false;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--provider" && i + 1 < args.Length) onlyProvider = args[++i];
    else if (args[i] == "--recognize") recognize = true;
    else positional.Add(args[i]);
}
if (onlyProvider is not null && (!ProviderCatalog.IsKnown(onlyProvider) || ProviderCatalog.Local.Equals(onlyProvider, StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine($"Unknown provider '{onlyProvider}'. Use one of: {string.Join(", ", ProviderCatalog.DefaultOrder.Skip(1))}");
    return 64;
}

TrackInfo query;
string? sourceApp = null;
if (positional.Count >= 2)
{
    query = new TrackInfo(positional[0], positional[1], null,
        positional.Count > 2 ? TimeSpan.FromSeconds(double.Parse(positional[2], CultureInfo.InvariantCulture)) : null);
}
else
{
    using var watcher = new MediaWatcher();
    await watcher.StartAsync();
    if (watcher.CurrentTrack is not { } track)
    {
        Console.WriteLine("SMTC: no active media session.");
        return 1;
    }
    var p = watcher.Playback;
    sourceApp = track.SourceApp;
    Console.WriteLine($"SMTC app      : {track.SourceApp}");
    Console.WriteLine($"SMTC raw      : artist='{track.Track.Artist}' title='{track.Track.Title}' album='{track.Track.Album}' duration={track.Track.Duration}");
    Console.WriteLine($"SMTC playback : playing={p.IsPlaying} position={p.Position:mm\\:ss\\.f} updatedAt={p.UpdatedAt:HH:mm:ss} rate={p.Rate}");
    Console.WriteLine($"Extrapolated  : {p.GetPosition(DateTimeOffset.Now):mm\\:ss\\.f}");
    query = TrackTextCleaner.Clean(track.Track);
}

if (recognize)
{
    var pid = LoopbackAudio.FindPlayerProcess(sourceApp);
    Console.WriteLine($"Capture       : {(pid is null ? "whole system output" : $"process tree of pid {pid}")}");
    using var shazamHttp = ShazamClient.CreateHttpClient();
    var recognizer = new ShazamRecognizer(new ShazamClient(shazamHttp), ct => LoopbackAudio.ReadAsync(sourceApp, ct));
    var rsw = Stopwatch.StartNew();
    var recognized = await recognizer.RecognizeAsync(CancellationToken.None);
    Console.WriteLine($"Shazam        : {(recognized is null ? "no match" : $"artist='{recognized.Artist}' title='{recognized.Title}'")} ({rsw.ElapsedMilliseconds} ms)");
    if (recognized is null) return 3;
    query = recognized with { Duration = query.Duration };
}

Console.WriteLine($"Query         : artist='{query.Artist}' title='{query.Title}' album='{query.Album}' duration={query.Duration}");

// Own state folder, so probe runs reuse one Musixmatch token without touching the app's settings.
using var catalog = new ProviderCatalog(Path.Combine(Path.GetTempPath(), "LyricsApp.Probe"));
var providers = catalog.Build(onlyProvider is null ? ProviderCatalog.DefaultOrder : [onlyProvider], local: null);
Console.WriteLine($"Providers     : {string.Join(" -> ", providers.Select(p => p.Name))}");
var service = new LyricsService(providers, cache: null);
var sw = Stopwatch.StartNew();
var result = await service.FindAsync(query, CancellationToken.None);
sw.Stop();

foreach (var e in result.Errors) Console.WriteLine($"Error         : {e}");
switch (result.Result)
{
    case null:
        Console.WriteLine($"Result        : not found ({sw.ElapsedMilliseconds} ms)");
        return 2;
    case { Lyrics: SyncedLyrics s } r:
        Console.WriteLine($"Result        : SYNCED, {s.Lines.Count} lines, first at {s.Lines[0].Time:mm\\:ss\\.ff}, last at {s.Lines[^1].Time:mm\\:ss\\.ff} ({sw.ElapsedMilliseconds} ms)");
        Console.WriteLine($"Matched       : '{r.MatchedTrack.Artist}' - '{r.MatchedTrack.Title}' ({r.MatchedTrack.Duration}) score={TrackMatcher.Score(query, r.MatchedTrack)} source={s.Source}");
        break;
    case { Lyrics: PlainLyrics pl } r:
        Console.WriteLine($"Result        : PLAIN, {pl.Text.Split('\n').Length} lines ({sw.ElapsedMilliseconds} ms)");
        Console.WriteLine($"Matched       : '{r.MatchedTrack.Artist}' - '{r.MatchedTrack.Title}' score={TrackMatcher.Score(query, r.MatchedTrack)} source={pl.Source}");
        break;
    case { Lyrics: InstrumentalLyrics i }:
        Console.WriteLine($"Result        : instrumental ({sw.ElapsedMilliseconds} ms) source={i.Source}");
        break;
}
return 0;
