using System.Globalization;
using System.IO;
using System.Windows;
using LyricsApp.Core;
using LyricsApp.Core.Providers;
using LyricsApp.Media;
using Windows.Storage.Streams;

namespace LyricsApp.Demo;

/// <summary>
/// Demo mode for Store screenshots: a made-up song with original (non-copyrighted) lyrics,
/// "playing" without any real player. Turned on with LYRICSAPP_DEMO=1;
/// LYRICSAPP_DEMO_POSITION=&lt;seconds&gt; picks where playback starts (default 40).
/// The language follows the UI culture (see App, LYRICSAPP_LANGUAGE).
/// </summary>
public static class DemoMode
{
    public static bool IsEnabled => Environment.GetEnvironmentVariable("LYRICSAPP_DEMO") is { Length: > 0 } v && v != "0";

    public static TimeSpan StartPosition =>
        double.TryParse(Environment.GetEnvironmentVariable("LYRICSAPP_DEMO_POSITION"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
            ? TimeSpan.FromSeconds(s)
            : TimeSpan.FromSeconds(40);

    private static bool Turkish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";

    public static TrackInfo Track => Turkish
        ? new TrackInfo("MrW Lyrics", "Gece Yarısı Şehir", "Demo", TimeSpan.FromSeconds(200))
        : new TrackInfo("MrW Lyrics", "City Lights at Midnight", "Demo", TimeSpan.FromSeconds(200));

    // Original lines written for the demo; safe to show in Store screenshots.
    private const string TurkishLrc = """
        [00:12.00]Sokak lambaları tek tek uyanıyor
        [00:17.00]Yağmur camda küçük notalar yazıyor
        [00:22.00]Adını fısıldıyor rüzgâr köşe başında
        [00:27.00]Bir şarkı başlıyor kalbimin ortasında
        [00:32.00]
        [00:35.00]Gece yarısı şehir benimle şarkı söylüyor
        [00:40.00]Her ışık bir hece, her adım bir nakarat
        [00:45.00]Köprüler boyunca sesimiz yankılanıyor
        [00:50.00]Bu gece bitmesin, dursun bütün saatler
        [00:55.00]
        [00:58.00]Kahve kokusu sinmiş eski bir sokakta
        [01:03.00]Bir pencerede yanan son ışık gibiyiz
        [01:08.00]Sözler dudağımda, melodi ayaklarımda
        [01:13.00]Kalabalıkta bile seninle yalnızız
        [01:18.00]
        [01:21.00]Gece yarısı şehir benimle şarkı söylüyor
        [01:26.00]Her ışık bir hece, her adım bir nakarat
        [01:31.00]Sabah olsa da bu ezgi bizde kalıyor
        [01:36.00]Bu gece bitmesin, dursun bütün saatler
        """;

    private const string EnglishLrc = """
        [00:12.00]Streetlights are waking one by one
        [00:17.00]Rain is writing little notes upon the glass
        [00:22.00]The wind keeps whispering your name around the bend
        [00:27.00]A song begins right in the middle of my chest
        [00:32.00]
        [00:35.00]City lights at midnight, singing back to me
        [00:40.00]Every light a syllable, every step a chorus line
        [00:45.00]Our voices echo all along the bridges
        [00:50.00]Don't let this night end, let the clocks stand still
        [00:55.00]
        [00:58.00]Down an old street that smells of coffee
        [01:03.00]We're like the last light burning in a window
        [01:08.00]Words upon my lips, the rhythm in my feet
        [01:13.00]Even in the crowd it's only you and me
        [01:18.00]
        [01:21.00]City lights at midnight, singing back to me
        [01:26.00]Every light a syllable, every step a chorus line
        [01:31.00]Even when the morning comes, the tune stays here
        [01:36.00]Don't let this night end, let the clocks stand still
        """;

    public static ILyricsProvider CreateLyricsProvider() => new DemoLyricsProvider();

    public static IMediaSource CreateMediaSource() => new DemoMediaSource();

    private sealed class DemoLyricsProvider : ILyricsProvider
    {
        public string Name => LocalFolderProvider.SourceName;

        public Task<LyricsResult?> FindAsync(TrackInfo track, CancellationToken cancellationToken)
        {
            var lines = LrcParser.Parse(Turkish ? TurkishLrc : EnglishLrc);
            var source = $"{LocalFolderProvider.SourceName}: {LocalFolderProvider.GetFileStem(Track)}.lrc";
            return Task.FromResult<LyricsResult?>(new LyricsResult(new SyncedLyrics(lines, source), Track));
        }
    }

    private sealed class DemoMediaSource : IMediaSource
    {
        public event EventHandler<MediaTrack?>? TrackChanged;
        public event EventHandler<PlaybackState>? PlaybackChanged;

        public MediaTrack? CurrentTrack { get; private set; }
        public PlaybackState Playback { get; private set; } = PlaybackState.Stopped;

        public async Task StartAsync()
        {
            CurrentTrack = new MediaTrack("MrW.Demo", Track, await CreateCoverAsync());
            Playback = new PlaybackState(true, StartPosition, DateTimeOffset.Now, 1.0, Track.Duration);
            TrackChanged?.Invoke(this, CurrentTrack);
            PlaybackChanged?.Invoke(this, Playback);
        }

        public Task RefreshAsync() => Task.CompletedTask;

        public void Dispose()
        {
        }

        /// <summary>The app icon (embedded resource) stands in for album art.</summary>
        private static async Task<IRandomAccessStreamReference?> CreateCoverAsync()
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/LyricsApp.ico"));
            if (resource is null) return null;
            using var source = resource.Stream;
            using var copy = new MemoryStream();
            await source.CopyToAsync(copy);

            var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(copy.ToArray());
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            return RandomAccessStreamReference.CreateFromStream(stream);
        }
    }
}
