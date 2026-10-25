using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LibVLCSharp.Shared;
using VideoSync.Core;

namespace VideoSync.App.Media;

/// <summary>One silent background decoder at a time; all commands and disposal run off native callbacks.</summary>
public sealed class WaveformService : IAsyncDisposable
{
    private const int CacheMagic = 0x56535731; // VSW1
    private readonly LibVLC _engine;
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed, _decodeCount, _activeDecodes;
    private long _decodedFrames;
    public long DecodedFrames => Interlocked.Read(ref _decodedFrames);
    public int DecodeCount => Volatile.Read(ref _decodeCount);
    public int ActiveDecodes => Volatile.Read(ref _activeDecodes);

    public WaveformService(LibVLC engine, string? cacheDirectory = null)
    {
        _engine = engine;
        _cacheDirectory = cacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoSync", "Waveforms", "v1");
    }

    public Task<AudioWaveform?> GetAsync(string path, long durationMs, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                var file = new FileInfo(path);
                if (!file.Exists || durationMs <= 0) return null;
                long size = file.Length, modified = file.LastWriteTimeUtc.Ticks;
                string identity = string.Join("|", Path.GetFullPath(path).ToUpperInvariant(), size.ToString(CultureInfo.InvariantCulture),
                    modified.ToString(CultureInfo.InvariantCulture), durationMs.ToString(CultureInfo.InvariantCulture));
                string cache = Path.Combine(_cacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".wave");
                var stored = ReadCache(cache, durationMs);
                if (stored is not null) return stored;
                Interlocked.Increment(ref _decodeCount);
                Interlocked.Increment(ref _activeDecodes);
                AudioWaveform waveform;
                try { waveform = await DecodeAsync(path, durationMs, linked.Token).ConfigureAwait(false); }
                finally { Interlocked.Decrement(ref _activeDecodes); }
                file.Refresh();
                if (!file.Exists || size != file.Length || modified != file.LastWriteTimeUtc.Ticks) return null;
                WriteCache(cache, waveform);
                return waveform;
            }
            finally { _gate.Release(); }
        }, cancellationToken);

    private static AudioWaveform? ReadCache(string path, long durationMs)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != CacheMagic || reader.ReadInt64() != durationMs) return null;
            long binMs = reader.ReadInt64();
            int count = reader.ReadInt32();
            if (count is < 1 or > AudioWaveform.MaximumBins || stream.Length != 24L + count * 2L) return null;
            var peaks = new ushort[count];
            for (int i = 0; i < count; i++) peaks[i] = reader.ReadUInt16();
            return new(durationMs, binMs, peaks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private void WriteCache(string path, AudioWaveform waveform)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            using (var stream = File.Create(temporary))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(CacheMagic); writer.Write(waveform.DurationMs);
                writer.Write(waveform.BinDurationMs); writer.Write(waveform.BinCount);
                foreach (ushort peak in waveform.Peaks) writer.Write(peak);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A cache is optional. */ }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    // VLC's smem sink supports unpaced PCM extraction, without touching any audible player.
    // Callback signatures: https://github.com/videolan/vlc/blob/3.0.24/modules/stream_out/smem.c
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AudioPreRender(IntPtr data, ref IntPtr samples, UIntPtr size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AudioPostRender(IntPtr data, IntPtr samples, uint channels, uint rate, uint frames, uint bits, UIntPtr size, long pts);

    private async Task<AudioWaveform> DecodeAsync(string path, long durationMs, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(durationMs / 10000d, 30, 600)));
        var token = timeout.Token;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = new AudioWaveformBuilder(durationMs);
        IntPtr buffer = IntPtr.Zero;
        int capacity = 0;
        short[] samples = new short[8192];
        long? firstPts = null;
        bool received = false;
        AudioPreRender before = (IntPtr _, ref IntPtr output, UIntPtr requested) =>
        {
            output = IntPtr.Zero;
            try
            {
                if (token.IsCancellationRequested || requested.ToUInt64() > 16 * 1024 * 1024) return;
                int length = checked((int)requested.ToUInt64());
                if (length > capacity)
                {
                    if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;
                    capacity = 0;
                    buffer = Marshal.AllocHGlobal(length);
                    capacity = length;
                }
                output = buffer;
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        };
        AudioPostRender after = (_, pcm, channels, rate, frames, bits, size, pts) =>
        {
            try
            {
                if (token.IsCancellationRequested || bits != 16 || channels != 2 || rate != 8000) return;
                int count = checked((int)size.ToUInt64() / 2);
                if (samples.Length < count) samples = new short[count];
                Marshal.Copy(pcm, samples, 0, count);
                firstPts ??= pts;
                builder.AddSamples(samples.AsSpan(0, count), (int)rate, (int)channels, (pts - firstPts.Value) / 1000d);
                received = true;
                Interlocked.Add(ref _decodedFrames, frames);
                // Yield periodically; only this single low-resolution decoder runs in the background.
                Thread.Yield();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        };
        using var player = new MediaPlayer(_engine);
        using var media = new LibVLCSharp.Shared.Media(_engine, path, FromType.FromPath);
        media.AddOption(":no-video"); media.AddOption(":no-spu"); media.AddOption(":no-sout-video");
        media.AddOption(":no-sout-spu"); media.AddOption(":no-sout-all");
        media.AddOption(":avcodec-threads=1");
        long pre = Marshal.GetFunctionPointerForDelegate(before).ToInt64();
        long post = Marshal.GetFunctionPointerForDelegate(after).ToInt64();
        // VLC chain booleans use the no- prefix; time-sync=0 actually enables pacing.
        media.AddOption(FormattableString.Invariant($":sout=#transcode{{acodec=s16l,channels=2,samplerate=8000,threads=1}}:smem{{audio-prerender-callback={pre},audio-postrender-callback={post},no-time-sync}}"));
        player.EndReached += (_, _) => completion.TrySetResult(true);
        player.EncounteredError += (_, _) => completion.TrySetException(new IOException("The audio waveform could not be decoded."));
        try
        {
            token.ThrowIfCancellationRequested();
            if (!player.Play(media)) throw new IOException("The audio waveform could not be opened.");
            await completion.Task.WaitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            // This continuation never runs on a native callback. Stop joins outstanding sample delivery.
            player.Stop();
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            GC.KeepAlive(before); GC.KeepAlive(after);
        }
        if (!received) throw new IOException("No waveform samples were decoded.");
        return builder.Build();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        // Queued requests may still be unwinding their linked cancellation registrations.
        // Keep these small synchronization objects alive until their tasks have exited.
    }
}
