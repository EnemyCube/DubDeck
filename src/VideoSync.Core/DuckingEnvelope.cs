using System;
namespace VideoSync.Core;

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
    public void Reset() { _hold = 0; Gain = 1; }
}
