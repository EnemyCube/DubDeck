using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DubDeck.App;

public partial class MainWindow
{
    private void StopClicked(object sender, RoutedEventArgs e)
    {
        if (_closing || _importing || sender is not Button button) return;
        bool open = !StopConfirmationPopup.IsOpen;
        CloseFloatingControls();
        if (open) { StopConfirmationPopup.PlacementTarget = button; StopConfirmationPopup.IsOpen = true; }
        TouchOsd();
    }

    private void StopConfirmationOpened(object sender, EventArgs e)
    {
        TouchOsd();
        CancelStopButton.Focus();
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (StopConfirmationPopup.IsOpen && !_closing) CancelStopButton.Focus();
        }));
    }

    private void StopConfirmationClosed(object sender, EventArgs e)
    {
        if (!_closing) TouchOsd();
    }

    private void CloseStopConfirmation()
    {
        bool wasOpen = StopConfirmationPopup.IsOpen;
        StopConfirmationPopup.IsOpen = false;
        if (wasOpen && !_closing && StopConfirmationPopup.PlacementTarget is UIElement target && target.IsVisible) target.Focus();
    }

    private void CancelStopClicked(object sender, RoutedEventArgs e) => CloseStopConfirmation();

    private async void ConfirmStopClicked(object sender, RoutedEventArgs e)
    {
        if (!StopConfirmationPopup.IsOpen || _closing || _importing) return;
        CloseStopConfirmation();
        await SafeAsync(async () => { CancelPendingSeek(); await _playback.StopAsync(); ApplyTrackPositions(); });
    }

    private void StopConfirmationKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CloseStopConfirmation(); e.Handled = true; }
        else if (e.Key != Key.Tab && e.Key != Key.Enter && e.Key != Key.Space && e.Key != Key.System) e.Handled = true;
    }

    private void CloseFloatingControls()
    {
        CloseThemeMenu();
        CloseTrackControls();
        CloseSubtitleMenu();
        CloseStopConfirmation();
    }

    private void UpdateEmptyTimeline() =>
        EmptyTimelineOverlay.Visibility = _project.Clips.Count == 0 && !_importing ? Visibility.Visible : Visibility.Collapsed;
}