using System.Buffers.Binary;
using System.IO.Hashing;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LyricsApp.Core;
using LyricsApp.Core.Recognition;

namespace LyricsApp.Core.Tests.Recognition;

public class ShazamTests
{
    private const int Rate = ShazamSignature.SampleRate;

    /// <summary>
    /// A made-up melody: a different pure tone every half second, two per signature band.
    /// Each note swells and fades, so its loudest moment is a clear peak.
    /// </summary>
    private static float[] Melody(double seconds)
    {
        double[] tones = [300, 800, 2000, 4000, 450, 1200, 2600, 5000];
        const int note = Rate / 2;
        var samples = new float[(int)(seconds * Rate)];
        for (var i = 0; i < samples.Length; i++)
        {
            var tone = tones[i / note % tones.Length];
            var envelope = Math.Sin(Math.PI * (i % note) / note);
            samples[i] = (float)(0.5 * envelope * Math.Sin(2 * Math.PI * tone * i / Rate));
        }
        return samples;
    }

    [Fact]
    public void Signature_has_a_valid_header_and_checksum()
    {
        var signature = new ShazamSignature();
        signature.Add(Melody(4));
        var data = signature.Build();

        Assert.Equal(0xCAFE2580u, BinaryPrimitives.ReadUInt32LittleEndian(data));
        Assert.Equal(Crc32.HashToUInt32(data.AsSpan(8)), BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)));
        Assert.Equal(data.Length - 48, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8)));
        Assert.Equal(data.Length - 48, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(52)));
        Assert.Equal(3, (int)(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(28)) >> 27));
        Assert.Equal(signature.SampleCount, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(40)));
        Assert.Equal(4 * Rate, signature.SampleCount);
        Assert.Equal(0, data.Length % 4);
    }

    [Fact]
    public void Signature_peaks_land_on_the_played_tones()
    {
        var signature = new ShazamSignature();
        // Odd chunk sizes: samples must be stitched across Add calls.
        var melody = Melody(4);
        for (var i = 0; i < melody.Length; i += 1000) signature.Add(melody.AsSpan(i, Math.Min(1000, melody.Length - i)));

        var peaks = ReadPeaks(signature.Build());

        Assert.Equal(4, peaks.Count);
        Assert.All(peaks, band => Assert.NotEmpty(band));
        double[] tones = [300, 800, 2000, 4000, 450, 1200, 2600, 5000];
        foreach (var (_, hz) in peaks.SelectMany(b => b))
            Assert.Contains(tones, t => Math.Abs(t - hz) < 15);
    }

    [Fact]
    public void Silence_gives_an_empty_but_valid_signature()
    {
        var signature = new ShazamSignature();
        signature.Add(new float[3 * Rate]);
        var peaks = ReadPeaks(signature.Build());
        Assert.All(peaks, band => Assert.Empty(band));
    }

    /// <summary>Reads the four band blocks back into (spectrum, Hz) pairs.</summary>
    private static List<List<(int Spectrum, double Hz)>> ReadPeaks(byte[] data)
    {
        var bands = new List<List<(int, double)>>();
        var offset = 56;
        while (offset < data.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 4));
            Assert.Equal(0x60030040u + (uint)bands.Count, type);
            var band = new List<(int, double)>();
            var end = offset + 8 + length;
            var spectrum = 0;
            for (var p = offset + 8; end - p >= 5;)
            {
                var step = data[p++];
                if (step == 255)
                {
                    spectrum = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p));
                    p += 4;
                    step = data[p++];
                }
                spectrum += step;
                var magnitude = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p));
                var bin = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 2)) / 64.0;
                p += 4;
                Assert.True(magnitude > 0);
                band.Add((spectrum, bin * Rate / 2048));
            }
            bands.Add(band);
            offset = end;
        }
        return bands;
    }

    [Fact]
    public void Match_response_gives_the_song()
    {
        using var doc = JsonDocument.Parse("""
        {
          "results": { "matches": [ { "id": "42" } ] },
          "resources": { "shazam-songs": { "42": { "attributes": { "title": " Made Up Song ", "artist": "Nobody Real" } } } }
        }
        """);
        var match = ShazamClient.Parse(doc.RootElement);
        Assert.Equal(new TrackInfo("Nobody Real", "Made Up Song"), match.Track);
        Assert.Null(match.RetryAfter);
    }

    [Fact]
    public void No_match_response_gives_the_retry_time_or_nothing()
    {
        using var retry = JsonDocument.Parse("""{ "results": { "matches": [], "retry": { "retryInMilliseconds": 6000 } } }""");
        Assert.Equal(TimeSpan.FromSeconds(6), ShazamClient.Parse(retry.RootElement).RetryAfter);
        Assert.Null(ShazamClient.Parse(retry.RootElement).Track);

        using var none = JsonDocument.Parse("""{ "results": { "matches": [] } }""");
        Assert.Equal(new ShazamMatch(null, null), ShazamClient.Parse(none.RootElement));
    }

    [Fact]
    public async Task Recognizer_listens_longer_when_asked_and_returns_the_match()
    {
        var requests = new List<int>();
        var handler = new StubHandler(body =>
        {
            using var doc = JsonDocument.Parse(body);
            requests.Add(doc.RootElement.GetProperty("signatures")[0].GetProperty("samplems").GetInt32());
            return requests.Count == 1
                ? """{ "results": { "matches": [], "retry": { "retryInMilliseconds": 6000 } } }"""
                : """{ "results": { "matches": [ { "id": "1" } ] }, "resources": { "shazam-songs": { "1": { "attributes": { "title": "T", "artist": "A" } } } } }""";
        });
        var recognizer = new ShazamRecognizer(Client(handler), ct => Chunks(Melody(20), ct));

        var track = await recognizer.RecognizeAsync(CancellationToken.None);

        Assert.Equal(new TrackInfo("A", "T"), track);
        Assert.Equal(2, requests.Count);
        Assert.InRange(requests[0], 3000, 3200);
        Assert.InRange(requests[1], 6000, 6200);
    }

    [Fact]
    public async Task Recognizer_gives_up_without_a_retry_or_when_the_audio_stops()
    {
        var calls = 0;
        var noRetry = new StubHandler(_ => { calls++; return """{ "results": { "matches": [] } }"""; });
        Assert.Null(await new ShazamRecognizer(Client(noRetry), ct => Chunks(Melody(20), ct)).RecognizeAsync(CancellationToken.None));
        Assert.Equal(1, calls);

        var tooLong = new StubHandler(_ => """{ "results": { "matches": [], "retry": { "retryInMilliseconds": 60000 } } }""");
        Assert.Null(await new ShazamRecognizer(Client(tooLong), ct => Chunks(Melody(20), ct)).RecognizeAsync(CancellationToken.None));

        var never = new StubHandler(_ => throw new InvalidOperationException("must not be called"));
        Assert.Null(await new ShazamRecognizer(Client(never), ct => Chunks(Melody(2), ct)).RecognizeAsync(CancellationToken.None));
    }

    private static ShazamClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(ShazamClient.BaseAddress) });

    private static async IAsyncEnumerable<float[]> Chunks(float[] samples, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < samples.Length; i += 1600)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return samples.AsSpan(i, Math.Min(1600, samples.Length - i)).ToArray();
        }
    }

    private sealed class StubHandler(Func<string, string> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.StartsWith("https://amp.shazam.com/match/v1/", request.RequestUri!.ToString());
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(body)) };
        }
    }
}
