namespace DubDeck.Core;

/// <summary>Rules shared by editing, persistence, and playback. Times are milliseconds.</summary>
public static class TimelineRules
{
    public static long Duration(ProjectDocument project) =>
        project.Clips.Count == 0 ? 0 : project.Clips.Max(clip => End(project, clip));

    public static long End(ProjectDocument project, TimelineClip clip)
    {
        var asset = project.Assets.FirstOrDefault(asset => asset.Id == clip.AssetId)
            ?? throw new InvalidDataException($"Clip '{clip.Id}' refers to unknown media '{clip.AssetId}'.");
        try { return checked(clip.StartMs + asset.DurationMs); }
        catch (OverflowException exception)
        {
            throw new InvalidDataException($"Clip '{clip.Id}' extends beyond the supported timeline range.", exception);
        }
    }

    /// <summary>End boundaries are exclusive so adjacent clips never play together.</summary>
    public static IEnumerable<TimelineClip> ActiveClips(ProjectDocument project, long positionMs) =>
        positionMs < 0 ? [] : project.Clips.Where(clip => clip.StartMs <= positionMs && positionMs < End(project, clip));

    public static bool TryMove(ProjectDocument project, string clipId, string trackId, long startMs, out string? error)
    {
        var clip = project.Clips.FirstOrDefault(clip => clip.Id == clipId);
        if (clip is null)
        {
            error = "The selected clip no longer exists.";
            return false;
        }
        if (!CanPlace(project, clip.AssetId, trackId, startMs, clipId, out error))
            return false;
        clip.TrackId = trackId;
        clip.StartMs = startMs;
        return true;
    }

    /// <summary>Checks placement before adding a clip or moving an existing clip.</summary>
    public static bool CanPlace(ProjectDocument project, string assetId, string trackId, long startMs,
        string? exceptClipId, out string? error)
    {
        error = null;
        var asset = project.Assets.FirstOrDefault(asset => asset.Id == assetId);
        var track = project.Tracks.FirstOrDefault(track => track.Id == trackId);
        if (asset is null) error = "The media for this clip could not be found in the project.";
        else if (track is null) error = "The destination track no longer exists.";
        else if (startMs < 0) error = "A clip cannot start before the beginning of the timeline.";
        else if (asset.DurationMs <= 0) error = "The media must have a positive duration.";
        else if (asset.DurationMs > long.MaxValue - startMs) error = "The clip extends beyond the supported timeline range.";
        else if (track.Kind == TrackKind.Video && !asset.HasVideo) error = "Only video files can be placed on the video track.";
        else if (track.Kind == TrackKind.Audio && (asset.HasVideo || !asset.HasAudio)) error = "Only audio files can be placed on an audio track.";
        else if (!Enum.IsDefined(track.Kind)) error = "The destination track has an unsupported type.";
        if (error is not null) return false;

        var endMs = startMs + asset!.DurationMs;
        foreach (var other in project.Clips.Where(clip => clip.TrackId == trackId && clip.Id != exceptClipId))
        {
            try
            {
                if (startMs < End(project, other) && other.StartMs < endMs)
                {
                    error = "Clips on the same track cannot overlap. Move this clip into a gap or onto another audio track.";
                    return false;
                }
            }
            catch (InvalidDataException exception)
            {
                error = exception.Message;
                return false;
            }
        }
        return true;
    }

    public static int EffectiveVolume(ProjectDocument project, TimelineTrack track) =>
        project.MasterMuted || track.IsMuted ? 0 : (int)Math.Round(
            Math.Clamp(project.MasterVolume, 0, 100) * Math.Clamp(track.Volume, 0, 100) / 100.0,
            MidpointRounding.AwayFromZero);

    /// <summary>Checks structural validity without requiring source files to be online.</summary>
    public static void Validate(ProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Version != 1) throw new InvalidDataException($"Project version {project.Version} is unsupported. This application supports version 1.");
        if (string.IsNullOrWhiteSpace(project.Name)) throw new InvalidDataException("The project must have a name.");
        if (project.PlayheadMs < 0) throw new InvalidDataException("Playhead position must not be negative.");
        if (project.DuckAmount is < 0 or > 100) throw new InvalidDataException("Duck amount must be between 0 and 100.");
        if (project.MasterVolume is < 0 or > 100) throw new InvalidDataException("Master volume must be between 0 and 100.");
        if (project.Assets is null || project.Tracks is null || project.Clips is null)
            throw new InvalidDataException("The project must contain media, track, and clip lists.");

