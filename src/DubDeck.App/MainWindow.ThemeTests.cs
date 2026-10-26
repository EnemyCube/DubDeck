using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DubDeck.App;

public partial class MainWindow
{
    private async Task CheckThemesAsync(string directory, Action<bool, string> check, Action<string> capture)
    {
        string originalTheme = ThemeManager.Current.Id;
        bool originalDirty = _dirty;
        string settingsPath = Path.Combine(directory, "theme-settings.json");
        long position = _playback.PositionMs;
        bool playing = _playback.IsPlaying;
        string projectBefore = JsonSerializer.Serialize(_project);
        _dirty = false;
        UpdateTitle();
        try
        {
            foreach (var theme in ThemeManager.Themes)
            {
                string previous = ThemeManager.Current.Id;
                int sceneBuilds = Timeline.SceneBuildCount;
                var mute = _trackWidgets.Values.First()[0].Mute;
                var previousIcon = (mute.Content as Image)?.Source;
                var previousIconColor = (mute.Foreground as SolidColorBrush)?.Color;
                Activate();
                ThemeClicked(ThemeButton, new RoutedEventArgs());
                await Settle();
                var rows = _themeMenu!.Items.OfType<MenuItem>().ToArray();
                check(_themeMenu.IsOpen && rows.Length == ThemeManager.Themes.Count && rows.Count(row => row.IsChecked) == 1,
                    theme.Name + ": theme menu lists all choices and marks exactly one active theme");
                rows.Single(row => row.Tag is string id && id == theme.Id).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Settle();
                check(ThemeManager.Current.Id == theme.Id && _themeMenu is null,
                    theme.Name + ": selecting a theme applies it immediately and closes the menu");
                check(Background is SolidColorBrush windowBrush && windowBrush.Color == ThemeManager.Brush("#10141D").Color &&
                      Header.Background is SolidColorBrush headerBrush && headerBrush.Color == ThemeManager.Brush("#191F2B").Color &&
                      !ThemeManager.Brush("#10141D").IsFrozen,
                    theme.Name + ": window and header use live theme brushes");
                if (theme.Id.StartsWith("midnight-", StringComparison.Ordinal))
                {
                    string[] surfaces = ["#10141D", "#191F2B", "#141A25", "#18202D", "#202938", "#1B2737", "#161E2B", "#293245", "#253244", "#253245", "#25364B"];
                    check(surfaces.All(color => ThemeManager.Brush(color).Color == (Color)ColorConverter.ConvertFromString(color)),
                        theme.Name + ": Midnight dark backgrounds and normal controls are preserved exactly");
                    check(ThemeManager.Brush("#6CE5C0").Color != (Color)ColorConverter.ConvertFromString("#6CE5C0"),
                        theme.Name + ": accent colors differ from the original mint");
                }
                check(Contrast(ThemeManager.Brush("#EBEFF7").Color, ThemeManager.Brush("#191F2B").Color) >= 4.5 &&
                      Contrast(ThemeManager.Brush("#98A6BC").Color, ThemeManager.Brush("#191F2B").Color) >= 4.5,
                    theme.Name + ": normal and muted interface text retain readable contrast");
                check(previous == theme.Id || Timeline.SceneBuildCount > sceneBuilds,
                    theme.Name + ": the cached timeline redraws after a theme change");
                check(previousIconColor == (mute.Foreground as SolidColorBrush)?.Color || !ReferenceEquals(previousIcon, (mute.Content as Image)?.Source),
                    theme.Name + ": existing track icons refresh with their foreground color");
                capture("theme-" + theme.Id + "-workspace.png");
                Activate();
                ThemeClicked(ThemeButton, new RoutedEventArgs());
                await Settle();
                check(_themeMenu!.Items.OfType<MenuItem>().Single(row => row.IsChecked).Tag is string selected && selected == theme.Id,
                    theme.Name + ": reopening the selector marks the selected theme");
                capture("theme-" + theme.Id + "-menu.png");
                var escape = SmokeKey(System.Windows.Input.Key.Escape);
                bool menuOpenBeforeEscape = _themeMenu?.IsOpen == true;
                WindowKeyDown(this, escape);
                check(_themeMenu is null && escape.Handled, theme.Name + $": Escape dismisses theme selection (open={menuOpenBeforeEscape}, handled={escape.Handled})");
                ToggleFullscreen();
                await Settle();
                capture("theme-" + theme.Id + "-fullscreen.png");
                TrackControlsClicked(_videoVisible ? FullTrackButton : EmptyTrackButton, new RoutedEventArgs());
                await Settle();
                check(TrackControlsOpen, theme.Name + ": fullscreen track controls open with the selected theme");
                capture("theme-" + theme.Id + "-mixer.png");
                CloseTrackControls();
                ToggleFullscreen();
                await ToggleSubtitlesMenuAsync(SubtitlesButton);
                await Settle();
                capture("theme-" + theme.Id + "-subtitles.png");
                CloseSubtitleMenu();
                check(!_dirty && _playback.PositionMs == position && _playback.IsPlaying == playing && JsonSerializer.Serialize(_project) == projectBefore,
                    theme.Name + ": changing appearance preserves the project and transport state");
            }
            Activate();
            ThemeClicked(ThemeButton, new RoutedEventArgs());
            await Settle();
            var themeFullscreen = SmokeKey(System.Windows.Input.Key.F11);
            _themeMenu!.RaiseEvent(themeFullscreen);
            check(_full && _themeMenu is null && themeFullscreen.Handled, "F11 closes theme selection and enters fullscreen");
            ToggleFullscreen();
            string persistedTheme = ThemeManager.Themes.LastOrDefault(theme => theme.Id.StartsWith("midnight-", StringComparison.Ordinal))?.Id ?? ThemeManager.Themes.Last().Id;
            ThemeManager.Apply(persistedTheme);
            ThemeManager.Apply("midnight", persist: false);
            ThemeManager.Initialize(settingsPath);
            await Settle();
            check(ThemeManager.Current.Id == persistedTheme, "Saved theme preferences are loaded again on initialization");
            check(!ThemeManager.Apply("unknown-theme") && ThemeManager.Current.Id == persistedTheme, "Unknown theme selections are ignored");
            string invalidSettings = Path.Combine(directory, "theme-invalid.json");
            File.WriteAllText(invalidSettings, "{ invalid json");
            ThemeManager.Initialize(invalidSettings);
            check(ThemeManager.Current.Id == "midnight", "Malformed theme settings fall back to Midnight without preventing startup");
            File.WriteAllText(invalidSettings, "{\"theme\":\"unknown-theme\"}");
            ThemeManager.Initialize(invalidSettings);
            check(ThemeManager.Current.Id == "midnight", "Unknown saved themes fall back to Midnight");
            string unavailableParent = Path.Combine(directory, "theme-settings-blocked-parent");
            File.WriteAllText(unavailableParent, "This file prevents creation of a settings directory.");
            ThemeManager.Initialize(Path.Combine(unavailableParent, "settings.json"));
            ThemeManager.Apply("light");
            check(ThemeManager.Current.Id == "light" && ThemeManager.PersistenceWarning is not null,
                "An unwritable settings destination does not prevent applying a theme");
        }
        finally
        {
            CloseThemeMenu();
            if (_full) ToggleFullscreen();
            ThemeManager.Initialize(settingsPath);
            ThemeManager.Apply(originalTheme);
            await Settle();
            _dirty = originalDirty;
            UpdateTitle();
        }
        async Task Settle()
        {
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Render);
            UpdateLayout();
            await Task.Delay(90);
        }
        static double Contrast(Color a, Color b)
        {
            double first = Luminance(a), second = Luminance(b);
            return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
        }
        static double Luminance(Color color)
        {
            static double Channel(byte value) { double v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
    }
}
