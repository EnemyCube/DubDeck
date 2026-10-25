# DubDeck

A Windows application built to sync up videos alongside layered audio tracks, such as movie commentary tracks or narration. It features auto-ducking audio, independent playback control of each track, and session saving.

![DubDeck workspace with a video track, two audio tracks, source waveforms, and mixer controls](docs/images/dubdeck-workspace.png)

*DubDeck's timeline and mixer with generated sample media.*

- Arrange video files in sequence and layer audio on separate tracks.
- Align cues by moving clips or pausing individual tracks while the others play.
- Mix track levels, mute tracks, and automatically lower other audio while a Voice track is audible.
- Play fullscreen with subtitles and track controls; save arrangements as `.dubdeck` projects.

## Download and run

### Portable version (recommended)

1. Download the portable EXE from [Releases](https://github.com/EnemyCube/DubDeck/releases/latest). New assets use `DubDeck-<version>-win.x64-portable.exe`; older releases may use `DubDeck.exe`.
2. Save it in a folder of your choice and double-click it. No installer, separate .NET runtime, or VLC installation is needed.
3. Choose **Import media** or drop video and audio files into the window.

### Folder version

Download the folder ZIP from the same release, extract it, and run `DubDeck.exe`. Keep all extracted files and subfolders together.

### Requirements and local files

- **Operating system:** Windows 11 on an x64 (Intel/AMD 64-bit) PC. macOS, Linux, and native Windows ARM64 builds are not provided.
- **Runtime:** .NET and the VLC media libraries are bundled. FFmpeg is needed only for development test-fixture generation.
- **Internet:** Not required to run the application or play local media.
- **Portable startup:** Bundled dependencies extract under `%TEMP%/.net/` in a folder named after the EXE (without `.exe`) and are reused later. You can delete that cache while the app is closed; it will be recreated.
- **Projects:** Media is referenced rather than embedded. Keep projects and their source media together when moving them between computers.

## Quick start

1. **Import:** Choose **Import media** (**Ctrl+I**) or drop files into the window. Videos append in sequence; each audio file starts at the playhead on a new track. Wait for loading to finish before importing more.
2. **Align and edit:** Drag clips to line up cues, or pause/resume an individual track while the others play. Press **Delete** to remove a selected clip; right-click it to replace or relink media.
3. **Mix:** Adjust track and master levels or mute tracks. Mark narration tracks **Voice** to trigger **Auto duck**, then set **Duck amount**. New projects enable ducking and mark the first audio track as Voice.
4. **Navigate:** Click the timeline or scrub bar to seek. **Space** plays/pauses, **Left/Right** jumps ten seconds, and **Ctrl+mouse wheel** zooms. **Stop** asks for confirmation before returning to the beginning.
5. **Watch:** Press **F11** for fullscreen and **Escape** to leave it. Use **Subtitles** to choose embedded tracks or open a subtitle file, and **Track controls** for the fullscreen mixer.
6. **Save:** **Ctrl+S** saves a `.dubdeck` project with its arrangement, mix, and playhead position. **Ctrl+O** opens a project; **Ctrl+N** starts a new one. Legacy `.videosync` and `.dubsync` projects also open normally.

## Limitations

DubDeck supports whole-file placement and playback synchronization. It does not trim or split clips, add fades or automation, or export a finished video or audio mix. Playback uses coordinated independent players; frame/sample accuracy and seamless video cuts are not guaranteed.

## Build and verify

For development, run these commands from a source checkout. `Start-DubDeck.ps1` opens the folder build under `artifacts/DubDeck-1.7.0` and builds it when absent; the older `Start-VideoSync.ps1` and `Start-DubSync.ps1` launchers forward to it. A source checkout does not include the published application.

Run these commands in PowerShell from this directory:

```powershell
.\scripts\Build.ps1 -Publish
.\scripts\Build.ps1 -Portable
.\scripts\Build.ps1 -MediaTests
.\scripts\Build.ps1 -Soak
```

The build script downloads Microsoft's .NET SDK 10.0.402 into .tools/dotnet when absent, restores the pinned NuGet packages, builds the solution, and runs the core tests. `-Publish` creates a self-contained x64 folder under artifacts/DubDeck-1.7.0. `-Portable` creates `DubDeck-<version>-win.x64-portable.exe` under `artifacts/DubDeck-<version>-Portable` using the Portable publish profile. Both names use the version from the application project. This profile compresses the bundle, excludes unused x86/ARM64 VLC copies and debug symbols, and keeps the full WPF runtime and x64 media plugins for compatibility. Licenses and notices are embedded and available in the extracted cache. SDK bootstrapping and NuGet restore need an internet connection; the app runs offline.

Media tests require FFmpeg on PATH only to generate fixtures. FFmpeg is not an application dependency. Fixtures exercise MP4, MOV, MKV, MP3, WAV, M4A, FLAC, corrupt input, transitions, gaps, seeking, and mixer controls. The optional soak performs ten minutes of real-time playback and writes timestamp/drift diagnostics. Headless video tests decode into native frame buffers, verify changing pulse pixels, and check actual DirectSound audio output counters. The separate visible WPF smoke test checks the GPU video target and presentation counters. These checks do not measure physical speaker/display timing.

For development, run `.\scripts\Test-Desktop.ps1 -Runs 3` in a visible Windows desktop session. It runs the workflow checks and three large-timeline/playback-close regressions, including simulated hardware-decoder failure recovery. Results are written to `artifacts/desktop-tests`. See `VALIDATION.md` in the source checkout for measured results.

For a real-window WPF smoke test after building:

```powershell
.\artifacts\DubDeck-1.7.0\DubDeck.exe --smoke-test .\artifacts\ui-smoke --fixtures .\artifacts\media-fixtures
```

Run the UI smoke test in a normal visible window so the GPU can present frames. It imports disposable fixture copies, exercises transport/fullscreen/project round trips and missing-source switching, writes result.json and WPF layout PNGs, then exits. WPF PNGs do not capture the native hardware video surface.

## Implementation

Timeline geometry is retained for the visible viewport; the playhead and drag feedback use a separate drawing layer. Playback notifications are coalesced, expensive native counters are opt-in, and edits reuse active decoders. Closing cancels queued work and stops native video before detaching its window. A five-second watchdog prevents an unresponsive native codec from blocking exit after the save prompt has completed. Hardware startup stalls are retried with software decoding.

The solution separates timeline rules/persistence (DubDeck.Core), WPF UI and serialized media coordination (DubDeck.App), deterministic domain checks (DubDeck.Tests), and actual native playback checks (DubDeck.MediaTests). Project files are version 1 JSON with relative paths and absolute fallbacks. Waveforms use one silent, unpaced VLC PCM decoder at a time, bounded peak envelopes, and viewport-limited drawing. The local cache is keyed by source path, file size, modification time, duration, and waveform format version; it does not change project files. See [VLC's memory output implementation](https://github.com/videolan/vlc/blob/3.0.24/modules/stream_out/smem.c) for the extraction interface. Voice detection uses a separate silent mono PCM decoder only for active Voice tracks when ducking is enabled. The engine uses DirectSound buffers for independent track gain/mute and prepares outputs while muted before starting the shared transport clock.

LibVLCSharp.WPF is pinned to 3.10.2, and VideoLAN.LibVLC.Windows to 3.0.24. Third-party licenses are included with the published application.

## License

DubDeck's own code is licensed under the [MIT License](LICENSE). You may use, modify, and redistribute it, including commercially, while preserving its copyright and license notice.

Third-party components retain their own licenses. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the [licenses](licenses/) directory for details. The application license and third-party notices are included in both published editions; the portable edition extracts them with its bundled dependencies.
