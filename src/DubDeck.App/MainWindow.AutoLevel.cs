using System.Windows;
using System.Windows.Controls;

namespace DubDeck.App;

public partial class MainWindow
{
    private readonly List<CheckBox> _autoLevelControls = [];
    private void AddAutoLevelControl(Panel host)
    {
        var toggle = new CheckBox
        {
            Content = "Auto level", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            ToolTip = "Match louder passages across video and audio automatically, preserving quiet dialogue and peak headroom."
        };
        toggle.SetResourceReference(Control.ForegroundProperty, "ThemeColorEBEFF7");
        System.Windows.Automation.AutomationProperties.SetName(toggle, "Automatically match audio levels");
        toggle.Click += (_, _) =>
        {
            if (_updating) return;
            _project.AutoLevelEnabled = toggle.IsChecked == true;
            SyncAutoLevelControls();
            TrackLevelChanged();
        };
        _autoLevelControls.Add(toggle);
        host.Children.Add(toggle);
    }
    private void SyncAutoLevelControls()
    {
        foreach (var toggle in _autoLevelControls) toggle.IsChecked = _project.AutoLevelEnabled;
    }
}