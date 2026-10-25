using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VideoSync.Core;
namespace VideoSync.App;

public partial class MainWindow
{
    private FrameworkElement CreateOsdTrack(TimelineTrack track)
    {
        var grid=new Grid { Height=86 };
        foreach(int height in new[]{30,24,32}) grid.RowDefinitions.Add(new RowDefinition { Height=new GridLength(height) });
        var heading=new DockPanel();
        var pause=new Button { Style=(Style)FindResource("OsdButton"), Width=30, Height=28, Padding=new Thickness(5), Margin=new Thickness(4,0,0,0) };
        DockPanel.SetDock(pause,Dock.Right); heading.Children.Add(pause);
        heading.Children.Add(new TextBlock { Text=track.Name, FontSize=14, FontWeight=FontWeights.SemiBold,
            Foreground=ColorBrush("#E9F0FA"), TextTrimming=TextTrimming.CharacterEllipsis, ToolTip=track.Name });
        grid.Children.Add(heading);
        var details=new DockPanel(); Grid.SetRow(details,1);
        var voice=new CheckBox { Style=(Style)FindResource("IconToggle"), Height=22, ToolTip="Use this track to trigger voice ducking" };
        DockPanel.SetDock(voice,Dock.Right); details.Children.Add(voice);
        var role=new TextBlock { FontSize=11 }; details.Children.Add(role); grid.Children.Add(details);
        var levels=new Grid { Margin=new Thickness(0,3,0,0) }; Grid.SetRow(levels,2);
        levels.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(34) });
        levels.ColumnDefinitions.Add(new ColumnDefinition());
        levels.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(38) });
        var mute=new Button { Style=(Style)FindResource("OsdButton"), Width=30, Height=28, Padding=new Thickness(5), Margin=new Thickness(0) };
        var slider=new Slider { Value=track.Volume, Margin=new Thickness(5,0,8,0), ToolTip=track.Name+" volume" };
        System.Windows.Automation.AutomationProperties.SetName(slider,track.Name+" volume");
        var value=new TextBlock { FontSize=11, Foreground=ColorBrush("#CDDBEE"), TextAlignment=TextAlignment.Right };
        levels.Children.Add(mute); Grid.SetColumn(slider,1); levels.Children.Add(slider); Grid.SetColumn(value,2); levels.Children.Add(value); grid.Children.Add(levels);
        var widgets=new TrackWidgets(slider,mute,value,pause,voice,role);
        if(!_trackWidgets.TryGetValue(track.Id,out var group)) _trackWidgets[track.Id]=group=[];
        group.Add(widgets);
        pause.Click+=(_,_)=>Run(async()=> { TouchOsd(); await _playback.SetTrackPausedAsync(track.Id,!_playback.PausedTrackIds.Contains(track.Id)); SyncPauseControls(); ApplyTrackPositions(); });
        mute.Click+=(_,_)=> { if(_updating)return; track.IsMuted=!track.IsMuted; SyncTrackControls(track); TrackLevelChanged(); };
        voice.Click+=(_,_)=> { if(_updating)return; track.IsDuckingSource=voice.IsChecked==true; SyncTrackControls(track); TrackLevelChanged(); };
        slider.ValueChanged+=(_,e)=> { if(_updating)return; track.Volume=(int)Math.Round(e.NewValue); SyncTrackControls(track); TrackLevelChanged(); };
        SyncTrackControls(track);
        return new Border { Width=304, Margin=new Thickness(6), Padding=new Thickness(12), CornerRadius=new CornerRadius(10),
            Background=ColorBrush("#1B2737"), BorderBrush=ColorBrush("#304157"), BorderThickness=new Thickness(1), Child=grid };
    }
}
