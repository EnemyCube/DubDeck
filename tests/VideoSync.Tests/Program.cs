using System.Text.Json.Nodes;
using VideoSync.Core;

var tests = new (string Name, Action Run)[]
{
    ("Empty projects have zero duration", EmptyProject),
    ("Active scheduling uses exclusive ends and includes audio past video", ActiveScheduling),
    ("Video gaps have no active video", VideoGaps),
    ("Moves reject overlap without changing clips", Moves),
    ("Placement enforces lane types and valid times", Placement),
    ("Volume combines master and track controls", Volume),
    ("Validation rejects malformed timeline data", Validation),
    ("Projects round-trip and replace existing saves", RoundTrip),
    ("Relative media follows moved project folders", PortablePaths),
    ("Absolute fallback finds media after moving only the project", AbsoluteFallback),
    ("Offline entries survive loading and can be relinked", OfflineRelinking),
    ("Invalid project files fail with useful errors", InvalidFiles)
};
var failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {name}\n{exception}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} core scenarios passed.");
return failures == 0 ? 0 : 1;

static ProjectDocument Fixture(string? sourceFolder = null)
{
    var project = new ProjectDocument();
    project.Assets.Add(new MediaAsset
    {
        Id = "movie", Name = "Picture.mp4", DurationMs = 1000,
        FilePath = Path.Combine(sourceFolder ?? Path.GetTempPath(), "Picture.mp4"), HasVideo = true, HasAudio = true
    });
    project.Assets.Add(new MediaAsset
    {
        Id = "music", Name = "Music.wav", DurationMs = 2000,
        FilePath = Path.Combine(sourceFolder ?? Path.GetTempPath(), "Music.wav"), HasAudio = true
    });
    project.Tracks.Add(new TimelineTrack { Id = "audio1", Name = "Audio 1", Kind = TrackKind.Audio });
    project.Tracks.Add(new TimelineTrack { Id = "audio2", Name = "Audio 2", Kind = TrackKind.Audio });
    project.Clips.Add(new TimelineClip { Id = "v1", AssetId = "movie", TrackId = "video", StartMs = 0 });
    project.Clips.Add(new TimelineClip { Id = "v2", AssetId = "movie", TrackId = "video", StartMs = 1000 });
    project.Clips.Add(new TimelineClip { Id = "a1", AssetId = "music", TrackId = "audio1", StartMs = 500 });
    return project;
}

static void EmptyProject()
{
    var project = new ProjectDocument();
    TimelineRules.Validate(project);
    Equal(0L, TimelineRules.Duration(project));
    Equal(0, TimelineRules.ActiveClips(project, 0).Count());
    Equal(0, TimelineRules.ActiveClips(project, -1).Count());
}

static void ActiveScheduling()
{
    var project = Fixture();
    TimelineRules.Validate(project);
    Equal(2500L, TimelineRules.Duration(project));
    Equal(2000L, TimelineRules.End(project, project.Clips[1]));
    Active(project, 0, "v1");
    Active(project, 499, "v1");
    Active(project, 500, "a1", "v1");
    Active(project, 999, "a1", "v1");
    Active(project, 1000, "a1", "v2");
    Active(project, 2000, "a1");
    Active(project, 2499, "a1");
    Active(project, 2500);
    project.Clips.Add(new TimelineClip { Id = "a2", AssetId = "music", TrackId = "audio2", StartMs = 500 });
    TimelineRules.Validate(project);
    Active(project, 1000, "a1", "a2", "v2");
}

static void VideoGaps()
{
    var project = Fixture();
    True(TimelineRules.TryMove(project, "v2", "video", 1500, out var error), error);
    Active(project, 1000, "a1");
    Active(project, 1499, "a1");
    Active(project, 1500, "a1", "v2");
    Equal(2500L, TimelineRules.Duration(project));
}

static void Moves()
{
    var project = Fixture();
    False(TimelineRules.TryMove(project, "v2", "video", 999, out var error));
    True(error?.Contains("overlap", StringComparison.OrdinalIgnoreCase) == true);
    Equal(1000L, project.Clips[1].StartMs);
    True(TimelineRules.TryMove(project, "v2", "video", 1000, out error), error);
    True(TimelineRules.TryMove(project, "a1", "audio2", 0, out error), error);
    Equal("audio2", project.Clips[2].TrackId);
    Equal(0L, project.Clips[2].StartMs);
    project.Clips.Add(new TimelineClip { Id = "a2", AssetId = "music", TrackId = "audio2", StartMs = 2000 });
    False(TimelineRules.TryMove(project, "a1", "audio2", 1, out error));
    Equal(0L, project.Clips[2].StartMs);
    TimelineRules.Validate(project);
}

