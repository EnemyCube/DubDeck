using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using VideoSync.Core;

namespace VideoSync.App;

public partial class MainWindow
{
    private sealed record TrackWidgets(Slider Slider, Button Mute, TextBlock Value, Button Pause, CheckBox Voice);
    private readonly Dictionary<string, List<TrackWidgets>> _trackWidgets = [];
    private long _lastOsdInteraction = Stopwatch.GetTimestamp();
    private bool _subtitleMenuOpen;

    private FrameworkElement CreateTrackControls(TimelineTrack track, bool fullscreen)
    {
        var grid = new Grid { Height = TimelineSurface.TrackHeight, Width = fullscreen ? 280 : double.NaN, Margin = new Thickness(12, 0, 10, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });
        var title = new TextBlock { Text = track.Name, FontWeight = FontWeights.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = track.Kind == TrackKind.Video ? "Original movie audio" : track.Name };
        title.MaxWidth = fullscreen ? 140 : 72;
        var heading = new DockPanel { LastChildFill = true };
        var pause = new Button { Content = "Pause", Width = 49, FontSize = 10, Padding = new Thickness(2), Margin = new Thickness(3,0,2,0), ToolTip = "Pause this track; resume at the current timeline position" };
        var voice = new CheckBox { Content = "Voice", FontSize = 10, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Use audible sound on this track to trigger auto-ducking" };
        DockPanel.SetDock(voice, Dock.Right); heading.Children.Add(voice);
        DockPanel.SetDock(pause, Dock.Right); heading.Children.Add(pause); heading.Children.Add(title);
        grid.Children.Add(heading);
        pause.Click += (_, _) => Run(async () => { TouchOsd(); await _playback.SetTrackPausedAsync(track.Id, !_playback.PausedTrackIds.Contains(track.Id)); SyncPauseControls(); });
        voice.Click += (_, _) => { if (_updating) return; track.IsDuckingSource = voice.IsChecked == true; SyncTrackControls(track); TrackLevelChanged(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetRow(row, 1);
        var mute = new Button { Width = 42, FontSize = 11, Padding = new Thickness(4), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Mute " + track.Name };
        var slider = new Slider { Width = fullscreen ? 138 : 105, Value = track.Volume, ToolTip = "Volume: " + track.Name };
        System.Windows.Automation.AutomationProperties.SetName(slider, track.Name + " volume");
        var value = new TextBlock { Width = 38, FontSize = 11, Margin = new Thickness(5, 0, 0, 0), Foreground = ColorBrush("#98A6BC") };
        var widgets = new TrackWidgets(slider, mute, value, pause, voice);
        if (!_trackWidgets.TryGetValue(track.Id, out var group)) _trackWidgets[track.Id] = group = [];
        group.Add(widgets);
        mute.Click += (_, _) => { if (_updating) return; track.IsMuted = !track.IsMuted; SyncTrackControls(track); TrackLevelChanged(); };
        slider.ValueChanged += (_, args) => { if (_updating) return; track.Volume = (int)Math.Round(args.NewValue); SyncTrackControls(track); TrackLevelChanged(); };
        row.Children.Add(mute); row.Children.Add(slider); row.Children.Add(value); grid.Children.Add(row);
        SyncTrackControls(track);
        return new Border { Child = grid, BorderBrush = ColorBrush("#293344"), BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    private void SyncTrackControls(TimelineTrack track)
    {
        bool wasUpdating = _updating; _updating = true;
        foreach (var widgets in _trackWidgets.GetValueOrDefault(track.Id, []))
        {
            widgets.Voice.IsChecked = track.IsDuckingSource;
            widgets.Pause.Content = track.IsPaused ? "Resume" : "Pause";
            widgets.Pause.Background = ColorBrush(track.IsPaused ? "#435A75" : "#293245");
            widgets.Slider.Value = track.Volume;
            widgets.Value.Text = track.Volume + "%";
            widgets.Mute.Content = track.IsMuted ? "Muted" : "Mute";
            widgets.Mute.Background = ColorBrush(track.IsMuted ? "#825447" : "#293245");
        }
        _updating = wasUpdating;
    }

    private readonly List<(CheckBox Enabled, Slider Amount, TextBlock Value)> _duckControls = [];
    private void SyncPauseControls()
    {
        foreach (var track in _project.Tracks)
        {
            bool paused = _playback.PausedTrackIds.Contains(track.Id);
            if (track.IsPaused == paused) continue;
            track.IsPaused = paused; SyncTrackControls(track);
        }
    }
    private void SyncDuckingControls()
    {
        bool previous = _updating; _updating = true;
        if (_duckControls.Count == 0)
        {
            foreach (var host in new[] { DuckControls, FullDuckControls, EmptyDuckControls })
            {
                var enabled = new CheckBox { Content = "Auto duck", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,14,0) };
                var amount = new Slider { Width = 120, Minimum = 0, Maximum = 100, ToolTip = "Reduce non-Voice tracks by this percentage while Voice tracks are audible" };
                var label = new TextBlock { Text = "Duck amount", Margin = new Thickness(0,0,8,0), VerticalAlignment = VerticalAlignment.Center };
                var value = new TextBlock { Width = 42, Margin = new Thickness(7,0,0,0), VerticalAlignment = VerticalAlignment.Center };
                host.Children.Add(enabled); host.Children.Add(label); host.Children.Add(amount); host.Children.Add(value);
                _duckControls.Add((enabled, amount, value));
                enabled.Click += (_, _) => { if (_updating) return; _project.AutoDuckingEnabled = enabled.IsChecked == true; SyncDuckingControls(); TrackLevelChanged(); };
                amount.ValueChanged += (_, e) => { if (_updating) return; _project.DuckAmount = (int)Math.Round(e.NewValue); SyncDuckingControls(); TrackLevelChanged(); };
            }
        }
        foreach (var widgets in _duckControls)
        {
            widgets.Enabled.IsChecked = _project.AutoDuckingEnabled;
            widgets.Amount.Value = _project.DuckAmount;
            widgets.Value.Text = _project.DuckAmount + "%";
        }
        _updating = previous;
    }
    private void TrackLevelChanged()
    {
        TouchOsd(); MarkDirty(); Run(() => _playback.UpdateVolumeAsync(_project));
    }
    private void TouchOsd()
    {
        _lastOsdInteraction = Stopwatch.GetTimestamp();
        SetOsdVisible(true);
    }
    private void SetOsdVisible(bool visible)
    {
        if (FullVideoControls is null || FullEmptyControls is null) return;
        double opacity = visible ? 1 : 0;
        if (FullVideoControls.Opacity == opacity && FullEmptyControls.Opacity == opacity) return;
        FullVideoControls.Opacity = FullEmptyControls.Opacity = opacity;
        FullVideoControls.IsHitTestVisible = FullEmptyControls.IsHitTestVisible = visible;
    }
    private void UpdateOsd()
    {
        if (!_full) return;
        bool keep = !_playback.IsPlaying || FullVideoControls.IsMouseOver || FullEmptyControls.IsMouseOver || _subtitleMenuOpen;
        SetOsdVisible(keep || Stopwatch.GetElapsedTime(_lastOsdInteraction).TotalSeconds < 3);
    }
    private void OverlayMouseMove(object sender, MouseEventArgs e) => TouchOsd();

    private async void SubtitlesClicked(object sender, RoutedEventArgs e)
    {
        TouchOsd();
        await SafeAsync(async () =>
        {
            var state = await _playback.GetSubtitlesAsync();
            var menu = new ContextMenu { Background = ColorBrush("#191F2B"), Foreground = Brushes.White, PlacementTarget = (UIElement)sender };
            _subtitleMenuOpen = true;
            menu.Closed += (_, _) => { _subtitleMenuOpen = false; TouchOsd(); };
            if (state.AssetId is null)
            {
                menu.Items.Add(new MenuItem { Header = "Play or seek into a movie to choose subtitles", IsEnabled = false });
            }
            else
            {
                var asset = _project.Assets.First(a => a.Id == state.AssetId);
                Add("Automatic", SubtitleMode.Auto, -1, null);
                Add("Off", SubtitleMode.Off, -1, null);
                menu.Items.Add(new Separator());
                foreach (var option in state.Options) Add(option.Label, option.Mode, option.EmbeddedIndex, option.FilePath);
                var browse = new MenuItem { Header = "Open subtitle file…" };
                browse.Click += async (_, _) =>
                {
                    var dialog = new OpenFileDialog { Title = "Choose subtitles for " + asset.Name, Filter = "Subtitle files|*.srt;*.ass;*.ssa;*.vtt;*.sub;*.idx|All files|*.*", CheckFileExists = true };
                    if (dialog.ShowDialog(this) == true) await SafeAsync(() => ChooseSubtitlesAsync(asset, SubtitleMode.External, -1, dialog.FileName));
                };
                menu.Items.Add(new Separator()); menu.Items.Add(browse);
                if (state.Warning is not null) menu.Items.Add(new MenuItem { Header = state.Warning, IsEnabled = false });
                void Add(string label, SubtitleMode mode, int index, string? path)
                {
                    var item = new MenuItem { Header = label, IsCheckable = true,
                        IsChecked = asset.Subtitles == mode && (mode != SubtitleMode.Embedded || asset.SubtitleTrackIndex == index) &&
                            (mode != SubtitleMode.External || string.Equals(asset.SubtitleFilePath, path, StringComparison.OrdinalIgnoreCase)) };
                    item.Click += (_, _) => Run(() => ChooseSubtitlesAsync(asset, mode, index, path));
                    menu.Items.Add(item);
                }
            }
            menu.IsOpen = true;
        });
    }
    private async Task ChooseSubtitlesAsync(MediaAsset asset, SubtitleMode mode, int index, string? path)
    {
        await _playback.SetSubtitlesAsync(asset.Id, mode, index, path);
        asset.Subtitles = mode; asset.SubtitleTrackIndex = index; asset.SubtitleFilePath = path;
        MarkDirty(); TouchOsd();
        if (_playback.Subtitles.Warning is string warning) StatusText.Text = warning;
    }
}
