using System.Net;
using System.Text;
using System.Text.Json;
using LyricsApp.Core;
using LyricsApp.Core.Providers;

namespace LyricsApp.Core.Tests;

// All lyric text here is made up.
public class YouTubeMusicParsingTests
{
    private const string SearchAnswer = """
    { "contents": { "tabs": [ { "items": [
      { "musicResponsiveListItemRenderer": {
          "playlistItemData": { "videoId": "cover" },
          "flexColumns": [
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Bahçe Kapısı" } ] } } },
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Başka Biri" }, { "text": " • " }, { "text": "Albüm" }, { "text": " • " }, { "text": "3:00" } ] } } } ] } },
      { "musicResponsiveListItemRenderer": {
          "playlistItemData": { "videoId": "long" },
          "flexColumns": [
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Bahçe Kapısı" } ] } } },
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Ayşe Yılmaz" }, { "text": " • " }, { "text": "Konser" }, { "text": " • " }, { "text": "9:40" } ] } } } ] } },
      { "musicResponsiveListItemRenderer": {
          "playlistItemData": { "videoId": "right" },
          "flexColumns": [
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Bahçe Kapısı" } ] } } },
            { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Ayşe Yılmaz" }, { "text": " ve " }, { "text": "Can Demir" }, { "text": " • " }, { "text": "Stüdyo" }, { "text": " • " }, { "text": "3:21" } ] } } } ] } },
      { "musicResponsiveListItemRenderer": { "flexColumns": [] } }
    ] } ] } }
    """;

    private static readonly TrackInfo Query = new("Ayşe Yılmaz", "BAHÇE KAPISI", null, TimeSpan.FromSeconds(200));

    [Fact]
    public void Search_picks_the_right_artist_and_length()
    {
        using var doc = JsonDocument.Parse(SearchAnswer);

        var best = YouTubeMusicProvider.PickBest(Query, doc.RootElement);

        Assert.Equal("right", best?.VideoId);
        Assert.Equal("Ayşe Yılmaz ve Can Demir", best?.Info.Artist);
        Assert.Equal("Stüdyo", best?.Info.Album);
        Assert.Equal(TimeSpan.FromSeconds(201), best?.Info.Duration);
    }

    [Fact]
    public void Search_without_an_acceptable_song_gives_null()
    {
        using var doc = JsonDocument.Parse(SearchAnswer);
        Assert.Null(YouTubeMusicProvider.PickBest(new TrackInfo("Ayşe Yılmaz", "Başka Şarkı"), doc.RootElement));

        using var empty = JsonDocument.Parse("{}");
        Assert.Null(YouTubeMusicProvider.PickBest(Query, empty.RootElement));
    }

    [Theory]
    [InlineData("""{ "runs": [ { "text": "Sanatçı" }, { "text": " • " }, { "text": "Albüm" } ] }""", null)]
    [InlineData("""{ "runs": [ { "text": "Sanatçı" }, { "text": " • " }, { "text": "1:02:03" } ] }""", 3723)]
    [InlineData("""{ "runs": [ { "text": "Sanatçı" } ] }""", null)]
    public void Song_row_tolerates_missing_album_or_length(string meta, int? seconds)
    {
        using var doc = JsonDocument.Parse($$"""
        { "playlistItemData": { "videoId": "v" }, "flexColumns": [
          { "musicResponsiveListItemFlexColumnRenderer": { "text": { "runs": [ { "text": "Şarkı" } ] } } },
          { "musicResponsiveListItemFlexColumnRenderer": { "text": {{meta}} } } ] }
        """);

        var song = YouTubeMusicProvider.ReadSong(doc.RootElement);

        Assert.Equal("Sanatçı", song?.Info.Artist);
        Assert.Equal(seconds is null ? null : TimeSpan.FromSeconds(seconds.Value), song?.Info.Duration);
        Assert.Null(song?.Info.Album);
    }

    [Fact]
    public void Finds_the_lyrics_page_among_other_tabs()
    {
        using var doc = JsonDocument.Parse("""
        { "tabs": [ { "endpoint": { "browseEndpoint": { "browseId": "FEmusic_related" } } },
                    { "endpoint": { "browseEndpoint": { "browseId": "MPLYt_abc" } } } ] }
        """);
        Assert.Equal("MPLYt_abc", YouTubeMusicProvider.FindLyricsBrowseId(doc.RootElement));

        using var none = JsonDocument.Parse("""{ "tabs": [ { "endpoint": { "browseEndpoint": { "browseId": "FEmusic_related" } } } ] }""");
        Assert.Null(YouTubeMusicProvider.FindLyricsBrowseId(none.RootElement));
    }

