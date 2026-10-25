using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace VideoSync.App;

public partial class MainWindow
{
    private bool TrackControlsOpen => FullTrackPopup.IsOpen || EmptyTrackPopup.IsOpen;
    private void TrackControlsClicked(object sender,RoutedEventArgs e)
    {
        if(!_full || _importing) return;
        var popup=ReferenceEquals(sender,FullTrackButton) ? FullTrackPopup : EmptyTrackPopup;
        bool open=!popup.IsOpen;
        CloseTrackControls();
        if(open) { popup.PlacementTarget=_videoVisible ? FullVideoControls : FullEmptyControls; popup.HorizontalOffset=24; popup.IsOpen=true; }
        TouchOsd();
    }
    private void CloseTrackControls()
    {
        FullTrackPopup.IsOpen=false; EmptyTrackPopup.IsOpen=false;
    }
    private void CloseTrackControlsClicked(object sender,RoutedEventArgs e) => CloseTrackControls();
    private void TrackPopupOpened(object sender,EventArgs e)
    {
        var popup=(Popup)sender;
        if(popup.Child is Border border) border.Width=Math.Min(720,Math.Max(320,VideoOverlay.ActualWidth-48));
        FullTrackScroll.MaxHeight=EmptyTrackScroll.MaxHeight=Math.Max(120,Math.Min(340,VideoOverlay.ActualHeight-(_videoVisible ? FullVideoControls.ActualHeight : FullEmptyControls.ActualHeight)-200));
        TouchOsd();
        popup.Child?.Focus();
    }
    private void TrackPopupClosed(object sender,EventArgs e)
    {
        if(_closing) return;
        TouchOsd();
    }
    private void TrackPopupKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape) { CloseTrackControls(); TouchOsd(); e.Handled=true; return; }
        WindowKeyDown(sender,e);
    }
    private void RetargetTrackControls(bool videoVisible)
    {
        var target=videoVisible ? FullVideoControls : FullEmptyControls;
        if(FullTrackPopup.IsOpen) FullTrackPopup.PlacementTarget=target;
        if(EmptyTrackPopup.IsOpen) EmptyTrackPopup.PlacementTarget=target;
    }
}
