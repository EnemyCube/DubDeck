# Video Sync

A local Windows 11 x64 desktop app for playing whole video files alongside layered audio.

## Version 1.6.0

Faint audio waveforms now appear behind each clip, including the movie's original audio. They are generated silently in the background, move with clip placements, and are reused from a local cache on later opens. The mouse wheel scrolls along the timeline; Shift+wheel scrolls between tracks, and Ctrl+wheel zooms. Track controls start below the time ruler and line up with the clip boxes. Short timelines stay anchored at the top of the editing area, and alignment is preserved when resizing or scrolling long track lists. Fullscreen keeps a compact playback bar with Track controls immediately to the left of Subtitles. Open its pop-up for every track slider, mute, pause, Voice trigger, and auto-ducking settings. Click outside or press Escape to dismiss it; playback continues. Fit timeline uses a text label; Voice and Auto duck use microphone and waveform toggles with on/off colors and tooltips. Audio tracks are created by importing audio; the Add audio track button has been removed. Saving remembers the playhead and reopening restores it paused. Fullscreen uses a focused player console with a seek bar, master volume readout, track mixer cards, clear paused/muted status, and a separate ducking footer. Imports show a prominent animated loading card with file counts and the current filename. Workspace controls and additional imports are disabled until loading and timeline preparation finish. Individual track pauses shift the track clips live on the timeline, and the alignment is saved in the project. Left/Right arrow keys seek backward/forward ten seconds. Common file, playback, mixer, fullscreen, and timeline buttons use vector icons with tooltips and accessible names. Auto duck remains enabled by default with Audio 1 as its Voice trigger.

For development, run `.\scripts\Test-Desktop.ps1 -Runs 3` in a visible Windows desktop session. It runs the workflow checks and three large-timeline/playback-close regressions, including simulated hardware-decoder failure recovery. Results are written to artifacts/desktop-tests. See VALIDATION.md for measured results.

## Run

For the **single-exe portable edition**, open **artifacts/VideoSync-1.6.0-Portable/VideoSync.exe**. Copy just that EXE to another Windows 11 x64 computer or a USB drive. It includes .NET, x64 VLC, all media plugins, and third-party licenses; no installer or separate runtime is needed. Dependencies unpack automatically under `%TEMP%/.net/VideoSync` on first launch and are reused on later launches. The cache may be deleted when the app is closed; it will be recreated. Keep project files and their media together separately.

Open **artifacts/VideoSync-1.6.0/VideoSync.exe**. Keep the entire VideoSync folder together; the executable uses its bundled .NET runtime and LibVLC libraries. No separate VLC or .NET installation is needed. You can also drag a .videosync project onto the executable, or pass a project/media path as its first argument.

## Use

- Choose **Import media**, or drop media files into the window. Videos append in sequence. Each audio file starts at the playhead on a new audio track. A successfully imported video sets the project title to its filename without the extension; if several videos are imported, the latest one supplies the title. Audio imports leave the title unchanged. The video title is kept when saving and suggested as the filename for a new project. A loading overlay shows the current file and batch count; wait until it disappears before importing another batch.
- Audio waveforms appear faintly behind the clips on each track as background analysis finishes. They show source sound and silence, including embedded movie audio, and follow clip moves or alignment delays. Volume and mute do not change the source waveform. Waveforms are cached under %LocalAppData%/VideoSync/Waveforms; missing files keep their offline appearance.
- Drag clips to change their start time. Audio clips can move between audio lanes. Clips in one lane cannot overlap; different lanes play together. Hold Alt to bypass snapping.
- Click an empty timeline area or drag the ruler to seek. Use the scrub bar below the preview for another way to seek. Use the mouse wheel to scroll backward/forward along the timeline. Shift+wheel scrolls vertically between tracks; the wheel over track labels also scrolls vertically. Use +/−, Fit timeline, or Ctrl+mouse wheel to zoom.
- Use each track's slider and mute button for its level. The Video slider controls embedded video sound. Master volume affects all tracks; muting preserves the slider setting.
- Press Space to play/pause, Home to seek to the beginning, Left/Right arrows to jump backward/forward ten seconds, F11 for fullscreen, and Escape to leave fullscreen. The fullscreen overlay includes a seek bar and elapsed/total time, playback and master volume controls, subtitles, and a **Track controls** button. Its scrollable pop-up contains track mixer cards with level/mute/pause/Voice controls and ducking settings. Escape first closes the pop-up, then exits fullscreen when pressed again. The playback bar stays visible while the pop-up is open. Move the mouse to reveal it; it hides after three seconds of playback inactivity. Double-click the video to toggle fullscreen.
- Use **Pause** on an individual track to freeze that movie or audio track while the timeline and other tracks continue. **Resume** continues from the paused position while the other tracks keep playing, so you can line up audio cues with video. The resulting delay stays in effect across global pause/resume and clip boundaries, and delayed tracks can finish beyond the original project end. While the global transport advances, every clip on the paused track shifts right on the timeline by the pause duration. Resume stops the shifting and continues from the paused cue. These are real placement changes: Stop and seeking preserve them, saving stores them, and reopening restores them. Seeking retains individual Pause buttons at the new timeline position. Global pause freezes both the timeline and the placement changes.
- Auto duck is on by default in new projects, and the first imported audio track (**Audio 1**) is marked **Voice**. Additional audio tracks are not Voice triggers by default. Saved project preferences are preserved. Mark narration tracks **Voice**, toggle **Auto duck**, and adjust **Duck amount**. Audible sound on any unmuted, unpaused Voice track lowers all other tracks, including the movie's original sound. A 60% amount leaves other tracks at 40% of their usual level; 100% silences them. Voice tracks retain their own levels. Silence restores the other levels with a short hold and smooth release. The detector responds to sound, rather than recognizing speech; background noise on a Voice track can also trigger it.
- Choose the **Subtitles icon** (caption lines inside a frame) / **Subtitles** while a movie is loaded for Automatic, Off, embedded language tracks, or **Open subtitle file**. Automatic prefers matching sidecars such as Movie.srt or Movie.en.srt beside Movie.mp4, then an embedded track. SRT, ASS/SSA, VTT, SUB, and IDX files can be selected where supported by VLC. Subtitle choices are saved per source; missing subtitle files can be located through the same menu.
- Select a clip and press Delete to remove it. Right-click a clip to locate or replace its source. A replacement must have the same media type and must fit without overlapping another clip.
- Save with Ctrl+S, Save As with Ctrl+Shift+S, open with Ctrl+O, start a project with Ctrl+N, and import with Ctrl+I. Saving includes the current playhead time; reopening restores that position with playback paused. Older projects without a saved position open at the beginning. If the saved time exceeds the project length, it is clamped to the end.

