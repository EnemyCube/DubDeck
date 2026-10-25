using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace DubDeck.App.Media;

/// <summary>Silent PCM tap in a separate decoder; the real player's audio output is untouched.</summary>
internal sealed class AudioActivityMeter : IDisposable
{
    private readonly MediaPlayer _player;
    private readonly LibVLCSharp.Shared.Media _media;
    private readonly MediaPlayer.LibVLCAudioPlayCb _play;
    private readonly MediaPlayer.LibVLCAudioPauseCb _pause;
    private readonly MediaPlayer.LibVLCAudioResumeCb _resume;
    private readonly MediaPlayer.LibVLCAudioFlushCb _flush;
    private readonly MediaPlayer.LibVLCAudioDrainCb _drain;
    private readonly ConcurrentQueue<Level> _levels = new();
    private readonly long _clockOffsetUs;
    private volatile bool _stopping;
    private short[] _samples = new short[4096];
    public long TimeMs => _player.Time;
    public bool IsReady => _player.CanPause && _player.IsSeekable && SamplesReceived > 0;
    private bool? _paused;
    public long SamplesReceived => Interlocked.Read(ref _samplesReceived);
    private long _samplesReceived;
    public AudioActivityMeter(LibVLC engine, string path)
    {
        _clockOffsetUs = engine.Clock - MonotonicUs();
        _player = new MediaPlayer(engine);
        _play = OnSamples;
        _pause = (_, _) => Clear();
        _resume = (_, _) => Clear();
        _flush = (_, _) => Clear();
        _drain = _ => { };
        // VLC 3 amem always emits native signed 16-bit PCM, despite FL32 in API docs.
        _player.SetAudioFormat("S16N", 16000, 1);
        _player.SetAudioCallbacks(_play, _pause, _resume, _flush, _drain);
        _media = new LibVLCSharp.Shared.Media(engine, path, FromType.FromPath);
        _media.AddOption(":no-video"); _media.AddOption(":no-spu"); _media.AddOption(":file-caching=100");
        _player.Play(_media);
    }
    private static long MonotonicUs() => (long)(Stopwatch.GetTimestamp() * (1000000d / Stopwatch.Frequency));
    private long NowUs => _clockOffsetUs + MonotonicUs();
    private void OnSamples(IntPtr opaque, IntPtr pcm, uint count, long pts)
    {
        if (_stopping || count == 0) return;
        // Bound decoder lead without calling LibVLC from a native callback.
        while (!_stopping && pts > NowUs + 200000) Thread.Sleep(5);
        if (_stopping) return;
        int length = checked((int)count);
        if (_samples.Length < length) _samples = new short[length];
        Marshal.Copy(pcm, _samples, 0, length);
        double squares = 0;
        for (int i = 0; i < length; i++) squares += (_samples[i] / 32768d) * (_samples[i] / 32768d);
        double db = 10 * Math.Log10(Math.Max(1e-12, squares / length));
        _levels.Enqueue(new(pts, pts + length * 1000000L / 16000, db));
        while (_levels.Count > 256) _levels.TryDequeue(out _);
        Interlocked.Add(ref _samplesReceived, length);
    }
    public double LevelDb
    {
        get
        {
            long now = NowUs;
            while (_levels.TryPeek(out var old) && old.EndUs < now - 120000) _levels.TryDequeue(out _);
            double peak = -120;
            foreach (var sample in _levels)
                if (sample.StartUs <= now + 50000 && sample.EndUs >= now - 120000) peak = Math.Max(peak, sample.Db);
            return peak;
        }
    }
    public void Seek(long positionMs) { Clear(); if (_player.IsSeekable) _player.Time = positionMs; }
    public void SetPaused(bool paused) { if (_paused == paused) return; if (_player.CanPause) { _player.SetPause(paused); _paused = paused; Clear(); } }
    public void Clear() { while (_levels.TryDequeue(out _)) { } }
    public void Dispose()
    {
        _stopping = true; _player.Stop(); _player.Dispose(); _media.Dispose();
    }
    private sealed record Level(long StartUs, long EndUs, double Db);
}
