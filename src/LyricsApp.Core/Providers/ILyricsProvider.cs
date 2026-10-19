namespace LyricsApp.Core.Providers;

public interface ILyricsProvider
{
    string Name { get; }

    /// <summary>Returns null when nothing acceptable was found. Network or parse failures throw.</summary>
    Task<LyricsResult?> FindAsync(TrackInfo track, CancellationToken cancellationToken);
}
