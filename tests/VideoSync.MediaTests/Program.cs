using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using VideoSync.App.Media;
using VideoSync.Core;

namespace VideoSync.MediaTests;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exitCode = 1;
        application.Startup += async (_, _) =>
        {
            try
            {
                await new MediaSuite(args).RunAsync();
                exitCode = 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
            }
            finally { application.Shutdown(exitCode); }
        };
        application.Run();
        return exitCode;
    }
}

internal sealed class MediaSuite
{
    private readonly string[] _args;
    private readonly string _root;
    private readonly string _fixtures;
    private readonly string _reportDirectory;
    private readonly List<TestResult> _results = [];
    private readonly Dictionary<string, MediaAsset> _assets = [];
    private PlaybackController _controller = null!;
    private NativeVideoSink _videoSink = null!;
    private PlaybackStatus? _status;
    private bool _videoVisible;

    public MediaSuite(string[] args)
    {
        _args = args;
        _root = FindRoot();
        var fixtureOption = Array.IndexOf(args, "--fixtures");
        _fixtures = Path.GetFullPath(fixtureOption >= 0 && fixtureOption + 1 < args.Length
            ? args[fixtureOption + 1] : Path.Combine(_root, "artifacts", "media-fixtures"));
        _reportDirectory = Path.Combine(_root, "artifacts", "media-test-results");
    }

