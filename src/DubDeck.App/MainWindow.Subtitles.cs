using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using DubDeck.Core;

namespace DubDeck.App;

public partial class MainWindow
{
    private ContextMenu? _subtitleMenu;
    private int _subtitleMenuGeneration;
    private bool _openingSubtitles;

    private async void SubtitlesClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) await SafeAsync(() => ToggleSubtitlesMenuAsync(button));
    }

    private bool CloseSubtitleMenuAtButton(Button button, Point position)
    {
        if (_subtitleMenu?.IsOpen != true || !ReferenceEquals(_subtitleMenu.PlacementTarget, button) ||
            !button.IsVisible || !new Rect(new Point(), button.RenderSize).Contains(position)) return false;
        // Close before automatic context-menu dismissal can make the click reopen it.
        CloseSubtitleMenu();
        TouchOsd();
        return true;
    }

    private async Task ToggleSubtitlesMenuAsync(Button button)
    {
        if (_closing || _importing || _openingSubtitles) return;
        if (_subtitleMenu?.IsOpen == true) { CloseSubtitleMenu(); return; }
        CloseFloatingControls();
        TouchOsd();
        _openingSubtitles = true;
        int generation = ++_subtitleMenuGeneration;
        try
        {
            var state = await _playback.GetSubtitlesAsync();
            if (_closing || _importing || generation != _subtitleMenuGeneration) return;
            var asset = _project.Assets.FirstOrDefault(a => a.Id == state.AssetId);
            var menu = new ContextMenu
            {
                Style = (Style)FindResource("SubtitlesMenu"), PlacementTarget = button,
                Placement = PlacementMode.Top, VerticalOffset = -10, DataContext = asset, Tag = asset?.Name ?? "No movie at this position"
            };
            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(_subtitleMenu, menu)) { _subtitleMenu = null; _subtitleMenuOpen = false; }
                if (!_closing) { TouchOsd(); if (generation == _subtitleMenuGeneration && button.IsVisible && !StopConfirmationPopup.IsOpen && !TrackControlsOpen) button.Focus(); }
            };
            if (asset is null) Note("Play or seek into a movie to choose subtitles.");
            else
            {
                Add("Automatic", "Use a matching file or subtitles from the movie.", SubtitleMode.Auto, -1, null);
                Add("Off", "Hide subtitles.", SubtitleMode.Off, -1, null);
                var embedded = state.Options.Where(option => option.Mode == SubtitleMode.Embedded).ToArray();
                if (embedded.Length > 0)
                {
                    Section("IN THIS MOVIE");
                    foreach (var option in embedded)
                        Add(WithoutPrefix(option.Label), "Embedded subtitles", option.Mode, option.EmbeddedIndex, null);
                }
                var files = state.Options.Where(option => option.Mode == SubtitleMode.External).ToArray();
                if (files.Length > 0)
                {
                    Section("SUBTITLE FILES");
                    foreach (var option in files)
                        Add(WithoutPrefix(option.Label), "External subtitle file", option.Mode, -1, option.FilePath);
                }
                if (state.Warning is string warning) Note(warning, warning: true);
            }
            AddMediaCommandBindings(menu);
            menu.PreviewKeyDown += SubtitlesMenuKeyDown;
            _subtitleMenu = menu;
            _subtitleMenuOpen = true;
            menu.IsOpen = true;

            void Add(string title, string description, SubtitleMode mode, int index, string? path)
            {
                var item = Row(title, description);
                item.IsCheckable = true;
                item.Tag = mode;
                item.IsChecked = asset!.Subtitles == mode &&
                    (mode != SubtitleMode.Embedded || asset.SubtitleTrackIndex == index) &&
                    (mode != SubtitleMode.External || string.Equals(asset.SubtitleFilePath, path, StringComparison.OrdinalIgnoreCase));
                if (path is not null) item.ToolTip = path;
                item.Click += (_, _) => { CloseSubtitleMenu(); Run(() => ChooseSubtitlesAsync(asset!, mode, index, path)); };
                menu.Items.Add(item);
            }
            MenuItem Row(string title, string description)
            {
                var header = new StackPanel { MaxWidth = 305 };
                header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold,
                    Foreground = ColorBrush("#E9F0FA"), TextTrimming = TextTrimming.CharacterEllipsis });
                header.Children.Add(new TextBlock { Text = description, FontSize = 11,
                    Foreground = ColorBrush("#9AADC6"), Margin = new Thickness(0,4,0,0), TextWrapping = TextWrapping.Wrap });
                var item = new MenuItem { Header = header };
                System.Windows.Automation.AutomationProperties.SetName(item, title);
                return item;
            }
            void Section(string title) => Note(title, heading: true);
            void Note(string text, bool heading = false, bool warning = false)
            {
                menu.Items.Add(new MenuItem { Style = (Style)FindResource("SubtitleNote"), Header = new TextBlock
                {
                    Text = text, FontSize = heading ? 10 : 11, MaxWidth = 335, TextWrapping = TextWrapping.Wrap,
                    FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = ColorBrush(warning ? "#F4C67F" : "#91A3BC"),
                    Margin = heading ? new Thickness(0,5,0,0) : new Thickness(0)
                } });
            }
        }
        finally { _openingSubtitles = false; }
    }

    private async void OpenSubtitleFileClicked(object sender, RoutedEventArgs e)
    {
        if (_closing || _importing || sender is not Button { DataContext: MediaAsset asset }) return;
        CloseSubtitleMenu();
        var dialog = new OpenFileDialog { Title = "Choose subtitles for " + asset.Name,
            Filter = "Subtitle files|*.srt;*.ass;*.ssa;*.vtt;*.sub;*.idx|All files|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
            await SafeAsync(() => ChooseSubtitlesAsync(asset, SubtitleMode.External, -1, dialog.FileName));
    }

    private void SubtitlesMenuKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CloseSubtitleMenu(); TouchOsd(); e.Handled = true; }
        else if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
    }
    private static string WithoutPrefix(string label) => label.Contains('·') ? label[(label.IndexOf('·') + 1)..].Trim() : label;

    private void CloseSubtitleMenu()
    {
        _subtitleMenuGeneration++;
        if (_subtitleMenu is not null) _subtitleMenu.IsOpen = false;
        _subtitleMenu = null;
        _subtitleMenuOpen = false;
    }
}