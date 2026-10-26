using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DubDeck.App;

public sealed record AppTheme(string Id, string Name);

/// <summary>Shared, mutable UI brushes and an application-wide theme preference.</summary>
public static class ThemeManager
{
    public static IReadOnlyList<AppTheme> Themes { get; } = Array.AsReadOnly(new[]
    {
        new AppTheme("midnight", "Midnight"),
        new AppTheme("midnight-blue", "Midnight Blue"),
        new AppTheme("midnight-violet", "Midnight Violet"),
        new AppTheme("midnight-rose", "Midnight Rose"),
        new AppTheme("midnight-amber", "Midnight Amber"),
        new AppTheme("light", "Light"),
        new AppTheme("dusk", "Dusk"),
        new AppTheme("jaboody-blue", "Jaboody Blue")
    });
    public static AppTheme Current { get; private set; } = Themes[0];
    public static string? PersistenceWarning { get; private set; }
    public static event Action? Changed;

    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ThemeColorSource> ColorSources = new(StringComparer.OrdinalIgnoreCase);
    private static string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DubDeck", "settings.json");
    private static readonly JsonSerializerOptions SettingsJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };

    private enum Role
    {
        Background, Panel, Timeline, Alternate, Card, Popup, Button, IconButton, Hover,
        Selected, SelectedHover, SelectedBorder, Border, BorderStrong, Divider, ScrollThumb,
        Text, SecondaryText, MutedText, SubtleText, Accent, AccentBright, AccentText, SliderThumb,
        Paused, Muted, MutedLabel, Warning, WarningText, Video, VideoBorder, Audio, Offline,
        OfflineBorder, Selection, Playhead, Danger
    }

    // Source colors identify existing UI roles. Their exact values remain the Midnight palette.
    private static readonly Dictionary<string, Role> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["10141D"] = Role.Background, ["191F2B"] = Role.Panel,
        ["141A25"] = Role.Timeline, ["18202D"] = Role.Alternate,
        ["202938"] = Role.Card, ["1B2737"] = Role.Card,
        ["161E2B"] = Role.Popup,
        ["293245"] = Role.Button, ["253244"] = Role.IconButton, ["253245"] = Role.IconButton,
        ["25364B"] = Role.Hover,
        ["204139"] = Role.Selected, ["20463F"] = Role.Selected, ["254D42"] = Role.Selected,
        ["285246"] = Role.SelectedHover, ["456D60"] = Role.SelectedBorder,
        ["3A465C"] = Role.Border, ["354158"] = Role.Border, ["35475D"] = Role.Border,
        ["36475C"] = Role.Border, ["344255"] = Role.Border, ["304157"] = Role.Border,
        ["43556E"] = Role.BorderStrong, ["465A73"] = Role.BorderStrong,
        ["293344"] = Role.Divider, ["2A3343"] = Role.Divider, ["2D3C50"] = Role.Divider,
        ["45536B"] = Role.ScrollThumb,
        ["EBEFF7"] = Role.Text, ["E9F0FA"] = Role.Text, ["E0E9F6"] = Role.Text,
        ["C1CEE0"] = Role.SecondaryText, ["C6D2E5"] = Role.SecondaryText, ["CDDBEE"] = Role.SecondaryText,
        ["98A6BC"] = Role.MutedText, ["93A2B8"] = Role.MutedText, ["9AADC6"] = Role.MutedText,
        ["91A3BC"] = Role.MutedText, ["A3B3C9"] = Role.MutedText,
        ["8194B0"] = Role.SubtleText, ["79879C"] = Role.SubtleText, ["9FB1C9"] = Role.SubtleText,
        ["6CE5C0"] = Role.Accent, ["8DEBCF"] = Role.AccentBright, ["10251E"] = Role.AccentText,
        ["D7F8EF"] = Role.SliderThumb,
        ["435A75"] = Role.Paused, ["825447"] = Role.Muted, ["DD9C92"] = Role.MutedLabel,
        ["F4C67F"] = Role.Warning, ["291F0D"] = Role.WarningText,
        ["24485C"] = Role.Video, ["5DADDF"] = Role.VideoBorder, ["284B49"] = Role.Audio,
        ["5D303A"] = Role.Offline, ["C97582"] = Role.OfflineBorder,
        ["F1FCF8"] = Role.Selection, ["F2C76D"] = Role.Playhead, ["EF7584"] = Role.Danger
    };

    private static readonly Dictionary<Role, (string Light, string Dusk, string JaboodyBlue)> Palettes = new()
    {
        [Role.Background] = ("F3F5F9", "191423", "3D56AA"),
        [Role.Panel] = ("FFFFFF", "251D32", "425BB0"),
        [Role.Timeline] = ("EDF1F7", "1E172B", "354D9E"),
        [Role.Alternate] = ("E4EAF3", "251D35", "3C54A4"),
        [Role.Card] = ("F4F7FB", "30253E", "465FB1"),
        [Role.Popup] = ("FFFFFF", "241C32", "3D56AA"),
        [Role.Button] = ("E2E8F2", "3A2C4C", "20204B"),
        [Role.IconButton] = ("E6EBF4", "352842", "20204B"),
        [Role.Hover] = ("D4DEEC", "47345F", "2E305F"),
        [Role.Selected] = ("D1EEE5", "46315E", "272B5A"),
        [Role.SelectedHover] = ("C5EADD", "50356D", "303666"),
        [Role.SelectedBorder] = ("4D937D", "9671C0", "9DB4FF"),
        [Role.Border] = ("A6B5C9", "645074", "6F86D0"),
        [Role.BorderStrong] = ("8192AA", "83679A", "A7BAFF"),
        [Role.Divider] = ("CDD7E4", "44354F", "6075BE"),
        [Role.ScrollThumb] = ("91A3BB", "745D88", "A4B9F2"),
        [Role.Text] = ("182537", "F4EEFA", "FFFFFF"),
        [Role.SecondaryText] = ("314B66", "DDCDEB", "EDF2FF"),
        [Role.MutedText] = ("4F647C", "BDAECC", "E0E7FF"),
        [Role.SubtleText] = ("5E6F87", "AB97C0", "CAD6F7"),
        [Role.Accent] = ("08765E", "C7A4FF", "91AEFF"),
        [Role.AccentBright] = ("08634F", "DEC4FF", "C7D5FF"),
        [Role.AccentText] = ("FFFFFF", "251638", "141A40"),
        [Role.SliderThumb] = ("C4E5D9", "E8D6FF", "E5ECFF"),
        [Role.Paused] = ("C8D8EE", "604975", "303666"),
        [Role.Muted] = ("F7DCD4", "764B55", "78465D"),
        [Role.MutedLabel] = ("9B4637", "E4A8A5", "F0B8CB"),
        [Role.Warning] = ("96600B", "EFC478", "F1C56F"),
        [Role.WarningText] = ("FFFFFF", "30210A", "2D200C"),
        [Role.Video] = ("D8E8F8", "31385D", "24345F"),
        [Role.VideoBorder] = ("1767B2", "9DA9F4", "89BCFF"),
        [Role.Audio] = ("CBE8DE", "463459", "27274F"),
        [Role.Offline] = ("F8DDE2", "653444", "643447"),
        [Role.OfflineBorder] = ("A52D45", "E690AA", "F59AAF"),
        [Role.Selection] = ("203C6D", "F8F1FF", "FFFFFF"),
        [Role.Playhead] = ("9E5B00", "EFC478", "F1C56F"),
        [Role.Danger] = ("BF2448", "F0839B", "F087A5")
    };

    // Midnight variants override accents and clip tints only. All other source
    // colors, including every dark application surface, stay exactly Midnight.
    private static readonly Dictionary<string, Dictionary<Role, string>> MidnightVariants = new(StringComparer.Ordinal)
    {
        ["midnight-blue"] = new()
        {
            [Role.Accent] = "8DBBFF", [Role.AccentBright] = "BDD8FF", [Role.AccentText] = "151A27",
            [Role.SliderThumb] = "DCEBFF", [Role.Selected] = "20314A", [Role.SelectedHover] = "263B55",
            [Role.SelectedBorder] = "668BC1", [Role.Audio] = "223752"
        },
        ["midnight-violet"] = new()
        {
            [Role.Accent] = "C4A0FF", [Role.AccentBright] = "DCC3FF", [Role.AccentText] = "151A27",
            [Role.SliderThumb] = "ECDEFF", [Role.Selected] = "332A49", [Role.SelectedHover] = "3C3253",
            [Role.SelectedBorder] = "9473BE", [Role.Audio] = "362E49"
        },
        ["midnight-rose"] = new()
        {
            [Role.Accent] = "FF9FBD", [Role.AccentBright] = "FFD0DF", [Role.AccentText] = "151A27",
            [Role.SliderThumb] = "FFE0EA", [Role.Selected] = "402934", [Role.SelectedHover] = "4A303D",
            [Role.SelectedBorder] = "B87590", [Role.Audio] = "3F2B35"
        },
        ["midnight-amber"] = new()
        {
            [Role.Accent] = "F4C16A", [Role.AccentBright] = "FFE1AC", [Role.AccentText] = "151A27",
            [Role.SliderThumb] = "FFEDD2", [Role.Selected] = "3D3223", [Role.SelectedHover] = "453724",
            [Role.SelectedBorder] = "AC8850", [Role.Audio] = "403526", [Role.Playhead] = "FFE7AD"
        }
    };
    private static readonly string[] AlphaColors =
    [
        "EC141A25", "F010141D", "F2161E2B", "FA161E2B",
        "886CE5C0", "88EF7584", "385DADDF", "388DEBCF"
    ];

    public static string Key(string originalHex) => "ThemeColor" + Normalize(originalHex)[1..];

    public static SolidColorBrush Brush(string originalHex)
    {
        string original = Normalize(originalHex);
        if (Brushes.TryGetValue(original, out var brush)) return brush;
        var colorSource = new ThemeColorSource(Resolve(original));
        brush = new SolidColorBrush();
        // WPF may freeze resource brushes automatically. A bound color keeps this
        // shared brush mutable even when styles and cached drawings retain it.
        BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty,
            new Binding(nameof(ThemeColorSource.Color)) { Source = colorSource, Mode = BindingMode.OneWay });
        ColorSources.Add(original, colorSource);
        Brushes.Add(original, brush);
        if (Application.Current is { } application) application.Resources[Key(original)] = brush;
        return brush;
    }

    public static void Initialize(string? settingsPath = null)
    {
        _settingsPath = settingsPath is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DubDeck", "settings.json")
            : Path.GetFullPath(settingsPath);
        string? selected = null;
        try
        {
            if (File.Exists(_settingsPath) && new FileInfo(_settingsPath).Length <= 64 * 1024)
            {
                using var stream = File.OpenRead(_settingsPath);
                selected = JsonSerializer.Deserialize<ThemeSettings>(stream, SettingsJson)?.Theme;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // A missing or malformed preference must never prevent application startup.
        }
        var theme = Themes.FirstOrDefault(candidate => string.Equals(candidate.Id, selected, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];
        Apply(theme.Id, persist: false);
    }

    public static bool Apply(string id, bool persist = true)
    {
        var theme = Themes.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        if (theme is null) return false;
        Application.Current?.Dispatcher.VerifyAccess();
        Current = theme;
        PersistenceWarning = null;
        foreach (string rgb in Roles.Keys) Brush("#" + rgb);
        foreach (string argb in AlphaColors) Brush("#" + argb);
        foreach (var entry in ColorSources) entry.Value.Color = Resolve(entry.Key);
        foreach (var brush in Brushes.Values)
            BindingOperations.GetBindingExpression(brush, SolidColorBrush.ColorProperty)?.UpdateTarget();
        if (Application.Current is { } application)
        {
            // Keep brush identities stable for cached drawings and programmatic controls.
            foreach (var entry in Brushes) application.Resources[Key(entry.Key)] = entry.Value;
            application.Resources["Bg"] = Brush("#10141D");
            application.Resources["Panel"] = Brush("#191F2B");
            application.Resources["Text"] = Brush("#EBEFF7");
            application.Resources["Muted"] = Brush("#98A6BC");
            application.Resources["Accent"] = Brush("#6CE5C0");
        }
        if (persist) SavePreference();
        Changed?.Invoke();
        return true;
    }

    private static Color Resolve(string original)
    {
        var source = Parse(original);
        if (Current.Id == "midnight") return source;
        string rgb = $"{source.R:X2}{source.G:X2}{source.B:X2}";
        if (!Roles.TryGetValue(rgb, out var role)) return source;
        if (MidnightVariants.TryGetValue(Current.Id, out var accents))
        {
            if (!accents.TryGetValue(role, out string? accentColor)) return source;
            var accent = Parse("#" + accentColor);
            accent.A = source.A;
            return accent;
        }
        var pair = Palettes[role];
        string themedColor = Current.Id switch
        {
            "light" => pair.Light,
            "dusk" => pair.Dusk,
            "jaboody-blue" => pair.JaboodyBlue,
            _ => throw new InvalidOperationException("Unrecognized theme palette.")
        };
        var replacement = Parse("#" + themedColor);
        replacement.A = source.A;
        return replacement;
    }

    private static string Normalize(string originalHex)
    {
        ArgumentNullException.ThrowIfNull(originalHex);
        string color = originalHex.Trim().ToUpperInvariant();
        if ((color.Length != 7 && color.Length != 9) || color[0] != '#' ||
            !uint.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException("Expected a #RRGGBB or #AARRGGBB color.", nameof(originalHex));
        return color;
    }

    private static Color Parse(string color)
    {
        uint value = uint.Parse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromArgb(color.Length == 9 ? (byte)(value >> 24) : (byte)255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    private static void SavePreference()
    {
        string? temporaryPath = null;
        try
        {
            string directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new ThemeSettings { Theme = Current.Id }, SettingsJson);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            PersistenceWarning = "The theme is applied, but its preference could not be saved.";
        }
        finally
        {
            if (temporaryPath is not null)
                try { File.Delete(temporaryPath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class ThemeColorSource(Color color) : INotifyPropertyChanged
    {
        private Color _color = color;
        public Color Color
        {
            get => _color;
            set
            {
                if (_color == value) return;
                _color = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private sealed class ThemeSettings
    {
        public ThemeSettings() { }
        public string? Theme { get; set; }
    }
}
