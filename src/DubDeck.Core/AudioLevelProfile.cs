namespace DubDeck.Core;

/// <summary>A fixed per-source correction based on loud passages, with peak headroom.</summary>
public sealed record AudioLevelProfile
{
    public double LoudLevelDb { get; }
    public double PeakDb { get; }
    public AudioLevelProfile(double loudLevelDb, double peakDb)
    {
        if (!double.IsFinite(loudLevelDb) || !double.IsFinite(peakDb) ||
            loudLevelDb is < -120 or > 0 || peakDb is < -120 or > 0 || loudLevelDb > peakDb + .01)
            throw new ArgumentException("Invalid audio level profile.");
        LoudLevelDb = loudLevelDb;
        PeakDb = peakDb;
    }
    public double Gain
    {
        get
        {
            if (LoudLevelDb <= -60) return 1;
            double gainDb = Math.Min(Math.Min(-20 - LoudLevelDb, 12), -3 - PeakDb);
            return Math.Pow(10, gainDb / 20);
        }
    }
    public double GainDb => 20 * Math.Log10(Gain);

    // VLC 3's DirectSound output uses a cubic volume curve. Keep the manual
    // fader behavior while applying the analyzed correction as linear PCM gain.
    public double PlayerVolumeScale => Math.Cbrt(Gain);
}

/// <summary>Bounded 400 ms RMS histogram; quiet gaps do not dilute the loud-passage level.</summary>
public sealed class AudioLevelAnalyzer
{
    private readonly long[] _histogram = new long[481];
    private double _squares, _peak;
    private long _samples;
    private int _frames, _sampleRate;

    public void AddSamples(ReadOnlySpan<short> samples, int sampleRate, int channels)
    {
        if (sampleRate <= 0 || channels <= 0 || samples.Length % channels != 0)
            throw new ArgumentException("Invalid level-analysis PCM block.");
        if (_sampleRate != 0 && _sampleRate != sampleRate) FinishBlock();
        _sampleRate = sampleRate;
        int blockFrames = Math.Max(1, (int)(sampleRate * .4));
        for (int frame = 0; frame < samples.Length / channels; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                double value = samples[frame * channels + channel] / 32768d;
                _squares += value * value;
                _peak = Math.Max(_peak, Math.Abs(value));
                _samples++;
            }
            if (++_frames >= blockFrames) FinishBlock();
        }
    }
    private void FinishBlock()
    {
        if (_samples == 0) return;
        double db = 10 * Math.Log10(Math.Max(1e-12, _squares / _samples));
        if (db > -60)
            _histogram[Math.Clamp((int)Math.Round((db + 120) * 4), 0, 480)] += _frames;
        _squares = 0; _samples = 0; _frames = 0;
    }
    public AudioLevelProfile Build()
    {
        FinishBlock();
        long count = _histogram.Sum(), target = (long)Math.Ceiling(count * .95);
        double loud = -120;
        if (count > 0)
        {
            long cumulative = 0;
            for (int bin = 0; bin < _histogram.Length; bin++)
                if ((cumulative += _histogram[bin]) >= target) { loud = bin / 4d - 120; break; }
        }
        double peak = Math.Max(-120, 20 * Math.Log10(Math.Max(1e-6, _peak)));
        return new(Math.Min(loud, peak), peak);
    }
}