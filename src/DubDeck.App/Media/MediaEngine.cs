using System.IO;
using LibVLCSharp.Shared;
using DubDeck.Core;

namespace DubDeck.App.Media;

/// <summary>Owns the application's single native VLC instance and performs local metadata reads.</summary>
public sealed class MediaEngine : IDisposable
{
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public LibVLC LibVLC { get; }

    public MediaEngine(bool regeneratePluginCache = false)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "libvlc", "win-x64"),
            Path.Combine(baseDirectory, "libvlc"),
            Path.Combine(baseDirectory, "runtimes", "win-x64", "native"),
            baseDirectory
        };
        var nativeDirectory = candidates.FirstOrDefault(directory =>
            File.Exists(Path.Combine(directory, "libvlc.dll")) &&
            File.Exists(Path.Combine(directory, "libvlccore.dll")));
        if (nativeDirectory is null)
            throw new FileNotFoundException("The bundled VLC media libraries are missing. Extract the complete DubDeck application folder and run DubDeck.exe again.");

        LibVLCSharp.Shared.Core.Initialize(nativeDirectory);
        // MMDevice shares a process audio-session gain across players. DirectSound
        // keeps volume and mute on each output buffer, so tracks mix independently.
        var options = new List<string> { "--no-video-title-show", "--no-snapshot-preview", "--no-osd", "--no-metadata-network-access", "--aout=directsound" };
        if (regeneratePluginCache) options.Add("--reset-plugins-cache");
        LibVLC = new LibVLC(options.ToArray());
    }

    public async Task<MediaAsset> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"The media file cannot be found: {Path.GetFileName(fullPath)}", fullPath);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        linked.CancelAfter(TimeSpan.FromSeconds(16));
        try
        {
            await _probeGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                return await Task.Run(async () =>
                {
                    linked.Token.ThrowIfCancellationRequested();
                    using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (stream.Length == 0)
                        throw new InvalidDataException("The file is empty.");
                    using var media = new LibVLCSharp.Shared.Media(LibVLC, fullPath, FromType.FromPath);
                    var result = await media.Parse(MediaParseOptions.ParseLocal, 12000, linked.Token).ConfigureAwait(false);
                    // LibVLCSharp can complete Parse on its native callback thread. Always
                    // dispatch before reading other native properties or releasing this media.
                    await Task.Yield();
                    linked.Token.ThrowIfCancellationRequested();
                    if (result != MediaParsedStatus.Done)
                        throw new InvalidDataException(result == MediaParsedStatus.Timeout
                            ? "Reading the media information timed out. Check that the file is available locally."
                            : "VLC could not read this media file. It may be unsupported or damaged.");
                    var tracks = media.Tracks;
                    var hasVideo = tracks.Any(track => track.TrackType == TrackType.Video);
                    var hasAudio = tracks.Any(track => track.TrackType == TrackType.Audio);
                    if (!hasVideo && !hasAudio)
                        throw new InvalidDataException("No playable video or audio streams were found in this file.");
                    if (media.Duration <= 0)
                        throw new InvalidDataException("The file has no usable duration. Use a complete local video or audio file.");
                    return new MediaAsset
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = Path.GetFileName(fullPath),
                        FilePath = fullPath,
                        DurationMs = media.Duration,
                        HasVideo = hasVideo,
                        HasAudio = hasAudio
                    };
                }, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                _probeGate.Release();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            throw new InvalidDataException($"Reading {Path.GetFileName(fullPath)} timed out. Check that the file is available locally.");
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException($"DubDeck cannot read {Path.GetFileName(fullPath)}. Check the file permissions.", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _probeGate.Wait();
        try { LibVLC.Dispose(); }
        finally { _probeGate.Release(); }
        _lifetime.Dispose();
        _probeGate.Dispose();
    }
}




