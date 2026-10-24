using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VideoSync.Core;

namespace VideoSync.App;

public sealed class TimelineSurface : FrameworkElement
{
    public const double RulerHeight = 30;
    public const double TrackHeight = 70;
    private ProjectDocument _project = new();
    private HashSet<string> _offline = new();
    private readonly Dictionary<string, bool> _availability = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, MediaAsset> _assets = new();
    private Dictionary<string, int> _lanes = new();
    private Dictionary<string, TimelineClip[]> _trackClips = new();
    private readonly DrawingGroup _cachedScene = new();
    private readonly DrawingVisual _overlay = new();
    private bool _sceneDirty = true;
    private Rect _viewport = new(0, 0, 1000, 300);
    public int SceneBuildCount { get; private set; }
    public int VisibleClipCount { get; private set; }
    public int RulerLabelCount { get; private set; }
    public double LastSceneBuildMs { get; private set; }
    public IReadOnlySet<string> OfflineAssetIds => _offline;
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? _overlay : throw new ArgumentOutOfRangeException(nameof(index));
    private double _scale = 50; // pixels per second
    private long _playhead;
    private string? _selected;
    private TimelineClip? _dragClip;
    private Point _dragOrigin;
    private long _ghostStart;
    private string? _ghostTrack;
    private bool _dragging, _seeking;
    private static readonly Dictionary<string, Brush> BrushCache = new();
    private static readonly Brush BackgroundBrush = Brush("#141A25");
    private static readonly Brush MutedBrush = Brush("#93A2B8");
    public event Action<long>? SeekRequested;
    public event Action? EditStarted;
    public event Action<string, string, long>? ClipMoved;
    public event Action<string?>? SelectionChanged;
    public string? SelectedId => _selected;
    public double Scale { get => _scale; set { _scale = Math.Clamp(value, .02, 400); ResizeSurface(); } }
    public long Playhead { get => _playhead; set { if (_playhead == value) return; _playhead = value; DrawOverlay(); } }

    public TimelineSurface()
    {
        AddVisualChild(_overlay);
        Focusable = true;
        ClipToBounds = true;
        MouseLeftButtonDown += Down;
        MouseMove += Move;
        MouseLeftButtonUp += Up;
        MouseRightButtonDown += RightDown;
        LostMouseCapture += (_, _) => { _dragClip = null; _dragging = _seeking = false; DrawOverlay(); };
    }