    [Fact]
    public void Timed_lines_are_read_in_order_and_pause_marks_become_empty()
    {
        using var doc = JsonDocument.Parse("""
        { "model": { "lyricsData": { "timedLyricsData": [
          { "lyricLine": "♪", "cueRange": { "startTimeMilliseconds": "0", "endTimeMilliseconds": "9000" } },
          { "lyricLine": "Kapının önünde kedi", "cueRange": { "startTimeMilliseconds": "9000" } },
          { "lyricLine": "Çay soğudu", "cueRange": { "startTimeMilliseconds": 12500 } },
          { "lyricLine": "Zamanı yok" }
        ] } } }
        """);

        var lines = YouTubeMusicProvider.ReadTimedLyrics(doc.RootElement);

        Assert.Equal(
            [new LyricLine(TimeSpan.Zero, ""), new LyricLine(TimeSpan.FromSeconds(9), "Kapının önünde kedi"), new LyricLine(TimeSpan.FromSeconds(12.5), "Çay soğudu")],
            lines);
    }

    [Fact]
    public void Only_pause_marks_or_no_data_means_no_timed_lyrics()
    {
        using var marks = JsonDocument.Parse("""{ "timedLyricsData": [ { "lyricLine": "♪ ♪", "cueRange": { "startTimeMilliseconds": "0" } } ] }""");
        Assert.Empty(YouTubeMusicProvider.ReadTimedLyrics(marks.RootElement));

        using var none = JsonDocument.Parse("{}");
        Assert.Empty(YouTubeMusicProvider.ReadTimedLyrics(none.RootElement));
    }

    [Fact]
    public void Plain_lyrics_come_from_the_description_shelf()
    {
        using var doc = JsonDocument.Parse("""
        { "musicDescriptionShelfRenderer": {
            "description": { "runs": [ { "text": "Kapının önünde kedi  \r\nÇay soğudu\n\n\n\nBahçe sessiz" } ] },
            "footer": { "runs": [ { "text": "Kaynak: Örnek" } ] } } }
        """);
        Assert.Equal("Kapının önünde kedi\nÇay soğudu\n\nBahçe sessiz", YouTubeMusicProvider.ReadPlainLyrics(doc.RootElement));

        using var none = JsonDocument.Parse("""{ "musicDescriptionShelfRenderer": { "description": { "runs": [ { "text": " " } ] } } }""");
        Assert.Null(YouTubeMusicProvider.ReadPlainLyrics(none.RootElement));
    }

    private const string NextAnswer = """{ "tabs": [ { "endpoint": { "browseEndpoint": { "browseId": "MPLYt_abc" } } } ] }""";
    private const string TimedAnswer = """{ "timedLyricsData": [ { "lyricLine": "Kapının önünde kedi", "cueRange": { "startTimeMilliseconds": "1000" } } ] }""";
    private const string PlainAnswer = """{ "musicDescriptionShelfRenderer": { "description": { "runs": [ { "text": "Kapının önünde kedi" } ] } } }""";

    [Fact]
    public async Task Timed_lyrics_are_asked_as_the_android_client_and_win()
    {
        var calls = new List<string>();
        using var http = Stub(calls, androidBrowse: TimedAnswer, webBrowse: PlainAnswer);

        var result = await new YouTubeMusicProvider(http).FindAsync(Query, CancellationToken.None);

        var synced = Assert.IsType<SyncedLyrics>(result?.Lyrics);
        Assert.Equal("YouTube Music: right", synced.Source);
        Assert.Equal(["search WEB_REMIX", "next WEB_REMIX", "browse ANDROID_MUSIC"], calls);
    }

    [Fact]
    public async Task Falls_back_to_plain_lyrics()
    {
        var calls = new List<string>();
        using var http = Stub(calls, androidBrowse: "{}", webBrowse: PlainAnswer);

        var result = await new YouTubeMusicProvider(http).FindAsync(Query, CancellationToken.None);

        Assert.IsType<PlainLyrics>(result?.Lyrics);
        Assert.Equal(["search WEB_REMIX", "next WEB_REMIX", "browse ANDROID_MUSIC", "browse WEB_REMIX"], calls);
    }

    [Fact]
    public async Task Song_without_a_lyrics_page_gives_null_after_two_requests()
    {
        var calls = new List<string>();
        using var http = Stub(calls, androidBrowse: "{}", webBrowse: "{}", next: "{}");

        Assert.Null(await new YouTubeMusicProvider(http).FindAsync(Query, CancellationToken.None));
        Assert.Equal(["search WEB_REMIX", "next WEB_REMIX"], calls);
    }

    [Fact]
    public async Task Server_errors_throw_so_the_chain_can_record_them()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))) { BaseAddress = new Uri(YouTubeMusicProvider.BaseAddress) };
        await Assert.ThrowsAsync<HttpRequestException>(() => new YouTubeMusicProvider(http).FindAsync(Query, CancellationToken.None));
    }

    private static HttpClient Stub(List<string> calls, string androidBrowse, string webBrowse, string next = NextAnswer) =>
        new(new StubHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(body);
            var client = doc.RootElement.GetProperty("context").GetProperty("client").GetProperty("clientName").GetString();
            var endpoint = request.RequestUri!.AbsolutePath.Split('/')[^1];
            calls.Add($"{endpoint} {client}");

            var answer = endpoint switch
            {
                "search" => SearchAnswer,
                "next" => next,
                _ => client == "ANDROID_MUSIC" ? androidBrowse : webBrowse,
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }))
        { BaseAddress = new Uri(YouTubeMusicProvider.BaseAddress) };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