    public async Task RunAsync()
    {
        Directory.CreateDirectory(_reportDirectory);
        var runName = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(_reportDirectory, $"media-{runName}.json");
        Console.WriteLine($"Real LibVLC media integration checks. Fixtures: {_fixtures}");
        using var engine = new MediaEngine(_args.Contains("--initialize-vlc-cache", StringComparer.Ordinal));
        if (_args.Contains("--initialize-vlc-cache", StringComparer.Ordinal))
        {
            var cache = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64", "plugins", "plugins.dat");
            Require(File.Exists(cache) && new FileInfo(cache).Length > 0, "VLC did not generate the requested native plugin cache");
            Console.WriteLine($"Native plugin cache ready: {cache}");
            return;
        }
        using var videoSink = new NativeVideoSink();
        _videoSink = videoSink;
        using var controller = new PlaybackController(engine);
        _controller = controller;
        _controller.StatusChanged += status => _status = status;
        _controller.VideoVisibilityChanged += visible => _videoVisible = visible;
        _videoSink.Attach(_controller.VideoPlayer);
        try
        {
            await Check("Seven real codec/container imports", async () =>
            {
                foreach (var name in new[] { "video.mp4", "video.mov", "video.mkv", "audio.mp3", "audio.wav", "audio.m4a", "audio.flac" })
                {
                    var asset = await engine.ProbeAsync(Fixture(name), CancellationToken.None);
                    Require(asset.DurationMs > 3500 && asset.DurationMs < 8500, $"{name}: unexpected duration {asset.DurationMs} ms");
                    Require(asset.HasAudio, $"{name}: no audio stream detected");
                    Require(asset.HasVideo == name.StartsWith("video", StringComparison.Ordinal), $"{name}: incorrect video classification");
                    _assets.Add(name, asset);
                    Console.WriteLine($"  {name}: {asset.DurationMs} ms; video={asset.HasVideo}, audio={asset.HasAudio}");
                }
            });
            await Check("Corrupt and missing media rejected", async () =>
            {
                await MustFail(() => engine.ProbeAsync(Fixture("corrupt.mp4"), CancellationToken.None), "Corrupt media was accepted");
                await MustFail(() => engine.ProbeAsync(Path.Combine(_fixtures, "does-not-exist.mp4"), CancellationToken.None), "Missing media was accepted");
            });
            await Check("Every fixture opens and decodes", async () =>
            {
                foreach (var entry in _assets)
                {
                    var asset = entry.Value;
                    var deliveredBefore = _videoSink.Frames;
                    var fixtureProject = new ProjectDocument
                    {
                        MasterMuted = true, Assets = [asset],
                        Tracks = [new() { Id = "video", Name = "Video", Kind = TrackKind.Video }],
                        Clips = [new() { AssetId = asset.Id, TrackId = asset.HasVideo ? "video" : "fixture", StartMs = 0 }]
                    };
                    if (!asset.HasVideo) fixtureProject.Tracks.Add(new() { Id = "fixture", Name = "Audio", Kind = TrackKind.Audio });
                    TimelineRules.Validate(fixtureProject);
                    await _controller.SetProjectAsync(fixtureProject);
                    await _controller.SeekAsync(0);
                    await _controller.PlayPauseAsync();
                    await Until(() => _controller.PositionMs >= 600 && _status?.IsBusy != true, TimeSpan.FromSeconds(10));
                    var diagnostic = _controller.Diagnostics.Single();
                    Require(diagnostic.NativeMediaTimeMs >= 100, $"{entry.Key}: native media time did not advance");
                    Require(diagnostic.DecodedAudio > 0, $"{entry.Key}: no decoded audio blocks");
                    Require(diagnostic.PlayedAudioBuffers > 0, $"{entry.Key}: native audio output played no buffers");
                    if (asset.HasVideo)
                    {
                        Require(_controller.VideoPlayer.VoutCount > 0, $"{entry.Key}: no native video output initialized");
                        Require(diagnostic.DecodedVideo > 0 && diagnostic.DisplayedPictures > 0, $"{entry.Key}: native video output presented no frames");
                        Require(_videoSink.Frames > deliveredBefore, $"{entry.Key}: native memory output delivered no decoded frames");
                    }
                    AssertHealthy();
                    Console.WriteLine($"  {entry.Key}: native position {diagnostic.NativeMediaTimeMs} ms; state {diagnostic.State}");
                    await _controller.PauseAsync();
                }
                await _controller.StopAsync();
            });
            var project = MakeSmokeProject();
            TimelineRules.Validate(project);
            await Check("Paused seek and real native playback", async () =>
            {
                await _controller.SetProjectAsync(project);
                await _controller.SeekAsync(1000);
                Require(!_controller.IsPlaying, "Seek started playback while paused");
                Near(_controller.PositionMs, 1000, 100, "Paused transport seek");
                Require(_videoVisible, "Video is hidden at an active video clip");
                var paused = _controller.PositionMs;
                await Task.Delay(300);
                Near(_controller.PositionMs, paused, 50, "Paused position changed");
                await _controller.PlayPauseAsync();
                await Until(() => _controller.PositionMs >= 2400 && _status?.IsBusy != true, TimeSpan.FromSeconds(10));
                Require(_controller.IsPlaying, "Playback did not remain running");
                Require(_controller.PositionMs >= 2400, "Common transport did not advance");
                Require(_controller.Diagnostics.Count == 3, "Expected one video and two overlapping audio players");
                Require(_controller.Diagnostics.All(x => x.NativeMediaTimeMs > 0), "A native player failed to advance");
                AssertHealthy();
                await _controller.PauseAsync();
                paused = _controller.PositionMs;
                await Task.Delay(350);
                Near(_controller.PositionMs, paused, 50, "Pause failed to freeze common transport");
            });
            await Check("Playing seek, video gap, automatic second-video start", async () =>
            {
                await _controller.SeekAsync(3500);
                await _controller.PlayPauseAsync();
                await Task.Delay(1250);
                Require(!_videoVisible, "Video output was not hidden in the timeline gap");
                Require(_controller.Diagnostics.Count(x => !x.IsVideo) == 2, "Audio did not continue through video gap");
                await Until(() => _controller.PositionMs >= 6200, TimeSpan.FromSeconds(8));
                Require(_videoVisible, "Video did not resume after gap");
                var video = _controller.Diagnostics.Single(x => x.IsVideo);
                Require(video.ClipId == "video-second", "Incorrect video after clip boundary");
                Require(video.NativeMediaTimeMs >= 100, "Second video native decoder did not advance");
                await _controller.SeekAsync(7000);
                Require(_controller.IsPlaying, "Playing seek paused the transport");
                Near(_controller.PositionMs, 7000, 200, "Playing seek");
                AssertHealthy();
                await _controller.PauseAsync();
            });
            await Check("Rapid seeks settle at the newest request", async () =>
            {
                await Task.WhenAll(_controller.SeekAsync(1000), _controller.SeekAsync(4600), _controller.SeekAsync(6500), _controller.SeekAsync(2500));
                Near(_controller.PositionMs, 2500, 100, "Latest seek target");
                Require(!_controller.IsPlaying, "Rapid paused seeks started playback");
                Require(_controller.Diagnostics.Count == 3, "Rapid seeks left stale/missing players");
                AssertHealthy();
            });
            await Check("Immediate native volume and mute updates", async () =>
            {
                // Paused playback makes nonzero gain checks silent.
                project.MasterMuted = false;
                project.MasterVolume = 80;
                project.Tracks[0].Volume = 75;
                project.Tracks[1].Volume = 50;
                project.Tracks[2].Volume = 25;
                await _controller.UpdateVolumeAsync(project);
                Require(_controller.Diagnostics.Single(x => x.IsVideo).Volume == 60, "Video gain does not combine master and track volume");
                Require(_controller.Diagnostics.Single(x => x.TrackId == "audio-one").Volume == 40, "First audio gain is incorrect");
                Require(_controller.Diagnostics.Single(x => x.TrackId == "audio-two").Volume == 20, "Second audio gain is incorrect");
                Require(_controller.Diagnostics.All(x => x.IsMuted), "Paused native players were left audible");
                await _controller.PlayPauseAsync();
                Require(_controller.Diagnostics.All(x => !x.IsMuted), "Unmuted playback did not restore native output");
                project.Tracks[1].IsMuted = true;
                await _controller.UpdateVolumeAsync(project);
                var muted = _controller.Diagnostics.Single(x => x.TrackId == "audio-one");
                Require(muted.IsMuted || muted.Volume == 0, "Track mute did not reach native player");
                Require(!_controller.Diagnostics.Single(x => x.TrackId == "audio-two").IsMuted, "Muting one audio track muted another");
                project.MasterMuted = true;
                await _controller.UpdateVolumeAsync(project);
                Require(_controller.Diagnostics.All(x => x.IsMuted || x.Volume == 0), "Master mute did not reach every player");
                await _controller.PauseAsync();
            });
            await Check("Audio tail, end of project, stop and return to start", async () =>
            {
                await _controller.SeekAsync(9700);
                Require(!_videoVisible, "Ended video remains visible over audio tail");
                Require(_controller.Diagnostics.Any(x => !x.IsVideo), "Audio tail did not schedule");
                await _controller.PlayPauseAsync();
                await Until(() => !_controller.IsPlaying, TimeSpan.FromSeconds(5));
                Near(_controller.PositionMs, project.Clips.Max(c => c.StartMs + project.Assets.Single(a => a.Id == c.AssetId).DurationMs), 200, "Project end");
                await _controller.StopAsync();
                Require(!_controller.IsPlaying, "Stop left playback running");
                Near(_controller.PositionMs, 0, 50, "Stop position");
                AssertHealthy();
            });
            await Check("Adjacent videos switch while external audio continues", async () =>
            {
                project.Clips.Single(x => x.Id == "video-second").StartMs = _assets["video.mp4"].DurationMs;
                await _controller.SetProjectAsync(project);
                await _controller.SeekAsync(_assets["video.mp4"].DurationMs - 350);
                await _controller.PlayPauseAsync();
                await Until(() => _controller.PositionMs >= 4600, TimeSpan.FromSeconds(8));
                Require(_videoVisible, "Adjacent second video did not become visible");
                Require(_controller.Diagnostics.Single(x => x.IsVideo).ClipId == "video-second", "Adjacent transition retained first video");
                Require(_controller.Diagnostics.Count(x => !x.IsVideo) == 2, "Adjacent transition interrupted scheduled audio");
                AssertHealthy();
                await _controller.PauseAsync();
            });
            if (_args.Contains("--soak", StringComparer.Ordinal))
                await Check("Ten real minutes of coordinated native playback", () => SoakAsync(engine, runName));
        }
        finally
        {
            try { await _controller.PauseAsync(); } catch { /* Preserve the original failure. */ }
            var report = new { utc = DateTime.UtcNow, fixtures = _fixtures, soakRequested = _args.Contains("--soak"), results = _results, finalStatus = _status, diagnostics = _controller.Diagnostics };
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Report: {reportPath}");
        }
        Console.WriteLine($"PASS: {_results.Count} media checks");
    }

