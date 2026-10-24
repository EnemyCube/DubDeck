using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Channels;
using LibVLCSharp.Shared;
using VideoSync.Core;

namespace VideoSync.App.Media;

public sealed record PlaybackStatus(long PositionMs, long DurationMs, bool IsPlaying, bool IsBusy, string? Error);

/// <summary>A sampled native clock for integration checks; timestamps from VLC are coarse.</summary>
public sealed record PlaybackDiagnostic(string ClipId, string TrackId, bool IsVideo,
    long TimelinePositionMs, long ExpectedMediaTimeMs, long NativeMediaTimeMs,
    long EstimatedMediaTimeMs, int Volume, bool IsMuted, string State,
    int DecodedAudio, int DecodedVideo, int PlayedAudioBuffers, int DisplayedPictures);

/// <summary>
/// Serializes all transport and native player operations. The stopwatch advances only when all
/// active inputs are ready; VLC callbacks only update samples and enqueue coordinator work.
/// </summary>
public sealed class PlaybackController : IDisposable
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
    private int _disposed;
    private IReadOnlyList<PlaybackDiagnostic> _diagnostics = Array.Empty<PlaybackDiagnostic>();

    public MediaPlayer VideoPlayer => _video.Player;
    public long PositionMs => Interlocked.Read(ref _publishedPosition);
    public bool IsPlaying => Volatile.Read(ref _playing);
    public IReadOnlyList<PlaybackDiagnostic> Diagnostics => Volatile.Read(ref _diagnostics);
    public event Action<PlaybackStatus>? StatusChanged;
    public event Action<bool>? VideoVisibilityChanged;

    public PlaybackController(MediaEngine engine)
    {
        _engine = engine;
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

    public Task SetProjectAsync(ProjectDocument project)
    {
        var snapshot = ProjectSnapshot.From(project);
        return Enqueue(async token =>
        {
            Freeze();
            _playing = false;
            _project = snapshot;
            _basePosition = Math.Clamp(_basePosition, 0, _project.Duration);
            _error = null;
            await ReconcileAsync(true, token).ConfigureAwait(false);
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
        if (_basePosition >= _project.Duration) _basePosition = 0;
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
        _basePosition = 0;
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
            _basePosition = Math.Clamp(positionMs, 0, _project.Duration);
            _error = null;
            if (_basePosition == _project.Duration) _playing = false;
            await ReconcileAsync(true, token).ConfigureAwait(false);
        }, true);
    }

    public Task UpdateVolumeAsync(ProjectDocument project)
    {
        var masterVolume = Math.Clamp(project.MasterVolume, 0, 100);
        var masterMuted = project.MasterMuted;
        var volumeTracks = project.Tracks.ToDictionary(track => track.Id,
            track => (Volume: Math.Clamp(track.Volume, 0, 100), Muted: track.IsMuted));
        return Enqueue(_ =>
        {
            var tracks = _project.Tracks.ToDictionary(pair => pair.Key, pair =>
                volumeTracks.TryGetValue(pair.Key, out var settings)
                    ? pair.Value with { Volume = settings.Volume, Muted = settings.Muted } : pair.Value);
            _project = _project with { MasterVolume = masterVolume, MasterMuted = masterMuted, Tracks = tracks };
            ApplyVolumes();
            return Task.CompletedTask;
        });
    }

    private Task Enqueue(Func<CancellationToken, Task> execute, bool supersede = false)
    {
        if (Volatile.Read(ref _disposed) != 0) return Task.FromException(new ObjectDisposedException(nameof(PlaybackController)));
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
                    Freeze();
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
                    Publish();
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
            _video.Dispose();
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
        if (position >= _project.Duration)
        {
            Freeze();
            _playing = false;
            _basePosition = _project.Duration;
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
    }

    private async Task ReconcileAsync(bool reposition, CancellationToken token)
    {
        Freeze();
        _busy = true;
        Publish();
        token.ThrowIfCancellationRequested();
        var desired = ActiveAt(_basePosition);
        var video = desired.FirstOrDefault(item => item.Track.Kind == TrackKind.Video);
        if (!Matches(_video, video))
        {
            SetVideoVisible(false);
            _video.Unload();
            if (video is not null) await OpenAsync(_video, video, token).ConfigureAwait(false);
        }
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
            if (!Matches(slot, item))
            {
                slot.Unload();
                await OpenAsync(slot, item, token).ConfigureAwait(false);
            }
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
            foreach (var slot in Slots().Where(slot => slot.Clip is not null))
            {
                slot.Player.SetPause(false);
                slot.Running = true;
                slot.ResetSample(slot.Player.Time);
            }
            _clock.Restart();
        }
    }

    private async Task OpenAsync(PlayerSlot slot, ActiveItem item, CancellationToken token)
    {
        if (!System.IO.File.Exists(item.Asset.FilePath))
            throw new System.IO.FileNotFoundException($"{item.Asset.Name} is missing. Relink it in the media library.", item.Asset.FilePath);
        slot.Clip = item.Clip;
        slot.Asset = item.Asset;
        slot.TrackId = item.Track.Id;
        slot.Media = new LibVLCSharp.Shared.Media(_engine.LibVLC, item.Asset.FilePath, FromType.FromPath);
        slot.Media.AddOption(":file-caching=200");
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
            var statistics = slot.Media!.Statistics;
            return slot.Player.CanPause &&
                (slot.Player.State is VLCState.Playing or VLCState.Paused) &&
                (!slot.IsVideo || (slot.Player.VoutCount > 0 && statistics.DisplayedPictures > 0)) &&
                (!item.Asset.HasAudio || (statistics.DecodedAudio > 0 && statistics.PlayedAudioBuffers > 0));
        }, slot, TimeSpan.FromSeconds(12), token,
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
            var muted = _project.MasterMuted || track.Muted || !_playing || !slot.Ready;
            slot.Player.Volume = (int)Math.Round(_project.MasterVolume * track.Volume / 100.0, MidpointRounding.AwayFromZero);
            slot.Player.Mute = muted;
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
        }
    }

    private long CurrentPosition() => Math.Clamp(_basePosition + _clock.ElapsedMilliseconds, 0, _project.Duration);
    private static long ExpectedTime(PlayerSlot slot, long position) => Math.Clamp(position - slot.Clip!.StartMs, 0, Math.Max(0, slot.Asset!.DurationMs - 1));
    private IEnumerable<PlayerSlot> Slots()
    {
        yield return _video;
        foreach (var slot in _audio.Values) yield return slot;
    }

    private List<ActiveItem> ActiveAt(long position) => _project.Clips
        .Where(clip => clip.StartMs <= position && _project.Assets.TryGetValue(clip.AssetId, out var asset) &&
            !asset.Offline && position - clip.StartMs < asset.DurationMs && _project.Tracks.ContainsKey(clip.TrackId))
        .Select(clip => new ActiveItem(clip, _project.Assets[clip.AssetId], _project.Tracks[clip.TrackId])).ToList();

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

    private void Publish()
    {
        var position = CurrentPosition();
        Interlocked.Exchange(ref _publishedPosition, position);
        var now = Stopwatch.GetTimestamp();
        var diagnostics = Slots().Where(slot => slot.Clip is not null).Select(slot =>
        {
            var statistics = slot.Media?.Statistics ?? default;
            return new PlaybackDiagnostic(slot.Clip!.Id, slot.TrackId, slot.IsVideo, position,
                ExpectedTime(slot, position), slot.Player.Time, slot.EstimatedTime(now),
                slot.Player.Volume, slot.Player.Mute, slot.Player.State.ToString(), statistics.DecodedAudio,
                statistics.DecodedVideo, statistics.PlayedAudioBuffers, statistics.DisplayedPictures);
        }).ToArray();
        Volatile.Write(ref _diagnostics, new ReadOnlyCollection<PlaybackDiagnostic>(diagnostics));
        var status = new PlaybackStatus(position, _project.Duration, _playing, _busy, _error);
        if (StatusChanged is { } handlers)
            foreach (Action<PlaybackStatus> handler in handlers.GetInvocationList())
                try { handler(status); } catch { /* A UI callback must not stop the transport worker. */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _commands.Writer.TryComplete();
        _worker.GetAwaiter().GetResult();
        _ticker.GetAwaiter().GetResult();
        _lifetime.Dispose();
    }

    private sealed record Work(Func<CancellationToken, Task> Execute, TaskCompletionSource? Completion, bool IsTick);
    private sealed record AssetData(string Id, string Name, string FilePath, long DurationMs, bool HasAudio, bool Offline);
    private sealed record TrackData(string Id, TrackKind Kind, int Volume, bool Muted);
    private sealed record ClipData(string Id, string AssetId, string TrackId, long StartMs);
    private sealed record ActiveItem(ClipData Clip, AssetData Asset, TrackData Track);
    private sealed record ProjectSnapshot(Dictionary<string, AssetData> Assets, Dictionary<string, TrackData> Tracks,
        ClipData[] Clips, int MasterVolume, bool MasterMuted, long Duration)
    {
        public static ProjectSnapshot Empty => new(new(), new(), [], 100, false, 0);
        public static ProjectSnapshot From(ProjectDocument project)
        {
            var assets = project.Assets.ToDictionary(asset => asset.Id, asset => new AssetData(asset.Id, asset.Name,
                asset.FilePath, asset.DurationMs, asset.HasAudio, asset.IsOffline));
            var tracks = project.Tracks.ToDictionary(track => track.Id, track => new TrackData(track.Id, track.Kind,
                Math.Clamp(track.Volume, 0, 100), track.IsMuted));
            var clips = project.Clips.Select(clip => new ClipData(clip.Id, clip.AssetId, clip.TrackId, clip.StartMs)).ToArray();
            var duration = clips.Where(clip => assets.ContainsKey(clip.AssetId))
                .Select(clip => clip.StartMs + assets[clip.AssetId].DurationMs).DefaultIfEmpty(0).Max();
            return new ProjectSnapshot(assets, tracks, clips, Math.Clamp(project.MasterVolume, 0, 100), project.MasterMuted, duration);
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








