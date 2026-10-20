using System.Buffers.Binary;

namespace LyricsApp.Core.Recognition;

/// <summary>
/// Turns 16 kHz mono audio into the fingerprint ("signature") that Shazam's match endpoint expects.
/// Only spectral peaks end up in it; the audio cannot be rebuilt from a signature.
/// The format and the peak rules follow the open-source shazam-for-real client (ISC licence, Aleksey Martynov).
/// </summary>
public sealed class ShazamSignature
{
    public const int SampleRate = 16000;

    private const int HopSize = 128;                 // 125 spectra per second
    private const int WindowSize = HopSize * 16;     // 2048-point FFT
    private const int BinCount = WindowSize / 2 + 1;

    // A peak must be the loudest point within ±45 spectra (0.36 s) over ±1 bin, and within ±3 spectra over ±10 bins.
    private const int TimeReach = 45, TimeReachBins = 1;
    private const int FreqReach = 3, FreqReachBins = 10;

    private static readonly int[] BandEdgesHz = [250, 520, 1450, 3500, 5500];
    private static readonly int MinBin = Math.Max(HzToBin(BandEdgesHz[0]), FreqReachBins);
    private static readonly int MaxBin = Math.Min(HzToBin(BandEdgesHz[^1]), BinCount - FreqReachBins);

    private const float MinPower = 1f / 512 / 512;
    private static readonly float LogMinPower = MathF.Log(MinPower);

    private static readonly float[] Hann = CreateHann();

    private readonly float[] ring = new float[WindowSize];
    private readonly float[] hop = new float[HopSize];
    private int hopFill;
    private readonly List<float[]?> spectra = [];
    private readonly List<Peak>[] bands = [[], [], [], []];
    private readonly float[] re = new float[WindowSize];
    private readonly float[] im = new float[WindowSize];

    private readonly record struct Peak(int Spectrum, float Bin, float Magnitude);

    /// <summary>Samples taken in so far (whole hops only).</summary>
    public int SampleCount { get; private set; }

    public TimeSpan Duration => TimeSpan.FromSeconds((double)SampleCount / SampleRate);

    /// <summary>Feeds mono samples in the range -1..1 at <see cref="SampleRate"/>.</summary>
    public void Add(ReadOnlySpan<float> samples)
    {
        while (!samples.IsEmpty)
        {
            var take = Math.Min(HopSize - hopFill, samples.Length);
            samples[..take].CopyTo(hop.AsSpan(hopFill));
            hopFill += take;
            samples = samples[take..];
            if (hopFill < HopSize) break;

            hop.CopyTo(ring.AsSpan(SampleCount % WindowSize));
            hopFill = 0;
            SampleCount += HopSize;
            if (SampleCount >= WindowSize) AddSpectrum();
        }
    }

    /// <summary>The signature for everything added so far.</summary>
    public byte[] Build()
    {
        // Keep the strongest peaks per band, as many as the official client would.
        var limit = 12 + spectra.Count / 12;
        var bandData = new byte[bands.Length][];
        for (var i = 0; i < bands.Length; i++)
        {
            var peaks = bands[i]
                .OrderByDescending(p => p.Magnitude)
                .Take(limit)
                .OrderBy(p => p.Spectrum)
                .ToList();
            bandData[i] = EncodeBand(peaks);
        }

        const int headerSize = 48;
        var size = headerSize + 8 + bandData.Sum(b => 8 + b.Length);
        var data = new byte[size];
        var span = data.AsSpan();
        var contentSize = size - headerSize;

        WriteUInt32(span, 0, 0xCAFE2580);
        WriteUInt32(span, 8, (uint)contentSize);
        WriteUInt32(span, 12, VersionCode());
        WriteUInt32(span, 28, 3u << 27);              // sample rate code: 3 = 16 kHz
        WriteUInt32(span, 40, (uint)SampleCount);
        WriteUInt32(span, 44, 0x007C0000);
        WriteUInt32(span, 48, 0x40000000);
        WriteUInt32(span, 52, (uint)contentSize);

        var offset = headerSize + 8;
        for (var i = 0; i < bandData.Length; i++)
        {
            WriteUInt32(span, offset, 0x60030040u + (uint)i);
            WriteUInt32(span, offset + 4, (uint)bandData[i].Length);
            bandData[i].CopyTo(span[(offset + 8)..]);
            offset += 8 + bandData[i].Length;
        }

        WriteUInt32(span, 4, Crc32.Compute(span[8..]));
        return data;
    }

