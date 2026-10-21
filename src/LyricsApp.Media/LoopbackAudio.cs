using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LyricsApp.Core.Recognition;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LyricsApp.Media;

/// <summary>
/// What the player is sending to the speakers, for song recognition. Only the player's own process tree is
/// captured when it can be found (Windows 10 2004+); otherwise the whole output. Nothing is written to disk.
/// </summary>
public static class LoopbackAudio
{
    /// <summary>The sequence ends after this long without sound (paused, muted, ad break that never ends…).</summary>
    public static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(5);

    // AppUserModelIds the media session reports for common browsers, mapped to their process names.
    private static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Chrome"] = "chrome",
        ["MSEdge"] = "msedge",
        ["308046B0AF4A39CB"] = "firefox",
        ["Brave"] = "brave",
        ["Opera"] = "opera",
        ["Vivaldi"] = "vivaldi",
    };

    /// <summary>Mono samples at <see cref="ShazamSignature.SampleRate"/> from the app that reports <paramref name="sourceApp"/>.</summary>
    public static async IAsyncEnumerable<float[]> ReadAsync(string? sourceApp, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var builder = new WasapiRecorderBuilder().WithFormat(new WaveFormat(ShazamSignature.SampleRate, 16, 1));
        builder = FindPlayerProcess(sourceApp) is { } pid && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
            ? builder.WithProcessLoopback((uint)pid, ProcessLoopbackMode.IncludeTargetProcessTree)
            : builder.WithLoopbackCapture();

        await using var recorder = await builder.BuildAsync();
        using var silence = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        silence.CancelAfter(SilenceTimeout);

        await using var buffers = recorder.CaptureAsync(silence.Token).GetAsyncEnumerator(silence.Token);
        while (true)
        {
            try
            {
                if (!await buffers.MoveNextAsync()) yield break;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                yield break; // silence timeout
            }

            var buffer = buffers.Current;
            if ((buffer.Flags & AudioClientBufferFlags.Silent) != 0 || buffer.Data.Length < 2) continue;
            var samples = ToFloats(buffer.Data);
            if (IsSilent(samples)) continue;

            silence.CancelAfter(SilenceTimeout);
            yield return samples;
        }
    }

    private static float[] ToFloats(ReadOnlyMemory<byte> pcm16)
    {
        var pcm = MemoryMarshal.Cast<byte, short>(pcm16.Span[..(pcm16.Length & ~1)]);
        var samples = new float[pcm.Length];
        for (var i = 0; i < pcm.Length; i++) samples[i] = pcm[i] / 32768f;
        return samples;
    }

    private static bool IsSilent(float[] samples)
    {
        foreach (var s in samples)
            if (Math.Abs(s) > 1f / 1024) return false;
        return true;
    }

    /// <summary>
    /// The root process of the player, found from the media session's AppUserModelId. When the id names no
    /// process (Edge, for example, reports a per-profile id), the only process playing sound right now.
    /// </summary>
    public static int? FindPlayerProcess(string? sourceApp)
    {
        if (string.IsNullOrWhiteSpace(sourceApp)) return null;
        return FindByAppId(sourceApp) ?? FindOnlyAudibleProcess();
    }

    private static int? FindByAppId(string sourceApp)
    {
        IEnumerable<Process> candidates;
        var bang = sourceApp.IndexOf('!');
        if (bang > 0)
        {
            // Packaged app: "PackageFamilyName!AppId".
            var family = sourceApp[..bang];
            candidates = Process.GetProcesses().Where(p => PackageFamilyName(p) is { } f && f.Equals(family, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var name = KnownApps.TryGetValue(sourceApp, out var known) ? known : Path.GetFileNameWithoutExtension(sourceApp);
            candidates = Process.GetProcessesByName(name);
        }

        // Browsers run many processes; the oldest one is the root, whose tree includes the audio process.
        int? best = null;
        var bestStart = DateTime.MaxValue;
        foreach (var process in candidates)
        {
            using (process)
            {
                try
                {
                    if (process.StartTime < bestStart)
                    {
                        bestStart = process.StartTime;
                        best = process.Id;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Exited or not ours to inspect.
                }
            }
        }
        return best;
    }

    /// <summary>
    /// The process behind the one audio session on the default speakers that is making sound, sampled a few
    /// times over a moment. Null when none or several are audible: then nobody can tell which is the player.
    /// </summary>
    private static int? FindOnlyAudibleProcess()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (!enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var device)) return null;
            using (device)
            {
                var sessions = device.AudioSessionManager;
                var audible = new HashSet<uint>();
                var self = (uint)Environment.ProcessId;
                for (var sample = 0; sample < 5; sample++)
                {
                    sessions.RefreshSessions();
                    var all = sessions.Sessions;
                    for (var i = 0; i < all.Count; i++)
                    {
                        var session = all[i];
                        if (session.IsSystemSoundsSession || session.GetProcessID == 0 || session.GetProcessID == self) continue;
                        if (session.AudioMeterInformation.MasterPeakValue > 0.001f) audible.Add(session.GetProcessID);
                    }
                    Thread.Sleep(60);
                }
                return audible.Count == 1 ? (int)audible.First() : null;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null; // No audio device or sessions not readable: fall back to the whole output.
        }
    }

    private static string? PackageFamilyName(Process process)
    {
        const uint QueryLimitedInformation = 0x1000;
        const int InsufficientBuffer = 122;
        var handle = OpenProcess(QueryLimitedInformation, false, (uint)process.Id);
        if (handle == IntPtr.Zero) return null;
        try
        {
            uint length = 0;
            if (GetPackageFamilyName(handle, ref length, null) != InsufficientBuffer) return null;
            var buffer = new char[length];
            return GetPackageFamilyName(handle, ref length, buffer) == 0 ? new string(buffer, 0, (int)length - 1) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(IntPtr process, ref uint length, [Out] char[]? name);
}