Projects contain source references, subtitle choices, arrangement/volume settings, saved playhead time, and ducking preferences, without copying or changing media files. Keep the project and media together when moving them. Missing sources remain red/offline until relinked. Manual timeline edits pause playback; deleting leaves a gap. Alignment through individual track pause keeps the other tracks playing.

This first version supports whole-file placement and everyday playback synchronization. It does not include trimming, splitting, fades, automation, or video export. Independent source players are coordinated by a shared clock, with bounded startup waits and drift corrections; frame/sample accuracy and seamless video cuts are not guaranteed.

## Build and verify

Run these commands in PowerShell from this directory:

```powershell
.\scripts\Build.ps1 -Publish
.\scripts\Build.ps1 -Portable
.\scripts\Build.ps1 -MediaTests
.\scripts\Build.ps1 -Soak
```

The build script downloads Microsoft's .NET SDK 10.0.402 into .tools/dotnet when absent, restores the pinned NuGet packages, builds the solution, and runs the core tests. `-Publish` creates a self-contained x64 folder under artifacts/VideoSync-1.6.0. `-Portable` creates only VideoSync.exe under artifacts/VideoSync-1.6.0-Portable using the Portable publish profile. This profile compresses the bundle, excludes unused x86/ARM64 VLC copies and debug symbols, and keeps the full WPF runtime and x64 media plugins for compatibility. Licenses and notices are embedded and available in the extracted cache. SDK bootstrapping and NuGet restore need an internet connection; the app runs offline.

Media tests require FFmpeg on PATH only to generate fixtures. FFmpeg is not an application dependency. Fixtures exercise MP4, MOV, MKV, MP3, WAV, M4A, FLAC, corrupt input, transitions, gaps, seeking, and mixer controls. The optional soak performs ten minutes of real-time playback and writes timestamp/drift diagnostics. Headless video tests decode into native frame buffers, verify changing pulse pixels, and check actual DirectSound audio output counters. The separate visible WPF smoke test checks the GPU video target and presentation counters. These checks do not measure physical speaker/display timing.

For a real-window WPF smoke test after building:

```powershell
.\artifacts\VideoSync-1.6.0\VideoSync.exe --smoke-test .\artifacts\ui-smoke --fixtures .\artifacts\media-fixtures
```

Run the UI smoke test in a normal visible window so the GPU can present frames. It imports disposable fixture copies, exercises transport/fullscreen/project round trips and missing-source switching, writes result.json and WPF layout PNGs, then exits. WPF PNGs do not capture the native hardware video surface.

## Implementation

Timeline geometry is retained for the visible viewport; the playhead and drag feedback use a separate drawing layer. Playback notifications are coalesced, expensive native counters are opt-in, and edits reuse active decoders. Closing cancels queued work and stops native video before detaching its window. A five-second watchdog prevents an unresponsive native codec from blocking exit after the save prompt has completed. Hardware startup stalls are retried with software decoding.



The solution separates timeline rules/persistence (VideoSync.Core), WPF UI and serialized media coordination (VideoSync.App), deterministic domain checks (VideoSync.Tests), and actual native playback checks (VideoSync.MediaTests). Project files are version 1 JSON with relative paths and absolute fallbacks. Waveforms use one silent, unpaced VLC PCM decoder at a time, bounded peak envelopes, and viewport-limited drawing. The local cache is keyed by source path, file size, modification time, duration, and waveform format version; it does not change project files. See [VLC's memory output implementation](https://github.com/videolan/vlc/blob/3.0.24/modules/stream_out/smem.c) for the extraction interface. Voice detection uses a separate silent mono PCM decoder only for active Voice tracks when ducking is enabled. The engine uses DirectSound buffers for independent track gain/mute and prepares outputs while muted before starting the shared transport clock.

LibVLCSharp.WPF is pinned to 3.10.2, and VideoLAN.LibVLC.Windows to 3.0.24. Third-party licenses are included with the published application.
