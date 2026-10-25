using System.Windows;
using System.Windows.Media;
using DubDeck.Core;

namespace DubDeck.App;

public sealed partial class TimelineSurface
{
    private readonly Dictionary<string, AudioWaveform> _waveforms = [];
    public int VisibleWaveformColumns { get; private set; }
    public bool HasWaveform(string assetId) => _waveforms.ContainsKey(assetId);
    public void ClearWaveforms() { _waveforms.Clear(); InvalidateScene(); }
    public void SetWaveform(string assetId, AudioWaveform waveform)
    {
        if (!_assets.ContainsKey(assetId)) return;
        _waveforms[assetId] = waveform;
        InvalidateScene();
    }

    private void DrawWaveform(DrawingContext dc, MediaAsset asset, Rect clip)
    {
        if (_offline.Contains(asset.Id) || !_waveforms.TryGetValue(asset.Id, out var waveform)) return;
        double left = Math.Max(clip.Left, _viewport.Left), right = Math.Min(clip.Right, _viewport.Right);
        double center = clip.Top + clip.Height / 2;
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            for (double x = Math.Ceiling(left / 2) * 2; x < right; x += 2)
            {
                double start = (x - clip.Left) * 1000 / _scale;
                double peak = waveform.PeakBetween(start, start + 2000 / _scale);
                VisibleWaveformColumns++;
                if (peak < .001) continue;
                double amplitude = Math.Sqrt(peak) * (clip.Height / 2 - 3);
                double width = Math.Min(1.3, right - x);
                drawing.BeginFigure(new Point(x, center - amplitude), true, true);
                drawing.LineTo(new Point(x + width, center - amplitude), true, false);
                drawing.LineTo(new Point(x + width, center + amplitude), true, false);
                drawing.LineTo(new Point(x, center + amplitude), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(Brush(asset.HasVideo ? "#385DADDF" : "#388DEBCF"), null, geometry);
    }
}
