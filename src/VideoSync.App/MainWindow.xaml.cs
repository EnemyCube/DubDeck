using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using VideoSync.App.Media;
using VideoSync.Core;

namespace VideoSync.App;

public partial class MainWindow : Window
{
    private readonly MediaEngine _engine;
    private readonly PlaybackController _playback;
    private readonly WaveformService _waveforms;
    private ProjectDocument _project = new();
    private string? _projectPath;
    private bool _dirty, _updating, _seekDragging, _closing, _full, _importing;
    private string? _startupPath;
    private WindowStyle _savedStyle;
    private ResizeMode _savedResize;
    private WindowState _savedState;
    private Rect _savedBounds;
    private bool _videoVisible, _hasOfflineSources, _preparing, _closeReady;
    private int _closeRequests;
    private readonly SemaphoreSlim _editLock = new(1, 1);
    private CancellationTokenSource? _seekCts;
    private PlaybackStatus? _latestStatus;
    private readonly System.Windows.Threading.DispatcherTimer _uiTimer;
    private string _trackHeaderSignature = "";
    public MainWindow(string? startupPath = null)
    {
        InitializeComponent();
        _startupPath = startupPath;
        _engine = new MediaEngine();
        _waveforms = new WaveformService(_engine.LibVLC);
        _playback = new PlaybackController(_engine, enableDiagnostics: App.SmokeTestDirectory is not null || App.PerformanceTestDirectory is not null, simulateVideoStartupStall: App.SimulateVideoStartupStall);
        VideoHost.MediaPlayer = _playback.VideoPlayer;
        _playback.StatusChanged += status => { if (!_closing) Volatile.Write(ref _latestStatus, status); };
        _uiTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        _uiTimer.Tick += (_, _) => { var latest = Interlocked.Exchange(ref _latestStatus, null); if (latest is not null && !_closing) ShowStatus(latest); UpdateOsd(); SyncPauseControls(); ApplyTrackPositions(); };
        _uiTimer.Start();
        _playback.VideoVisibilityChanged += visible => { if (!_closing) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => { if (!_closing) SetVideoVisible(visible); }); };
        Timeline.SeekRequested += position => QueueSeek(position);
        Timeline.EditStarted += () => Run(() => PauseForEditAsync());
        Timeline.ClipMoved += (id, track, start) => Run(() => MoveClipAsync(id, track, start));
        Timeline.SelectionChanged += _ => UpdateContextMenu();
        var context = new ContextMenu { Background = (Brush)FindResource("Panel"), Foreground = Brushes.White };
        var locate = new MenuItem { Header = "Locate / replace source…" };
        locate.Click += (_, _) => Run(RelinkSelectedAsync);
        var delete = new MenuItem { Header = "Delete selected clip", InputGestureText = "Delete" };
        delete.Click += (_, _) => Run(DeleteSelectedAsync);
        context.Items.Add(locate); context.Items.Add(new Separator()); context.Items.Add(delete);
        Timeline.ContextMenu = context;

