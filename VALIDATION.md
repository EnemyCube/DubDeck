# Video Sync 1.5.1 validation

Validated on Windows 11 x64 on 2026-10-24 (America/Toronto).
Executable: artifacts/VideoSync-1.5.1/VideoSync.exe. Keep the complete published folder together.

Successfully importing a video now sets the project title from its filename without the extension. The most recently imported video supplies the title when several videos are imported. Audio imports leave it unchanged. The header, window title and both fullscreen titles update together. The video title is retained on save and suggested as the filename for a new project; unnamed audio-only projects can still take their title from the chosen save filename.

- Release build: zero warnings and errors.
- Core: 17/17 checks passed.
- Packaged workflow: 47 checks passed, including matching project/window/fullscreen names after video/audio import and retaining the video title when saving to a differently named project file. Normal shutdown took 467 ms.
- Packaged performance: 12 checks passed with over 10,000 clips, voice analysis, simulated hardware startup recovery, decoder reuse and repeated close requests during playback. Normal shutdown took 81 ms without forced shutdown.

Evidence: artifacts/desktop-tests/20261025-001529/summary.json and its workflow/performance folders (UTC-stamped).
Reproduce: scripts/Build.ps1 -Publish, then scripts/Test-Desktop.ps1 -Runs 1.
Prior full native playback validation is preserved in earlier validation reports; native media and the ten-minute soak were not repeated for this naming change.

Published VideoSync.dll SHA-256: 4CC0F940444699D82A649E75B5F08D2D68D55228542AB5C96429F4F5D38393C6.
