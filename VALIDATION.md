# DubDeck 1.8.1 validation

Validated on Windows 11 x64 on 2026-10-26 (America/Toronto).

Application source commit: 117a439d28f6ea6469aab72751d96b6e185edf2c. Both editions report ProductVersion 1.8.1 with this source revision and FileVersion 1.8.1.0.

## Results

- Release solution builds: zero warnings and errors.
- Core: 21/21 scenarios passed, including the 30% ducking default, preserved explicit saved amounts, and fallback for projects without a stored amount.
- Native media: 13/13 checks passed across MP4, MOV, MKV, MP3, WAV, M4A and FLAC, including waveform generation, playback, seeking, subtitles, track pause/resume, volume/mute and voice ducking.
- Folder edition: 216/216 desktop workflow checks passed; normal shutdown completed in 705 ms.
- Folder performance: 13/13 checks passed with 10,042 clips, actual playback, decoder-startup fallback, dragging, voice analysis and repeated close requests; normal shutdown completed in 96 ms.
- Portable edition: 216/216 desktop workflow checks passed; normal shutdown completed in 81 ms.
- Menu tests cover theme/subtitle button toggle logic, keyboard activation and Escape, including timeline, fullscreen video and fullscreen audio-only controls.
- Media-control tests cover Windows media commands, routed media-key input, 10-second skips and timeline bounds, repeated Play/Pause, Stop confirmation, focus restrictions and fullscreen video/audio transitions.
- All eight themes, saved preferences, live icon/timeline refresh, contrast, project round trips and existing playback/mixer behavior remain covered.
- Inspected rendered workspace and fullscreen track-control screenshots.
- Portable bundle README, license, third-party notices, all bundled license texts and workspace image match the source.
- Folder ZIP: all 1,666 entries passed CRC verification; executable, application DLL, README, licenses, notices, screenshot and native VLC files match the tested folder.

Generated fixture copies on the local disk were used for repeatable timing. The ten-minute soak was not repeated for this release.

## Release assets

| Asset | Bytes | SHA-256 |
| --- | ---: | --- |
| DubDeck-1.8.1-win.x64-portable.exe | 115501152 | 892317718aac9a41bc6adf01db0385bb7ef7c5c319594378c881479306983049 |
| DubDeck-1.8.1-win-x64.zip | 214171104 | addd7c3e807beac338d4e0a1f3325b470c9b5f80fa628c5e0fdde0d95c5d37a2 |

Folder DubDeck.dll SHA-256: 965f4739d0e901fab8540363e77deca58502b36009813351e3c4749cab7f32af.

## Local evidence

- artifacts/media-test-results/media-20261026-152603.json
- artifacts/release-1.8.1-validation/folder-workflow/
- artifacts/release-1.8.1-validation/folder-performance/
- artifacts/release-1.8.1-validation/portable-workflow/
- artifacts/release-1.8.1-validation/SHA256SUMS.txt
- Previous release validation: artifacts/validation-1.8.0.md

Rebuild with scripts/Build.ps1 -Publish or scripts/Build.ps1 -Portable. Run packaged desktop coverage with scripts/Test-Desktop.ps1 and the desired ApplicationPath.
