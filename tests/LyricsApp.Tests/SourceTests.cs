using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using LyricsApp.Core;
using LyricsApp.Core.Providers;
using LyricsApp.Core.Recognition;
using LyricsApp.Media;

namespace LyricsApp.Tests;

/// <summary>Which player is followed, what is done with something that is not a song, and the "different version" note.</summary>
public class SourceTests
{
    private static readonly TrackInfo Song = new("Artist", "Song", "Album", TimeSpan.FromSeconds(250));

    [Fact]
    public void A_version_of_another_length_is_pointed_out_until_the_note_is_switched_off() => OnDispatcherThread(() =>
    {
        var source = new FakeSource();
        var settings = new AppSettings();
        // The player has a 4:10 video; the lyrics were timed for the 3:35 album track.
        using var viewModel = Create(settings, source, new FakeLyrics(Synced(), TimeSpan.FromSeconds(215)));

        source.Play(Song);
        PumpUntil(() => viewModel.IsSynced);
        Assert.True(viewModel.ShowVersionNotice);

        settings.Display.VersionNotice = false;
        Assert.False(viewModel.ShowVersionNotice);
        settings.Display.VersionNotice = true;
        Assert.True(viewModel.ShowVersionNotice);

        // The player's length arrives late or is corrected: the note follows it.
        source.Play(Song with { Duration = TimeSpan.FromSeconds(216) }, sameSong: true);
        PumpUntil(() => !viewModel.ShowVersionNotice);
    });

    [Fact]
    public void No_note_when_the_lengths_agree_or_are_not_known() => OnDispatcherThread(() =>
    {
        var source = new FakeSource();
        using var same = Create(new AppSettings(), source, new FakeLyrics(Synced(), TimeSpan.FromSeconds(248)));
        source.Play(Song);
        PumpUntil(() => same.IsSynced);
        Assert.False(same.ShowVersionNotice);

        var other = new FakeSource();
        using var unknown = Create(new AppSettings(), other, new FakeLyrics(Synced(), matchedLength: null));
        other.Play(Song);
        PumpUntil(() => unknown.IsSynced);
        Assert.False(unknown.ShowVersionNotice);
    });

    [Fact]
    public void No_note_for_lyrics_without_timing() => OnDispatcherThread(() =>
    {
        var source = new FakeSource();
        using var viewModel = Create(new AppSettings(), source, new FakeLyrics(new PlainLyrics("made-up line", "Fake"), TimeSpan.FromSeconds(215)));
        source.Play(Song);
        PumpUntil(() => viewModel.Lines.Count > 0);
        Assert.False(viewModel.ShowVersionNotice);
    });

    [Fact]
    public void A_long_recording_without_an_album_is_not_listened_to() => OnDispatcherThread(() =>
    {
        var source = new FakeSource();
        var recognizer = new FakeRecognizer();
        using var viewModel = Create(new AppSettings(), source, new FakeLyrics(null, null), recognizer);

        source.Play(new TrackInfo("Channel", "An hour of talking", null, TimeSpan.FromMinutes(63)));
        PumpUntil(() => source.NoLyricsReports == 1);
        Assert.Equal(0, recognizer.Calls);
        Assert.Equal(Strings.NotFound, viewModel.Status);
    });

    [Fact]
    public void A_song_without_lyrics_is_listened_to_once_and_then_reported() => OnDispatcherThread(() =>
    {
        var source = new FakeSource();
        var recognizer = new FakeRecognizer();
        using var viewModel = Create(new AppSettings(), source, new FakeLyrics(null, null), recognizer);

        source.Play(Song);
        PumpUntil(() => source.NoLyricsReports == 1);
        Assert.Equal(1, recognizer.Calls);
    });

    [Fact]
    public void Found_lyrics_are_not_reported_as_missing() => OnDispatcherThread(() =>
    {
        var source = new FakeSource();
        using var viewModel = Create(new AppSettings(), source, new FakeLyrics(Synced(), TimeSpan.FromSeconds(250)));
        source.Play(Song);
        PumpUntil(() => viewModel.IsSynced);
        Assert.Equal(0, source.NoLyricsReports);
    });

    [Fact]
    public void Apps_switched_off_in_the_settings_reach_the_media_source() => OnDispatcherThread(() =>
    {
        var source = new FakeSource();
        var settings = new AppSettings { IgnoredApps = [new SavedApp("browser-id", "Browser")] };
        using var viewModel = Create(settings, source, new FakeLyrics(null, null));
        Assert.Equal(["browser-id"], source.Ignored);

        settings.IgnoredApps.Clear();
        viewModel.ApplySourceSettings();
        Assert.Empty(source.Ignored);
    });

