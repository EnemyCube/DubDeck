using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DubDeck.Core;

namespace DubDeck.App;

public partial class MainWindow
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    private async Task CheckMediaControlsAsync(Action<bool, string> check)
    {
        long originalPosition = _playback.PositionMs;
        bool originalPlaying = _playback.IsPlaying;
        try
        {
            Activate();
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Input);
            PlayButton.Focus();
            await _playback.SeekAsync(500);
            check(MediaCommands.TogglePlayPause.CanExecute(null, PlayButton),
                "Focused playback enables the media commands");

            SendMediaAppCommand(14, this);
            await _playback.GetSubtitlesAsync();
            check(_playback.IsPlaying, "The Windows play/pause media command starts playback once");
            PressMediaKey(Key.MediaPlayPause);
            await _playback.GetSubtitlesAsync();
            check(!_playback.IsPlaying, "The keyboard media play/pause key pauses playback once");

            MediaCommands.Play.Execute(null, PlayButton);
            MediaCommands.Play.Execute(null, PlayButton);
            await _playback.GetSubtitlesAsync();
            check(_playback.IsPlaying, "Repeated explicit media Play commands keep playback running");
            MediaCommands.Pause.Execute(null, PlayButton);
            MediaCommands.Pause.Execute(null, PlayButton);
            await _playback.GetSubtitlesAsync();
            check(!_playback.IsPlaying, "Repeated explicit media Pause commands keep playback paused");

            await _playback.SeekAsync(500);
            PressMediaKey(Key.MediaNextTrack);
            await WaitForMediaSeekAsync(Math.Min(10500, TimelineRules.Duration(_project)));
            check(!_playback.IsPlaying, "Media Next skips forward ten seconds and preserves paused playback");
            PressMediaKey(Key.MediaPreviousTrack);
            await WaitForMediaSeekAsync(500);
            check(!_playback.IsPlaying, "Media Previous skips back ten seconds and preserves paused playback");
            PressMediaKey(Key.MediaPreviousTrack);
            await WaitForMediaSeekAsync(0);
            check(_playback.PositionMs == 0, "Media backward skipping clamps at the timeline start");
            await _playback.SeekAsync(Math.Max(0, TimelineRules.Duration(_project) - 500));
            PressMediaKey(Key.MediaNextTrack);
            await WaitForMediaSeekAsync(TimelineRules.Duration(_project));
            check(!_playback.IsPlaying, "Media forward skipping clamps at the timeline end");

            await _playback.SeekAsync(500);
            Activate();
            PlayButton.Focus();
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Input);
            check(MediaCommands.Stop.CanExecute(null, PlayButton), "The focused player enables the media Stop key");
            PressMediaKey(Key.MediaStop);
            await Task.Delay(100);
            check(StopConfirmationPopup.IsOpen && _playback.PositionMs == 500,
                $"The media Stop key uses the existing confirmation without rewinding immediately (open={StopConfirmationPopup.IsOpen}, position={_playback.PositionMs}, focus={Keyboard.FocusedElement}, active={IsActive})");
            CancelStopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.ContextIdle);

            var otherWindow = new Window { Owner = this, Width = 200, Height = 100, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow };
            try
            {
                otherWindow.Show();
                otherWindow.Activate();
                await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Input);
                check(!MediaCommands.TogglePlayPause.CanExecute(null, PlayButton),
                    "Media controls are unavailable when another window has focus");
                MediaCommands.TogglePlayPause.Execute(null, PlayButton);
                await _playback.GetSubtitlesAsync();
                check(!_playback.IsPlaying, "A background media command cannot change playback");
            }
            finally { otherWindow.Close(); }
            Activate();
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Input);

            ToggleFullscreen();
            await Task.Delay(150);
            FullPlay.Focus();
            SendMediaAppCommand(11, VideoOverlay);
            await WaitForMediaSeekAsync(Math.Min(10500, TimelineRules.Duration(_project)));
            check(_full && !_playback.IsPlaying, "The Windows Next media command skips ten seconds in fullscreen");
            PressMediaKey(Key.MediaPlayPause);
            await _playback.GetSubtitlesAsync();
            check(_playback.IsPlaying, "The media play/pause key also works on fullscreen controls");
            PressMediaKey(Key.MediaPlayPause);
            await _playback.GetSubtitlesAsync();
            check(!_playback.IsPlaying, "Fullscreen media pause leaves the playhead paused");
            ToggleFullscreen();
        }
        finally
        {
            if (_full) ToggleFullscreen();
            CloseStopConfirmation();
            await _playback.PauseAsync();
            await _playback.SeekAsync(originalPosition);
            if (originalPlaying) await _playback.PlayAsync();
        }
    }

    private void SendMediaAppCommand(int command, Visual target)
    {
        if (!CanUseMediaControls) throw new InvalidOperationException("Media command test requires the focused player.");
        var source = PresentationSource.FromVisual(target) as HwndSource ??
            throw new InvalidOperationException("The media command target has no native window.");
        SendMessage(source.Handle, 0x0319, source.Handle, new IntPtr(command << 16));
    }

    private void PressMediaKey(Key key) =>
        InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(Keyboard.FocusedElement as Visual ?? this), Environment.TickCount, key)
            { RoutedEvent = Keyboard.KeyDownEvent });

    private async Task WaitForMediaSeekAsync(long expected)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while ((_pendingSeek is not null || Math.Abs(_playback.PositionMs - expected) >= 150) && timer.ElapsedMilliseconds < 5000)
            await Task.Delay(30);
        if (_pendingSeek is not null || Math.Abs(_playback.PositionMs - expected) >= 150)
            throw new InvalidOperationException($"Media seek did not settle at {expected} ms (actual {_playback.PositionMs} ms).");
    }
}
