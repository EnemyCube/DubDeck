# DubDeck

Sync video with commentary, narration, music, and other audio tracks. DubDeck is a Windows desktop app with independent track controls, automatic audio ducking, and saved projects.

![DubDeck workspace with video and audio tracks](docs/images/dubdeck-workspace.png)

## Features

- Arrange whole video files in sequence and layer audio on a timeline.
- Align clips by dragging them or pausing individual tracks while others continue.
- Adjust track levels and automatically lower other audio while narration plays.
- Play fullscreen with subtitles and track mixer controls.
- Choose Midnight accent variants, Light, Dusk, or Jaboody Blue from the **Theme** menu; your choice is remembered.
- Save arrangements, mix settings, and the playhead position in `.dubdeck` projects.

## Download

Get the latest build from [GitHub Releases](https://github.com/EnemyCube/DubDeck/releases/latest).

- **Portable EXE (recommended):** Download and run `DubDeck-<version>-win.x64-portable.exe`.
- **Folder ZIP:** Extract the entire archive and run `DubDeck.exe`. Keep the extracted files and folders together.

**Requires Windows 11 x64.** Both editions include .NET and VLC libraries, require no separate runtime installation, and run offline.

## Quick start

1. **Import:** Drop media into the window or press **Ctrl+I**. Videos append in sequence; audio starts at the playhead on a new track.
2. **Align:** Drag clips to line up cues, or pause/resume a single track while the others play. Right-click a clip to replace or relink its media.
3. **Mix:** Adjust track levels and mute controls. Mark narration tracks **Voice** and set **Duck amount** to lower other audio while narration is audible. New projects enable Auto duck and mark the first audio track as Voice.
4. **Play:** Press **Space** to play/pause, click the timeline to seek, and use **Ctrl+mouse wheel** to zoom.
5. **Fullscreen:** Press **F11** to enter fullscreen and **Escape** to leave it. Use **Subtitles** and **Track controls** for captions and the mixer.
6. **Save:** Press **Ctrl+S** to save a project or **Ctrl+O** to reopen one.

Projects reference media files rather than embedding them. Keep projects and their media together when moving them between computers.

## Limitations

DubDeck supports whole-file arrangement and playback. It does not trim or split clips, add fades or automation, or export an audio/video mix. Frame/sample accuracy and seamless video cuts are not guaranteed.

## Build from source

On Windows, run these commands in PowerShell from the repository root:

```powershell
.\scripts\Build.ps1 -Portable
.\scripts\Build.ps1 -Publish
```

`-Portable` creates the standalone EXE; `-Publish` creates the folder edition. Outputs appear under `artifacts/`. The script downloads the required .NET SDK locally and runs the core tests. Initial setup requires an internet connection.

## License

DubDeck is licensed under the [MIT License](LICENSE). Third-party components retain their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