        foreach (var bar in new[] { SeekBar, FullSeekBar, EmptySeekBar })
            bar.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { _seekDragging = false; QueueSeek((long)bar.Value); }));
        UpdateProjectUI();
    }
    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        await SafeAsync(async () =>
        {
            await SetPlaybackProjectAsync();
            if (_startupPath is not null)
            {
                if (Path.GetExtension(_startupPath).Equals(".videosync", StringComparison.OrdinalIgnoreCase)) await OpenProjectAsync(_startupPath);
                else await ImportFilesAsync(new[] { _startupPath });
            }
        });
        if (App.SmokeTestDirectory is not null) await RunSmokeTestAsync(App.SmokeTestDirectory);
        if (App.PerformanceTestDirectory is not null) await RunPerformanceTestAsync(App.PerformanceTestDirectory);
    }
    private void Run(Func<Task> operation) => _ = SafeAsync(operation);
    private async Task SafeAsync(Func<Task> operation)
    {
        if (_closing) return;
        try { await operation(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (_closing) return; StatusText.Text = ex.Message; MessageBox.Show(this, ex.Message, "Video Sync", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void MarkDirty() { _dirty = true; UpdateTitle(); }
    private void UpdateTitle()
    {
        FullTitle.Text = EmptyTitle.Text = _project.Name;
        FullTitle.ToolTip = EmptyTitle.ToolTip = _project.Name;
        ProjectLabel.Text = _project.Name + (_dirty ? " •" : "");
        Title = $"{_project.Name}{(_dirty ? " *" : "")} — Video Sync";
    }
    private void UpdateProjectUI(bool refreshSources = false)
    {
        ApplyTrackPositions();
        _updating = true;
        Timeline.SetProject(_project, refreshSources);
        TrackHeaders.MinHeight = Math.Max(2, _project.Tracks.Count) * TimelineSurface.TrackHeight;
        Timeline.SetViewport(new Rect(TimelineScroll.HorizontalOffset, TimelineScroll.VerticalOffset, Math.Max(1, TimelineScroll.ViewportWidth), Math.Max(1, TimelineScroll.ViewportHeight)));
        _hasOfflineSources = Timeline.OfflineAssetIds.Count > 0;
        RequestWaveforms(refreshSources);
        string headerSignature = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_project) + ":" + string.Join("|", _project.Tracks.Select(t => t.Id + ":" + t.Name));
        if (_trackHeaderSignature != headerSignature)
        {
        _trackHeaderSignature = headerSignature;
        TrackHeaders.Children.Clear(); FullVideoMixer.Children.Clear(); FullEmptyMixer.Children.Clear(); _trackWidgets.Clear();
        foreach (var track in _project.Tracks)
        {
            TrackHeaders.Children.Add(CreateTrackControls(track, fullscreen: false));
            FullVideoMixer.Children.Add(CreateTrackControls(track, fullscreen: true));
            FullEmptyMixer.Children.Add(CreateTrackControls(track, fullscreen: true));
        }
        }
        MasterVolume.Value = FullVolume.Value = EmptyVolume.Value = _project.MasterVolume;
        MasterValue.Text = FullMasterValue.Text = EmptyMasterValue.Text = _project.MasterVolume + "%";
        SetMuteLabels();
        SyncDuckingControls();
        SeekBar.Maximum = Math.Max(1, TimelineRules.Duration(_project));
        UpdateEmptyTimeline();
        PreviewTitle.Text = _project.Clips.Count == 0 ? "Your timeline starts here" : "No video at this position";
        PreviewHint.Text = _project.Clips.Count == 0 ? "Import video or audio, or drop files into the window" : "Audio continues across video gaps";
        ZoomLabel.Text = Math.Round(Timeline.Scale / 50 * 100) + "%";
        _updating = false;
        SetVideoVisible(_videoVisible && _project.Clips.Count > 0);
        UpdateTitle(); UpdateContextMenu();
    }
    private void UpdateContextMenu()
    {
        if (Timeline.ContextMenu is null) return;
        foreach (var item in Timeline.ContextMenu.Items.OfType<MenuItem>()) item.IsEnabled = Timeline.SelectedId is not null;
    }
    private void ShowStatus(PlaybackStatus status)
    {
        if (_closing) return;
        _preparing = status.IsBusy;
        BlankVideo.Visibility = _videoVisible && !_preparing ? Visibility.Collapsed : Visibility.Visible;
        _updating = true;
        Timeline.Playhead = status.PositionMs;
        foreach (var bar in new[] { SeekBar, FullSeekBar, EmptySeekBar })
        {
            bar.Maximum = Math.Max(1, status.DurationMs);
            if (!_seekDragging) bar.Value = Math.Clamp(status.PositionMs, 0, bar.Maximum);
        }
        TimeLabel.Text = TimelineSurface.Format(status.PositionMs) + " / " + TimelineSurface.Format(status.DurationMs);
        FullTime.Text = EmptyTime.Text = TimelineSurface.Format(status.PositionMs, false) + " / " + TimelineSurface.Format(status.DurationMs, false);
        AppIcons.SetButton(PlayButton, status.IsPlaying ? "pause" : "play", status.IsPlaying ? "Pause · Space" : "Play · Space");
        AppIcons.SetButton(FullPlay, status.IsPlaying ? "pause" : "play", status.IsPlaying ? "Pause · Space" : "Play · Space");
        AppIcons.SetButton(EmptyPlay, status.IsPlaying ? "pause" : "play", status.IsPlaying ? "Pause · Space" : "Play · Space");
        if (status.Error is not null) StatusText.Text = status.Error;
        else if (status.IsBusy) StatusText.Text = "Preparing playback…";
        else if (status.IsPlaying) StatusText.Text = "Playing";
        else if (!_importing) StatusText.Text = _hasOfflineSources ? "Some media is offline · right-click a clip to locate its source" : _project.Clips.Count == 0 ? "Ready · Import files to get started" : "Paused · drag clips to arrange, click the timeline to seek";
        _updating = false;
    }
    private void SetVideoVisible(bool visible)
    {
        _videoVisible = visible;
        BlankVideo.Visibility = visible && !_preparing ? Visibility.Collapsed : Visibility.Visible;
        EmptyPreview.Visibility = visible || _project.Clips.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        FullEmptyControls.Visibility = _full && !visible ? Visibility.Visible : Visibility.Collapsed;
        if(_full) RetargetTrackControls(visible);
        FullVideoControls.Visibility = _full && visible ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        if (_importing || _closing) return;
        var files=paths.ToArray();
        if(files.Length==0) return;
        SetImportBusy(true,files.Length);
        bool ownsLock=false;
        try
        {
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            await _editLock.WaitAsync(); ownsLock=true;
            CancelPendingSeek();
            await PauseForEditAsync();
            if (_closing) return;
            long audioStart = _playback.PositionMs;
            var errors = new List<string>();
            int index=0;
            foreach (var path in files)
            {
                ShowImportFile(++index,files.Length,path);
                if (Directory.Exists(path)) { errors.Add(Path.GetFileName(path) + ": folders cannot be imported."); continue; }
                StatusText.Text = "Reading " + Path.GetFileName(path) + "…";
                try
                {
                    var asset = await _engine.ProbeAsync(path);
                    if (_closing) return;
                    _project.Assets.Add(asset);
                    TimelineTrack track;
                    long start;
                    if (asset.HasVideo)
                    {
                        track = _project.Tracks.Single(t => t.Kind == TrackKind.Video);
                        start = _project.Clips.Where(c => c.TrackId == track.Id).Select(c => TimelineRules.End(_project, c)).DefaultIfEmpty(0).Max();
                    }
                    else
                    {
                        track = _project.AddAudioTrack(); start = audioStart;
                    }
                    _project.Clips.Add(new TimelineClip { AssetId = asset.Id, TrackId = track.Id, StartMs = start });
                    if (asset.HasVideo)
                    {
                        var videoTitle=Path.GetFileNameWithoutExtension(asset.FilePath);
                        if(!string.IsNullOrWhiteSpace(videoTitle)) _project.Name=videoTitle;
                    }
                    MarkDirty(); UpdateProjectUI();
                }
                catch (Exception ex) { errors.Add(Path.GetFileName(path) + ": " + ex.Message); }
            }
            ImportHeading.Text="Preparing timeline…";
            ImportDetail.Text=$"{files.Length} of {files.Length} files checked";
            await SetPlaybackProjectAsync();
            UpdateProjectUI();
            if (errors.Count > 0) MessageBox.Show(this, string.Join("\n\n", errors), "Some files could not be imported", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { if(ownsLock) _editLock.Release(); SetImportBusy(false); }
    }
    private async Task MoveClipAsync(string id, string track, long start)
    {
        await _editLock.WaitAsync();
        try
        {
            CancelPendingSeek();
            await PauseForEditAsync();
            if (_closing) return;
            if (!TimelineRules.TryMove(_project, id, track, start, out var error)) { StatusText.Text = error; return; }
            MarkDirty(); UpdateProjectUI(); await SetPlaybackProjectAsync();
        }
        finally { _editLock.Release(); }
    }
    private async Task DeleteSelectedAsync()
    {
        if (Timeline.SelectedId is not string id) return;
        await _editLock.WaitAsync();
        try
        {
            CancelPendingSeek();
            await PauseForEditAsync();
            if (_closing) return;
            _project.Clips.RemoveAll(c => c.Id == id);
            _project.Assets.RemoveAll(a => !_project.Clips.Any(c => c.AssetId == a.Id));
            MarkDirty(); UpdateProjectUI(); await SetPlaybackProjectAsync();
        }
        finally { _editLock.Release(); }
    }
    private async Task RelinkSelectedAsync()
    {
        var clip = _project.Clips.FirstOrDefault(c => c.Id == Timeline.SelectedId);
        if (clip is null) return;
        var asset = _project.Assets.First(a => a.Id == clip.AssetId);
        var dialog = new OpenFileDialog { Title = "Locate source for " + asset.Name, Filter = MediaFilter, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        await _editLock.WaitAsync();
        try
        {
            CancelPendingSeek();
            await PauseForEditAsync();
            if (_closing) return;
            var replacement = await _engine.ProbeAsync(dialog.FileName);
            if (_closing) return;
            if (replacement.HasVideo != asset.HasVideo) throw new InvalidOperationException("Choose a source of the same type (video or audio).");
            string oldPath = asset.FilePath, oldName = asset.Name; long oldDuration = asset.DurationMs; bool oldAudio = asset.HasAudio;
            asset.FilePath = replacement.FilePath; asset.Name = replacement.Name; asset.DurationMs = replacement.DurationMs; asset.HasAudio = replacement.HasAudio;
            try { TimelineRules.Validate(_project); }
            catch { asset.FilePath = oldPath; asset.Name = oldName; asset.DurationMs = oldDuration; asset.HasAudio = oldAudio; throw new InvalidOperationException("The replacement file would overlap another clip. Move the neighboring clip first or choose the original file."); }
            MarkDirty(); UpdateProjectUI(refreshSources: true); await SetPlaybackProjectAsync();
        }
        finally { _editLock.Release(); }
    }
    private const string MediaFilter = "Media files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v;*.wmv;*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.wma|All files|*.*";
    private async void ImportClicked(object sender, RoutedEventArgs e)
    {
        if (_importing || _closing) return;
        var dialog = new OpenFileDialog { Title = "Import video or audio", Filter = MediaFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) await SafeAsync(() => ImportFilesAsync(dialog.FileNames));
    }
    private void FilesDragOver(object sender, DragEventArgs e) { e.Effects = !_importing && !_closing && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void FilesDropped(object sender, DragEventArgs e)
    {
        if (_importing || _closing) { e.Effects=DragDropEffects.None; e.Handled=true; return; }
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        e.Handled = true;
        await SafeAsync(async () =>
        {
            if (paths.Length == 1 && Path.GetExtension(paths[0]).Equals(".videosync", StringComparison.OrdinalIgnoreCase))
            { if (ConfirmDiscard()) await OpenProjectAsync(paths[0]); }
            else await ImportFilesAsync(paths);
        });
    }
    private async void PlayClicked(object sender, RoutedEventArgs e) => await SafeAsync(() => _playback.PlayPauseAsync());

    private long? _pendingSeek;
    private void CancelPendingSeek() { _seekCts?.Cancel(); _pendingSeek = null; }
    private void JumpBy(long milliseconds) => QueueSeek((_pendingSeek ?? _playback.PositionMs) + milliseconds);
    private void QueueSeek(long position)
    {
        if (_closing) return;
        position = Math.Clamp(position, 0, (long)Math.Max(SeekBar.Maximum, TimelineRules.Duration(_project)));
        CancelPendingSeek(); _seekCts?.Dispose(); _seekCts = new CancellationTokenSource();
        _pendingSeek = position;
        var token = _seekCts.Token;
        Run(async () => { try { await Task.Delay(65, token); await _playback.SeekAsync(position); ApplyTrackPositions(); } finally { if (!token.IsCancellationRequested) _pendingSeek = null; } });
    }
    private void SeekStarted(object sender, MouseButtonEventArgs e) => _seekDragging = true;
    private void SeekFinished(object sender, MouseButtonEventArgs e) { _seekDragging = false; QueueSeek((long)((sender as Slider)?.Value ?? SeekBar.Value)); }
    private void SeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_updating) QueueSeek((long)e.NewValue); }
    private void MasterVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || _playback is null || MasterVolume is null || FullVolume is null || EmptyVolume is null) return;
        TouchOsd();
        _updating = true; _project.MasterVolume = (int)Math.Round(e.NewValue);
        MasterVolume.Value = FullVolume.Value = EmptyVolume.Value = _project.MasterVolume;
        MasterValue.Text = FullMasterValue.Text = EmptyMasterValue.Text = _project.MasterVolume + "%"; _updating = false;
        MarkDirty(); Run(() => _playback.UpdateVolumeAsync(_project));
    }
    private void SetMuteLabels()
    {
        foreach (var button in new[] { MasterMute, FullMute, EmptyMute }) AppIcons.SetButton(button, _project.MasterMuted ? "volume-x" : "volume-2", _project.MasterMuted ? "Unmute master" : "Mute master");
        MasterMute.Background = FullMute.Background = EmptyMute.Background = ColorBrush(_project.MasterMuted ? "#825447" : "#293245");
    }
    private void MasterMuteClicked(object sender, RoutedEventArgs e) { _project.MasterMuted = !_project.MasterMuted; SetMuteLabels(); MarkDirty(); Run(() => _playback.UpdateVolumeAsync(_project)); }
    private void FullscreenClicked(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void ToggleFullscreen()
    {
        CloseFloatingControls();
        _full = !_full;
        TouchOsd();
        if (_full)
        {
            _savedState = WindowState; _savedStyle = WindowStyle; _savedResize = ResizeMode;
            _savedBounds = RestoreBounds;
            WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal; WindowStyle = _savedStyle; ResizeMode = _savedResize;
            Left = _savedBounds.Left; Top = _savedBounds.Top; Width = _savedBounds.Width; Height = _savedBounds.Height; WindowState = _savedState;
        }
        Header.Visibility = Transport.Visibility = TimelinePanel.Visibility = Footer.Visibility = _full ? Visibility.Collapsed : Visibility.Visible;
        Root.RowDefinitions[0].Height = new GridLength(_full ? 0 : 66);
        Root.RowDefinitions[2].Height = new GridLength(_full ? 0 : 82);
        Root.RowDefinitions[3].Height = new GridLength(_full ? 0 : 316);
        Root.RowDefinitions[4].Height = new GridLength(_full ? 0 : 30);
        PreviewBorder.Margin = _full ? new Thickness(0) : new Thickness(18, 14, 18, 0);
        FullVideoControls.Visibility = _full && _videoVisible ? Visibility.Visible : Visibility.Collapsed;
        FullEmptyControls.Visibility = _full && !_videoVisible ? Visibility.Visible : Visibility.Collapsed;
    }
    private void PreviewClicked(object sender, MouseButtonEventArgs e) { if (!_importing && e.ClickCount == 2) { ToggleFullscreen(); e.Handled = true; } }
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        TouchOsd();
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if(e.Key==Key.Escape && StopConfirmationPopup.IsOpen) { CloseStopConfirmation(); e.Handled=true; return; }
        if(e.Key==Key.Escape && _subtitleMenuOpen) { CloseSubtitleMenu(); e.Handled=true; return; }
        if(e.Key==Key.Escape && TrackControlsOpen) { CloseTrackControls(); e.Handled=true; return; }
        if (e.Key == Key.F11 || e.Key == Key.Escape && _full) { ToggleFullscreen(); e.Handled = true; return; }
        if (_importing) { if(e.Key!=Key.System) e.Handled=true; return; }
        if (ctrl)
        {
            if (e.Key == Key.S) { SaveProject((Keyboard.Modifiers & ModifierKeys.Shift) != 0); e.Handled = true; }
            else if (e.Key == Key.O) { OpenClicked(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.N) { NewClicked(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.I) { ImportClicked(this, new RoutedEventArgs()); e.Handled = true; }
            return;
        }
        if (_subtitleMenuOpen || Keyboard.FocusedElement is TextBoxBase) return;
        if (e.Key == Key.Space) { Run(() => _playback.PlayPauseAsync()); e.Handled = true; }
        else if (e.Key == Key.Left || e.Key == Key.Right) { JumpBy(e.Key == Key.Left ? -10000 : 10000); e.Handled = true; }
        else if (e.Key == Key.Home) { QueueSeek(0); e.Handled = true; }
        else if (e.Key == Key.Delete && !_full) { Run(DeleteSelectedAsync); e.Handled = true; }
    }
    private void ZoomInClicked(object sender, RoutedEventArgs e) => Zoom(1.4);
    private void ZoomOutClicked(object sender, RoutedEventArgs e) => Zoom(1 / 1.4);
    private void Zoom(double factor) { Timeline.Scale *= factor; ZoomLabel.Text = Math.Round(Timeline.Scale / 50 * 100) + "%"; }
    private void FitClicked(object sender, RoutedEventArgs e)
    {
        Timeline.Scale = Math.Max(1, TimelineScroll.ViewportWidth - 70) * 1000 / Math.Max(1000, TimelineRules.Duration(_project));
        ZoomLabel.Text = Math.Round(Timeline.Scale / 50 * 100) + "%"; TimelineScroll.ScrollToHorizontalOffset(0);
    }
    private void TimelineScrolled(object sender, ScrollChangedEventArgs e) { TrackScroll.ScrollToVerticalOffset(e.VerticalOffset); Timeline.SetViewport(new Rect(e.HorizontalOffset, e.VerticalOffset, Math.Max(1, e.ViewportWidth), Math.Max(1, e.ViewportHeight))); }
    private void TimelineWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollTimelineWheel(e.Delta, Keyboard.Modifiers);
        e.Handled = true;
    }
    private void TrackHeadersWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollTimelineWheel(e.Delta, Keyboard.Modifiers, overTrackHeaders: true);
        e.Handled = true;
    }
    private void ScrollTimelineWheel(int delta, ModifierKeys modifiers, bool overTrackHeaders = false)
    {
        if (delta == 0) return;
        if ((modifiers & ModifierKeys.Control) != 0) Zoom(delta > 0 ? 1.2 : 1 / 1.2);
        else if (overTrackHeaders || (modifiers & ModifierKeys.Shift) != 0)
            TimelineScroll.ScrollToVerticalOffset(TimelineScroll.VerticalOffset - delta / 3d);
        else
            TimelineScroll.ScrollToHorizontalOffset(TimelineScroll.HorizontalOffset - delta / 3d);
    }
    private bool ConfirmDiscard(bool allowBusy = false)
    {
        if (!allowBusy && (_importing || _editLock.CurrentCount == 0)) { StatusText.Text = "Wait for the current timeline operation to finish."; return false; }
        ApplyTrackPositions();
        if (!_dirty) return true;
        var response = MessageBox.Show(this, "Save changes to " + _project.Name + "?", "Unsaved project", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return response == MessageBoxResult.No || response == MessageBoxResult.Yes && SaveProject(false);
    }
    private bool SaveProject(bool saveAs)
    {
        try
        {
            ApplyTrackPositions();
            string? path = _projectPath;
            if (saveAs || path is null)
            {
                var dialog = new SaveFileDialog { Title = "Save Video Sync project", Filter = "Video Sync project|*.videosync", DefaultExt = ".videosync", FileName = _projectPath is null ? _project.Name + ".videosync" : Path.GetFileName(_projectPath), AddExtension = true };
                if (dialog.ShowDialog(this) != true) return false;
                path = dialog.FileName;
            }
            string oldName = _project.Name;
            if (_project.Name == "Untitled" && !_project.Assets.Any(a => a.HasVideo))
                _project.Name = Path.GetFileNameWithoutExtension(path);
            ApplyTrackPositions();
            _project.PlayheadMs = Math.Clamp(_playback.PositionMs, 0, TimelineRules.Duration(_project));
            try { ProjectStore.Save(_project, path); } catch { _project.Name = oldName; throw; }
            _projectPath = path; _dirty = false; UpdateTitle(); StatusText.Text = "Saved " + path; return true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save project", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
    }
    private void SaveClicked(object sender, RoutedEventArgs e) => SaveProject(false);
    private void SaveAsClicked(object sender, RoutedEventArgs e) => SaveProject(true);
    private async void NewClicked(object sender, RoutedEventArgs e)
    {
        if (ConfirmDiscard()) await SafeAsync(NewProjectAsync);
    }
    private async Task NewProjectAsync()
    {
        await _editLock.WaitAsync();
        try
        {
            CancelPendingSeek(); await PauseForEditAsync();
            if (_closing) return;
            _project = new ProjectDocument(); _projectPath = null; _dirty = false;
            UpdateProjectUI(); await SetPlaybackProjectAsync();
        }
        finally { _editLock.Release(); }
    }
    private async void OpenClicked(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Title = "Open Video Sync project", Filter = "Video Sync project|*.videosync" };
        if (dialog.ShowDialog(this) == true) await SafeAsync(() => OpenProjectAsync(dialog.FileName));
    }
    private async Task OpenProjectAsync(string path)
    {
        var loaded = await Task.Run(() => ProjectStore.Load(path));
        await _editLock.WaitAsync();
        try
        {
            CancelPendingSeek(); await PauseForEditAsync();
            if (_closing) return;
            // Clear the previous transport without reopening its first source: it may
            // have become unavailable. Opening remains paused while the saved playhead position is restored.
            await _playback.SetProjectAsync(new ProjectDocument());
            _project = loaded; _projectPath = Path.GetFullPath(path); _dirty = false;
            UpdateProjectUI(refreshSources: true); await SetPlaybackProjectAsync(_project.PlayheadMs);
        }
        finally { _editLock.Release(); }
    }
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        _closeRequests++;
        if (_closing) { e.Cancel = !_closeReady; return; }
        e.Cancel = true;
        if (!ConfirmDiscard(allowBusy: true)) return;
        CloseFloatingControls();
        _closing = true;
        _uiTimer.Stop();
        CancelPendingSeek();
        IsEnabled = false;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var cleanup = CleanupPlaybackAsync();
        bool timedOut = await Task.WhenAny(cleanup, Task.Delay(5000)) != cleanup;
        bool cleanupFailed = false;
        if (!timedOut)
        {
            try { await cleanup; }
            catch (Exception exception) { cleanupFailed = true; System.Diagnostics.Debug.WriteLine(exception); }
        }
        string? reportDirectory = App.PerformanceTestDirectory ?? App.SmokeTestDirectory;
        if (reportDirectory is not null)
        {
            try
            {
                System.IO.Directory.CreateDirectory(reportDirectory);
                System.IO.File.WriteAllText(System.IO.Path.Combine(reportDirectory, "shutdown.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { timedOut, cleanupFailed, closeRequests = _closeRequests, elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds }));
            }
            catch (System.IO.IOException) { }
        }
        // The project prompt/save has completed. A broken codec must never require
        // Task Manager; this watchdog is only a last resort for an unresponsive DLL.
        if (timedOut || cleanupFailed) { System.Diagnostics.Process.GetCurrentProcess().Kill(); return; }
        _closeReady = true;
        Close();
    }
    private async Task CleanupPlaybackAsync()
    {
        // Keep WPF and the HWND alive while native Stop sends its window messages.
        _waveformCancellation.Cancel();
        var waveformCleanup = _waveforms.DisposeAsync().AsTask();
        await _playback.StopForShutdownAsync();
        VideoHost.MediaPlayer = null;
        await waveformCleanup;
        await Task.Run(() => { _playback.Dispose(); _engine.Dispose(); });
    }
    private static Brush ColorBrush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