    private ProjectDocument MakeSmokeProject()
    {
        var first = _assets["video.mp4"];
        var second = _assets["video.mkv"];
        return new ProjectDocument
        {
            Name = "Real media integration", MasterMuted = true,
            Assets = [first, second, _assets["audio.wav"], _assets["audio.m4a"]],
            Tracks = [new() { Id = "video", Name = "Video", Kind = TrackKind.Video },
                new() { Id = "audio-one", Name = "Audio 1", Kind = TrackKind.Audio },
                new() { Id = "audio-two", Name = "Audio 2", Kind = TrackKind.Audio }],
            Clips = [new() { Id = "video-first", AssetId = first.Id, TrackId = "video", StartMs = 0 },
                new() { Id = "video-second", AssetId = second.Id, TrackId = "video", StartMs = 5500 },
                new() { Id = "audio-first", AssetId = _assets["audio.wav"].Id, TrackId = "audio-one", StartMs = 0 },
                new() { Id = "audio-second", AssetId = _assets["audio.m4a"].Id, TrackId = "audio-two", StartMs = 2000 }]
        };
    }

    private async Task SoakAsync(MediaEngine engine, string runName)
    {
        var video = await engine.ProbeAsync(Fixture("pulse-10min.mp4"), CancellationToken.None);
        var audio = await engine.ProbeAsync(Fixture("pulse-10min.flac"), CancellationToken.None);
        Require(video.DurationMs >= 604000 && audio.DurationMs >= 604000, "Soak fixtures must contain at least 604 seconds");
        var project = new ProjectDocument
        {
            MasterVolume = 0, MasterMuted = true, Assets = [video, audio],
            Tracks = [new() { Id = "video", Name = "Video", Kind = TrackKind.Video }, new() { Id = "soak-a", Name = "Audio A", Kind = TrackKind.Audio }, new() { Id = "soak-b", Name = "Audio B", Kind = TrackKind.Audio }],
            Clips = [new() { Id = "soak-video", AssetId = video.Id, TrackId = "video" },
                new() { Id = "soak-a", AssetId = audio.Id, TrackId = "soak-a" },
                new() { Id = "soak-b", AssetId = audio.Id, TrackId = "soak-b" }]
        };
        TimelineRules.Validate(project);
        await _controller.SetProjectAsync(project);
        await _controller.SeekAsync(0);
        await _controller.PlayPauseAsync();
        await Task.Delay(1000);
        var initialPosition = _controller.PositionMs;
        var initialFrames = _videoSink.Frames;
        var initialTransitions = _videoSink.PulseTransitions;
        var stopwatch = Stopwatch.StartNew();
        var rows = new List<DriftSample>();
        var samplePath = Path.Combine(_reportDirectory, $"soak-{runName}.csv");
        await using var stream = new StreamWriter(samplePath, false, Encoding.UTF8);
        await stream.WriteLineAsync("elapsedMs,clipId,transportMs,expectedMediaMs,nativeMediaMs,estimatedMediaMs,nativeDriftMs,estimatedDriftMs,decodedAudio,decodedVideo,playedAudioBuffers,displayedPictures,state");
        long nextSampleMs = 1000;
        while (stopwatch.Elapsed < TimeSpan.FromMinutes(10))
        {
            await Task.Delay((int)Math.Max(1, nextSampleMs - stopwatch.ElapsedMilliseconds));
            nextSampleMs += 1000;
            AssertHealthy(false);
            Require(_controller.IsPlaying, "Soak playback stopped early");
            var diagnostics = _controller.Diagnostics;
            Require(diagnostics.Count == 3, "Soak lost an active native player");
            foreach (var player in diagnostics)
            {
                var sample = new DriftSample(stopwatch.ElapsedMilliseconds, player.ClipId, player.TimelinePositionMs,
                    player.ExpectedMediaTimeMs, player.NativeMediaTimeMs, player.EstimatedMediaTimeMs,
                    player.NativeMediaTimeMs - player.ExpectedMediaTimeMs,
                    player.EstimatedMediaTimeMs - player.ExpectedMediaTimeMs, player.DecodedAudio, player.DecodedVideo,
                    player.PlayedAudioBuffers, player.DisplayedPictures, player.State);
                rows.Add(sample);
                await stream.WriteLineAsync($"{sample.ElapsedMs},{sample.ClipId},{sample.TransportMs},{sample.ExpectedMs},{sample.NativeMs},{sample.EstimatedMs},{sample.NativeDriftMs},{sample.EstimatedDriftMs},{sample.DecodedAudio},{sample.DecodedVideo},{sample.PlayedAudioBuffers},{sample.DisplayedPictures},{sample.State}");
            }
            if (rows.Count % 90 == 0) await stream.FlushAsync();
            if ((int)stopwatch.Elapsed.TotalSeconds % 30 == 0)
                Console.WriteLine($"  Soak {stopwatch.Elapsed:mm\\:ss}: transport {_controller.PositionMs} ms; max estimated drift {rows.TakeLast(3).Max(x => Math.Abs(x.EstimatedDriftMs))} ms");
        }
        await _controller.PauseAsync();
        var advance = _controller.PositionMs - initialPosition;
        Require(advance >= 597000, $"Transport advanced only {advance} ms in ten real minutes");
        Require(rows.Count >= 1770, $"Too few actual native-time samples: {rows.Count}");
        Require(_videoSink.Frames - initialFrames >= 5000, "Native memory video output delivered too few real frames");
        Require(_videoSink.PulseTransitions - initialTransitions >= 500, "Decoded pulse pixels did not change throughout playback");
        Console.WriteLine($"  Delivered frames={_videoSink.Frames - initialFrames}; actual decoded pulse transitions={_videoSink.PulseTransitions - initialTransitions}");
        foreach (var group in rows.Where(x => x.ElapsedMs >= 5000).GroupBy(x => x.ClipId))
        {
            var samples = group.ToArray();
            Require(samples[^1].PlayedAudioBuffers - samples[0].PlayedAudioBuffers >= 500,
                $"{group.Key}: native audio output did not continue through the soak");
            if (group.Key == "soak-video")
                Require(samples[^1].DisplayedPictures - samples[0].DisplayedPictures >= 5000,
                    "Native video output did not continue through the soak");
            foreach (var window in samples.GroupBy(x => x.ElapsedMs / 60000).Where(x => x.Count() >= 10))
            {
                Require(window.Last().PlayedAudioBuffers > window.First().PlayedAudioBuffers,
                    $"{group.Key}: native audio output stopped during minute {window.Key + 1}");
                if (group.Key == "soak-video")
                    Require(window.Last().DisplayedPictures > window.First().DisplayedPictures,
                        $"Native video output stopped during minute {window.Key + 1}");
            }
            var maxRaw = samples.Max(x => Math.Abs(x.NativeDriftMs));
            var maxEstimated = samples.Max(x => Math.Abs(x.EstimatedDriftMs));
            var first = Percentile95(samples.Where(x => x.ElapsedMs < 65000).Select(x => Math.Abs(x.EstimatedDriftMs)));
            var last = Percentile95(samples.Where(x => x.ElapsedMs > 540000).Select(x => Math.Abs(x.EstimatedDriftMs)));
            Console.WriteLine($"  {group.Key}: max native drift={maxRaw} ms; max estimated drift={maxEstimated} ms; first/last minute p95={first}/{last} ms");
            Require(maxRaw <= 1500, $"{group.Key}: raw native drift exceeded 1500 ms ({maxRaw})");
            Require(maxEstimated <= 750, $"{group.Key}: estimated native drift exceeded 750 ms ({maxEstimated})");
            Require(last <= first + 250, $"{group.Key}: drift accumulated from {first} to {last} ms p95");
        }
        Console.WriteLine($"  Actual drift samples: {samplePath}");
    }

