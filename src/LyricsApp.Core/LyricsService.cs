using LyricsApp.Core.Providers;

namespace LyricsApp.Core;

public sealed record LyricsSearchResult(LyricsResult? Result, string? ProviderName, IReadOnlyList<string> Errors);

/// <summary>
/// Tries providers in order. Synced lyrics win immediately; plain lyrics are kept while later
/// providers are asked for a synced version. Online results are saved to the local folder.
/// </summary>
public sealed class LyricsService(IReadOnlyList<ILyricsProvider> providers, LocalFolderProvider? cache)
{
    public async Task<LyricsSearchResult> FindAsync(TrackInfo track, CancellationToken cancellationToken)
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
                return await CacheAndReturn(provider, track, result, errors, cancellationToken);
            plain ??= (provider, result);
        }

        return plain is { } p
            ? await CacheAndReturn(p.Provider, track, p.Result, errors, cancellationToken)
            : new LyricsSearchResult(null, null, errors);
    }

    private async Task<LyricsSearchResult> CacheAndReturn(ILyricsProvider provider, TrackInfo track, LyricsResult result, List<string> errors, CancellationToken cancellationToken)
    {
        if (cache is not null && !ReferenceEquals(provider, cache))
        {
            try
            {
                await cache.SaveAsync(track, result.Lyrics, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{cache.Name}: kaydedilemedi ({ex.Message})");
            }
        }
        return new LyricsSearchResult(result, provider.Name, errors);
    }
}
