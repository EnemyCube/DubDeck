using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace DubDeck.App;

public partial class MainWindow
{
    private ContextMenu? _themeMenu;
    private bool _themeRefreshQueued;
    private bool _themesDisposed;

    private void InitializeThemes()
    {
        UpdateThemeSelection();
        ThemeManager.Changed += ThemeChanged;
        Closed += (_, _) => DisposeThemes();
    }

    private void ThemeClicked(object sender, RoutedEventArgs e)
    {
        if (_closing || _importing) return;
        if (_themeMenu?.IsOpen == true) { CloseThemeMenu(); return; }
        CloseFloatingControls();
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("ThemeMenu"), PlacementTarget = ThemeButton,
            Placement = PlacementMode.Bottom, VerticalOffset = 8
        };
        foreach (var theme in ThemeManager.Themes)
        {
            var item = new MenuItem { Header = theme.Name, Tag = theme.Id, IsCheckable = true, IsChecked = theme.Id == ThemeManager.Current.Id };
            AutomationProperties.SetName(item, theme.Name + " theme");
            item.Click += (_, _) =>
            {
                CloseThemeMenu();
                ThemeManager.Apply(theme.Id);
                if (ThemeManager.PersistenceWarning is string warning) StatusText.Text = warning;
            };
            menu.Items.Add(item);
        }
        menu.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { CloseThemeMenu(); args.Handled = true; }
            else if (args.Key == Key.F11) { CloseThemeMenu(); ToggleFullscreen(); args.Handled = true; }
        };
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_themeMenu, menu)) _themeMenu = null;
            if (_themeMenu is null && !_closing && IsActive && ThemeButton.IsVisible && Timeline.ContextMenu?.IsOpen != true && !StopConfirmationPopup.IsOpen && !TrackControlsOpen && !_subtitleMenuOpen)
                ThemeButton.Focus();
        };
        _themeMenu = menu;
        menu.IsOpen = true;
    }

    private void UpdateThemeSelection()
    {
        ThemeButton.ToolTip = "Color theme: " + ThemeManager.Current.Name;
        if (_themeMenu is not null)
            foreach (var item in _themeMenu.Items.OfType<MenuItem>())
                item.IsChecked = item.Tag is string id && id == ThemeManager.Current.Id;
    }

    private void ThemeChanged()
    {
        if (_closing || _themesDisposed) return;
        UpdateThemeSelection();
        Timeline.RefreshTheme();
        if (_themeRefreshQueued) return;
        _themeRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _themeRefreshQueued = false;
            if (!_closing && !_themesDisposed) AppIcons.RefreshTheme();
        });
    }

    private void CloseThemeMenu()
    {
        var menu = _themeMenu;
        _themeMenu = null;
        if (menu is not null) menu.IsOpen = false;
    }

    private void DisposeThemes()
    {
        if (_themesDisposed) return;
        _themesDisposed = true;
        ThemeManager.Changed -= ThemeChanged;
        CloseThemeMenu();
    }
}