static void Placement()
{
    var project = Fixture();
    False(TimelineRules.CanPlace(project, "music", "video", 3000, null, out _));
    False(TimelineRules.CanPlace(project, "movie", "audio2", 0, null, out _));
    True(TimelineRules.CanPlace(project, "music", "audio2", 500, null, out _));
    False(TimelineRules.TryMove(project, "a1", "audio1", -1, out _));
    False(TimelineRules.TryMove(project, "a1", "audio1", long.MaxValue, out _));
    False(TimelineRules.TryMove(project, "a1", "unknown", 0, out _));
    False(TimelineRules.TryMove(project, "unknown", "audio1", 0, out _));
    False(TimelineRules.CanPlace(project, "unknown", "audio2", 0, null, out _));
    project.Assets[1].DurationMs = 0;
    False(TimelineRules.CanPlace(project, "music", "audio2", 0, null, out _));
}

static void Volume()
{
    var project = Fixture();
    var track = project.Tracks[1];
    Equal(100, TimelineRules.EffectiveVolume(project, track));
    project.MasterVolume = 50;
    track.Volume = 55;
    Equal(28, TimelineRules.EffectiveVolume(project, track));
    track.IsMuted = true;
    Equal(0, TimelineRules.EffectiveVolume(project, track));
    track.IsMuted = false;
    project.MasterMuted = true;
    Equal(0, TimelineRules.EffectiveVolume(project, track));
    project.MasterMuted = false;
    track.Volume = 0;
    Equal(0, TimelineRules.EffectiveVolume(project, track));
    project.MasterVolume = 1000;
    track.Volume = 1000;
    Equal(100, TimelineRules.EffectiveVolume(project, track));
    track.Volume = -1;
    Equal(0, TimelineRules.EffectiveVolume(project, track));
}

static void Validation()
{
    var mutations = new Action<ProjectDocument>[]
    {
        p => p.Version = 2,
        p => p.Name = "",
        p => p.MasterVolume = -1,
        p => p.MasterVolume = 101,
        p => p.Assets[0].DurationMs = 0,
        p => p.Assets[0].FilePath = "relative.mp4",
        p => p.Assets[0].HasAudio = p.Assets[0].HasVideo = false,
        p => p.Assets[1].Id = p.Assets[0].Id,
        p => p.Tracks[1].Volume = 101,
        p => p.Tracks[1].Kind = (TrackKind)10,
        p => p.Tracks[1].Id = p.Tracks[0].Id,
        p => p.Tracks.RemoveAt(0),
        p => p.Tracks.Add(new TimelineTrack { Name = "Extra video", Kind = TrackKind.Video }),
        p => p.Clips[0].AssetId = "unknown",
        p => p.Clips[0].TrackId = "unknown",
        p => p.Clips[0].TrackId = "audio1",
        p => p.Clips[2].TrackId = "video",
        p => p.Clips[0].StartMs = -1,
        p => p.Clips[0].StartMs = long.MaxValue,
        p => p.Clips[1].StartMs = 999,
        p => p.Clips[1].Id = p.Clips[0].Id,
        p => p.Assets.Add(null!),
        p => p.Tracks.Add(null!),
        p => p.Clips.Add(null!),
        p => p.Assets = null!
    };
    foreach (var mutate in mutations)
    {
        var project = Fixture();
        mutate(project);
        Throws<InvalidDataException>(() => TimelineRules.Validate(project));
    }
}

static void RoundTrip()
{
    using var files = new TestFiles();
    files.Write("Picture.mp4");
    files.Write("Music.wav");
    var path = Path.Combine(files.Folder, "Example.videosync");
    var project = Fixture(files.Folder);
    project.Name = "Example \u2014 \u97F3\u697D";
    project.MasterVolume = 81;
    project.MasterMuted = true;
    project.Tracks[1].Volume = 45;
    project.Tracks[1].IsMuted = true;
    ProjectStore.Save(project, path);
    var loaded = ProjectStore.Load(path);
    Equal(project.Name, loaded.Name);
    Equal(81, loaded.MasterVolume);
    True(loaded.MasterMuted);
    Equal(45, loaded.Tracks[1].Volume);
    True(loaded.Tracks[1].IsMuted);
    Equal(project.Assets[0].FilePath, loaded.Assets[0].FilePath);
    False(loaded.Assets[0].IsOffline);
    Equal(3, loaded.Clips.Count);
    Equal("v2", loaded.Clips[1].Id);
    Equal(1000L, loaded.Clips[1].StartMs);
    var json = JsonNode.Parse(File.ReadAllText(path))!;
    Equal("Picture.mp4", json["assets"]![0]!["relativePath"]!.GetValue<string>());
    Equal(project.Assets[0].FilePath, json["assets"]![0]!["absolutePath"]!.GetValue<string>());
    True(json["assets"]![0]!["isOffline"] is null);
    project.Name = "Replaced";
    ProjectStore.Save(project, path);
    Equal("Replaced", ProjectStore.Load(path).Name);
    Equal(0, Directory.GetFiles(files.Folder, "*.tmp").Length);
}