    private void AddSpectrum()
    {
        // Oldest sample first: the ring's next write position holds the oldest hop.
        var start = SampleCount % WindowSize;
        for (var i = 0; i < WindowSize; i++)
        {
            re[i] = ring[(start + i) % WindowSize] * Hann[i];
            im[i] = 0;
        }
        Fft.Forward(re, im);

        var power = new float[BinCount];
        for (var bin = 0; bin < BinCount; bin++)
            power[bin] = 2 * (re[bin] * re[bin] + im[bin] * im[bin]);
        spectra.Add(power);

        // A spectrum can be judged once TimeReach newer ones exist.
        if (spectra.Count > 2 * TimeReach)
        {
            var index = spectra.Count - TimeReach - 1;
            FindPeaks(index);
            spectra[index - TimeReach] = null; // no longer inside anyone's neighbourhood
        }
    }

    private void FindPeaks(int index)
    {
        var center = spectra[index]!;
        for (var bin = MinBin; bin < MaxBin; bin++)
        {
            var value = center[bin];
            if (value < MinPower) continue;
            if (!IsLoudest(index, bin, value, TimeReach, TimeReachBins)) continue;
            if (!IsLoudest(index, bin, value, FreqReach, FreqReachBins)) continue;

            // Quadratic interpolation on the log magnitudes refines the peak's frequency.
            var left = LogMagnitude(center[bin - 1]);
            var mid = LogMagnitude(value);
            var right = LogMagnitude(center[bin + 1]);
            var denominator = left - 2 * mid + right;
            var shift = denominator == 0 ? 0 : (left - right) / denominator / 2;
            var exactBin = bin + shift;

            var band = BandOf(exactBin * SampleRate / WindowSize);
            if (band >= 0) bands[band].Add(new Peak(index, exactBin, mid));
        }
    }

    private bool IsLoudest(int index, int bin, float value, int reach, int reachBins)
    {
        for (var s = index - reach; s <= index + reach; s++)
        {
            var spectrum = spectra[s]!;
            for (var b = bin - reachBins; b <= bin + reachBins; b++)
            {
                if (s == index && b == bin) continue;
                if (spectrum[b] >= value) return false;
            }
        }
        return true;
    }

    private static int BandOf(float hz)
    {
        if (hz < BandEdgesHz[0]) return -1;
        for (var i = 1; i < BandEdgesHz.Length; i++)
            if (hz < BandEdgesHz[i]) return i - 1;
        return -1;
    }

    private static float LogMagnitude(float power) => 18 * 1024 * (1 - MathF.Log(power) / LogMinPower);

    private static byte[] EncodeBand(List<Peak> peaks)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var last = 0;
        foreach (var peak in peaks)
        {
            // Spectrum numbers are stored as small steps; a big jump is written out in full first.
            if (peak.Spectrum - last >= 100)
            {
                last = peak.Spectrum;
                writer.Write((byte)255);
                writer.Write(last);
            }
            writer.Write((byte)(peak.Spectrum - last));
            writer.Write((ushort)Math.Clamp(MathF.Round(peak.Magnitude), 1, ushort.MaxValue));
            writer.Write((ushort)Math.Clamp(MathF.Round(64 * peak.Bin), 1, ushort.MaxValue));
            last = peak.Spectrum;
        }
        while (stream.Length % 4 != 0) writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    // Generator 10.1.3, unpacker 7.0.0, as reported by current official clients.
    private static uint VersionCode() =>
        ((10u << 25) + (1u << 20) + (3u << 15) + (7u << 10)) ^ 0x80000000;

    private static int HzToBin(int hz) => (int)Math.Round((double)hz * WindowSize / SampleRate);

    private static void WriteUInt32(Span<byte> span, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], value);

    private static float[] CreateHann()
    {
        var window = new float[WindowSize];
        for (var i = 0; i < WindowSize; i++)
            window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (WindowSize - 1)));
        return window;
    }

    /// <summary>In-place radix-2 FFT, enough for one fixed power-of-two size.</summary>
    private static class Fft
    {
        public static void Forward(float[] re, float[] im)
        {
            var n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                var bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (var length = 2; length <= n; length <<= 1)
            {
                var angle = -2 * Math.PI / length;
                var stepRe = Math.Cos(angle);
                var stepIm = Math.Sin(angle);
                for (var i = 0; i < n; i += length)
                {
                    double wRe = 1, wIm = 0;
                    for (var k = 0; k < length / 2; k++)
                    {
                        var a = i + k;
                        var b = a + length / 2;
                        var tRe = (float)(re[b] * wRe - im[b] * wIm);
                        var tIm = (float)(re[b] * wIm + im[b] * wRe);
                        re[b] = re[a] - tRe;
                        im[b] = im[a] - tIm;
                        re[a] += tRe;
                        im[a] += tIm;
                        (wRe, wIm) = (wRe * stepRe - wIm * stepIm, wRe * stepIm + wIm * stepRe);
                    }
                }
            }
        }
    }

    private static class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (var i = 0u; i < 256; i++)
            {
                var c = i;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }
    }
}
