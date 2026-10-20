namespace LyricsApp.Core.Recognition;

/// <summary>Names the song that is playing from a few seconds of its audio.</summary>
public interface ITrackRecognizer
{
    /// <summary>The recognised song, or null when it could not be named.</summary>
    Task<TrackInfo?> RecognizeAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Listens for a few seconds, asks Shazam, and listens a little longer when Shazam asks for more.
/// Audio stays in memory only long enough to compute the signature.
/// </summary>
/// <param name="openAudio">Mono samples at <see cref="ShazamSignature.SampleRate"/>; the sequence ends when no sound arrives.</param>
public sealed class ShazamRecognizer(ShazamClient client, Func<CancellationToken, IAsyncEnumerable<float[]>> openAudio) : ITrackRecognizer
{
    public static readonly TimeSpan FirstAttempt = TimeSpan.FromSeconds(3);

    /// <summary>Never listens longer than this, whatever Shazam asks for.</summary>
    public static readonly TimeSpan MaxListening = TimeSpan.FromSeconds(12);

    public async Task<TrackInfo?> RecognizeAsync(CancellationToken cancellationToken)
    {
        var signature = new ShazamSignature();
        var sessionId = Guid.NewGuid().ToString();
        var nextAttempt = FirstAttempt;

        await foreach (var samples in openAudio(cancellationToken).WithCancellation(cancellationToken))
        {
            signature.Add(samples);
            if (signature.Duration < nextAttempt) continue;

            var match = await client.MatchAsync(signature.Build(), signature.Duration, sessionId, cancellationToken);
            if (match.Track is { } track) return track;
            if (match.RetryAfter is not { } retry || retry > MaxListening) return null;
            nextAttempt = retry > signature.Duration ? retry : signature.Duration + FirstAttempt;
            if (nextAttempt > MaxListening) return null;
        }
        return null;
    }
}
