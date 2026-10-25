namespace VideoSync.Core;

/// <summary>Immutable, bounded peak envelope with a range-max tree for drawing at any zoom.</summary>
public sealed class AudioWaveform
{
    public const int MaximumBins = 65536;
    private readonly ushort[] _peaks;
    private readonly ushort[] _tree;
    private readonly int _leafCount;
    public long DurationMs { get; }
    public long BinDurationMs { get; }
    public int BinCount => _peaks.Length;
    public ReadOnlySpan<ushort> Peaks => _peaks;

    public AudioWaveform(long durationMs, long binDurationMs, ReadOnlySpan<ushort> peaks)
    {
        if (durationMs <= 0 || binDurationMs <= 0 || peaks.Length is < 1 or > MaximumBins ||
            Math.Ceiling(durationMs / (double)binDurationMs) != peaks.Length)
            throw new ArgumentException("Invalid waveform dimensions.");
        DurationMs = durationMs;
        BinDurationMs = binDurationMs;
        _peaks = peaks.ToArray();
        _leafCount = 1;
        while (_leafCount < _peaks.Length) _leafCount *= 2;
        _tree = new ushort[_leafCount * 2];
        for (int i = 0; i < _peaks.Length; i++)
        {
            if (_peaks[i] > 32768) throw new ArgumentException("Invalid waveform peak.");
            _tree[_leafCount + i] = _peaks[i];
        }
        for (int i = _leafCount - 1; i > 0; i--) _tree[i] = Math.Max(_tree[i * 2], _tree[i * 2 + 1]);
    }

    public double PeakBetween(double startMs, double endMs)
    {
        if (!double.IsFinite(startMs) || !double.IsFinite(endMs) || endMs <= startMs || endMs <= 0 || startMs >= DurationMs) return 0;
        int left = (int)(Math.Max(0, startMs) / BinDurationMs) + _leafCount;
        int right = Math.Min(BinCount, (int)Math.Ceiling(Math.Min(DurationMs, endMs) / BinDurationMs)) + _leafCount;
        ushort peak = 0;
        while (left < right)
        {
            if ((left & 1) != 0) peak = Math.Max(peak, _tree[left++]);
            if ((right & 1) != 0) peak = Math.Max(peak, _tree[--right]);
            left /= 2; right /= 2;
        }
        return peak / 32768d;
    }
}

/// <summary>Combines signed PCM channels into peaks without retaining decoded samples.</summary>
public sealed class AudioWaveformBuilder
{
    private readonly long _durationMs, _binMs;
    private readonly ushort[] _peaks;
    public AudioWaveformBuilder(long durationMs)
    {
        if (durationMs <= 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
        _durationMs = durationMs;
        _binMs = Math.Max(10, (long)Math.Ceiling(durationMs / (double)AudioWaveform.MaximumBins));
        _peaks = new ushort[(int)Math.Ceiling(durationMs / (double)_binMs)];
    }

    public void AddSamples(ReadOnlySpan<short> samples, int sampleRate, int channels, double startMs)
    {
        if (sampleRate <= 0 || channels <= 0 || samples.Length % channels != 0 || !double.IsFinite(startMs))
            throw new ArgumentException("Invalid waveform PCM block.");
        for (int frame = 0; frame < samples.Length / channels; frame++)
        {
            double time = startMs + frame * (1000d / sampleRate);
            if (time < 0) continue;
            if (time >= _durationMs) break;
            int bin = (int)(time / _binMs);
            int peak = _peaks[bin];
            for (int channel = 0; channel < channels; channel++)
                peak = Math.Max(peak, Math.Abs((int)samples[frame * channels + channel]));
            _peaks[bin] = (ushort)peak;
        }
    }
    public AudioWaveform Build() => new(_durationMs, _binMs, _peaks);
}
