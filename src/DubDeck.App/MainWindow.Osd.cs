using System.Windows;
using System.Windows.Controls;
using DubDeck.Core;
namespace DubDeck.App;

public partial class MainWindow
{
    private FrameworkElement CreateOsdTrack(TimelineTrack track)
    {
        var grid = new Grid { Height = 58, Margin = new Thickness(6, 2, 6, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        heading.Children.Add(new TextBlock { Text = track.Name, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = ColorBrush("#E9F0FA"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = track.Name });
        var role = new TextBlock { FontSize = 10, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        heading.Children.Add(role);
        grid.Children.Add(heading);

        var mute = new Button { Style = (Style)FindResource("OsdButton"), Width = 30, Height = 30, Padding = new Thickness(5), Margin = new Thickness(0) };
        var slider = new Slider { Value = track.Volume, Margin = new Thickness(8, 0, 8, 0), ToolTip = track.Name + " volume" };
        System.Windows.Automation.AutomationProperties.SetName(slider, track.Name + " volume");
        var value = new TextBlock { FontSize = 11, Foreground = ColorBrush("#CDDBEE"), TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 8, 0) };
        var pause = new Button { Style = (Style)FindResource("OsdButton"), Width = 30, Height = 30, Padding = new Thickness(5), Margin = new Thickness(0) };
        var voice = new CheckBox { Style = (Style)FindResource("IconToggle"), Height = 30, ToolTip = "Use this track to trigger voice ducking" };
        Grid.SetColumn(mute, 1); grid.Children.Add(mute);
        Grid.SetColumn(slider, 2); grid.Children.Add(slider);
        Grid.SetColumn(value, 3); grid.Children.Add(value);
        Grid.SetColumn(pause, 4); grid.Children.Add(pause);
        Grid.SetColumn(voice, 5); grid.Children.Add(voice);
        var widgets=new TrackWidgets(slider,mute,value,pause,voice,role);
        if(!_trackWidgets.TryGetValue(track.Id,out var group)) _trackWidgets[track.Id]=group=[];
        group.Add(widgets);
        pause.Click+=(_,_)=>Run(async()=> { TouchOsd(); await _playback.SetTrackPausedAsync(track.Id,!_playback.PausedTrackIds.Contains(track.Id)); SyncPauseControls(); ApplyTrackPositions(); });
        mute.Click+=(_,_)=> { if(_updating)return; track.IsMuted=!track.IsMuted; SyncTrackControls(track); TrackLevelChanged(); };
        voice.Click+=(_,_)=> { if(_updating)return; track.IsDuckingSource=voice.IsChecked==true; SyncTrackControls(track); TrackLevelChanged(); };
        slider.ValueChanged+=(_,e)=> { if(_updating)return; track.Volume=(int)Math.Round(e.NewValue); SyncTrackControls(track); TrackLevelChanged(); };
        SyncTrackControls(track);
        return grid;
    }
}
