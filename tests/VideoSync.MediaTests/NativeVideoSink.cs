using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace VideoSync.MediaTests;

/// <summary>
/// A real VLC vmem output: VLC decodes into the supplied aligned pixel buffer and
/// calls Display for presented frames. No HWND, desktop automation, or fake clock.
/// The application's separate visible UI smoke covers its native GPU/WPF output.
/// </summary>
internal sealed class NativeVideoSink : IDisposable
{
    private const int Width = 160;
    private const int Height = 90;
    private const int Pitch = Width * 4;
    private readonly IntPtr _allocation = Marshal.AllocHGlobal(Pitch * Height + 31);
    private readonly IntPtr _pixels;
    private readonly MediaPlayer.LibVLCVideoLockCb _lock;
    private readonly MediaPlayer.LibVLCVideoDisplayCb _display;
    private long _frames;
    private long _transitions;
    private int _previousBrightness = -1;
    private readonly byte[] _frameBytes = new byte[Pitch * Height];
    public bool MeasureSubtitlePixels { get; set; }
    private int _whitePixels;
    public int WhitePixels => Volatile.Read(ref _whitePixels);

    public long Frames => Interlocked.Read(ref _frames);
    public long PulseTransitions => Interlocked.Read(ref _transitions);

    public NativeVideoSink()
    {
        _pixels = new IntPtr((_allocation.ToInt64() + 31) & ~31L);
        _lock = Lock;
        _display = Display;
    }

    public void Attach(MediaPlayer player)
    {
        player.EnableHardwareDecoding = false;
        player.SetVideoFormat("RV32", Width, Height, Pitch);
        player.SetVideoCallbacks(_lock, null, _display);
    }

    private IntPtr Lock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, _pixels);
        return IntPtr.Zero;
    }

    private void Display(IntPtr opaque, IntPtr picture)
    {
        // Center B/G/R channel is 0 or 255 in the synthetic black/white pulse video.
        var brightness = Marshal.ReadByte(_pixels, (Height / 2) * Pitch + (Width / 2) * 4);
        var previous = Interlocked.Exchange(ref _previousBrightness, brightness);
        if (previous >= 0 && Math.Abs(previous - brightness) >= 128)
            Interlocked.Increment(ref _transitions);
        if (MeasureSubtitlePixels)
        {
            Marshal.Copy(_pixels, _frameBytes, 0, _frameBytes.Length);
            int count = 0;
            for (int i = 0; i < _frameBytes.Length; i += 4) if (_frameBytes[i] > 128 && _frameBytes[i+1] > 128 && _frameBytes[i+2] > 128) count++;
            Volatile.Write(ref _whitePixels, count);
        }
        Interlocked.Increment(ref _frames);
    }

    // The owning scope disposes PlaybackController first, after native output stops.
    public void Dispose() => Marshal.FreeHGlobal(_allocation);
}
