using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DubDeck.App;

public partial class MainWindow
{
    private bool CanUseMediaControls => !_closing && !_importing && !StopConfirmationPopup.IsOpen &&
        _project.Clips.Count > 0 && (IsActive || PresentationSource.FromVisual(VideoOverlay)?.RootVisual is Window { IsActive: true });

    private UIElement? _mediaOverlayRoot;
    private readonly HashSet<HwndSource> _mediaSources = [];

    private void AttachNativeMediaControls(Visual target)
    {
        if (PresentationSource.FromVisual(target) is HwndSource source && _mediaSources.Add(source))
            source.AddHook(MediaWindowProc);
    }

    private void DisposeMediaControls()
    {
        foreach (var source in _mediaSources)
            if (!source.IsDisposed) source.RemoveHook(MediaWindowProc);
        _mediaSources.Clear();
    }

    private IntPtr MediaWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Native video focus need not have a WPF command target. Consume supported
        // messages here so DefWindowProc cannot send the same key to another player.
        RoutedCommand? command = message switch
        {
            0x0319 => ((lParam.ToInt64() >> 16) & 0x0FFF) switch // WM_APPCOMMAND
            {
                11 => MediaCommands.NextTrack,
                12 => MediaCommands.PreviousTrack,
                13 => MediaCommands.Stop,
                14 => MediaCommands.TogglePlayPause,
                46 => MediaCommands.Play,
                47 => MediaCommands.Pause,
                49 => MediaCommands.FastForward,
                50 => MediaCommands.Rewind,
                _ => null
            },
            0x0100 => wParam.ToInt32() switch // WM_KEYDOWN
            {
                0xB0 => MediaCommands.NextTrack,
                0xB1 => MediaCommands.PreviousTrack,
                0xB2 => MediaCommands.Stop,
                0xB3 => MediaCommands.TogglePlayPause,
                _ => null
            },
            _ => null
        };
        if (command is null || !CanUseMediaControls) return IntPtr.Zero;
        handled = true;
        // A held key should toggle only once, including on keyboards that repeat it.
        if (message != 0x0100 || (lParam.ToInt64() & (1L << 30)) == 0)
            ExecuteMediaCommand(command);
        return new IntPtr(1);
    }

    private void InitializeMediaControls()
    {
        SourceInitialized += (_, _) => AttachNativeMediaControls(this);
        VideoOverlay.Loaded += (_, _) => AttachMediaOverlayRoot();
        AttachMediaOverlayRoot();
        AddMediaCommandBindings(this);
        AddMediaCommandBindings(VideoOverlay);
        AddMediaCommandBindings(FullTrackPopup.Child);
        AddMediaCommandBindings(EmptyTrackPopup.Child);
    }

    private void AttachMediaOverlayRoot()
    {
        AttachNativeMediaControls(VideoOverlay);
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
        ExecuteMediaCommand(e.Command);
    }

    private void ExecuteMediaCommand(ICommand command)
    {
        TouchOsd();
        if (command == MediaCommands.TogglePlayPause) Run(() => _playback.PlayPauseAsync());
        else if (command == MediaCommands.Play) Run(() => _playback.PlayAsync());
        else if (command == MediaCommands.Pause) Run(() => _playback.PauseAsync());
        else if (command == MediaCommands.NextTrack || command == MediaCommands.FastForward) JumpBy(10000);
        else if (command == MediaCommands.PreviousTrack || command == MediaCommands.Rewind) JumpBy(-10000);
        else if (command == MediaCommands.Stop)
            StopClicked(_full ? (_videoVisible ? FullStopButton : EmptyStopButton) : StopButton, new RoutedEventArgs());
    }
}