static void PortablePaths()
{
    using var files = new TestFiles();
    var original = Path.Combine(files.Folder, "original");
    var moved = Path.Combine(files.Folder, "moved");
    Directory.CreateDirectory(original);
    File.WriteAllText(Path.Combine(original, "Picture.mp4"), "fixture");
    File.WriteAllText(Path.Combine(original, "Music.wav"), "fixture");
    ProjectStore.Save(Fixture(original), Path.Combine(original, "Project.videosync"));
    Directory.Move(original, moved);
    var loaded = ProjectStore.Load(Path.Combine(moved, "Project.videosync"));
    Equal(Path.Combine(moved, "Picture.mp4"), loaded.Assets[0].FilePath);
    False(loaded.Assets[0].IsOffline);
}

static void AbsoluteFallback()
{
    using var files = new TestFiles();
    files.Write("Picture.mp4");
    files.Write("Music.wav");
    var path = Path.Combine(files.Folder, "Project.videosync");
    ProjectStore.Save(Fixture(files.Folder), path);
    var moved = Path.Combine(files.Folder, "moved");
    Directory.CreateDirectory(moved);
    var movedPath = Path.Combine(moved, "Project.videosync");
    File.Move(path, movedPath);
    var loaded = ProjectStore.Load(movedPath);
    Equal(Path.Combine(files.Folder, "Picture.mp4"), loaded.Assets[0].FilePath);
    False(loaded.Assets[0].IsOffline);
}

static void OfflineRelinking()
{
    using var files = new TestFiles();
    var projectPath = Path.Combine(files.Folder, "Offline.videosync");
    ProjectStore.Save(Fixture(files.Folder), projectPath);
    var loaded = ProjectStore.Load(projectPath);
    True(loaded.Assets.All(asset => asset.IsOffline));
    Equal(3, loaded.Clips.Count);
    var relinked = files.Write("Relinked.mp4");
    loaded.Assets[0].FilePath = relinked;
    False(loaded.Assets[0].IsOffline);
    ProjectStore.Save(loaded, projectPath);
    var reloaded = ProjectStore.Load(projectPath);
    Equal(relinked, reloaded.Assets[0].FilePath);
    False(reloaded.Assets[0].IsOffline);
    True(reloaded.Assets[1].IsOffline);
}

static void InvalidFiles()
{
    using var files = new TestFiles();
    var path = Path.Combine(files.Folder, "Invalid.videosync");
    foreach (var content in new[] { "{", "null", "{}", "{\"version\":999}", "{\"version\":1,\"tracks\":null}" })
    {
        File.WriteAllText(path, content);
        Throws<InvalidDataException>(() => ProjectStore.Load(path));
    }
    ProjectStore.Save(Fixture(files.Folder), path);
    var valid = File.ReadAllText(path);
    var mutations = new Action<JsonNode>[]
    {
        j => j["masterVolume"] = 101,
        j => j["clips"]![0]!.AsObject().Remove("id"),
        j => j["clips"]![0]!.AsObject().Remove("startMs"),
        j => j["tracks"]![0]!.AsObject().Remove("kind"),
        j => j["clips"]![0]!["startMs"] = -1,
        j => j["clips"]![0]!["assetId"] = "missing",
        j => j["assets"]![0]!["durationMs"] = 0,
        j => j["assets"]![0]!["relativePath"] = Path.Combine(files.Folder, "absolute.mp4"),
        j => j["assets"]![0]!["absolutePath"] = "relative.mp4",
        j => { j["assets"]![0]!["relativePath"] = null; j["assets"]![0]!["absolutePath"] = null; }
    };
    foreach (var mutate in mutations)
    {
        var node = JsonNode.Parse(valid)!;
        mutate(node);
        File.WriteAllText(path, node.ToJsonString());
        Throws<InvalidDataException>(() => ProjectStore.Load(path));
    }
    File.WriteAllText(path, valid);
    var invalid = Fixture(files.Folder);
    invalid.Clips[0].StartMs = -1;
    Throws<InvalidDataException>(() => ProjectStore.Save(invalid, path));
    Equal(valid, File.ReadAllText(path));
}

static void Active(ProjectDocument project, long time, params string[] ids) =>
    Equal(string.Join(",", ids.Order()), string.Join(",", TimelineRules.ActiveClips(project, time).Select(clip => clip.Id).Order()));
static void True(bool value, string? detail = null) { if (!value) throw new Exception(detail ?? "Expected true."); }
static void False(bool value) => True(!value, "Expected false.");
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected '{expected}', got '{actual}'.");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T exception) { True(!string.IsNullOrWhiteSpace(exception.Message), "Expected a useful error message."); return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

sealed class TestFiles : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"VideoSyncTests-{Guid.NewGuid():N}");
    public TestFiles() => Directory.CreateDirectory(Folder);
    public string Write(string name)
    {
        var path = Path.Combine(Folder, name);
        File.WriteAllText(path, "fixture");
        return path;
    }
    public void Dispose() => Directory.Delete(Folder, recursive: true);
}


