using System.Text.Json.Nodes;
using DubDeck.Core;

var tests = new (string Name, Action Run)[]
{
    ("DubDeck projects retain compatibility with existing project files", BrandedProjects),
    ("Waveform peaks preserve silence, timing, and both signed PCM channels", WaveformPeaks),
    ("Waveform range queries retain brief transients at every zoom", WaveformRanges),
    ("Waveform memory stays bounded for very long sources", WaveformBounds),
    ("Saved playhead round-trips, clamps, and defaults for older projects", PlayheadPersistence),
    ("New projects enable ducking with Audio 1 as the default Voice source", DefaultDucking),
    ("Voice ducking smooths, holds, and releases", Ducking),
    ("Ducking preferences save while runtime pause does not", DuckPreferences),
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
    ("Invalid project files fail with useful errors", InvalidFiles),
    ("Subtitle choices and portable sidecars round-trip", SubtitlePreferences)
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


static void SubtitlePreferences()
{
    using var files = new TestFiles();
    files.Write("Picture.mp4"); files.Write("Music.wav"); files.Write("Picture.en.srt");
    var project = Fixture(files.Folder);
    var asset = project.Assets[0];
    asset.Subtitles = SubtitleMode.External;
    asset.SubtitleFilePath = Path.Combine(files.Folder, "Picture.en.srt");
    string path = Path.Combine(files.Folder, "Subs.videosync");
    ProjectStore.Save(project, path);
    var loaded = ProjectStore.Load(path);
    Equal(SubtitleMode.External, loaded.Assets[0].Subtitles);
    Equal(asset.SubtitleFilePath, loaded.Assets[0].SubtitleFilePath);
    var json = JsonNode.Parse(File.ReadAllText(path))!;
    Equal("Picture.en.srt", json["assets"]![0]!["subtitleRelativePath"]!.GetValue<string>());
    File.Delete(asset.SubtitleFilePath);
    loaded = ProjectStore.Load(path);
    Equal(asset.SubtitleFilePath, loaded.Assets[0].SubtitleFilePath);
    asset.Subtitles = SubtitleMode.Embedded; asset.SubtitleTrackIndex = 1; asset.SubtitleFilePath = null;
    ProjectStore.Save(project, path);
    Equal(1, ProjectStore.Load(path).Assets[0].SubtitleTrackIndex);
    asset.SubtitleTrackIndex = -1;
    Throws<InvalidDataException>(() => TimelineRules.Validate(project));
    asset.Subtitles = (SubtitleMode)99;
    Throws<InvalidDataException>(() => TimelineRules.Validate(project));
}
static void Ducking()
{
    var envelope = new DuckingEnvelope();
    for (int i=0;i<5;i++) envelope.Update(true,true,60,.04);
    if (Math.Abs(envelope.Gain-.4)>.001) throw new Exception("Duck amount must reduce by 60%");
    envelope.Update(true,false,60,.2);
    if (Math.Abs(envelope.Gain-.4)>.001) throw new Exception("Hold should bridge short voice gaps");
    for(int i=0;i<30;i++) envelope.Update(true,false,60,.04);
    if (Math.Abs(envelope.Gain-1)>.001) throw new Exception("Silence should restore volume");
    for(int i=0;i<5;i++) envelope.Update(true,true,100,.04);
    if(envelope.Gain>.001) throw new Exception("100% should silence beds");
    for(int i=0;i<15;i++) envelope.Update(false,true,100,.04);
    if(Math.Abs(envelope.Gain-1)>.001) throw new Exception("Disabled must restore volume");
}
static void DuckPreferences()
{
    var project=Fixture(); project.AutoDuckingEnabled=true; project.DuckAmount=73;
    project.Tracks[1].IsDuckingSource=true; project.Tracks[1].IsPaused=true;
    var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".videosync");
    try {
        ProjectStore.Save(project,path); var loaded=ProjectStore.Load(path);
        if(!loaded.AutoDuckingEnabled||loaded.DuckAmount!=73||!loaded.Tracks[1].IsDuckingSource||loaded.Tracks[1].IsPaused)
            throw new Exception("Ducking settings or runtime pause persistence is incorrect");
    } finally { File.Delete(path); }
}
static void DefaultDucking()
{
    var project = new ProjectDocument();
    True(project.AutoDuckingEnabled);
    Equal(30, project.DuckAmount);
    var first = project.AddAudioTrack();
    var second = project.AddAudioTrack();
    Equal("Audio 1", first.Name); True(first.IsDuckingSource);
    Equal("Audio 2", second.Name); False(second.IsDuckingSource);
    False(project.Tracks[0].IsDuckingSource);
    project.AutoDuckingEnabled = false; first.IsDuckingSource = false;
    project.AddAudioTrack();
    False(project.AutoDuckingEnabled); False(first.IsDuckingSource);
    using var files = new TestFiles();
    var path = Path.Combine(files.Folder,"Defaults.videosync");
    ProjectStore.Save(project,path);
    var loaded = ProjectStore.Load(path);
    False(loaded.AutoDuckingEnabled); False(loaded.Tracks[1].IsDuckingSource);
    Equal(30, loaded.DuckAmount);
    var json = JsonNode.Parse(File.ReadAllText(path))!;
    json["duckAmount"] = 60;
    File.WriteAllText(path, json.ToJsonString());
    Equal(60, ProjectStore.Load(path).DuckAmount);
    json.AsObject().Remove("duckAmount");
    File.WriteAllText(path, json.ToJsonString());
    Equal(30, ProjectStore.Load(path).DuckAmount);
}
static void PlayheadPersistence()
{
    using var files=new TestFiles();
    var project=Fixture(files.Folder); project.PlayheadMs=750;
    var path=Path.Combine(files.Folder,"Playhead.videosync");
    ProjectStore.Save(project,path);
    Equal(750L,ProjectStore.Load(path).PlayheadMs);
    var json=JsonNode.Parse(File.ReadAllText(path))!;
    json.AsObject().Remove("playheadMs"); File.WriteAllText(path,json.ToJsonString());
    Equal(0L,ProjectStore.Load(path).PlayheadMs);
    json["playheadMs"]=long.MaxValue; File.WriteAllText(path,json.ToJsonString());
    Equal(TimelineRules.Duration(project),ProjectStore.Load(path).PlayheadMs);
    project.PlayheadMs=long.MaxValue; ProjectStore.Save(project,path);
    Equal(TimelineRules.Duration(project),ProjectStore.Load(path).PlayheadMs);
    project.PlayheadMs=-1; Throws<InvalidDataException>(()=>ProjectStore.Save(project,path));
    json["playheadMs"]=-1; File.WriteAllText(path,json.ToJsonString());
    Throws<InvalidDataException>(()=>ProjectStore.Load(path));
}
static void WaveformPeaks()
{
    var builder = new AudioWaveformBuilder(100);
    builder.AddSamples(new short[] { 0, 0, -32768, 1000, 2000, -2000 }, 100, 2, 0);
    builder.AddSamples(new short[] { 16384, -1000 }, 100, 2, 70);
    var wave = builder.Build();
    Equal(0d, wave.PeakBetween(0, 10));
    Equal(1d, wave.PeakBetween(10, 20));
    Equal(2000 / 32768d, wave.PeakBetween(20, 30));
    Equal(0d, wave.PeakBetween(30, 70));
    Equal(.5d, wave.PeakBetween(70, 80));
    Equal(0d, wave.PeakBetween(80, 100));
}

