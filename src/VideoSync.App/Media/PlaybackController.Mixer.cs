using System.Diagnostics;
using LibVLCSharp.Shared;
using VideoSync.Core;
namespace VideoSync.App.Media;

public sealed partial class PlaybackController
{
    private readonly Dictionary<string, long> _pausedTracks = [];
    private readonly Dictionary<string, long> _trackDelays = [];
    private readonly Dictionary<string, long> _committedShifts = [];
    private long _alignmentGeneration;
    private TrackAlignment _alignment = new(null, 0, new Dictionary<string,long>());
    public TrackAlignment Alignment => Volatile.Read(ref _alignment);
    public sealed record TrackAlignment(ProjectDocument? Document, long Generation, IReadOnlyDictionary<string,long> Shifts);

    private long LiveDelay(string id, long position) => _pausedTracks.TryGetValue(id, out var frozen)
        ? Math.Max(0, position - frozen) : _trackDelays.GetValueOrDefault(id);

    private void PublishAlignment(long position)
    {
        var shifts = _committedShifts.Keys.Concat(_trackDelays.Keys).Concat(_pausedTracks.Keys).Distinct()
            .ToDictionary(id => id, id => _committedShifts.GetValueOrDefault(id) + LiveDelay(id, position));
        Volatile.Write(ref _alignment, new TrackAlignment(_document, _alignmentGeneration, shifts));
    }
    private void CommitTrackPositions(long position)
    {
        var shifts = _project.Tracks.Keys.ToDictionary(id => id, id => LiveDelay(id, position));
        if (shifts.Values.Any(value => value != 0))
        {
            var clips = _project.Clips.Select(clip => clip with { StartMs = clip.StartMs + shifts.GetValueOrDefault(clip.TrackId) }).ToArray();
            _project = _project with { Clips = clips, Duration = clips.Select(c => c.StartMs + _project.Assets[c.AssetId].DurationMs).DefaultIfEmpty(0).Max() };
            foreach (var slot in Slots())
                if (slot.Clip is not null) slot.Clip = slot.Clip with { StartMs = slot.Clip.StartMs + shifts.GetValueOrDefault(slot.TrackId) };
            foreach (var id in _trackEnds.Keys.ToArray()) _trackEnds[id] += shifts.GetValueOrDefault(id);
            foreach (var pair in shifts) _committedShifts[pair.Key] = _committedShifts.GetValueOrDefault(pair.Key) + pair.Value;
        }
        _trackDelays.Clear();
        foreach (var id in _pausedTracks.Keys.ToArray()) _pausedTracks[id] = position;
    }
    private Dictionary<string, long> _trackEnds = [];
    private IReadOnlySet<string> _publishedPaused = new HashSet<string>();
    public IReadOnlySet<string> PausedTrackIds => Volatile.Read(ref _publishedPaused);
    private readonly DuckingEnvelope _ducking = new();
    private long _duckStamp = Stopwatch.GetTimestamp();
    private ProjectDocument? _document;
    public double DuckGain => _ducking.Gain;

    public Task SetTrackPausedAsync(string trackId, bool paused) => Enqueue(_ =>
    {
        if (!_project.Tracks.ContainsKey(trackId)) return Task.CompletedTask;
        long position = CurrentPosition();
        if (paused)
        {
            if (!_pausedTracks.TryAdd(trackId, TrackPosition(trackId, position))) return Task.CompletedTask;
            var slot = Slots().FirstOrDefault(s => s.TrackId == trackId && s.Clip is not null);
            if (slot is not null)
            {
                slot.Player.Mute = true;
                if (slot.Player.CanPause) slot.Player.SetPause(true);
                slot.Running = false;
                if (slot.MeterPending) { slot.Meter?.Dispose(); slot.Meter = null; }
                else slot.Meter?.SetPaused(true);
            }
        }
        else if (_pausedTracks.Remove(trackId, out var frozenPosition))
        {
            // Preserve this lane's local clock. Never seek or pause any other player.
            _trackDelays[trackId] = Math.Max(0, position - frozenPosition);
            var slot = Slots().FirstOrDefault(s => s.TrackId == trackId && s.Clip is not null);
            if (slot is not null && _playing)
            {
                slot.Player.SetPause(false);
                slot.Running = true;
                slot.ResetSample(slot.Player.Time);
            }
        }
        Volatile.Write(ref _publishedPaused, new HashSet<string>(_pausedTracks.Keys));
        SyncMeters(); ApplyVolumes();
        return Task.CompletedTask;
    });

    private long TrackPosition(string trackId, long position) =>
        _pausedTracks.GetValueOrDefault(trackId, Math.Max(0, position - _trackDelays.GetValueOrDefault(trackId)));

    private long TransportDuration(long position) => _trackEnds.Select(track => track.Value +
        (_pausedTracks.TryGetValue(track.Key, out var frozen) && frozen < track.Value
            ? Math.Max(0, position - frozen) : _trackDelays.GetValueOrDefault(track.Key))).DefaultIfEmpty(0).Max();
    private void ResetTrackPauses()
    {
        _pausedTracks.Clear();
        _trackDelays.Clear();
        Volatile.Write(ref _publishedPaused, new HashSet<string>());
    }
    private void SyncMeters()
    {
        foreach (var slot in Slots())
        {
            bool needed = slot.Clip is not null && slot.Asset!.HasAudio && _project.DuckingEnabled &&
                _project.Tracks.TryGetValue(slot.TrackId, out var track) && track.Voice;
            if (!needed) { slot.Meter?.Dispose(); slot.Meter = null; continue; }
            if (slot.Meter is null && (!_playing || _pausedTracks.ContainsKey(slot.TrackId))) continue;
            if (slot.Meter is null)
            {
                slot.Meter = new AudioActivityMeter(_engine.LibVLC, slot.Asset!.FilePath);
                slot.MeterTarget = ExpectedTime(slot, CurrentPosition());
                slot.MeterPending = true;
            }
            if (slot.MeterPending && slot.Meter.IsReady)
            {
                slot.Meter.Seek(ExpectedTime(slot, CurrentPosition()));
                slot.MeterPending = false;
            }
            if (!slot.MeterPending) slot.Meter.SetPaused(!_playing || _pausedTracks.ContainsKey(slot.TrackId));
        }
    }
    private void UpdateDucking()
    {
        SyncMeters();
        var now = Stopwatch.GetTimestamp();
        double dt = Stopwatch.GetElapsedTime(_duckStamp, now).TotalSeconds;
        _duckStamp = now;
        bool voice = Slots().Any(slot => slot.Running && slot.Meter is not null && !slot.MeterPending &&
            _project.Tracks.TryGetValue(slot.TrackId, out var track) && !track.Muted && track.Volume > 0 &&
            slot.Meter.LevelDb + 20 * Math.Log10(track.Volume / 100d) >= -40);
        _ducking.Update(_project.DuckingEnabled, voice, _project.DuckAmount, dt);
        ApplyVolumes();
    }
}
