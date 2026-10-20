using LyricsApp.Core.Providers;

namespace LyricsApp.Core;

public sealed record LyricsSearchResult(LyricsResult? Result, string? ProviderName, IReadOnlyList<string> Errors);

/// <summary>
/// Tries providers in order. Synced lyrics win immediately; plain lyrics are kept while later
/// providers are asked for a synced version. Online results are saved to the local folder.
/// </summary>
public sealed class LyricsService(IReadOnlyList<ILyricsProvider> providers, LocalFolderProvider? cache)
{
    /// <param name="saveAs">
    /// Name to cache the result under, when it differs from <paramref name="track"/> (a song recognised from audio is
    /// saved under what the player reports, so the next play finds it in the local folder).
    /// </param>
    public async Task<LyricsSearchResult> FindAsync(TrackInfo track, CancellationToken cancellationToken, TrackInfo? saveAs = null)
    {
        (ILyricsProvider Provider, LyricsResult Result)? plain = null;
        var errors = new List<string>();

        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LyricsResult? result;
            try
            {
                result = await provider.FindAsync(track, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{provider.Name}: {ex.Message}");
                continue;
            }

            if (result is null) continue;
            if (result.Lyrics is SyncedLyrics or InstrumentalLyrics)
                return await CacheAndReturn(provider, saveAs ?? track, saveAs is not null, result, errors, cancellationToken);
            plain ??= (provider, result);
        }

        return plain is { } p
            ? await CacheAndReturn(p.Provider, saveAs ?? track, saveAs is not null, p.Result, errors, cancellationToken)
            : new LyricsSearchResult(null, null, errors);
    }

    private async Task<LyricsSearchResult> CacheAndReturn(ILyricsProvider provider, TrackInfo saveAs, bool renamed, LyricsResult result, List<string> errors, CancellationToken cancellationToken)
    {
        // A local hit is already cached under its own name, but a renamed search still needs a copy.
        if (cache is not null && (!ReferenceEquals(provider, cache) || renamed))
        {
            try
            {
                await cache.SaveAsync(saveAs, result.Lyrics, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{cache.Name}: kaydedilemedi ({ex.Message})");
            }
        }
        return new LyricsSearchResult(result, provider.Name, errors);
    }
}
