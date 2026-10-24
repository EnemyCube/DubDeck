using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VideoSync.Core;

namespace VideoSync.App;

// Runs the actual WPF window without injecting desktop input. Screenshots contain WPF
// surfaces; native hardware video is verified separately by the media integration harness.
public partial class MainWindow
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    private async Task RunSmokeTestAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        try
        {
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); File.AppendAllText(Path.Combine(directory, "progress.txt"), message + Environment.NewLine); }
            await Task.Delay(300);
            Capture("01-empty.png");
            Check(ActualWidth >= 960 && Timeline.ActualWidth >= 900, "Initial workspace is laid out");
            string fixture = App.FixtureDirectory ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/media-fixtures"));
            string disposableVideo = Path.Combine(directory, "test-video.mp4");
            File.Copy(Path.Combine(fixture, "video.mp4"), disposableVideo, true);
            await ImportFilesAsync(new[] { disposableVideo, Path.Combine(fixture, "video.mov"), Path.Combine(fixture, "audio.wav"), Path.Combine(fixture, "audio.flac") });
            Check(_project.Clips.Count == 4 && _project.Tracks.Count == 3, "Import places sequential video and layered audio");
            long duration = TimelineRules.Duration(_project);
            var audio = _project.Assets.First(a => !a.HasVideo);
            var track = _project.Tracks.First(t => t.Kind == TrackKind.Audio);
            _project.Clips.Add(new TimelineClip { AssetId = audio.Id, TrackId = track.Id, StartMs = duration + 1000 });
            _project.MasterMuted = true;
            UpdateProjectUI();
            await _playback.SetProjectAsync(_project);
            FitClicked(this, new RoutedEventArgs());
            await _playback.SeekAsync(1500);
            await _playback.PlayPauseAsync();
            await Task.Delay(1200);
            await _playback.PauseAsync();
            Check(_playback.PositionMs >= 2300 && !_playback.IsPlaying, "Playback advances and pauses");
            File.WriteAllText(Path.Combine(directory, "playback-diagnostics.json"), JsonSerializer.Serialize(new {
                windowVisible = IsWindowVisible(new System.Windows.Interop.WindowInteropHelper(this).Handle),
                videoHandle = _playback.VideoPlayer.Hwnd.ToInt64(),
                videoWindowVisible = IsWindowVisible(_playback.VideoPlayer.Hwnd),
                viewVisibility = VideoHost.Visibility.ToString(),
                diagnostics = _playback.Diagnostics
            }, new JsonSerializerOptions { WriteIndented = true }));
            Check(_playback.VideoPlayer.Hwnd != IntPtr.Zero && _playback.Diagnostics.Any(d => d.IsVideo && d.DisplayedPictures > 0), "WPF preview owns a native target and presents video frames");
            var modules = System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>().Where(m => m.ModuleName is "coreclr.dll" or "libvlc.dll" or "libvlccore.dll").Select(m => new { m.ModuleName, m.FileName }).ToArray();
            File.WriteAllText(Path.Combine(directory, "loaded-modules.json"), JsonSerializer.Serialize(modules, new JsonSerializerOptions { WriteIndented = true }));
            double oldVolume = MasterVolume.Value;
            MasterVolume.Value = 43;
            Check(_project.MasterVolume == 43 && FullVolume.Value == 43, "Master sliders stay synchronized");
            MasterVolume.Value = oldVolume;
            var clip = _project.Clips.First(c => c.TrackId == "video");
            Timeline.Select(clip.Id);
            Check(!TimelineRules.TryMove(_project, clip.Id, "video", 3000, out _), "Overlapping video placement is rejected");
            UpdateProjectUI();
            Capture("02-timeline.png");
            long paused = _playback.PositionMs;
            ToggleFullscreen();
            await Task.Delay(250);
            Check(Header.Visibility == Visibility.Collapsed && FullVideoControls.Visibility == Visibility.Visible, "Fullscreen hides editing panels and exposes transport");
            Check(_playback.PositionMs == paused, "Fullscreen preserves paused position");

            Check(Icon is not null, "The application window has its own icon");
            Check(FullVideoMixer.Children.Count == _project.Tracks.Count && FullEmptyMixer.Children.Count == _project.Tracks.Count, "Fullscreen includes every track for both video and audio-only playback");
            var mixerTrack = _project.Tracks.First(t => t.Kind == TrackKind.Audio);
            var mixerWidgets = _trackWidgets[mixerTrack.Id];
            mixerWidgets[1].Slider.Value = 37;
            await _playback.UpdateVolumeAsync(_project);
            Check(mixerTrack.Volume == 37 && mixerWidgets.All(w => w.Slider.Value == 37), "Fullscreen and timeline track sliders stay synchronized");
            mixerWidgets[1].Mute.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await _playback.UpdateVolumeAsync(_project);
            Check(mixerTrack.IsMuted && mixerWidgets.All(w => (string)w.Mute.Content == "Muted"), "Fullscreen mute controls update the shared track mixer");
            mixerWidgets[2].Slider.Value = 64;
            Check(mixerTrack.Volume == 64 && mixerWidgets.All(w => w.Slider.Value == 64), "Audio-only fullscreen sliders share the same levels");

            mixerWidgets[1].Voice.IsChecked = true;
            mixerWidgets[1].Voice.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await _playback.UpdateVolumeAsync(_project);
            Check(mixerTrack.IsDuckingSource && mixerWidgets.All(w => w.Voice.IsChecked == true), "Voice trigger controls stay synchronized across mixers");
            _duckControls[1].Enabled.IsChecked = true;
            _duckControls[1].Enabled.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            _duckControls[2].Amount.Value = 72;
            await _playback.UpdateVolumeAsync(_project);
            Check(_project.AutoDuckingEnabled && _project.DuckAmount == 72 && _duckControls.All(w => w.Enabled.IsChecked == true && w.Amount.Value == 72), "Fullscreen ducking settings update all controls");
            mixerWidgets[1].Pause.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await _playback.GetSubtitlesAsync();
            SyncPauseControls();
            Check(_playback.PausedTrackIds.Contains(mixerTrack.Id) && mixerWidgets.All(w => (string)w.Pause.Content == "Resume"), "Fullscreen track pause updates every mixer");
            mixerWidgets[2].Pause.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await _playback.GetSubtitlesAsync();
            SyncPauseControls();
            Check(!_playback.PausedTrackIds.Contains(mixerTrack.Id) && mixerWidgets.All(w => (string)w.Pause.Content == "Pause"), "Track resume controls stay synchronized");

            await _playback.SeekAsync(duration + 1500);
            await Task.Delay(250);
            Check(!_videoVisible && EmptyPreview.Visibility == Visibility.Collapsed && FullEmptyControls.Visibility == Visibility.Visible, "Video gaps stay black while fullscreen audio controls remain available");
            Capture("03-fullscreen.png");
            ToggleFullscreen();
            await Task.Delay(200);
            Check(Header.Visibility == Visibility.Visible && WindowStyle == WindowStyle.SingleBorderWindow, "Escape-style fullscreen exit restores the workspace");
            string save = Path.Combine(directory, "roundtrip.videosync");
            ProjectStore.Save(_project, save);
            var loaded = ProjectStore.Load(save);
            Check(loaded.Clips.Count == _project.Clips.Count && loaded.MasterVolume == _project.MasterVolume, "Project round trip preserves arrangement and volume");
            File.Delete(disposableVideo);
            await NewProjectAsync();
            Check(_project.Clips.Count == 0 && _playback.PositionMs == 0, "New clears the arrangement and resets transport");
            await OpenProjectAsync(save);
            Check(_project.Clips.Count == loaded.Clips.Count && _playback.PositionMs == 0, "Open restores the project at its beginning");
            Check(_project.Assets.Any(a => a.FilePath == disposableVideo && a.IsOffline), "Missing sources stay in the project and do not prevent New or Open");
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { success = true, checks, nativeVideoScreenshot = false }, new JsonSerializerOptions { WriteIndented = true }));
            _dirty = false;
            Close();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "failure.txt"), ex.ToString());
            _dirty = false; _importing = false;
            Close();
        }
        void Capture(string name)
        {
            UpdateLayout();
            FrameworkElement target = _full ? VideoOverlay : this;
            double dpi = VisualTreeHelper.GetDpi(target).DpiScaleX;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth * dpi), (int)Math.Ceiling(target.ActualHeight * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            bitmap.Render(target);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
        }
    }
}