static void WaveformRanges()
{
    var peaks = new ushort[] { 0, 1000, 5000, 0, 16000, 200, 32768, 0, 800 };
    var wave = new AudioWaveform(90, 10, peaks);
    for (int start = 0; start < peaks.Length; start++)
        for (int end = start + 1; end <= peaks.Length; end++)
            Equal(peaks.Skip(start).Take(end - start).Max() / 32768d, wave.PeakBetween(start * 10, end * 10));
    peaks[6] = 0; // The envelope must not retain mutable caller storage.
    Equal(1d, wave.PeakBetween(-100, 1000));
    Equal(0d, wave.PeakBetween(-100, 0));
    Equal(0d, wave.PeakBetween(90, 100));
}

static void WaveformBounds()
{
    var wave = new AudioWaveformBuilder(1000L * 60 * 60 * 1000).Build();
    True(wave.BinCount <= AudioWaveform.MaximumBins, "Long-file waveform allocation is unbounded.");
    Equal(0d, wave.PeakBetween(0, wave.DurationMs));
    bool rejected = false;
    try { _ = new AudioWaveform(10, 10, new ushort[] { 40000 }); }
    catch (ArgumentException) { rejected = true; }
    True(rejected, "Malformed cached waveform peaks were accepted.");
}
static void BrandedProjects()
{
    True(ProjectFile.Extension == ".dubdeck");
    foreach (string extension in new[] { ".dubdeck", ".DUBDECK", ".videosync", ".VIDEOSYNC", ".dubsync" })
        True(ProjectFile.IsProjectPath("A project" + extension), "Project detection rejected " + extension);
    False(ProjectFile.IsProjectPath("movie.mp4"));
    using var files = new TestFiles();
    var document = Fixture(files.Folder);
    document.PlayheadMs = 750;
    string legacy = Path.Combine(files.Folder, "Original.videosync");
    string branded = Path.Combine(files.Folder, "Renamed.dubdeck");
    ProjectStore.Save(document, legacy);
    ProjectStore.Save(ProjectStore.Load(legacy), branded);
    var loaded = ProjectStore.Load(branded);
    Equal(document.Version, loaded.Version);
    Equal(document.Clips.Count, loaded.Clips.Count);
    Equal(750L, loaded.PlayheadMs);
    Equal(document.Name, loaded.Name);
}
sealed class TestFiles : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"DubDeckTests-{Guid.NewGuid():N}");
    public TestFiles() => Directory.CreateDirectory(Folder);
    public string Write(string name)
    {
        var path = Path.Combine(Folder, name);
        File.WriteAllText(path, "fixture");
        return path;
    }
    public void Dispose() => Directory.Delete(Folder, recursive: true);
}