    public void SetProject(ProjectDocument project, bool refreshSources = false)
    {
        _project = project;
        _assets = project.Assets.ToDictionary(a => a.Id);
        _lanes = project.Tracks.Select((t, i) => (t.Id, i)).ToDictionary(t => t.Id, t => t.i);
        _trackClips = project.Clips.GroupBy(c => c.TrackId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.StartMs).ToArray());
        _offline = new();
        foreach (var asset in project.Assets)
        {
            if (refreshSources || !_availability.TryGetValue(asset.FilePath, out bool online))
                _availability[asset.FilePath] = online = System.IO.File.Exists(asset.FilePath);
            if (!online) _offline.Add(asset.Id);
        }
        if (!_project.Clips.Any(c => c.Id == _selected)) _selected = null;
        ResizeSurface();
    }

    public void Select(string? id) { _selected = id; SelectionChanged?.Invoke(id); InvalidateScene(); }
    public void ResizeSurface()
    {
        Width = Math.Max(900, _project.Clips.Select(c => c.StartMs + _assets[c.AssetId].DurationMs).DefaultIfEmpty(0).Max() / 1000d * _scale + 240);
        Height = RulerHeight + Math.Max(2, _project.Tracks.Count) * TrackHeight;
        InvalidateScene();
    }

    public void SetViewport(Rect viewport)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0 || viewport == _viewport) return;
        _viewport = viewport; InvalidateScene();
    }
    private void InvalidateScene() { _sceneDirty = true; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_sceneDirty)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            using (var scene = _cachedScene.Open()) { scene.PushClip(new RectangleGeometry(_viewport)); DrawScene(scene); scene.Pop(); }
            LastSceneBuildMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            SceneBuildCount++; _sceneDirty = false;
        }
        dc.DrawDrawing(_cachedScene);
        DrawOverlay();
    }
    private void DrawScene(DrawingContext dc)
    {
        VisibleClipCount = RulerLabelCount = 0;
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var pen = new Pen(Brush("#293344"), 1);
        double visibleLeft = _viewport.Left, visibleRight = _viewport.Right;
        var stepSeconds = NiceStep(80 / _scale);
        for (double seconds = Math.Floor(visibleLeft / _scale / stepSeconds) * stepSeconds;
             seconds * _scale < Math.Min(ActualWidth, visibleRight); seconds += stepSeconds)
        {
            double x = seconds * _scale;
            dc.DrawLine(pen, new Point(x, RulerHeight), new Point(x, ActualHeight));
            if (_viewport.Top < RulerHeight) { Text(dc, Format((long)(seconds * 1000), false), x + 5, 5, 10, MutedBrush, 100); RulerLabelCount++; }
        }
        dc.DrawLine(pen, new Point(0, RulerHeight), new Point(ActualWidth, RulerHeight));
        for (int i = Math.Max(0, (int)((_viewport.Top - RulerHeight) / TrackHeight)); i < Math.Min(_project.Tracks.Count, (int)(_viewport.Bottom / TrackHeight) + 1); i++)
        {
            double y = RulerHeight + i * TrackHeight;
            if (i % 2 == 1) dc.DrawRectangle(Brush("#18202D"), null, new Rect(0, y, ActualWidth, TrackHeight));
            dc.DrawLine(pen, new Point(0, y + TrackHeight), new Point(ActualWidth, y + TrackHeight));
        }
        foreach (var clip in _project.Clips)
        {
            int lane = _lanes.GetValueOrDefault(clip.TrackId, -1);
            var asset = _assets.GetValueOrDefault(clip.AssetId);
            if (lane < 0 || asset is null) continue;
            var rect = ClipRect(clip.StartMs, asset.DurationMs, lane);
            if (rect.Right < visibleLeft || rect.Left > visibleRight || rect.Bottom < _viewport.Top || rect.Top > _viewport.Bottom) continue;
            VisibleClipCount++;
            bool selected = _selected == clip.Id;
            Brush fill = _offline.Contains(asset.Id) ? Brush("#5D303A") : asset.HasVideo ? Brush("#24485C") : Brush("#284B49");
            var border = new Pen(selected ? Brush("#F1FCF8") : _offline.Contains(asset.Id) ? Brush("#C97582") : asset.HasVideo ? Brush("#5DADDF") : Brush("#6CE5C0"), selected ? 2 : 1);
            dc.DrawRoundedRectangle(fill, border, rect, 5, 5);
            dc.PushClip(new RectangleGeometry(rect));
            Text(dc, (_offline.Contains(asset.Id) ? "OFFLINE · " : "") + asset.Name, rect.X + 10, rect.Y + 9, 12, Brushes.White, Math.Max(0, rect.Width - 18));
            Text(dc, Format(asset.DurationMs, false), rect.X + 10, rect.Y + 30, 10, MutedBrush, Math.Max(0, rect.Width - 18));
            dc.Pop();
        }
    }

    private void DrawOverlay()
    {
        using var dc = _overlay.RenderOpen();
        dc.PushClip(new RectangleGeometry(_viewport));
        if (_dragging && _dragClip is not null && _ghostTrack is not null)
        {
            var asset = _assets[_dragClip.AssetId];
            int lane = _project.Tracks.FindIndex(t => t.Id == _ghostTrack);
            if (lane >= 0)
            {
                bool valid = CanPreviewPlacement(asset, _ghostTrack, _ghostStart, _dragClip.Id);
                dc.DrawRoundedRectangle(Brush(valid ? "#886CE5C0" : "#88EF7584"), new Pen(Brushes.White, 1), ClipRect(_ghostStart, asset.DurationMs, lane), 5, 5);
            }
        }
        double px = _playhead / 1000d * _scale;
        dc.DrawLine(new Pen(Brush("#F2C76D"), 1.5), new Point(px, 0), new Point(px, ActualHeight));
        var triangle = new StreamGeometry();
        using (var context = triangle.Open()) { context.BeginFigure(new Point(px - 6, 0), true, true); context.LineTo(new Point(px + 6, 0), true, false); context.LineTo(new Point(px, 10), true, false); }
        dc.DrawGeometry(Brush("#F2C76D"), null, triangle);
        dc.Pop();
    }

    private bool CanPreviewPlacement(MediaAsset asset, string trackId, long start, string except)
    {
        if (!_lanes.TryGetValue(trackId, out int lane) || (_project.Tracks[lane].Kind == TrackKind.Video) != asset.HasVideo) return false;
        long end = start + asset.DurationMs;
        foreach (var clip in _trackClips.GetValueOrDefault(trackId, []))
        {
            if (clip.StartMs >= end) break;
            if (clip.Id != except && clip.StartMs + _assets[clip.AssetId].DurationMs > start) return false;
        }
        return true;
    }
    internal void PreviewDrag(string clipId, long startMs, string trackId)
    {
        _dragClip = _project.Clips.First(c => c.Id == clipId);
        _dragging = true; _ghostStart = startMs; _ghostTrack = trackId; DrawOverlay();
    }
    internal void CancelPreviewDrag() { _dragClip = null; _dragging = false; DrawOverlay(); }

    private void Down(object sender, MouseButtonEventArgs e)
    {
        Focus(); var p = e.GetPosition(this);
        var clip = HitClip(p);
        if (clip is not null)
        {
            Select(clip.Id); _dragClip = clip; _dragOrigin = p;
            _ghostStart = clip.StartMs; _ghostTrack = clip.TrackId;
        }
        else { Select(null); _seeking = true; SeekRequested?.Invoke(ToMs(p.X)); }
        CaptureMouse(); e.Handled = true;
    }
    private void Move(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_seeking && IsMouseCaptured) { SeekRequested?.Invoke(ToMs(p.X)); return; }
        if (_dragClip is not null && IsMouseCaptured)
        {
            if (!_dragging && (p - _dragOrigin).Length > 4) { _dragging = true; EditStarted?.Invoke(); }
            if (_dragging)
            {
                _ghostStart = Math.Max(0, _dragClip.StartMs + (long)((p.X - _dragOrigin.X) * 1000 / _scale));
                int lane = Math.Clamp((int)((p.Y - RulerHeight) / TrackHeight), 0, _project.Tracks.Count - 1);
                _ghostTrack = _project.Tracks[lane].Id;
                if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0)
                {
                    var edges = _trackClips.GetValueOrDefault(_ghostTrack, []).Where(c => c.Id != _dragClip.Id)
                        .SelectMany(c => new[] { c.StartMs, (c.StartMs + _assets[c.AssetId].DurationMs) }).Append(0).Append(_playhead);
                    long duration = _assets[_dragClip.AssetId].DurationMs;
                    double threshold = 9 * 1000 / _scale;
                    long best = _ghostStart; double distance = threshold;
                    foreach (long edge in edges)
                        foreach (long candidate in new[] { edge, edge - duration })
                            if (candidate >= 0 && Math.Abs((double)candidate - _ghostStart) < distance)
                            { best = candidate; distance = Math.Abs((double)candidate - _ghostStart); }
                    _ghostStart = best;
                }
                DrawOverlay();
            }
            return;
        }
        var hover = HitClip(p);
        ToolTip = hover is null ? "Click to seek · drag clips to move · Alt disables snapping" :
            _assets[hover.AssetId].FilePath + "\nRight-click to locate a missing source or delete the clip";
        Cursor = hover is null ? Cursors.Cross : Cursors.SizeAll;
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        var clip = _dragClip; bool moved = _dragging; var start = _ghostStart; var track = _ghostTrack;
        _dragClip = null; _dragging = _seeking = false; ReleaseMouseCapture();
        if (moved && clip is not null && track is not null) ClipMoved?.Invoke(clip.Id, track, start);
        DrawOverlay(); e.Handled = true;
    }
    private void RightDown(object sender, MouseButtonEventArgs e) => Select(HitClip(e.GetPosition(this))?.Id);
    private TimelineClip? HitClip(Point p)
    {
        int lane = (int)Math.Floor((p.Y - RulerHeight) / TrackHeight);
        if (lane < 0 || lane >= _project.Tracks.Count) return null;
        long ms = ToMs(p.X);
        return _trackClips.GetValueOrDefault(_project.Tracks[lane].Id, []).LastOrDefault(c => c.StartMs <= ms && (c.StartMs + _assets[c.AssetId].DurationMs) > ms);
    }
    private long ToMs(double x) => Math.Max(0, (long)(x * 1000 / _scale));
    private Rect ClipRect(long start, long duration, int lane) => new(start / 1000d * _scale, RulerHeight + lane * TrackHeight + 7, Math.Max(3, duration / 1000d * _scale), TrackHeight - 14);
    private void Text(DrawingContext dc, string text, double x, double y, double size, Brush color, double width)
    {
        if (width < 2) return;
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = width, MaxTextHeight = size * 1.5, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(formatted, new Point(x, y));
    }
    private static double NiceStep(double minimum)
    {
        foreach (double value in new double[] { .1, .2, .5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600, 7200, 14400, 86400 }) if (value >= minimum) return value;
        return Math.Ceiling(minimum / 86400) * 86400;
    }

    private static Brush Brush(string color) { if (BrushCache.TryGetValue(color, out var cached)) return cached; var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return BrushCache[color] = b; }
    public static string Format(long ms, bool precise = true)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        string value = t.TotalHours >= 1 ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        return precise ? value + $".{t.Milliseconds:000}" : value;
    }
}