    private async Task Check(string name, Func<Task> action)
    {
        Console.WriteLine($"RUN: {name}");
        var timer = Stopwatch.StartNew();
        try
        {
            await action();
            _results.Add(new(name, true, timer.ElapsedMilliseconds, null));
            Console.WriteLine($"PASS: {name} ({timer.ElapsedMilliseconds} ms)");
        }
        catch (Exception error)
        {
            _results.Add(new(name, false, timer.ElapsedMilliseconds, error.ToString()));
            Console.Error.WriteLine($"FAIL: {name}: {error.Message}");
            Console.Error.WriteLine(JsonSerializer.Serialize(new { status = _status, diagnostics = _controller.Diagnostics }));
            throw;
        }
    }

    private void AssertHealthy(bool requireSettled = true)
    {
        Require(_status is not null, "No status was published");
        Require(string.IsNullOrEmpty(_status?.Error), $"Native playback error: {_status?.Error}");
        if (requireSettled) Require(_status?.IsBusy != true, "Controller remains busy after completed operation");
    }

    private static async Task Until(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > timeout) throw new TimeoutException($"Playback condition not met within {timeout}");
            await Task.Delay(50);
        }
    }

    private static async Task MustFail(Func<Task<MediaAsset>> operation, string message)
    {
        try { await operation(); }
        catch (Exception error) when (error is not OperationCanceledException) { return; }
        throw new InvalidOperationException(message);
    }

    private string Fixture(string name)
    {
        var path = Path.Combine(_fixtures, name);
        if (!File.Exists(path)) throw new FileNotFoundException("Run scripts/Generate-Fixtures.ps1 -IncludeSoak first", path);
        return path;
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "VideoSync.App", "VideoSync.App.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("Run this harness from the Video-Sync repository");
    }

    private static long Percentile95(IEnumerable<long> values)
    {
        var ordered = values.Order().ToArray();
        Require(ordered.Length > 0, "Drift window has no actual samples");
        return ordered[(int)Math.Ceiling(ordered.Length * .95) - 1];
    }

    private static void Near(long actual, long expected, long tolerance, string context) =>
        Require(Math.Abs(actual - expected) <= tolerance, $"{context}: expected {expected} +/- {tolerance} ms, got {actual} ms");

    private static void Require(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }

    private sealed record TestResult(string Name, bool Passed, long ElapsedMs, string? Error);
    private sealed record DriftSample(long ElapsedMs, string ClipId, long TransportMs, long ExpectedMs,
        long NativeMs, long EstimatedMs, long NativeDriftMs, long EstimatedDriftMs, int DecodedAudio,
        int DecodedVideo, int PlayedAudioBuffers, int DisplayedPictures, string State);
}