    [Fact]
    public void The_source_menu_choice_goes_to_the_media_source_and_into_the_settings() => OnDispatcherThread(() =>
    {
        var source = new FakeSource();
        var settings = new AppSettings();
        using var viewModel = Create(settings, source, new FakeLyrics(null, null));
        Assert.Null(viewModel.PinnedApp);
        Assert.Null(source.PinnedApp);

        viewModel.PinnedApp = new SavedApp("music-id", "Music");
        Assert.Equal("music-id", source.PinnedApp);
        Assert.Equal(new SavedApp("music-id", "Music"), settings.PinnedApp);

        viewModel.PinnedApp = null;
        Assert.Null(source.PinnedApp);
        Assert.Null(settings.PinnedApp);
    });

    [Fact]
    public void A_saved_source_choice_is_in_force_from_the_start() => OnDispatcherThread(() =>
    {
        // Chosen in an earlier run; that player need not be open now.
        var source = new FakeSource();
        var settings = new AppSettings { PinnedApp = new SavedApp("music-id", "Music") };
        using var viewModel = Create(settings, source, new FakeLyrics(null, null));
        Assert.Equal("music-id", source.PinnedApp);
        Assert.Equal(new SavedApp("music-id", "Music"), viewModel.PinnedApp);
    });

    [Fact]
    public void Switching_the_chosen_player_off_takes_the_choice_back() => OnDispatcherThread(() =>
    {
        var source = new FakeSource();
        var settings = new AppSettings { PinnedApp = new SavedApp("browser-id", "Browser") };
        using var viewModel = Create(settings, source, new FakeLyrics(null, null));

        settings.IgnoredApps.Add(new SavedApp("browser-id", "Browser"));
        viewModel.ApplySourceSettings();
        Assert.Null(settings.PinnedApp);
        Assert.Null(source.PinnedApp);
        Assert.Equal(["browser-id"], source.Ignored);
    });

    [Fact]
    public void The_player_buttons_pass_their_command_to_the_media_source() => OnDispatcherThread(() =>
    {
        var source = new FakeSource();
        using var viewModel = Create(new AppSettings(), source, new FakeLyrics(null, null));
        viewModel.SendMediaCommand(MediaCommand.Previous);
        viewModel.SendMediaCommand(MediaCommand.PlayPause);
        viewModel.SendMediaCommand(MediaCommand.Next);
        Assert.Equal([MediaCommand.Previous, MediaCommand.PlayPause, MediaCommand.Next], source.Commands);
    });

    [Fact]
    public void The_source_menu_lists_automatic_first_and_marks_the_followed_player()
    {
        var players = new[] { new MediaPlayerInfo("music-id", "Music", IsFollowed: true), new MediaPlayerInfo("browser-id", "Web_Browser", IsFollowed: false) };

        var automatic = SourceMenu.Entries(players, pinned: null);
        Assert.Equal(3, automatic.Count);
        Assert.Equal(new SourceMenuEntry(Strings.SourceAutomatic, IsChecked: true, App: null), automatic[0]);
        Assert.Equal(new SourceMenuEntry(Strings.SourceFollowedNow("Music"), IsChecked: false, App: new SavedApp("music-id", "Music")), automatic[1]);
        // An underscore would otherwise turn the next letter into the entry's access key; the saved name keeps it.
        Assert.Equal(new SourceMenuEntry("Web__Browser", IsChecked: false, App: new SavedApp("browser-id", "Web_Browser")), automatic[2]);

        var byHand = SourceMenu.Entries(players, pinned: new SavedApp("browser-id", "Web_Browser"));
        Assert.Equal(3, byHand.Count);
        Assert.False(byHand[0].IsChecked);
        Assert.Equal(new SourceMenuEntry("Music", IsChecked: false, App: new SavedApp("music-id", "Music")), byHand[1]);
        Assert.True(byHand[2].IsChecked);

        Assert.Single(SourceMenu.Entries([], pinned: null));
    }

    [Fact]
    public void The_source_menu_keeps_showing_a_chosen_player_that_is_closed()
    {
        var players = new[] { new MediaPlayerInfo("music-id", "Music", IsFollowed: true) };
        var closed = new SavedApp("browser-id", "Browser");

        var entries = SourceMenu.Entries(players, pinned: closed);
        Assert.Equal(3, entries.Count);
        Assert.False(entries[0].IsChecked);
        // The rule's own pick is not marked while a choice is in force, even though it is followed for now.
        Assert.Equal(new SourceMenuEntry("Music", IsChecked: false, App: new SavedApp("music-id", "Music")), entries[1]);
        Assert.Equal(new SourceMenuEntry(Strings.SourceNotOpen("Browser"), IsChecked: true, App: closed), entries[2]);
    }

