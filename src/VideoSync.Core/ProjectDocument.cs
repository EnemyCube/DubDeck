using System.Text.Json.Serialization;

namespace VideoSync.Core;

public enum TrackKind { Video, Audio }

public sealed class MediaAsset
{
    [JsonRequired]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long DurationMs { get; set; }
    public bool HasVideo { get; set; }
    public bool HasAudio { get; set; }

    [JsonIgnore]
    public bool IsOffline => !File.Exists(FilePath);
}

public sealed class TimelineTrack
{
    [JsonRequired]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    [JsonRequired]
    public TrackKind Kind { get; set; }
    public int Volume { get; set; } = 100;
    public bool IsMuted { get; set; }
}

public sealed class TimelineClip
{
    [JsonRequired]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AssetId { get; set; } = "";
    public string TrackId { get; set; } = "";
    [JsonRequired]
    public long StartMs { get; set; }
}

public sealed class ProjectDocument
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Untitled";
    public int MasterVolume { get; set; } = 100;
    public bool MasterMuted { get; set; }
    public List<MediaAsset> Assets { get; set; } = [];
    public List<TimelineTrack> Tracks { get; set; } =
        [new TimelineTrack { Id = "video", Name = "Video", Kind = TrackKind.Video }];
    public List<TimelineClip> Clips { get; set; } = [];
}

