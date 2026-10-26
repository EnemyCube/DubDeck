using System.Windows;
using System.Windows.Input;

namespace DubDeck.App;

public partial class MainWindow
{
    private bool CanUseMediaControls => !_closing && !_importing && !StopConfirmationPopup.IsOpen &&
        _project.Clips.Count > 0 && (IsActive || PresentationSource.FromVisual(VideoOverlay)?.RootVisual is Window { IsActive: true });

    private UIElement? _mediaOverlayRoot;

    private void InitializeMediaControls()
    {
        VideoOverlay.Loaded += (_, _) => AttachMediaOverlayRoot();
        AttachMediaOverlayRoot();
        AddMediaCommandBindings(this);
        AddMediaCommandBindings(VideoOverlay);
        AddMediaCommandBindings(FullTrackPopup.Child);
        AddMediaCommandBindings(EmptyTrackPopup.Child);
    }

    private void AttachMediaOverlayRoot()
    {
        // LibVLC's fullscreen overlay has its own WPF window. Focus can move to
        // that window itself when a video/audio-only transition hides a button.
        if (PresentationSource.FromVisual(VideoOverlay)?.RootVisual is UIElement root &&
            !ReferenceEquals(root, this) && !ReferenceEquals(root, _mediaOverlayRoot))
        {
            _mediaOverlayRoot = root;
            AddMediaCommandBindings(root);
            root.PreviewKeyDown += WindowKeyDown;
        }
    }

    private void AddMediaCommandBindings(UIElement target)
    {
        foreach (var command in new[] {
            MediaCommands.TogglePlayPause, MediaCommands.Play, MediaCommands.Pause, MediaCommands.Stop,
            MediaCommands.NextTrack, MediaCommands.PreviousTrack, MediaCommands.FastForward, MediaCommands.Rewind })
        {
            target.CommandBindings.Add(new CommandBinding(command, MediaCommandExecuted, (_, args) =>
            {
                args.CanExecute = CanUseMediaControls;
                args.Handled = true;
            }));
        }
        target.InputBindings.Add(new KeyBinding(MediaCommands.TogglePlayPause, Key.MediaPlayPause, ModifierKeys.None));
        target.InputBindings.Add(new KeyBinding(MediaCommands.NextTrack, Key.MediaNextTrack, ModifierKeys.None));
        target.InputBindings.Add(new KeyBinding(MediaCommands.PreviousTrack, Key.MediaPreviousTrack, ModifierKeys.None));
        target.InputBindings.Add(new KeyBinding(MediaCommands.Stop, Key.MediaStop, ModifierKeys.None));
    }

    private void MediaCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        if (!CanUseMediaControls) return;
        TouchOsd();
        if (e.Command == MediaCommands.TogglePlayPause) Run(() => _playback.PlayPauseAsync());
        else if (e.Command == MediaCommands.Play) Run(() => _playback.PlayAsync());
        else if (e.Command == MediaCommands.Pause) Run(() => _playback.PauseAsync());
        else if (e.Command == MediaCommands.NextTrack || e.Command == MediaCommands.FastForward) JumpBy(10000);
        else if (e.Command == MediaCommands.PreviousTrack || e.Command == MediaCommands.Rewind) JumpBy(-10000);
        else if (e.Command == MediaCommands.Stop)
            StopClicked(_full ? (_videoVisible ? FullStopButton : EmptyStopButton) : StopButton, new RoutedEventArgs());
    }
}
