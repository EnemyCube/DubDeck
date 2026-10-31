using System;
namespace DubDeck.Core;

/// <summary>Voice activity hold and smooth attenuation, independent of playback timestamps.</summary>
public sealed class DuckingEnvelope
{
    private double _hold;
    public double Gain { get; private set; } = 1;
    public double Update(bool enabled, bool voiceActive, int amount, double elapsedSeconds)
    {
        var dt = Math.Clamp(elapsedSeconds, 0, .25);
        if (enabled && voiceActive) _hold = .3;
        else _hold = Math.Max(0, _hold - dt);
        double target = enabled && (voiceActive || _hold > 0) ? 1 - Math.Clamp(amount, 0, 100) / 100d : 1;
        double duration = target < Gain ? .08 : .45;
        Gain += Math.Clamp(target - Gain, -dt / duration, dt / duration);
        return Gain;
    }
    /// <summary>Preserve quiet target audio, gradually applying the duck gain to louder passages.</summary>
    public double GainAtLevel(double sourceLevelDb, int trackVolume)
    {
        if (trackVolume <= 0 || double.IsNaN(sourceLevelDb)) return 1;
        // Measure before ducking to avoid gain feedback. Master volume does not
        // change which passages are quiet relative to the track's own mix level.
        // VLC's DirectSound fader is cubic, hence 60 rather than 20 log10.
        double level = sourceLevelDb + 60 * Math.Log10(Math.Clamp(trackVolume, 0, 100) / 100d);
        const double quietLevelDb = -30, fullDuckLevelDb = -24;
        double strength = Math.Clamp((level - quietLevelDb) / (fullDuckLevelDb - quietLevelDb), 0, 1);
        return 1 - (1 - Gain) * strength;
    }
    public void Reset() { _hold = 0; Gain = 1; }
}
