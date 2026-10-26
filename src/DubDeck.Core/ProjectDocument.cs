using System.Text.Json.Serialization;

namespace DubDeck.Core;

public enum TrackKind { Video, Audio }
public enum SubtitleMode { Auto, Off, Embedded, External }

public sealed class MediaAsset
{
    [JsonRequired]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long DurationMs { get; set; }
    public bool HasVideo { get; set; }
    public bool HasAudio { get; set; }
    public SubtitleMode Subtitles { get; set; } = SubtitleMode.Auto;
    public int SubtitleTrackIndex { get; set; } = -1;
    public string? SubtitleFilePath { get; set; }

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
    public bool IsDuckingSource { get; set; }
    [JsonIgnore]
    public bool IsPaused { get; set; }
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
    public long PlayheadMs { get; set; }
    public int MasterVolume { get; set; } = 100;
    public bool MasterMuted { get; set; }
    public bool AutoDuckingEnabled { get; set; } = true;
    public int DuckAmount { get; set; } = 30;
    public List<MediaAsset> Assets { get; set; } = [];
    public List<TimelineTrack> Tracks { get; set; } =
        [new TimelineTrack { Id = "video", Name = "Video", Kind = TrackKind.Video }];
    public List<TimelineClip> Clips { get; set; } = [];

    public TimelineTrack AddAudioTrack()
    {
        int existing = Tracks.Count(track => track.Kind == TrackKind.Audio);
        var track = new TimelineTrack { Name = "Audio " + (existing + 1), Kind = TrackKind.Audio,
            IsDuckingSource = existing == 0 };
        Tracks.Add(track);
        return track;
    }
}

