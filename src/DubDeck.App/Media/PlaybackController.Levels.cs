using System.IO;
using DubDeck.Core;

namespace DubDeck.App.Media;

public sealed partial class PlaybackController
{
    private readonly WaveformService _levelAnalysis;
    private readonly bool _ownsLevelAnalysis;
    private readonly Dictionary<string, AudioLevelProfile> _levelProfiles = new(StringComparer.OrdinalIgnoreCase);
    private bool _levelsPrepared;
    public int LevelAnalysisDecodeCount => _levelAnalysis.DecodeCount;

    private async Task PrepareAudioLevelsAsync(CancellationToken token)
    {
        var used = _project.Clips.Select(clip => clip.AssetId).ToHashSet();
        var assets = new Dictionary<string, AssetData>(_project.Assets);
        foreach (var asset in _project.Assets.Values.Where(a => a.HasAudio && !a.Offline && used.Contains(a.Id)))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var file = new FileInfo(asset.FilePath);
                if (!file.Exists) continue;
                string identity = string.Join("|", file.FullName, file.Length, file.LastWriteTimeUtc.Ticks, asset.DurationMs);
                if (!_levelProfiles.TryGetValue(identity, out var levels))
                {
                    levels = (await _levelAnalysis.GetAsync(asset.FilePath, asset.DurationMs, token).ConfigureAwait(false))?.Levels;
                    if (levels is null) continue;
                    _levelProfiles[identity] = levels;
                }
                assets[asset.Id] = asset with { Levels = levels };
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // Optional analysis can time out without preventing ordinary playback.
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine("Audio levels unavailable: " + exception.Message);
            }
        }
        _project = _project with { Assets = assets };
        _levelsPrepared = true;
    }

    private double LevelGain(PlayerSlot slot) => _project.AutoLevelEnabled ? slot.Asset?.Levels?.Gain ?? 1 : 1;
    private double LevelGainDb(PlayerSlot slot) => 20 * Math.Log10(LevelGain(slot));
}