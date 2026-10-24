using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using VideoSync.Core;

namespace VideoSync.App;

public partial class MainWindow
{
    private async Task RunPerformanceTestAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        try
        {
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(directory, "progress.txt"), message + Environment.NewLine); }
            string fixture = App.FixtureDirectory ?? throw new InvalidOperationException("--fixtures is required.");
            var video = await _engine.ProbeAsync(Path.Combine(fixture, "pulse-10min.mp4"));
            var audio = await _engine.ProbeAsync(Path.Combine(fixture, "audio.wav"));
            _project = new ProjectDocument { Name = "Performance regression", Assets = [video, audio], MasterMuted = true };
            for (int i = 0; i < 40; i++) _project.Clips.Add(new TimelineClip { AssetId = video.Id, TrackId = "video", StartMs = i * video.DurationMs });
            for (int lane = 0; lane < 40; lane++)
            {
                var track = new TimelineTrack { Kind = TrackKind.Audio, Name = "Audio " + lane };
                _project.Tracks.Add(track);
                if (lane < 2) _project.Clips.Add(new TimelineClip { AssetId = audio.Id, TrackId = track.Id, StartMs = 0 });
                for (int i = 0; i < 250; i++) _project.Clips.Add(new TimelineClip { AssetId = audio.Id, TrackId = track.Id, StartMs = 3600000L + i * 9000L });
            }
            UpdateProjectUI();
            await _playback.SetProjectAsync(_project, Timeline.OfflineAssetIds);
            await Task.Delay(300);
            if (App.SimulateVideoStartupStall) Check(_playback.UsedSoftwareVideoFallback, "A hardware startup stall retries real playback using software decoding");
            Check(TimelineRules.Duration(_project) > 6 * 3600000L && _project.Clips.Count > 10000, "A six-hour timeline with over 10,000 clips is loaded");
            Check(Timeline.VisibleClipCount < 100 && Timeline.RulerLabelCount < 100, "Drawing work is restricted to the visible viewport");
            int initialScenes = Timeline.SceneBuildCount;
            var playheadTimer = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) Timeline.Playhead = i * 10;
            Check(Timeline.SceneBuildCount == initialScenes, "1,000 playhead updates do not rebuild clip text or ruler geometry");
            double playheadMs = playheadTimer.Elapsed.TotalMilliseconds;
            await _playback.PlayPauseAsync();
            await Task.Delay(400);
            var heartbeat = new List<double>();
            long stamp = Stopwatch.GetTimestamp();
            var pulse = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(20) };
            pulse.Tick += (_, _) => { long now = Stopwatch.GetTimestamp(); heartbeat.Add(Stopwatch.GetElapsedTime(stamp, now).TotalMilliseconds); stamp = now; };
            pulse.Start();
            var dragTimes = new List<double>();
            var dragClip = _project.Clips.First(c => c.TrackId != "video");
            int dragScenes = Timeline.SceneBuildCount;
            for (int i = 0; i < 240; i++)
            {
                long start = Stopwatch.GetTimestamp();
                Timeline.PreviewDrag(dragClip.Id, i * 20, dragClip.TrackId);
                dragTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                await Task.Delay(8);
            }
            Timeline.CancelPreviewDrag();
            pulse.Stop();
            Check(Timeline.SceneBuildCount == dragScenes, "Dragging uses only the overlay while video is playing");
            double p95Drag = Percentile(dragTimes), p95Heartbeat = Percentile(heartbeat);
            Check(p95Drag < 16, "Drag rendering p95 stays below one 60 Hz frame");
            Check(heartbeat.Count > 20 && p95Heartbeat < 100, "Input-priority UI callbacks remain responsive during playback and dragging");
            await _playback.PauseAsync();
            var controls = TrackHeaders.Children[0];
            int opensBeforeMoves = _playback.SourceOpenCount;
            var moved = _project.Clips.First(c => c.TrackId != "video" && c.StartMs >= 3600000);
            long original = moved.StartMs;
            var moveTimer = Stopwatch.StartNew();
            await MoveClipAsync(moved.Id, moved.TrackId, original + 300);
            await MoveClipAsync(moved.Id, moved.TrackId, original);
            double twoMovesMs = moveTimer.Elapsed.TotalMilliseconds;
            Check(ReferenceEquals(controls, TrackHeaders.Children[0]), "Clip moves retain existing track controls");
            Check(moved.StartMs == original, "Real clip moves still obey and update the timeline");
            await MoveClipAsync(dragClip.Id, dragClip.TrackId, 200);
            Check(_playback.SourceOpenCount == opensBeforeMoves, "Moving clips reuses active decoders instead of reopening source files");
            await _playback.PlayPauseAsync();
            await Task.Delay(400);
            Check(_playback.IsPlaying && _playback.Diagnostics.Any(d => d.IsVideo && d.DisplayedPictures > 0), "Close regression starts with actual video playing");
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new {
                success = true, checks, clipCount = _project.Clips.Count,
                visibleClips = Timeline.VisibleClipCount, rulerLabels = Timeline.RulerLabelCount,
                playhead1000UpdatesMs = playheadMs, dragP95Ms = p95Drag, inputHeartbeatP95Ms = p95Heartbeat,
                twoCommittedMovesMs = twoMovesMs, closesDuringPlayback = _playback.IsPlaying, softwareVideoFallback = _playback.UsedSoftwareVideoFallback
            }, new JsonSerializerOptions { WriteIndented = true }));
            _dirty = false;
            Close(); // Deliberately close with the native video and audio outputs still running.
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Close)); // Repeated X/Alt+F4 must not destroy an active HWND.
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
            _dirty = false; _importing = false;
            Close();
        }
        static double Percentile(List<double> values) => values.Count == 0 ? double.PositiveInfinity : values.Order().ElementAt((int)Math.Floor((values.Count - 1) * .95));
    }
}