    [Fact]
    public void The_new_settings_have_their_defaults_and_survive_saving()
    {
        Assert.True(new DisplaySettings().VersionNotice);
        Assert.Empty(new AppSettings().IgnoredApps);
        Assert.Null(new AppSettings().PinnedApp);

        var saved = JsonSerializer.Serialize(new AppSettings
        {
            IgnoredApps = [new SavedApp("browser-id", "Browser")],
            PinnedApp = new SavedApp("music-id", "Music"),
            Display = new DisplaySettings { VersionNotice = false },
        });
        var loaded = JsonSerializer.Deserialize<AppSettings>(saved)!;
        Assert.Equal([new SavedApp("browser-id", "Browser")], loaded.IgnoredApps);
        Assert.Equal(new SavedApp("music-id", "Music"), loaded.PinnedApp);
        Assert.False(loaded.Display.VersionNotice);

        // A settings file written before these existed.
        var old = JsonSerializer.Deserialize<AppSettings>("""{ "Display": { "LyricsOnly": true } }""")!;
        Assert.True(old.Display.VersionNotice);
        Assert.Empty(old.IgnoredApps);
        Assert.Null(old.PinnedApp);
    }

    [Theory]
    [InlineData("Player.exe", "Player")]
    [InlineData("PLAYER.EXE", "PLAYER")]
    [InlineData("Vendor.MusicPlayer_abc123xyz!App", "MusicPlayer")]
    [InlineData("Vendor.Suite.Player_abc123xyz!Vendor.Suite.Player", "Player")]
    [InlineData("A1B2C3D4E5F60718", "A1B2C3D4E5F60718")]
    [InlineData("Browser", "Browser")]
    public void A_player_without_a_start_menu_name_gets_one_from_its_id(string id, string expected) =>
        Assert.Equal(expected, AppNames.Fallback(id));

    private static SyncedLyrics Synced() => new(LrcParser.Parse("[00:01.00]made-up line\n[00:05.00]another made-up line"), "Fake");

    private static MainViewModel Create(AppSettings settings, FakeSource source, FakeLyrics lyrics, ITrackRecognizer? recognizer = null) =>
        new(settings, source, new LyricsService([lyrics], cache: null), recognizer, TrackOffsets.InMemory(), Dispatcher.CurrentDispatcher);

    /// <summary>The view model posts to its dispatcher, so each test gets a thread with one and runs it until the condition holds.</summary>
    private static void OnDispatcherThread(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void PumpUntil(Func<bool> done)
    {
        var frame = new DispatcherFrame();
        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
        {
            if (done() || watch.Elapsed > TimeSpan.FromSeconds(10)) frame.Continue = false;
        }, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.True(done(), "The expected state was not reached in time.");
    }

    private sealed class FakeSource : IMediaSource
    {
        public event EventHandler<MediaTrack?>? TrackChanged;
        public event EventHandler<PlaybackState>? PlaybackChanged;

        public MediaTrack? CurrentTrack { get; private set; }
        public PlaybackState Playback { get; private set; } = PlaybackState.Stopped;
        public IReadOnlyList<MediaPlayerInfo> Players => [];
        public string? PinnedApp { get; set; }
        public List<string> Ignored { get; private set; } = [];
        public int NoLyricsReports { get; private set; }

        public void Play(TrackInfo track, bool sameSong = false)
        {
            CurrentTrack = new MediaTrack("music-id", track, null);
            Playback = new PlaybackState(true, TimeSpan.FromSeconds(10), DateTimeOffset.Now, 1.0, track.Duration);
            if (!sameSong) TrackChanged?.Invoke(this, CurrentTrack);
            PlaybackChanged?.Invoke(this, Playback);
        }

        public void SetIgnoredApps(IEnumerable<string> apps) => Ignored = [.. apps];

        public void ReportNoLyrics() => NoLyricsReports++;

        public List<MediaCommand> Commands { get; } = [];

        public Task SendAsync(MediaCommand command)
        {
            Commands.Add(command);
            return Task.CompletedTask;
        }

        public Task StartAsync() => Task.CompletedTask;

        public Task RefreshAsync() => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FakeLyrics(Lyrics? lyrics, TimeSpan? matchedLength) : ILyricsProvider
    {
        public string Name => "Fake";

        public Task<LyricsResult?> FindAsync(TrackInfo track, CancellationToken cancellationToken) =>
            Task.FromResult(lyrics is null ? null : new LyricsResult(lyrics, track with { Duration = matchedLength }));
    }

    private sealed class FakeRecognizer : ITrackRecognizer
    {
        public int Calls { get; private set; }

        public Task<TrackInfo?> RecognizeAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<TrackInfo?>(null);
        }
    }
}
