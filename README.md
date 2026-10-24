# Video Sync

A local Windows 11 x64 desktop app for playing whole video files alongside layered audio.

## Version 1.2.0

This update adds an application icon, embedded and external subtitles, fullscreen track mixers, independent track pause, and voice-triggered auto-ducking. It includes the timeline and playback-shutdown performance fixes. Existing .videosync projects remain compatible.

For development, run `.\scripts\Test-Desktop.ps1 -Runs 3` in a visible Windows desktop session. It runs the workflow checks and three large-timeline/playback-close regressions, including simulated hardware-decoder failure recovery. Results are written to artifacts/desktop-tests. See VALIDATION.md for measured results.

## Run

Open **artifacts/VideoSync-1.2.0/VideoSync.exe**. Keep the entire VideoSync folder together; the executable uses its bundled .NET runtime and LibVLC libraries. No separate VLC or .NET installation is needed. You can also drag a .videosync project onto the executable, or pass a project/media path as its first argument.

## Use

- Choose **Import media**, or drop media files into the window. Videos append in sequence. Each audio file starts at the playhead on a new audio track.
- Drag clips to change their start time. Audio clips can move between audio lanes. Clips in one lane cannot overlap; different lanes play together. Hold Alt to bypass snapping.
- Click an empty timeline area or drag the ruler to seek. Use the scrub bar below the preview for another way to seek. Use +/−, Fit, or Ctrl+mouse wheel to zoom, and scroll horizontally for long projects.
- Use each track's slider and mute button for its level. The Video slider controls embedded video sound. Master volume affects all tracks; muting preserves the slider setting.
- Press Space to play/pause, Home to seek to the beginning, F11 for fullscreen, and Escape to leave fullscreen. The fullscreen overlay includes playback, master volume, every track level/mute/pause/Voice control, subtitles, and ducking settings. Move the mouse to reveal it; it hides after three seconds of playback inactivity. Double-click the video to toggle fullscreen.
- Use **Pause** on an individual track to freeze that movie or audio track while the timeline and other tracks continue. **Resume** catches up to the current timeline position, including a different clip or a gap. Stop clears individual pauses; seeking moves paused tracks to the new timeline position. Pauses are temporary and are not saved in projects.
- Mark narration tracks **Voice**, enable **Auto duck**, and adjust **Duck amount**. Audible sound on any unmuted, unpaused Voice track lowers all other tracks, including the movie's original sound. A 60% amount leaves other tracks at 40% of their usual level; 100% silences them. Voice tracks retain their own levels. Silence restores the other levels with a short hold and smooth release. The detector responds to sound, rather than recognizing speech; background noise on a Voice track can also trigger it.
- Choose **CC / Subtitles** while a movie is loaded for Automatic, Off, embedded language tracks, or **Open subtitle file**. Automatic prefers matching sidecars such as Movie.srt or Movie.en.srt beside Movie.mp4, then an embedded track. SRT, ASS/SSA, VTT, SUB, and IDX files can be selected where supported by VLC. Subtitle choices are saved per source; missing subtitle files can be located through the same menu.
- Select a clip and press Delete to remove it. Right-click a clip to locate or replace its source. A replacement must have the same media type and must fit without overlapping another clip.
- Save with Ctrl+S, Save As with Ctrl+Shift+S, open with Ctrl+O, start a project with Ctrl+N, and import with Ctrl+I.

Projects contain source references, subtitle choices, arrangement/volume settings, and ducking preferences, without copying or changing media files. Keep the project and media together when moving them. Missing sources remain red/offline until relinked. Edits pause playback; deleting leaves a gap.

This first version supports whole-file placement and everyday playback synchronization. It does not include trimming, splitting, fades, automation, or video export. Independent source players are coordinated by a shared clock, with bounded startup waits and drift corrections; frame/sample accuracy and seamless video cuts are not guaranteed.

## Build and verify

Run these commands in PowerShell from this directory:

```powershell
.\scripts\Build.ps1 -Publish
.\scripts\Build.ps1 -MediaTests
.\scripts\Build.ps1 -Soak
```

The build script downloads Microsoft's .NET SDK 10.0.402 into .tools/dotnet when absent, restores the pinned NuGet packages, builds the solution, and runs the core tests. Publishing creates a self-contained x64 folder under artifacts/VideoSync-1.2.0. SDK bootstrapping and NuGet restore need an internet connection; the app runs offline.

Media tests require FFmpeg on PATH only to generate fixtures. FFmpeg is not an application dependency. Fixtures exercise MP4, MOV, MKV, MP3, WAV, M4A, FLAC, corrupt input, transitions, gaps, seeking, and mixer controls. The optional soak performs ten minutes of real-time playback and writes timestamp/drift diagnostics. Headless video tests decode into native frame buffers, verify changing pulse pixels, and check actual DirectSound audio output counters. The separate visible WPF smoke test checks the GPU video target and presentation counters. These checks do not measure physical speaker/display timing.

For a real-window WPF smoke test after building:

```powershell
.\artifacts\VideoSync-1.2.0\VideoSync.exe --smoke-test .\artifacts\ui-smoke --fixtures .\artifacts\media-fixtures
```

Run the UI smoke test in a normal visible window so the GPU can present frames. It imports disposable fixture copies, exercises transport/fullscreen/project round trips and missing-source switching, writes result.json and WPF layout PNGs, then exits. WPF PNGs do not capture the native hardware video surface.

## Implementation

Timeline geometry is retained for the visible viewport; the playhead and drag feedback use a separate drawing layer. Playback notifications are coalesced, expensive native counters are opt-in, and edits reuse active decoders. Closing cancels queued work and stops native video before detaching its window. A five-second watchdog prevents an unresponsive native codec from blocking exit after the save prompt has completed. Hardware startup stalls are retried with software decoding.



The solution separates timeline rules/persistence (VideoSync.Core), WPF UI and serialized media coordination (VideoSync.App), deterministic domain checks (VideoSync.Tests), and actual native playback checks (VideoSync.MediaTests). Project files are version 1 JSON with relative paths and absolute fallbacks. Voice detection uses a silent mono PCM decoder only for active Voice tracks when ducking is enabled. The engine uses DirectSound buffers for independent track gain/mute and prepares outputs while muted before starting the shared transport clock.

LibVLCSharp.WPF is pinned to 3.10.2, and VideoLAN.LibVLC.Windows to 3.0.24. Third-party licenses are included with the published application.
