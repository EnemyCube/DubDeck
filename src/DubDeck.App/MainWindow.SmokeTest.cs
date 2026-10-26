using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DubDeck.Core;

namespace DubDeck.App;

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
            UpdateLayout();
            Check(BrandLabel.Text == "DUBDECK" && FullBrandLabel.Text == "DUBDECK" && EmptyBrandLabel.Text == "DUBDECK" &&
                Title.EndsWith("— DubDeck") && typeof(MainWindow).Assembly.GetName().Name == "DubDeck",
                "Workspace, fullscreen labels, window title, and executable assembly use DubDeck");
            var emptyLane = (FrameworkElement)TrackHeaders.Children[0];
            Check(Math.Abs(emptyLane.TranslatePoint(new Point(), TimelinePanel).Y -
                Timeline.TranslatePoint(new Point(0, TimelineSurface.RulerHeight), TimelinePanel).Y) < .5,
                "The initial single-track workspace reserves the time ruler before the first track controls");
            Check(ThemeButton.Content is System.Windows.Controls.Image && AppIcons.GetKind(ThemeButton) == "palette" &&
                System.Windows.Automation.AutomationProperties.GetName(ThemeButton) == "Choose a color theme",
                "Theme selection uses an accessible palette icon button");
            Check(EmptyTimelineOverlay.IsVisible, "Empty timelines display the drag-and-drop/import prompt");
            await ToggleSubtitlesMenuAsync(SubtitlesButton);
            await Task.Delay(120);
            Check(_subtitleMenu?.IsOpen == true && _subtitleMenu.Items.Count == 1,
                "The styled subtitles menu explains when no movie is active");
            Capture("01-empty-subtitles.png");
            CloseSubtitleMenu();
            Capture("01-empty.png");
            Check(ActualWidth >= 960 && Timeline.ActualWidth >= 900, "Initial workspace is laid out");
            string fixture = App.FixtureDirectory ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../artifacts/media-fixtures"));
            string disposableVideo = Path.Combine(directory, "test-video.mp4");
            File.Copy(Path.Combine(fixture, "video.mp4"), disposableVideo, true);
            var importing=ImportFilesAsync(new[] { disposableVideo, Path.Combine(fixture, "video.mov"), Path.Combine(fixture, "voice-gated.wav"), Path.Combine(fixture, "audio.flac") });
            Check(_importing && ImportOverlay.Visibility==Visibility.Visible && !ImportButton.IsEnabled && !Header.IsEnabled && !TimelinePanel.IsEnabled,
                "Import shows an obvious loading overlay and disables workspace controls");
            await ImportFilesAsync(new[] { Path.Combine(fixture,"audio.mp3") });
            Check(_project.Clips.Count==0,"A second import is ignored while a batch is loading");
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Capture("00-loading.png");
            Check(ImportDetail.Text.Contains("of 4") || ImportDetail.Text.Contains("4 files"),"Loading indicator reports progress for the current batch");
            await importing;
            Check(!_importing && ImportOverlay.Visibility==Visibility.Collapsed && ImportButton.IsEnabled && Header.IsEnabled && TimelinePanel.IsEnabled,
                "Completing import removes the loading indicator and restores controls");
            Check(EmptyTimelineOverlay.Visibility == Visibility.Collapsed, "The empty timeline prompt disappears when clips are imported");
            Check(_project.Clips.Count == 4 && _project.Tracks.Count == 3, "Import places sequential video and layered audio");
            Check(_project.Name=="video" && FullTitle.Text=="video" && EmptyTitle.Text=="video" && Title.StartsWith("video"), "The latest imported video names the project without its extension; later audio imports preserve it");
            Check(_project.AutoDuckingEnabled && _project.Tracks.First(t => t.Kind == TrackKind.Audio).IsDuckingSource && !_project.Tracks.Last().IsDuckingSource,
                "Import defaults Auto duck on with only Audio 1 marked Voice");
            Check(SubtitlesButton.Content is System.Windows.Controls.Image { Source: not null }, "Subtitles button displays the downloaded captions icon");
            await WaitForWaveformsAsync();
            Check(_project.Assets.Where(a => a.HasAudio).All(a => Timeline.HasWaveform(a.Id)),
                "Background waveforms are available for imported videos and external audio files");
            await CheckTimelineNavigationAsync(directory, Check);
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
            Check(Timeline.VisibleWaveformColumns > 0 && Timeline.VisibleWaveformColumns < 5000,
                "Faint waveform drawing is bounded to visible clip backgrounds");
            Capture("02-timeline.png");
            await CheckThemesAsync(directory, Check, Capture);
            Activate(); // A real click activates the owning window before opening its popup.
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Input);
            long stopPosition = _playback.PositionMs;
            StopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Task.Delay(120);
            Check(StopConfirmationPopup.IsOpen && _playback.PositionMs == stopPosition &&
                System.Windows.Input.Keyboard.FocusedElement == CancelStopButton,
                $"Stop asks before changing playback, with Cancel focused by default (open={StopConfirmationPopup.IsOpen}, position={_playback.PositionMs}/{stopPosition}, focus={System.Windows.Input.Keyboard.FocusedElement}, active={IsActive})");
            Capture("02-stop-confirmation.png");
            var stopEscape = SmokeKey(System.Windows.Input.Key.Escape);
            StopConfirmationKeyDown(this, stopEscape);
            Check(!StopConfirmationPopup.IsOpen && _playback.PositionMs == stopPosition && stopEscape.Handled,
                "Escape dismisses Stop confirmation without rewinding");
            await _playback.PlayPauseAsync();
            StopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Task.Delay(150);
            CancelStopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(!StopConfirmationPopup.IsOpen && _playback.IsPlaying && _playback.PositionMs > stopPosition,
                "Canceling Stop leaves ongoing playback uninterrupted");
            await _playback.PauseAsync();
            StopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Task.Delay(80);
            ConfirmStopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await _playback.GetSubtitlesAsync();
            Check(!StopConfirmationPopup.IsOpen && _playback.PositionMs == 0 && !_playback.IsPlaying,
                "Confirming Stop stops playback and returns to the beginning");
            await _playback.SeekAsync(stopPosition);
            long paused = _playback.PositionMs;
            ToggleFullscreen();
            await Task.Delay(250);
            Check(Header.Visibility == Visibility.Collapsed && FullVideoControls.Visibility == Visibility.Visible, "Fullscreen hides editing panels and exposes transport");
            Check(_playback.PositionMs == paused, "Fullscreen preserves paused position");
            FullStopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Task.Delay(80);
            Check(StopConfirmationPopup.IsOpen && _playback.PositionMs == paused,
                "The video fullscreen Stop button also requires confirmation");
            var fullStopEscape = SmokeKey(System.Windows.Input.Key.Escape);
            StopConfirmationKeyDown(this, fullStopEscape);
            Check(_full && !StopConfirmationPopup.IsOpen, "Escape dismisses Stop confirmation while retaining fullscreen");
            Check(TrackButtonBesideSubtitles(FullTrackButton, FullSubtitlesButton), "Video fullscreen places Track controls immediately left of Subtitles");
            ShowStatus(new DubDeck.App.Media.PlaybackStatus(_playback.PositionMs,TimelineRules.Duration(_project),false,false,null));
            double fullscreenTarget=FullSeekBar.Value+300;
            FullSeekBar.Value=fullscreenTarget;
            await Task.Delay(400);
            Check(Math.Abs(_playback.PositionMs-fullscreenTarget)<100 && !_playback.IsPlaying,"Fullscreen scrubber seeks while preserving paused playback");
            Check(FullMasterValue.Text==_project.MasterVolume+"%" && EmptyMasterValue.Text==FullMasterValue.Text,"Fullscreen master readouts stay synchronized");

            Check(!TrackControlsOpen,"Fullscreen track controls start collapsed");
            FullTrackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Task.Delay(150);
            Check(FullTrackPopup.IsOpen && FullVideoMixer.IsVisible,"Track controls button opens the video mixer pop-up");
            SmokeMouseDown(FullTrackButton);
            Check(FullTrackPopup.IsOpen, "Pressing the open mixer button preserves its state until the toggle click");
            FullTrackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(!TrackControlsOpen && _full, "Clicking Track controls a second time closes its popup and retains fullscreen");
            FullTrackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            SmokeMouseDown(_trackWidgets.Values.First()[1].Slider);
            Check(FullTrackPopup.IsOpen, "Interacting with mixer controls keeps the popup open");
            SmokeMouseDown(FullTime);
            Check(!TrackControlsOpen, "Clicking outside the mixer dismisses it");
            FullTrackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            TrackControlsDeactivated(this, EventArgs.Empty);
            Check(!TrackControlsOpen, "Leaving the application dismisses track controls");
            FullTrackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Task.Delay(100);
            UpdateOsd();
            Check(FullVideoControls.Opacity==1,"The playback bar stays visible while the mixer pop-up is open");
            Capture("03-video-track-controls.png");
            Check(Icon is not null, "The application window has its own icon");
            Check(FullVideoMixer.Children.Count == _project.Tracks.Count && FullEmptyMixer.Children.Count == _project.Tracks.Count, "Fullscreen includes every track for both video and audio-only playback");
            var mixerTrack = _project.Tracks.First(t => t.Kind == TrackKind.Audio);
            var mixerWidgets = _trackWidgets[mixerTrack.Id];
            Check(mixerWidgets.All(w => w.Voice.Content is System.Windows.Controls.Image && AppIcons.GetKind(w.Voice)=="mic") && _duckControls.All(w => w.Enabled.Content is System.Windows.Controls.Image && AppIcons.GetKind(w.Enabled)=="audio-waveform"), "Voice and Auto duck controls use labeled, stateful icons in every mixer");
            mixerWidgets[1].Slider.Value = 37;
            await _playback.UpdateVolumeAsync(_project);
            Check(mixerTrack.Volume == 37 && mixerWidgets.All(w => w.Slider.Value == 37), "Fullscreen and timeline track sliders stay synchronized");
            mixerWidgets[1].Mute.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await _playback.UpdateVolumeAsync(_project);
            Check(mixerTrack.IsMuted && mixerWidgets.All(w => AppIcons.GetKind(w.Mute) == "volume-x"), "Fullscreen mute controls update the shared track mixer");
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
            Check(_playback.PausedTrackIds.Contains(mixerTrack.Id) && mixerWidgets.All(w => AppIcons.GetKind(w.Pause) == "play" &&
                w.Pause.Background is SolidColorBrush background && background.Color == Color.FromRgb(255, 209, 102) &&
                w.Pause.Foreground is SolidColorBrush foreground && foreground.Color == Color.FromRgb(38, 32, 21)),
                "Paused track buttons offer resume and turn yellow across every mixer");
            string pauseTheme = ThemeManager.Current.Id;
            foreach (var theme in ThemeManager.Themes)
            {
                ThemeManager.Apply(theme.Id, persist: false);
                Check(mixerWidgets.All(w => AppIcons.GetKind(w.Pause) == "play" &&
                    w.Pause.Background is SolidColorBrush background && background.Color == Color.FromRgb(255, 209, 102) &&
                    w.Pause.Foreground is SolidColorBrush foreground && foreground.Color == Color.FromRgb(38, 32, 21)),
                    theme.Name + ": paused track buttons retain a readable yellow indicator");
            }
            ThemeManager.Apply(pauseTheme, persist: false);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            Capture("06-track-paused.png");
            mixerWidgets[2].Pause.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await _playback.GetSubtitlesAsync();
            SyncPauseControls();
            Check(!_playback.PausedTrackIds.Contains(mixerTrack.Id) && mixerWidgets.All(w => AppIcons.GetKind(w.Pause) == "pause" &&
                ReferenceEquals(w.Pause.Background, ColorBrush("#293245")) && ReferenceEquals(w.Pause.Foreground, ColorBrush("#EBEFF7")) &&
                w.Pause.ToolTip is string tooltip && tooltip.StartsWith("Pause ")),
                "Resumed track buttons retain the pause icon and restore normal themed colors");

            // Individual pause edits placements while native playback on other lanes continues.
            ToggleFullscreen();
            await _playback.SeekAsync(500);
            ApplyTrackPositions();
            long originalStart = _project.Clips.First(c=>c.TrackId==mixerTrack.Id).StartMs;
            await _playback.PlayPauseAsync();
            await _playback.SetTrackPausedAsync(mixerTrack.Id,true);
            await Task.Delay(650);
            ApplyTrackPositions();
            long shiftedStart = _project.Clips.First(c=>c.TrackId==mixerTrack.Id).StartMs;
            Check(shiftedStart >= originalStart+450 && _dirty, "Paused track clips move live on the timeline and mark the project changed");
            await _playback.SetTrackPausedAsync(mixerTrack.Id,false);
            await _playback.PauseAsync();
            ApplyTrackPositions();
            shiftedStart = _project.Clips.First(c=>c.TrackId==mixerTrack.Id).StartMs;
            await _playback.StopAsync();
            ApplyTrackPositions();
            Check(_project.Clips.First(c=>c.TrackId==mixerTrack.Id).StartMs == shiftedStart, "Stop preserves the pause-created timeline placement");
            string alignmentSave = Path.Combine(directory,"alignment.videosync");
            ProjectStore.Save(_project,alignmentSave);
            Check(ProjectStore.Load(alignmentSave).Clips.First(c=>c.TrackId==mixerTrack.Id).StartMs == shiftedStart,"Pause alignment is saved in the project");
            QueueSeek(5000); await Task.Delay(600);
            JumpBy(10000); await Task.Delay(600);
            Check(Math.Abs(_playback.PositionMs-15000)<150,"Right-arrow action seeks forward ten seconds while paused");
            JumpBy(-10000); await Task.Delay(600);
            Check(Math.Abs(_playback.PositionMs-5000)<150,"Left-arrow action seeks backward ten seconds while paused");
            JumpBy(-10000); JumpBy(-10000); await Task.Delay(600);
            Check(_playback.PositionMs==0,"Repeated backward jumps clamp at the timeline start");
            QueueSeek(Math.Max(0,TimelineRules.Duration(_project)-5000)); JumpBy(10000); JumpBy(10000);
            var seekDeadline=System.Diagnostics.Stopwatch.StartNew();
            while((_pendingSeek is not null || Math.Abs(_playback.PositionMs-TimelineRules.Duration(_project))>=150) && seekDeadline.ElapsedMilliseconds<5000) await Task.Delay(50);
            Check(_pendingSeek is null && Math.Abs(_playback.PositionMs-TimelineRules.Duration(_project))<150,$"Repeated forward jumps accumulate and clamp at the timeline end (actual {_playback.PositionMs}, end {TimelineRules.Duration(_project)})");
            ToggleFullscreen();
            await _playback.SeekAsync(duration + 1500);
            await Task.Delay(250);
            Check(!_videoVisible && EmptyPreview.Visibility == Visibility.Collapsed && FullEmptyControls.Visibility == Visibility.Visible, "Video gaps stay black while fullscreen audio controls remain available");
            Check(TrackButtonBesideSubtitles(EmptyTrackButton, EmptySubtitlesButton), "Audio-only fullscreen places Track controls immediately left of Subtitles");
            EmptyStopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Task.Delay(80);
            Check(StopConfirmationPopup.IsOpen, "Audio-only fullscreen Stop also requires confirmation");
            CancelStopButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Capture("03-fullscreen.png");
            EmptyTrackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Task.Delay(150);
            Check(EmptyTrackPopup.IsOpen && FullEmptyMixer.IsVisible,"Track controls pop-up also works during video gaps");
            SmokeMouseDown(EmptyTrackButton);
            EmptyTrackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(!TrackControlsOpen && _full, "Audio-only Track controls also closes on a second click");
            EmptyTrackButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Task.Delay(100);
            Capture("04-track-controls.png");
            var escape=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(this),Environment.TickCount,System.Windows.Input.Key.Escape) { RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            TrackPopupKeyDown(this,escape);
            Check(!TrackControlsOpen && _full && escape.Handled,"Escape dismisses track controls without leaving fullscreen");
            ToggleFullscreen();
            await Task.Delay(200);
            Check(Header.Visibility == Visibility.Visible && WindowStyle == WindowStyle.SingleBorderWindow, "Escape-style fullscreen exit restores the workspace");
            string save = Path.Combine(directory, "roundtrip.videosync");
            long savedPosition = _playback.PositionMs;
            _projectPath=save;
            Check(SaveProject(false),"Save captures the current playhead position");
            var loaded = ProjectStore.Load(save);
            Check(loaded.Clips.Count == _project.Clips.Count && loaded.MasterVolume == _project.MasterVolume && loaded.PlayheadMs==savedPosition && loaded.Name=="video", "Project round trip preserves the video title, arrangement, volume, and playhead");
            string brandedSave = Path.Combine(directory, "roundtrip.dubdeck");
            ProjectStore.Save(loaded, brandedSave);
            await OpenProjectAsync(brandedSave);
            Check(_projectPath == brandedSave && _project.Clips.Count == loaded.Clips.Count &&
                Math.Abs(_playback.PositionMs - savedPosition) < 100,
                "DubDeck projects open with the same arrangement and saved playhead as legacy projects");
            File.Delete(disposableVideo);
            await NewProjectAsync();
            Check(_project.Clips.Count == 0 && _playback.PositionMs == 0, "New clears the arrangement and resets transport");
            Check(EmptyTimelineOverlay.Visibility == Visibility.Visible, "The empty timeline prompt returns for a new project");
            await OpenProjectAsync(save);
            Check(_project.Clips.Count == loaded.Clips.Count && Math.Abs(_playback.PositionMs-savedPosition)<100 && !_playback.IsPlaying, "Open restores the saved playhead while keeping playback paused");
            await Task.Delay(150); ApplyTrackPositions();
            Check(_project.Clips.All(c => loaded.Clips.Single(saved => saved.Id == c.Id).StartMs == c.StartMs), "Reopening preserves shifted placements without applying the alignment twice");
            Check(_project.Assets.Any(a => a.FilePath == disposableVideo && a.IsOffline), "Missing sources stay in the project and do not prevent New or Open");
            await ImportFilesAsync(new[] { Path.Combine(fixture, "subtitle-embedded.mkv") });
            var captionClip = _project.Clips.Last(c => c.TrackId == "video");
            await _playback.SeekAsync(captionClip.StartMs + 500);
            await ToggleSubtitlesMenuAsync(SubtitlesButton);
            await Task.Delay(150);
            var subtitleRows = _subtitleMenu!.Items.OfType<System.Windows.Controls.MenuItem>().ToArray();
            Check(subtitleRows.Any(item => item.Tag is SubtitleMode.Embedded) && subtitleRows.Any(item => item.Tag is SubtitleMode.External) &&
                ((System.Windows.Controls.Button)_subtitleMenu.Template.FindName("OpenSubtitleFileButton", _subtitleMenu)).IsVisible && subtitleRows.Count(item => item.IsChecked) == 1,
                "The styled subtitles menu groups embedded/file choices, offers browsing, and marks the current setting");
            Capture("05-subtitles-menu.png");
            subtitleRows.First(item => item.Tag is SubtitleMode mode && mode == SubtitleMode.Off)
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            await _playback.GetSubtitlesAsync();
            Check(!_subtitleMenuOpen && _project.Assets.Single(a => a.Id == captionClip.AssetId).Subtitles == SubtitleMode.Off,
                "Choosing a subtitle row applies the setting and dismisses the menu");
            ToggleFullscreen();
            await ToggleSubtitlesMenuAsync(FullSubtitlesButton);
            await Task.Delay(100);
            Check(_subtitleMenu?.IsOpen == true && FullVideoControls.Opacity == 1,
                "The styled subtitles menu works over fullscreen video and keeps the OSD visible");
            var subtitleEscape = SmokeKey(System.Windows.Input.Key.Escape);
            WindowKeyDown(this, subtitleEscape);
            Check(_full && !_subtitleMenuOpen && subtitleEscape.Handled,
                "Escape dismisses subtitles before leaving fullscreen");
            ToggleFullscreen();
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
            FrameworkElement target = _themeMenu?.IsOpen == true ? _themeMenu : StopConfirmationPopup.IsOpen ? (FrameworkElement)StopConfirmationPopup.Child : _subtitleMenu?.IsOpen == true ? _subtitleMenu : FullTrackPopup.IsOpen ? (FrameworkElement)FullTrackPopup.Child : EmptyTrackPopup.IsOpen ? (FrameworkElement)EmptyTrackPopup.Child : _full || _importing ? VideoOverlay : this;
            double dpi = VisualTreeHelper.GetDpi(target).DpiScaleX;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth * dpi), (int)Math.Ceiling(target.ActualHeight * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
            bitmap.Render(target);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
        }
    }
    private static void SmokeMouseDown(UIElement target) =>
        target.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,
            Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent });
    private System.Windows.Input.KeyEventArgs SmokeKey(System.Windows.Input.Key key) =>
        new(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), Environment.TickCount, key)
        { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
    private static bool TrackButtonBesideSubtitles(System.Windows.Controls.Button tracks, System.Windows.Controls.Button subtitles)
    {
        if (tracks.Parent is not System.Windows.Controls.StackPanel parent || subtitles.Parent != parent) return false;
        var position = tracks.TranslatePoint(new Point(), subtitles);
        return parent.Children.IndexOf(subtitles) == parent.Children.IndexOf(tracks) + 1 &&
            Math.Abs(position.Y) < .5 && position.X + tracks.ActualWidth <= .5;
    }

    private async Task CheckTimelineNavigationAsync(string directory, Action<bool, string> check)
    {
        int originalTrackCount = _project.Tracks.Count;
        double originalScale = Timeline.Scale;
        var originalPanelHeight = Root.RowDefinitions[3].Height;
        try
        {
            Root.RowDefinitions[3].Height = new GridLength(500);
            UpdateProjectUI();
            await SettleLayout();
            check(Timeline.ActualHeight < TimelineScroll.ViewportHeight && LabelsAligned(),
                "Short timelines align labels beneath the ruler when the viewport is taller than the tracks");
            Root.RowDefinitions[3].Height = originalPanelHeight;
            await SettleLayout();
            check(LabelsAligned(), "Resizing the timeline viewport preserves the ruler and track-label alignment");
            while (_project.Tracks.Count < 24) _project.AddAudioTrack();
            UpdateProjectUI();
            Timeline.Scale = 400;
            await SettleLayout();
            check(TrackHeaders.Children.Cast<FrameworkElement>().All(header => Math.Abs(header.ActualHeight - TimelineSurface.TrackHeight) < .5) && LabelsAligned(),
                "Every track label uses the timeline lane height and aligns at the top");

            long position = _playback.PositionMs;
            var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
                { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent };
            Timeline.RaiseEvent(wheel);
            await SettleLayout();
            check(wheel.Handled && TimelineScroll.HorizontalOffset > 0 && TimelineScroll.VerticalOffset == 0 && _playback.PositionMs == position,
                "The routed mouse wheel scrolls along the timeline without moving the playhead");
            ScrollTimelineWheel(120, System.Windows.Input.ModifierKeys.None);
            await SettleLayout();
            check(TimelineScroll.HorizontalOffset == 0, "Reverse wheel scrolling returns toward the timeline start");

            ScrollTimelineWheel(-120, System.Windows.Input.ModifierKeys.Shift);
            await SettleLayout();
            var headerWheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
                { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent };
            TrackHeaders.RaiseEvent(headerWheel);
            await SettleLayout();
            check(headerWheel.Handled && TimelineScroll.VerticalOffset >= 79 && LabelsAligned(),
                "Shift-wheel and wheel over track labels scroll vertically with matching lane positions");
            TimelineScroll.ScrollToBottom();
            await SettleLayout();
            check(TimelineScroll.VerticalOffset > 1000 && Math.Abs(TrackScroll.VerticalOffset - TimelineScroll.VerticalOffset) < .5 && LabelsAligned(),
                "All 24 track labels remain aligned at the bottom with a horizontal scrollbar");
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(directory, "02a-scrolled-tracks.png"))) encoder.Save(output);

            ScrollTimelineWheel(-120, System.Windows.Input.ModifierKeys.Control);
            await SettleLayout();
            check(Timeline.Scale < 400 && Timeline.Scale > 300, "Ctrl-wheel retains timeline zoom");
            if (TimelineScroll.ViewportWidth >= 900)
            {
                Timeline.Scale = .02;
                await SettleLayout();
                TimelineScroll.ScrollToBottom();
                await SettleLayout();
                check(TimelineScroll.ScrollableWidth == 0 && LabelsAligned(),
                    "Track labels stay aligned when the horizontal scrollbar disappears");
            }
        }
        finally
        {
            _project.Tracks.RemoveRange(originalTrackCount, _project.Tracks.Count - originalTrackCount);
            Root.RowDefinitions[3].Height = originalPanelHeight;
            Timeline.Scale = originalScale;
            UpdateProjectUI();
            TimelineScroll.ScrollToHorizontalOffset(0);
            TimelineScroll.ScrollToVerticalOffset(0);
            await SettleLayout();
        }

        bool LabelsAligned()
        {
            var timelineOrigin = Timeline.TranslatePoint(new Point(), TimelinePanel);
            return TrackHeaders.Children.Cast<FrameworkElement>().Select((header, lane) =>
                Math.Abs(header.TranslatePoint(new Point(), TimelinePanel).Y -
                    (timelineOrigin.Y + TimelineSurface.RulerHeight + lane * TimelineSurface.TrackHeight)) < .5).All(aligned => aligned);
        }
        async Task SettleLayout()
        {
            UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            await Task.Delay(50);
            UpdateLayout();
        }
    }
}
