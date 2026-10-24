using System.IO;
using LibVLCSharp.Shared;
using VideoSync.Core;

namespace VideoSync.App.Media;

public sealed record SubtitleOption(string Key, string Label, SubtitleMode Mode, int EmbeddedIndex, string? FilePath, int NativeId);
public sealed record SubtitleState(string? AssetId, IReadOnlyList<SubtitleOption> Options, int SelectedNativeId, SubtitleMode Mode, string? Warning);

public sealed partial class PlaybackController
{
    private readonly List<SubtitleOption> _embeddedSubtitles = [];
    private readonly Dictionary<string, int> _externalSubtitles = new(StringComparer.OrdinalIgnoreCase);
    private string[] _sidecars = [];
    private string? _subtitleWarning;
    private SubtitleState _subtitleState = new(null, [], -1, SubtitleMode.Auto, null);
    public SubtitleState Subtitles => Volatile.Read(ref _subtitleState);

    public async Task<SubtitleState> GetSubtitlesAsync()
    {
        await Enqueue(_ => { if (_video.Clip is not null) CaptureEmbeddedSubtitles(); RefreshSubtitleState(); return Task.CompletedTask; }).ConfigureAwait(false);
        return Subtitles;
    }

    public Task SetSubtitlesAsync(string assetId, SubtitleMode mode, int embeddedIndex = -1, string? externalPath = null)
    {
        if (mode == SubtitleMode.External)
        {
            if (string.IsNullOrWhiteSpace(externalPath)) throw new ArgumentException("Choose a subtitle file.");
            externalPath = Path.GetFullPath(externalPath);
        }
        string? selectedPath = externalPath;
        return Enqueue(async token =>
        {
            if (!_project.Assets.TryGetValue(assetId, out var asset)) return;
            asset = asset with { Subtitles = mode, SubtitleTrackIndex = embeddedIndex, SubtitleFilePath = selectedPath };
            _project.Assets[assetId] = asset;
            if (_video.Asset?.Id == assetId && _video.Clip is not null)
            {
                // Restart decoding at the common position so an already-consumed long
                // subtitle cue is displayed when a track is enabled mid-cue.
                Freeze();
                _busy = true; Publish();
                _video.Asset = asset;
                await ConfigureSubtitlesAsync(asset, token, newInput: false).ConfigureAwait(false);
                await ReconcileAsync(true, token).ConfigureAwait(false);
            }
        });
    }

    private async Task ConfigureSubtitlesAsync(AssetData asset, CancellationToken token, bool newInput = true)
    {
        _subtitleWarning = null;
        if (newInput)
        {
            _embeddedSubtitles.Clear(); _externalSubtitles.Clear();
            _sidecars = FindSidecars(asset.FilePath);
            CaptureEmbeddedSubtitles();
        }
        switch (asset.Subtitles)
        {
            case SubtitleMode.Off:
                VideoPlayer.SetSpu(-1);
                break;
            case SubtitleMode.Embedded:
                CaptureEmbeddedSubtitles();
                if (asset.SubtitleTrackIndex >= 0 && asset.SubtitleTrackIndex < _embeddedSubtitles.Count)
                    VideoPlayer.SetSpu(_embeddedSubtitles[asset.SubtitleTrackIndex].NativeId);
                else { VideoPlayer.SetSpu(-1); _subtitleWarning = "The saved embedded subtitle track is not available in this movie."; }
                break;
            case SubtitleMode.External:
                await LoadSubtitleFileAsync(asset.SubtitleFilePath, token).ConfigureAwait(false);
                break;
            default:
                if (_sidecars.Length > 0) await LoadSubtitleFileAsync(_sidecars[0], token).ConfigureAwait(false);
                else if (VideoPlayer.Spu < 0 && _embeddedSubtitles.Count > 0) VideoPlayer.SetSpu(_embeddedSubtitles[0].NativeId);
                break;
        }
        RefreshSubtitleState();
    }

    private void CaptureEmbeddedSubtitles()
    {
        var externalIds = _externalSubtitles.Values.ToHashSet();
        foreach (var track in VideoPlayer.SpuDescription ?? [])
        {
            if (track.Id < 0 || externalIds.Contains(track.Id) || _embeddedSubtitles.Any(s => s.NativeId == track.Id)) continue;
            _embeddedSubtitles.Add(new("embedded:" + _embeddedSubtitles.Count, "Embedded · " + track.Name,
                SubtitleMode.Embedded, _embeddedSubtitles.Count, null, track.Id));
        }
    }

    private async Task LoadSubtitleFileAsync(string? path, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            VideoPlayer.SetSpu(-1);
            _subtitleWarning = "The subtitle file is missing. Choose Subtitles → Open subtitle file to locate it.";
            return;
        }
        path = Path.GetFullPath(path);
        if (_externalSubtitles.TryGetValue(path, out int knownId)) { VideoPlayer.SetSpu(knownId); return; }
        var before = (VideoPlayer.SpuDescription ?? []).Select(t => t.Id).ToHashSet();
        if (!VideoPlayer.AddSlave(MediaSlaveType.Subtitle, new Uri(path).AbsoluteUri, true))
        { _subtitleWarning = "VLC could not load this subtitle file."; return; }
        long deadline = System.Diagnostics.Stopwatch.GetTimestamp();
        do
        {
            token.ThrowIfCancellationRequested();
            var added = (VideoPlayer.SpuDescription ?? []).Where(t => t.Id >= 0 && !before.Contains(t.Id)).Take(1).ToArray();
            if (added.Length > 0)
            {
                _externalSubtitles[path] = added[0].Id;
                VideoPlayer.SetSpu(added[0].Id);
                return;
            }
            await Task.Delay(25, token).ConfigureAwait(false);
        } while (System.Diagnostics.Stopwatch.GetElapsedTime(deadline).TotalMilliseconds < 1500);
        _subtitleWarning = "The subtitle file was attached. Resume playback if its track has not appeared yet.";
    }

    private void RefreshSubtitleState()
    {
        if (_video.Clip is null || _video.Asset is null)
        { Volatile.Write(ref _subtitleState, new SubtitleState(null, [], -1, SubtitleMode.Auto, null)); return; }
        var options = new List<SubtitleOption>(_embeddedSubtitles);
        foreach (string path in _sidecars.Concat(_externalSubtitles.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            options.Add(new("file:" + path, "File · " + Path.GetFileName(path), SubtitleMode.External, -1, path, _externalSubtitles.GetValueOrDefault(path, -2)));
        Volatile.Write(ref _subtitleState, new SubtitleState(_video.Asset.Id, options, VideoPlayer.Spu, _video.Asset.Subtitles, _subtitleWarning));
    }

    private static string[] FindSidecars(string moviePath)
    {
        try
        {
            string directory = Path.GetDirectoryName(moviePath)!;
            string basename = Path.GetFileNameWithoutExtension(moviePath);
            string[] extensions = [".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx"];
            return Directory.EnumerateFiles(directory)
                .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Where(path => Path.GetFileNameWithoutExtension(path).Equals(basename, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileNameWithoutExtension(path).StartsWith(basename + ".", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileNameWithoutExtension(path).Equals(basename, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}