        var assetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in project.Assets)
        {
            if (asset is null) throw new InvalidDataException("The media list contains an empty entry.");
            RequireId(asset.Id, assetIds, "Media");
            if (string.IsNullOrWhiteSpace(asset.Name)) throw new InvalidDataException($"Media '{asset.Id}' must have a name.");
            if (string.IsNullOrWhiteSpace(asset.FilePath) || !Path.IsPathFullyQualified(asset.FilePath) || asset.FilePath.Contains('\0'))
                throw new InvalidDataException($"Media '{asset.Name}' must have an absolute source path.");
            if (asset.DurationMs <= 0) throw new InvalidDataException($"Media '{asset.Name}' must have a positive duration.");
            if (!Enum.IsDefined(asset.Subtitles)) throw new InvalidDataException("Unsupported subtitle mode.");
            if (asset.Subtitles == SubtitleMode.Embedded && asset.SubtitleTrackIndex < 0) throw new InvalidDataException("Choose a valid embedded subtitle track.");
            if (asset.Subtitles == SubtitleMode.External && (string.IsNullOrWhiteSpace(asset.SubtitleFilePath) || !Path.IsPathFullyQualified(asset.SubtitleFilePath)))
                throw new InvalidDataException("External subtitles must have an absolute source path.");
            if (!asset.HasVideo && !asset.HasAudio) throw new InvalidDataException($"Media '{asset.Name}' contains no video or audio.");
        }

        var trackIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in project.Tracks)
        {
            if (track is null) throw new InvalidDataException("The track list contains an empty entry.");
            RequireId(track.Id, trackIds, "Track");
            if (string.IsNullOrWhiteSpace(track.Name)) throw new InvalidDataException($"Track '{track.Id}' must have a name.");
            if (!Enum.IsDefined(track.Kind)) throw new InvalidDataException($"Track '{track.Name}' has an unsupported type.");
            if (track.Volume is < 0 or > 100) throw new InvalidDataException($"Volume on '{track.Name}' must be between 0 and 100.");
        }
        if (project.Tracks.Count(track => track.Kind == TrackKind.Video) != 1)
            throw new InvalidDataException("The project must contain exactly one video track.");

        var clipIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var clip in project.Clips)
        {
            if (clip is null) throw new InvalidDataException("The clip list contains an empty entry.");
            RequireId(clip.Id, clipIds, "Clip");
            if (!assetIds.Contains(clip.AssetId ?? "")) throw new InvalidDataException($"Clip '{clip.Id}' refers to unknown media '{clip.AssetId}'.");
            if (!trackIds.Contains(clip.TrackId ?? "")) throw new InvalidDataException($"Clip '{clip.Id}' refers to unknown track '{clip.TrackId}'.");
            if (clip.StartMs < 0) throw new InvalidDataException($"Clip '{clip.Id}' cannot start before the beginning of the timeline.");
            var asset = project.Assets.First(asset => asset.Id == clip.AssetId);
            var track = project.Tracks.First(track => track.Id == clip.TrackId);
            if ((track.Kind == TrackKind.Video) != asset.HasVideo)
                throw new InvalidDataException($"Clip '{clip.Id}' is on the wrong track type.");
            _ = End(project, clip);
        }

        foreach (var track in project.Tracks)
        {
            long previousEnd = 0;
            foreach (var clip in project.Clips.Where(clip => clip.TrackId == track.Id).OrderBy(clip => clip.StartMs))
            {
                if (clip.StartMs < previousEnd) throw new InvalidDataException($"Clips overlap on track '{track.Name}'.");
                previousEnd = End(project, clip);
            }
        }
    }

    private static void RequireId(string id, HashSet<string> seen, string kind)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException($"{kind} entries must have an ID.");
        if (!seen.Add(id)) throw new InvalidDataException($"{kind} ID '{id}' occurs more than once.");
    }
}
