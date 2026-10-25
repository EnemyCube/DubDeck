using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Channels;
using LibVLCSharp.Shared;
using DubDeck.Core;

namespace DubDeck.App.Media;

public sealed record PlaybackStatus(long PositionMs, long DurationMs, bool IsPlaying, bool IsBusy, string? Error);

/// <summary>A sampled native clock for integration checks; timestamps from VLC are coarse.</summary>
public sealed record PlaybackDiagnostic(string ClipId, string TrackId, bool IsVideo,
    long TimelinePositionMs, long ExpectedMediaTimeMs, long NativeMediaTimeMs,
    long EstimatedMediaTimeMs, int Volume, bool IsMuted, string State,
    int DecodedAudio, int DecodedVideo, int PlayedAudioBuffers, int DisplayedPictures, double? ActivityDb = null, long? MeterTimeMs = null);

/// <summary>
/// Serializes all transport and native player operations. The stopwatch advances only when all
/// active inputs are ready; VLC callbacks only update samples and enqueue coordinator work.
/// </summary>
public sealed partial class PlaybackController : IDisposable
{
    private readonly MediaEngine _engine;
    private readonly Channel<Work> _commands = Channel.CreateUnbounded<Work>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _operationLock = new();
    private CancellationTokenSource? _activeOperation;
    private readonly Stopwatch _clock = new();
    private readonly PlayerSlot _video;
    private readonly Dictionary<string, PlayerSlot> _audio = new();
    private readonly Task _worker;
    private readonly Task _ticker;
    private ProjectSnapshot _project = ProjectSnapshot.Empty;
    private long _basePosition;
    private long _publishedPosition;
    private long _seekVersion;
    private bool _playing;
    private bool _busy;
    private string? _error;
    private bool _videoVisible;
    private int _tickPending;
    private int _disposed, _stopping;
    private int _sourceOpenCount;
    public int SourceOpenCount => Volatile.Read(ref _sourceOpenCount);
    private readonly bool _enableDiagnostics;
    private readonly bool _simulateVideoStartupStall;
    private bool _softwareVideoFallback;
    public bool UsedSoftwareVideoFallback => _softwareVideoFallback;
    private long _lastPublishStamp;
    private IReadOnlyList<PlaybackDiagnostic> _diagnostics = Array.Empty<PlaybackDiagnostic>();

    public MediaPlayer VideoPlayer => _video.Player;
    public long PositionMs => Interlocked.Read(ref _publishedPosition);
    public bool IsPlaying => Volatile.Read(ref _playing);
    public IReadOnlyList<PlaybackDiagnostic> Diagnostics => Volatile.Read(ref _diagnostics);
    public event Action<PlaybackStatus>? StatusChanged;
    public event Action<bool>? VideoVisibilityChanged;

