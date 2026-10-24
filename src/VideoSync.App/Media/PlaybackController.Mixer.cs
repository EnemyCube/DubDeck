using System.Diagnostics;
using LibVLCSharp.Shared;
using VideoSync.Core;
namespace VideoSync.App.Media;

public sealed partial class PlaybackController
{
    private readonly Dictionary<string, long> _pausedTracks = [];
    private IReadOnlySet<string> _publishedPaused = new HashSet<string>();
    public IReadOnlySet<string> PausedTrackIds => Volatile.Read(ref _publishedPaused);
    private readonly DuckingEnvelope _ducking = new();
    private long _duckStamp = Stopwatch.GetTimestamp();
    private ProjectDocument? _document;
    public double DuckGain => _ducking.Gain;

    public Task SetTrackPausedAsync(string trackId, bool paused) => Enqueue(async token =>
    {
        if (!_project.Tracks.ContainsKey(trackId)) return;
        if (paused)
        {
            _pausedTracks.TryAdd(trackId, CurrentPosition());
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
        else if (_pausedTracks.Remove(trackId))
        {
            // Only prepare this lane. Other players and the transport clock keep running.
            var item = ActiveAt(CurrentPosition()).FirstOrDefault(i => i.Track.Id == trackId);
            var slot = trackId == _video.TrackId || _project.Tracks[trackId].Kind == TrackKind.Video
                ? _video : _audio.GetValueOrDefault(trackId);
            if (item is null)
            {
                slot?.Unload();
                if (slot == _video) SetVideoVisible(false);
                else if (slot is not null) { slot.Dispose(); _audio.Remove(trackId); }
            }
            else
            {
                if (slot is null) { slot = new PlayerSlot(this, new MediaPlayer(_engine.LibVLC), false, trackId); _audio.Add(trackId, slot); }
                if (!MatchesSource(slot, item)) { slot.Unload(); await OpenAsync(slot, item, token).ConfigureAwait(false); }
                slot.Clip = item.Clip; slot.Asset = item.Asset;
                await SeekSlotAsync(slot, ExpectedTime(slot, CurrentPosition()), token).ConfigureAwait(false);
                if (_playing) { slot.Player.SetPause(false); slot.Running = true; slot.ResetSample(slot.Player.Time); }
                if (slot == _video) SetVideoVisible(true);
            }
        }
        Volatile.Write(ref _publishedPaused, new HashSet<string>(_pausedTracks.Keys));
        SyncMeters(); ApplyVolumes();
    });

    private long TrackPosition(string trackId, long position) => _pausedTracks.GetValueOrDefault(trackId, position);
    private void ResetTrackPauses()
    {
        _pausedTracks.Clear();
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
                slot.MeterTarget = ExpectedTime(slot, TrackPosition(slot.TrackId, CurrentPosition()));
                slot.MeterPending = true;
            }
            if (slot.MeterPending && slot.Meter.IsReady)
            {
                slot.Meter.Seek(ExpectedTime(slot, TrackPosition(slot.TrackId, CurrentPosition())));
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
