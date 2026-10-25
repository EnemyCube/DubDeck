using DubDeck.Core;
namespace DubDeck.App;

public partial class MainWindow
{
    private long _appliedAlignmentGeneration = -1;
    private readonly Dictionary<string,long> _appliedShifts = [];

    private void ApplyTrackPositions()
    {
        var alignment = _playback.Alignment;
        if (!ReferenceEquals(alignment.Document, _project)) return;
        if (_appliedAlignmentGeneration != alignment.Generation)
        {
            _appliedAlignmentGeneration = alignment.Generation;
            _appliedShifts.Clear();
        }
        bool changed = false;
        foreach (var pair in alignment.Shifts)
        {
            long delta = pair.Value - _appliedShifts.GetValueOrDefault(pair.Key);
            if (delta == 0) continue;
            foreach (var clip in _project.Clips.Where(c => c.TrackId == pair.Key))
            {
                clip.StartMs = checked(clip.StartMs + delta);
                changed = true;
            }
            _appliedShifts[pair.Key] = pair.Value;
        }
        if (changed) { MarkDirty(); Timeline.ResizeSurface(); }
    }
    private async Task PauseForEditAsync()
    {
        await _playback.PauseAsync();
        ApplyTrackPositions();
    }
    private Task SetPlaybackProjectAsync(long? startPositionMs = null)
    {
        ApplyTrackPositions();
        return _playback.SetProjectAsync(_project, Timeline.OfflineAssetIds, startPositionMs);
    }
}
