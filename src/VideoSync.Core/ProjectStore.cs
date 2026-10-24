using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoSync.Core;

/// <summary>Versioned, portable project files. Media bytes are never copied into projects.</summary>
public static class ProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static ProjectDocument Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(fullPath)!;
        StoredProject stored;
        try
        {
            using var stream = File.OpenRead(fullPath);
            stored = JsonSerializer.Deserialize<StoredProject>(stream, JsonOptions)
                ?? throw new InvalidDataException("The project file is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The project file contains invalid JSON: {exception.Message}", exception);
        }
        if (stored.Version != 1)
            throw new InvalidDataException($"Project version {stored.Version} is unsupported. This application supports version 1.");
        if (stored.Assets is null || stored.Tracks is null || stored.Clips is null)
            throw new InvalidDataException("The project must contain media, track, and clip lists.");

        var project = new ProjectDocument
        {
            Version = stored.Version,
            Name = stored.Name ?? "",
            MasterVolume = stored.MasterVolume,
            MasterMuted = stored.MasterMuted,
            AutoDuckingEnabled = stored.AutoDuckingEnabled,
            DuckAmount = stored.DuckAmount,
            Tracks = stored.Tracks,
            Clips = stored.Clips,
            Assets = []
        };
        foreach (var asset in stored.Assets)
        {
            if (asset is null) throw new InvalidDataException("The media list contains an empty entry.");
            project.Assets.Add(new MediaAsset
            {
                Id = asset.Id ?? "",
                Name = asset.Name ?? "",
                FilePath = ResolveSourcePath(asset, folder),
                DurationMs = asset.DurationMs,
                HasVideo = asset.HasVideo,
                HasAudio = asset.HasAudio,
                Subtitles = asset.Subtitles,
                SubtitleTrackIndex = asset.SubtitleTrackIndex,
                SubtitleFilePath = string.IsNullOrWhiteSpace(asset.SubtitleAbsolutePath) && string.IsNullOrWhiteSpace(asset.SubtitleRelativePath) ? null :
                    ResolveSourcePath(new StoredAsset { Name = asset.Name + " subtitles", RelativePath = asset.SubtitleRelativePath, AbsolutePath = asset.SubtitleAbsolutePath }, folder)
            });
        }
        TimelineRules.Validate(project);
        return project;
    }

    public static void Save(ProjectDocument project, string path)
    {
        TimelineRules.Validate(project);
        var fullPath = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(fullPath)!;
        var stored = new StoredProject
        {
            Version = project.Version,
            Name = project.Name,
            MasterVolume = project.MasterVolume,
            MasterMuted = project.MasterMuted,
            AutoDuckingEnabled = project.AutoDuckingEnabled,
            DuckAmount = project.DuckAmount,
            Tracks = project.Tracks,
            Clips = project.Clips,
            Assets = project.Assets.Select(asset =>
            {
                var absolutePath = Path.GetFullPath(asset.FilePath);
                var relativePath = Path.GetRelativePath(folder, absolutePath);
                return new StoredAsset
                {
                    Id = asset.Id,
                    Name = asset.Name,
                    AbsolutePath = absolutePath,
                    RelativePath = Path.IsPathRooted(relativePath) ? null : relativePath,
                    DurationMs = asset.DurationMs,
                    HasVideo = asset.HasVideo,
                    HasAudio = asset.HasAudio,
                    Subtitles = asset.Subtitles,
                    SubtitleTrackIndex = asset.SubtitleTrackIndex,
                    SubtitleAbsolutePath = asset.SubtitleFilePath,
                    SubtitleRelativePath = asset.SubtitleFilePath is null || Path.IsPathRooted(Path.GetRelativePath(folder, asset.SubtitleFilePath)) ? null : Path.GetRelativePath(folder, asset.SubtitleFilePath)
                };
            }).ToList()
        };

        Directory.CreateDirectory(folder);
        // Write and flush beside the destination, then replace it in one filesystem operation.
        var temporaryPath = Path.Combine(folder, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, stored, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string ResolveSourcePath(StoredAsset asset, string folder)
    {
        string? relative = null;
        string? absolute = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(asset.RelativePath))
            {
                if (Path.IsPathRooted(asset.RelativePath))
                    throw new InvalidDataException($"The relative source path for '{asset.Name}' must be relative to the project.");
                relative = Path.GetFullPath(asset.RelativePath, folder);
            }
            if (!string.IsNullOrWhiteSpace(asset.AbsolutePath))
            {
                if (!Path.IsPathFullyQualified(asset.AbsolutePath))
                    throw new InvalidDataException($"The fallback source path for '{asset.Name}' must be absolute.");
                absolute = Path.GetFullPath(asset.AbsolutePath);
            }
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"The source path for '{asset.Name}' is invalid.", exception);
        }
        if (relative is not null && File.Exists(relative)) return relative;
        if (absolute is not null && File.Exists(absolute)) return absolute;
        // Offline media keeps a meaningful intended location for display and relinking.
        return relative ?? absolute
            ?? throw new InvalidDataException($"Media '{asset.Name}' is missing its source path.");
    }

    private sealed class StoredProject
    {
        public StoredProject() { }
        public int Version { get; set; }
        public string? Name { get; set; }
        public int MasterVolume { get; set; } = 100;
        public bool MasterMuted { get; set; }
        public bool AutoDuckingEnabled { get; set; }
        public int DuckAmount { get; set; } = 60;
        public List<StoredAsset>? Assets { get; set; }
        public List<TimelineTrack>? Tracks { get; set; }
        public List<TimelineClip>? Clips { get; set; }
    }

    private sealed class StoredAsset
    {
        public StoredAsset() { }
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? RelativePath { get; set; }
        public string? AbsolutePath { get; set; }
        public long DurationMs { get; set; }
        public bool HasVideo { get; set; }
        public bool HasAudio { get; set; }
        public SubtitleMode Subtitles { get; set; } = SubtitleMode.Auto;
        public int SubtitleTrackIndex { get; set; } = -1;
        public string? SubtitleAbsolutePath { get; set; }
        public string? SubtitleRelativePath { get; set; }
    }
}
