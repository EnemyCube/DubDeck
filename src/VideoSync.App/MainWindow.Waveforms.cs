using System.IO;
using VideoSync.Core;

namespace VideoSync.App;

public partial class MainWindow
{
    private readonly Dictionary<string, string> _waveformRequests = [];
    private readonly Dictionary<string, Task<AudioWaveform?>> _waveformJobs = new(StringComparer.OrdinalIgnoreCase);
    private ProjectDocument? _waveformDocument;
    private int _waveformGeneration;
    private CancellationTokenSource _waveformCancellation = new();

    private void RequestWaveforms(bool refreshSources)
    {
        if (_closing) return;
        if (refreshSources || !ReferenceEquals(_waveformDocument, _project))
        {
            _waveformCancellation.Cancel();
            _waveformCancellation.Dispose();
            _waveformCancellation = new();
            _waveformDocument = _project;
            _waveformGeneration++;
            _waveformRequests.Clear(); _waveformJobs.Clear();
            Timeline.ClearWaveforms();
        }
        var used = _project.Clips.Select(c => c.AssetId).ToHashSet();
        foreach (var asset in _project.Assets.Where(a => a.HasAudio && used.Contains(a.Id) && !Timeline.OfflineAssetIds.Contains(a.Id)))
        {
            string signature = asset.FilePath + "|" + asset.DurationMs;
            if (_waveformRequests.GetValueOrDefault(asset.Id) == signature) continue;
            _waveformRequests[asset.Id] = signature;
            if (!_waveformJobs.TryGetValue(signature, out var job))
                _waveformJobs[signature] = job = _waveforms.GetAsync(asset.FilePath, asset.DurationMs, _waveformCancellation.Token);
            _ = ShowWaveformAsync(_project, asset.Id, signature, _waveformGeneration, job);
        }
    }

    private async Task ShowWaveformAsync(ProjectDocument document, string assetId, string signature, int generation, Task<AudioWaveform?> job)
    {
        try
        {
            var waveform = await job;
            if (!_closing && generation == _waveformGeneration && ReferenceEquals(document, _project) && _waveformRequests.GetValueOrDefault(assetId) == signature && waveform is not null)
                Timeline.SetWaveform(assetId, waveform);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Waveform unavailable: " + ex.Message); }
    }

    private async Task WaitForWaveformsAsync()
    {
        // Used only by desktop regression capture: normal imports never wait for waveforms.
        try { await Task.WhenAll(_waveformJobs.Values).WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (OperationCanceledException) { }
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
    }
}