    public PlaybackController(MediaEngine engine, bool enableDiagnostics = false, bool simulateVideoStartupStall = false)
    {
        _engine = engine;
        _enableDiagnostics = enableDiagnostics;
        _simulateVideoStartupStall = simulateVideoStartupStall;
        _video = new PlayerSlot(this, new MediaPlayer(engine.LibVLC), true, string.Empty);
        VideoPlayer.EnableKeyInput = false;
        VideoPlayer.EnableMouseInput = false;
        VideoPlayer.Scale = 0;
        _worker = Task.Run(ProcessCommandsAsync);
        _ticker = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(40));
                while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
                    if (Volatile.Read(ref _playing)) QueueTick();
            }
            catch (OperationCanceledException) { }
        });
    }

    public Task SetProjectAsync(ProjectDocument project, IReadOnlySet<string>? offlineAssetIds = null, long? startPositionMs = null)
    {
        var snapshot = ProjectSnapshot.From(project, offlineAssetIds);
        return Enqueue(async token =>
        {
            Freeze();
            _playing = false;
            if (!ReferenceEquals(_document, project)) { ResetTrackPauses(); _ducking.Reset(); _document = project; }
            _trackDelays.Clear(); _committedShifts.Clear(); _alignmentGeneration++;
            foreach (var id in _pausedTracks.Keys.ToArray()) _pausedTracks[id] = _basePosition;
            _project = offlineAssetIds is not null ? snapshot : snapshot with { Assets = snapshot.Assets.ToDictionary(p => p.Key, p => p.Value with { Offline = !System.IO.File.Exists(p.Value.FilePath) }) };
            _trackEnds = _project.Clips.GroupBy(c => c.TrackId).ToDictionary(g => g.Key,
                g => g.Max(c => c.StartMs + _project.Assets[c.AssetId].DurationMs));
            if (startPositionMs.HasValue) _basePosition = startPositionMs.Value;
            _basePosition = Math.Clamp(_basePosition, 0, TransportDuration(_basePosition));
            _error = null;
            var desired = ActiveAt(_basePosition);
            bool reposition = !Matches(_video, desired.FirstOrDefault(item => item.Track.Kind == TrackKind.Video)) ||
                desired.Count(item => item.Track.Kind == TrackKind.Audio) != _audio.Count ||
                desired.Where(item => item.Track.Kind == TrackKind.Audio).Any(item => !_audio.TryGetValue(item.Track.Id, out var slot) || !Matches(slot, item));
            await ReconcileAsync(reposition, token).ConfigureAwait(false);
        }, true);
    }

    public Task PlayPauseAsync() => Enqueue(async token =>
    {
        _error = null;
        if (_playing)
        {
            Freeze();
            _playing = false;
            return;
        }
        if (_project.Duration == 0) return;
        if (_basePosition >= TransportDuration(_basePosition)) { CommitTrackPositions(_basePosition); ResetTrackPauses(); _basePosition = 0; }
        _playing = true;
        await ReconcileAsync(true, token).ConfigureAwait(false);
    }, true);

    public Task PauseAsync() => Enqueue(_ =>
    {
        Freeze();
        _playing = false;
        return Task.CompletedTask;
    }, true);

    public Task StopAsync() => Enqueue(async token =>
    {
        Freeze();
        _playing = false;
        CommitTrackPositions(_basePosition);
        _basePosition = 0;
        ResetTrackPauses(); _ducking.Reset();
        _error = null;
        await ReconcileAsync(true, token).ConfigureAwait(false);
    }, true);

    public Task SeekAsync(long positionMs)
    {
        var version = Interlocked.Increment(ref _seekVersion);
        return Enqueue(async token =>
        {
            if (version != Interlocked.Read(ref _seekVersion)) return;
            Freeze();
            CommitTrackPositions(_basePosition);
            _basePosition = Math.Clamp(positionMs, 0, _project.Duration);
            foreach (var id in _pausedTracks.Keys.ToArray()) _pausedTracks[id] = _basePosition;
            _error = null;
            if (_basePosition == _project.Duration) _playing = false;
            await ReconcileAsync(true, token).ConfigureAwait(false);
        }, true);
    }

    public Task UpdateVolumeAsync(ProjectDocument project)
    {
        var masterVolume = Math.Clamp(project.MasterVolume, 0, 100);
        var masterMuted = project.MasterMuted;
        var ducking = project.AutoDuckingEnabled; var duckAmount = project.DuckAmount;
        var volumeTracks = project.Tracks.ToDictionary(track => track.Id,
            track => (Volume: Math.Clamp(track.Volume, 0, 100), Muted: track.IsMuted, Voice: track.IsDuckingSource));
        return Enqueue(_ =>
        {
            var tracks = _project.Tracks.ToDictionary(pair => pair.Key, pair =>
                volumeTracks.TryGetValue(pair.Key, out var settings)
                    ? pair.Value with { Volume = settings.Volume, Muted = settings.Muted, Voice = settings.Voice } : pair.Value);
            _project = _project with { MasterVolume = masterVolume, MasterMuted = masterMuted, Tracks = tracks, DuckingEnabled = ducking, DuckAmount = duckAmount };
            UpdateDucking();
            return Task.CompletedTask;
        });
    }

    private Task Enqueue(Func<CancellationToken, Task> execute, bool supersede = false)
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _stopping) != 0) return Task.FromException(new ObjectDisposedException(nameof(PlaybackController)));
        if (supersede)
            lock (_operationLock) _activeOperation?.Cancel();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_commands.Writer.TryWrite(new Work(execute, completion, false)))
            completion.TrySetException(new ObjectDisposedException(nameof(PlaybackController)));
        return completion.Task;
    }

    // Invoked by native callbacks. No native call may be made on their event threads.
    private void QueueTick()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _tickPending, 1) != 0) return;
        if (!_commands.Writer.TryWrite(new Work(TickAsync, null, true))) Interlocked.Exchange(ref _tickPending, 0);
    }

    private async Task ProcessCommandsAsync()
    {
        try
        {
            await foreach (var work in _commands.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (work.IsTick) Interlocked.Exchange(ref _tickPending, 0);
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                lock (_operationLock) _activeOperation = operation;
                Exception? failure = null;
                try
                {
                    await work.Execute(operation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (operation.IsCancellationRequested)
                {
                    if (!_lifetime.IsCancellationRequested) Freeze();
                    // A newer transport request owns the next position/play state.
                }
                catch (Exception exception)
                {
                    Freeze();
                    _playing = false;
                    _error = exception.Message;
                    failure = exception;
                }
                finally
                {
                    lock (_operationLock) if (ReferenceEquals(_activeOperation, operation)) _activeOperation = null;
                    _busy = false;
                    if (!_lifetime.IsCancellationRequested) Publish(work.IsTick);
                    if (failure is null) work.Completion?.TrySetResult();
                    else work.Completion?.TrySetException(failure);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally
        {
            _playing = false;
            _clock.Stop();
            foreach (var slot in _audio.Values) slot.Dispose();
            _audio.Clear();
            _video.Unload(); // Keep its HWND binding valid until the UI detaches it.
            while (_commands.Reader.TryRead(out var pending))
                pending.Completion?.TrySetException(new ObjectDisposedException(nameof(PlaybackController)));
        }
    }

    private async Task TickAsync(CancellationToken token)
    {
        foreach (var slot in Slots())
            if (Interlocked.Exchange(ref slot.Failed, 0) != 0)
                throw new InvalidOperationException($"Playback failed for {slot.Asset?.Name ?? "a media file"}. Check that the file is still available and playable.");
        if (!_playing) return;
        var position = CurrentPosition();
        if (position >= TransportDuration(position))
        {
            Freeze();
            _playing = false;
            _basePosition = TransportDuration(position);
            await ReconcileAsync(false, token).ConfigureAwait(false);
            return;
        }
        var desired = ActiveAt(position);
        var needsChange = !Matches(_video, desired.FirstOrDefault(item => item.Track.Kind == TrackKind.Video)) ||
            desired.Count(item => item.Track.Kind == TrackKind.Audio) != _audio.Count ||
            desired.Where(item => item.Track.Kind == TrackKind.Audio).Any(item => !_audio.TryGetValue(item.Track.Id, out var slot) || !Matches(slot, item));
        if (needsChange)
        {
            await ReconcileAsync(true, token).ConfigureAwait(false);
            return;
        }
        var waiting = Slots().Where(NeedsBufferWait).ToArray();
        if (waiting.Length != 0)
        {
            Freeze();
            _busy = true;
            Publish();
            foreach (var slot in waiting)
                await WaitUntilAsync(() => Volatile.Read(ref slot.Buffering) == 0,
                    slot, TimeSpan.FromSeconds(8), token, "Media buffering timed out.").ConfigureAwait(false);
            // Buffering recovery must never seek again: seeking emits more cache events.
            await ReconcileAsync(false, token).ConfigureAwait(false);
            return;
        }
        CorrectDrift(position);
        UpdateDucking();
    }

    private async Task ReconcileAsync(bool reposition, CancellationToken token)
    {
        Freeze();
        _busy = true;
        Publish();
        token.ThrowIfCancellationRequested();
        var desired = ActiveAt(_basePosition);
        var video = desired.FirstOrDefault(item => item.Track.Kind == TrackKind.Video);
        bool subtitlePreferencesChanged = video is not null && MatchesSource(_video, video) &&
            (_video.Asset!.Subtitles != video.Asset.Subtitles || _video.Asset.SubtitleTrackIndex != video.Asset.SubtitleTrackIndex ||
             _video.Asset.SubtitleFilePath != video.Asset.SubtitleFilePath);
        if (!MatchesSource(_video, video))
        {
            SetVideoVisible(false);
            _video.Unload();
            if (video is not null) await OpenAsync(_video, video, token).ConfigureAwait(false);
        }
        if (video is not null) { _video.Clip = video.Clip; _video.Asset = video.Asset; }
        if (subtitlePreferencesChanged) await ConfigureSubtitlesAsync(video!.Asset, token, newInput: false).ConfigureAwait(false);
        var audioItems = desired.Where(item => item.Track.Kind == TrackKind.Audio).ToDictionary(item => item.Track.Id);
        foreach (var trackId in _audio.Keys.Where(id => !audioItems.ContainsKey(id)).ToArray())
        {
            _audio[trackId].Dispose();
            _audio.Remove(trackId);
        }
        foreach (var item in audioItems.Values)
        {
            if (!_audio.TryGetValue(item.Track.Id, out var slot))
            {
                slot = new PlayerSlot(this, new MediaPlayer(_engine.LibVLC), false, item.Track.Id);
                _audio.Add(item.Track.Id, slot);
            }
            if (!MatchesSource(slot, item))
            {
                slot.Unload();
                await OpenAsync(slot, item, token).ConfigureAwait(false);
            }
            slot.Clip = item.Clip; slot.Asset = item.Asset;
        }
        foreach (var slot in Slots().Where(slot => slot.Clip is not null))
        {
            token.ThrowIfCancellationRequested();
            await WaitUntilAsync(() => slot.Player.State != VLCState.Buffering,
                slot, TimeSpan.FromSeconds(8), token, "Media buffering timed out.").ConfigureAwait(false);
            if (reposition)
                await SeekSlotAsync(slot, ExpectedTime(slot, _basePosition), token).ConfigureAwait(false);
        }
        ApplyVolumes();
        SetVideoVisible(_video.Clip is not null);
        _busy = false;
        if (_playing)
        {
            foreach (var slot in Slots().Where(slot => slot.Clip is not null && !_pausedTracks.ContainsKey(slot.TrackId)))
            {
                slot.Player.SetPause(false);
                slot.Running = true;
                slot.ResetSample(slot.Player.Time);
            }
            _clock.Restart();
            SyncMeters();
        }
    }

    private async Task OpenAsync(PlayerSlot slot, ActiveItem item, CancellationToken token)
    {
        try { await OpenSourceAsync(slot, item, token).ConfigureAwait(false); }
        catch (TimeoutException) when (slot.IsVideo && !_softwareVideoFallback && !token.IsCancellationRequested)
        {
            // Some drivers initialize one hardware frame and then stall both video
            // and embedded audio. Retry once using the CPU, keeping the UI alive.
            slot.Unload();
            _softwareVideoFallback = true;
            slot.Player.EnableHardwareDecoding = false;
            await OpenSourceAsync(slot, item, token).ConfigureAwait(false);
        }
        if (slot.IsVideo) await ConfigureSubtitlesAsync(item.Asset, token).ConfigureAwait(false);
    }
    private async Task OpenSourceAsync(PlayerSlot slot, ActiveItem item, CancellationToken token)
    {
        if (!System.IO.File.Exists(item.Asset.FilePath))
            throw new System.IO.FileNotFoundException($"{item.Asset.Name} is missing. Relink it in the media library.", item.Asset.FilePath);
        Interlocked.Increment(ref _sourceOpenCount);
        slot.Clip = item.Clip;
        slot.Asset = item.Asset;
        slot.TrackId = item.Track.Id;
        slot.Media = new LibVLCSharp.Shared.Media(_engine.LibVLC, item.Asset.FilePath, FromType.FromPath);
        slot.Media.AddOption(":file-caching=200");
        if (slot.IsVideo && _softwareVideoFallback)
        {
            slot.Media.AddOption(":avcodec-hw=none");
            slot.Media.AddOption(":avcodec-threads=" + Math.Clamp(Environment.ProcessorCount / 2, 2, 8));
        }
        slot.Media.AddOption(":play-and-pause");
        slot.Media.AddOption(":no-sub-autodetect-file");
        if (!slot.IsVideo) slot.Media.AddOption(":no-video");
        slot.Player.Volume = 0;
        slot.Player.Mute = true;
        Interlocked.Exchange(ref slot.Failed, 0);
        Interlocked.Exchange(ref slot.Buffering, 0);
        slot.Ready = false;
        // D3D output can wait for a visible HWND. Reveal the video surface before
        // opening, with audio muted until every active player is prepared.
        if (slot.IsVideo) SetVideoVisible(true);
        if (!slot.Player.Play(slot.Media))
            throw new InvalidOperationException($"VLC could not open {item.Asset.Name}.");
        await WaitUntilAsync(() =>
        {
            if (slot.IsVideo && _simulateVideoStartupStall && !_softwareVideoFallback) return false;
            var statistics = slot.Media!.Statistics;
            return slot.Player.CanPause &&
                (slot.Player.State is VLCState.Playing or VLCState.Paused) &&
                (!slot.IsVideo || (slot.Player.VoutCount > 0 && statistics.DisplayedPictures > 0)) &&
                (!item.Asset.HasAudio || (statistics.DecodedAudio > 0 && statistics.PlayedAudioBuffers > 0));
        }, slot, slot.IsVideo && !_softwareVideoFallback ? TimeSpan.FromMilliseconds(_simulateVideoStartupStall ? 100 : 4000) : TimeSpan.FromSeconds(12), token,
            $"Opening {item.Asset.Name} timed out before media output was ready.").ConfigureAwait(false);
        var pauseRetry = Stopwatch.StartNew();
        await WaitUntilAsync(() =>
        {
            if (slot.Player.State == VLCState.Paused) return true;
            if (slot.Player.CanPause && pauseRetry.ElapsedMilliseconds >= 80)
            {
                slot.Player.SetPause(true);
                pauseRetry.Restart();
            }
            return false;
        }, slot, TimeSpan.FromSeconds(6), token,
            $"Could not pause {item.Asset.Name} for synchronized playback.").ConfigureAwait(false);
        slot.Running = false;
        slot.Ready = true;
        slot.ResetSample(slot.Player.Time);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, PlayerSlot slot, TimeSpan timeout,
        CancellationToken token, string timeoutMessage)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref slot.Failed) != 0 || slot.Player.State == VLCState.Error)
                throw new InvalidOperationException($"VLC could not play {slot.Asset?.Name ?? "this media"}. The file may be damaged or unavailable.");
            if (condition()) return;
            if (Stopwatch.GetTimestamp() >= deadline)
                throw new TimeoutException(timeoutMessage, new InvalidOperationException(
                    $"VLC state: {slot.Player.State}, time: {slot.Player.Time} ms, pause supported: {slot.Player.CanPause}, seek supported: {slot.Player.IsSeekable}, decoded video: {slot.Media?.Statistics.DecodedVideo ?? 0}, decoded audio: {slot.Media?.Statistics.DecodedAudio ?? 0}"));
            await Task.Delay(20, token).ConfigureAwait(false);
        }
    }

    private async Task SeekSlotAsync(PlayerSlot slot, long mediaTime, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!slot.Player.IsSeekable && mediaTime > 100)
            throw new InvalidOperationException($"{slot.Asset?.Name} does not support timeline seeking.");
        slot.Player.Time = mediaTime;
        if (slot.Meter is not null) { slot.MeterTarget = mediaTime; slot.MeterPending = true; }
        await WaitUntilAsync(() => Math.Abs(slot.Player.Time - mediaTime) <= 350,
            slot, TimeSpan.FromSeconds(3), token, $"Seeking in {slot.Asset?.Name} timed out.").ConfigureAwait(false);
        slot.ResetSample(slot.Player.Time);
        slot.LastCorrectionStamp = Stopwatch.GetTimestamp();
        slot.DriftCount = 0;
    }

    private static bool NeedsBufferWait(PlayerSlot slot)
    {
        if (slot.Clip is null || !slot.Running || Volatile.Read(ref slot.Buffering) == 0) return false;
        var now = Stopwatch.GetTimestamp();
        var since = Interlocked.Read(ref slot.BufferingSinceStamp);
        var sample = Interlocked.Read(ref slot.SampleTime);
        if (sample > Interlocked.Read(ref slot.BufferingStartTime) + 80)
        {
            // Cache events describe decoder preroll too. A progressing native clock
            // proves this event is no longer an input stall.
            Interlocked.Exchange(ref slot.Buffering, 0);
            return false;
        }
        return (now - slot.LastCorrectionStamp) / (double)Stopwatch.Frequency > 1 &&
            (now - since) / (double)Stopwatch.Frequency > .5 &&
            (now - Interlocked.Read(ref slot.SampleStamp)) / (double)Stopwatch.Frequency > .5;
    }
    private void CorrectDrift(long position)
    {
        var now = Stopwatch.GetTimestamp();
        foreach (var slot in Slots().Where(slot => slot.Clip is not null && slot.Running))
        {
            if ((now - slot.LastCorrectionStamp) / (double)Stopwatch.Frequency < 2) continue;
            var stamp = Interlocked.Read(ref slot.SampleStamp);
            if (stamp == 0 || (now - stamp) / (double)Stopwatch.Frequency > 1.5) continue;
            var expected = ExpectedTime(slot, position);
            var estimate = slot.EstimatedTime(now);
            var drift = expected - estimate;
            // Native time updates are usually about 250 ms apart. Two large samples in the
            // same direction and a cooldown avoid turning quantization into repeated seeks.
            if (Math.Abs(drift) < 350)
            {
                slot.DriftCount = 0;
                continue;
            }
            var direction = Math.Sign(drift);
            slot.DriftCount = slot.DriftDirection == direction ? slot.DriftCount + 1 : 1;
            slot.DriftDirection = direction;
            if (slot.DriftCount < 3) continue;
            slot.Player.Time = expected;
            slot.ResetSample(expected);
            slot.LastCorrectionStamp = now;
            slot.DriftCount = 0;
        }
    }

    private void ApplyVolumes()
    {
        foreach (var slot in Slots())
        {
            if (slot.Clip is null || !_project.Tracks.TryGetValue(slot.TrackId, out var track)) continue;
            var muted = _project.MasterMuted || track.Muted || _pausedTracks.ContainsKey(slot.TrackId) || !_playing || !slot.Ready;
            var gain = _project.DuckingEnabled && !track.Voice ? _ducking.Gain : 1;
            var volume = (int)Math.Round(_project.MasterVolume * track.Volume / 100.0 * gain, MidpointRounding.AwayFromZero);
            if (slot.Player.Volume != volume) slot.Player.Volume = volume;
            if (slot.Player.Mute != muted) slot.Player.Mute = muted;
        }
    }

    private void Freeze()
    {
        if (_clock.IsRunning)
        {
            _basePosition = CurrentPosition();
            _clock.Reset();
        }
        foreach (var slot in Slots())
        {
            if (slot.Clip is null) continue;
            // During canceled opening the player's pause request may have arrived too early.
            // Muting also covers that interval until the next request finishes preparation.
            slot.Player.Mute = true;
            // VLC 3 stops the input if pause is requested before can-pause is set.
            if (slot.Player.CanPause) slot.Player.SetPause(true);
            slot.Running = false;
            if (slot.MeterPending) { slot.Meter?.Dispose(); slot.Meter = null; }
            else slot.Meter?.SetPaused(true);
        }
    }

    private long CurrentPosition() { long position = _basePosition + _clock.ElapsedMilliseconds; return Math.Clamp(position, 0, TransportDuration(position)); }
    private long ExpectedTime(PlayerSlot slot, long position) => Math.Clamp(TrackPosition(slot.TrackId, position) - slot.Clip!.StartMs, 0, Math.Max(0, slot.Asset!.DurationMs - 1));
    private IEnumerable<PlayerSlot> Slots()
    {
        yield return _video;
        foreach (var slot in _audio.Values) yield return slot;
    }

    private List<ActiveItem> ActiveAt(long position) => _project.Clips
        .Where(clip => clip.StartMs <= TrackPosition(clip.TrackId, position) && _project.Assets.TryGetValue(clip.AssetId, out var asset) &&
            !asset.Offline && TrackPosition(clip.TrackId, position) - clip.StartMs < asset.DurationMs && _project.Tracks.ContainsKey(clip.TrackId))
        .Select(clip => new ActiveItem(clip, _project.Assets[clip.AssetId], _project.Tracks[clip.TrackId])).ToList();

    private static bool MatchesSource(PlayerSlot slot, ActiveItem? item) => item is null ? slot.Clip is null :
        slot.Ready && slot.Clip?.Id == item.Clip.Id && slot.Asset?.FilePath == item.Asset.FilePath &&
        slot.Asset.DurationMs == item.Asset.DurationMs && slot.Asset.HasAudio == item.Asset.HasAudio && slot.TrackId == item.Track.Id;
    private static bool Matches(PlayerSlot slot, ActiveItem? item) => item is null ? slot.Clip is null :
        slot.Ready && slot.Clip?.Id == item.Clip.Id && slot.Clip.StartMs == item.Clip.StartMs &&
        slot.Asset?.FilePath == item.Asset.FilePath && slot.TrackId == item.Track.Id;

    private void SetVideoVisible(bool visible)
    {
        if (_videoVisible == visible) return;
        _videoVisible = visible;
        if (VideoVisibilityChanged is { } handlers)
            foreach (Action<bool> handler in handlers.GetInvocationList())
                try { handler(visible); } catch { /* UI subscribers must not interrupt native cleanup. */ }
    }

    private void Publish(bool throttle = false)
    {
        long stamp = Stopwatch.GetTimestamp();
        if (throttle && Stopwatch.GetElapsedTime(_lastPublishStamp, stamp).TotalMilliseconds < 100) return;
        _lastPublishStamp = stamp;
        var position = CurrentPosition();
        Interlocked.Exchange(ref _publishedPosition, position);
        PublishAlignment(position);
        var now = Stopwatch.GetTimestamp();
        if (_enableDiagnostics)
        {
        var diagnostics = Slots().Where(slot => slot.Clip is not null).Select(slot =>
        {
            var statistics = slot.Media?.Statistics ?? default;
            return new PlaybackDiagnostic(slot.Clip!.Id, slot.TrackId, slot.IsVideo, position,
                ExpectedTime(slot, position), slot.Player.Time, slot.EstimatedTime(now),
                slot.Player.Volume, slot.Player.Mute, slot.Player.State.ToString(), statistics.DecodedAudio,
                statistics.DecodedVideo, statistics.PlayedAudioBuffers, statistics.DisplayedPictures, slot.Meter?.LevelDb, slot.Meter?.TimeMs);
        }).ToArray();
        Volatile.Write(ref _diagnostics, new ReadOnlyCollection<PlaybackDiagnostic>(diagnostics));
        }
        var status = new PlaybackStatus(position, TransportDuration(position), _playing, _busy, _error);
        if (StatusChanged is { } handlers)
            foreach (Action<PlaybackStatus> handler in handlers.GetInvocationList())
                try { handler(status); } catch { /* A UI callback must not stop the transport worker. */ }
    }

    /// <summary>Cancel queued work and stop native outputs while the UI still pumps the video HWND.</summary>
    public Task StopForShutdownAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 0)
        {
            _lifetime.Cancel();
            _commands.Writer.TryComplete();
        }
        return _worker;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        StopForShutdownAsync().GetAwaiter().GetResult();
        _ticker.GetAwaiter().GetResult();
        _video.Dispose();
        _lifetime.Dispose();
    }

    private sealed record Work(Func<CancellationToken, Task> Execute, TaskCompletionSource? Completion, bool IsTick);
    private sealed record AssetData(string Id, string Name, string FilePath, long DurationMs, bool HasAudio, bool Offline, SubtitleMode Subtitles, int SubtitleTrackIndex, string? SubtitleFilePath);
    private sealed record TrackData(string Id, TrackKind Kind, int Volume, bool Muted, bool Voice);
    private sealed record ClipData(string Id, string AssetId, string TrackId, long StartMs);
    private sealed record ActiveItem(ClipData Clip, AssetData Asset, TrackData Track);
    private sealed record ProjectSnapshot(Dictionary<string, AssetData> Assets, Dictionary<string, TrackData> Tracks,
        ClipData[] Clips, int MasterVolume, bool MasterMuted, long Duration, bool DuckingEnabled, int DuckAmount)
    {
        public static ProjectSnapshot Empty => new(new(), new(), [], 100, false, 0, false, 60);
        public static ProjectSnapshot From(ProjectDocument project, IReadOnlySet<string>? offlineAssetIds)
        {
            var assets = project.Assets.ToDictionary(asset => asset.Id, asset => new AssetData(asset.Id, asset.Name,
                asset.FilePath, asset.DurationMs, asset.HasAudio, offlineAssetIds?.Contains(asset.Id) ?? false, asset.Subtitles, asset.SubtitleTrackIndex, asset.SubtitleFilePath));
            var tracks = project.Tracks.ToDictionary(track => track.Id, track => new TrackData(track.Id, track.Kind,
                Math.Clamp(track.Volume, 0, 100), track.IsMuted, track.IsDuckingSource));
            var clips = project.Clips.Select(clip => new ClipData(clip.Id, clip.AssetId, clip.TrackId, clip.StartMs)).ToArray();
            var duration = clips.Where(clip => assets.ContainsKey(clip.AssetId))
                .Select(clip => clip.StartMs + assets[clip.AssetId].DurationMs).DefaultIfEmpty(0).Max();
            return new ProjectSnapshot(assets, tracks, clips, Math.Clamp(project.MasterVolume, 0, 100), project.MasterMuted, duration, project.AutoDuckingEnabled, project.DuckAmount);
        }
    }

    private sealed class PlayerSlot : IDisposable
    {
        private readonly PlaybackController _owner;
        public MediaPlayer Player { get; }
        public bool IsVideo { get; }
        public string TrackId;
        public ClipData? Clip;
        public AssetData? Asset;
        public LibVLCSharp.Shared.Media? Media;
        public AudioActivityMeter? Meter;
        public bool MeterPending;
        public long MeterTarget;
        public bool Ready;
        public bool Running;
        public int Failed;
        public int Buffering;
        public long BufferingSinceStamp;
        public long BufferingStartTime;
        public long SampleTime;
        public long SampleStamp;
        public long LastCorrectionStamp;
        public int DriftCount;
        public int DriftDirection;

        public PlayerSlot(PlaybackController owner, MediaPlayer player, bool isVideo, string trackId)
        {
            _owner = owner;
            Player = player;
            IsVideo = isVideo;
            TrackId = trackId;
            Player.TimeChanged += OnTimeChanged;
            Player.Buffering += OnBuffering;
            Player.EncounteredError += OnError;
            Player.EndReached += OnEnd;
        }
        private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs args) => ResetSample(args.Time);
        private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs args)
        {
            if (args.Cache >= 100)
            {
                Interlocked.Exchange(ref Buffering, 0);
            }
            else if (Interlocked.Exchange(ref Buffering, 1) == 0)
            {
                Interlocked.Exchange(ref BufferingSinceStamp, Stopwatch.GetTimestamp());
                Interlocked.Exchange(ref BufferingStartTime, Interlocked.Read(ref SampleTime));
            }
            _owner.QueueTick();
        }
        private void OnError(object? sender, EventArgs args)
        {
            Interlocked.Exchange(ref Failed, 1);
            _owner.QueueTick();
        }
        private void OnEnd(object? sender, EventArgs args) => _owner.QueueTick();
        public void ResetSample(long time)
        {
            Interlocked.Exchange(ref SampleTime, Math.Max(0, time));
            Interlocked.Exchange(ref SampleStamp, Stopwatch.GetTimestamp());
        }
        public long EstimatedTime(long now)
        {
            var sample = Interlocked.Read(ref SampleTime);
            var stamp = Interlocked.Read(ref SampleStamp);
            return sample + (Running && stamp > 0 ? (long)((now - stamp) * 1000.0 / Stopwatch.Frequency) : 0);
        }
        public void Unload()
        {
            Meter?.Dispose(); Meter = null;
            if (Media is not null || Clip is not null) Player.Stop();
            Player.Media = null;
            Media?.Dispose();
            Media = null;
            Clip = null;
            Asset = null;
            Ready = false;
            Running = false;
            Interlocked.Exchange(ref Failed, 0);
            Interlocked.Exchange(ref Buffering, 0);
            ResetSample(0);
        }
        public void Dispose()
        {
            Player.TimeChanged -= OnTimeChanged;
            Player.Buffering -= OnBuffering;
            Player.EncounteredError -= OnError;
            Player.EndReached -= OnEnd;
            Unload();
            Player.Dispose();
        }
    }
}








