# DubDeck 1.8.2 validation

Validated locally on Windows 11 x64 on 2026-10-31 (America/Toronto). This release has not been published to GitHub.

Application source commit: 8018f14b7cd6705744474e096be5eb1a35607d72. Both editions report ProductVersion 1.8.2 with this source revision and FileVersion 1.8.2.0.

## Changes

- Fullscreen hides the cursor with its playback controls and restores it on mouse movement or exit.
- New windows start the timeline at 10% zoom.
- Native Windows media keys work while DubDeck has focus, including fullscreen and video/audio-only transitions. Held Play/Pause keys do not repeatedly toggle playback.
- Quiet non-Voice audio, including whispered video dialogue, retains its mix level during commentary. Louder passages receive up to the selected duck amount.
- Quiet protection uses pre-duck RMS levels after the track volume control: no reduction at or below -30 dBFS, transitioning to the selected amount at -24 dBFS. Commentary activation retains its existing threshold.
- Commentary mute and removal of the Voice designation retain the smooth ducking release.

## Results

- Release solution and both self-contained editions build successfully; the solution build reports zero warnings and errors.
- Core: 22/22 scenarios passed, including quiet-passage protection, intermediate loudness, track mix levels, disabled ducking and the existing envelope behavior.
- Native media: 14/14 checks passed across MP4, MOV, MKV, MP3, WAV, M4A and FLAC, including constant commentary over alternating loud and whisper-level video audio.
- Folder edition: 226/226 desktop workflow checks passed; normal shutdown completed in 142 ms.
- Portable edition: 226/226 desktop workflow checks passed; normal shutdown completed in 89 ms.
- Folder performance: 13/13 checks passed with 10,042 clips, actual playback, simulated hardware-decoder startup fallback, dragging, voice analysis and repeated close requests. Normal shutdown completed in 209 ms.
- Inspected rendered workspace and fullscreen track-control screenshots.
- Both editions contain eight matching README, license, third-party notice, bundled license and workspace-image files. The portable bundle was extracted and inspected.
- All 1,666 folder ZIP entries passed CRC verification and SHA-256 comparison with the tested folder edition.

Generated fixtures were staged on the local disk for repeatable timing. The ten-minute soak was not repeated for this release.

## Local release assets

| Asset | Bytes | SHA-256 |
| --- | ---: | --- |
| DubDeck-1.8.2-win.x64-portable.exe | 115502765 | 5c6e4a0c5be5ffbd2fddaa41b984060d34456702c6e541e70ffba4ba00cb5db4 |
| DubDeck-1.8.2-win-x64.zip | 214173193 | 1154e2f5362fe5fb3d3668aae8d68411d30a107e5bd6d799616c96fd0eefe17e |

Folder DubDeck.dll SHA-256: 71bfa17bc78299dfc4044c0fb0e29c4233cc224f7e15f46dea9bcae313e46091.

## Local evidence

- artifacts/release-1.8.2-validation/test-summary.json
- artifacts/release-1.8.2-validation/native-media.json
- artifacts/release-1.8.2-validation/folder-workflow/
- artifacts/release-1.8.2-validation/folder-performance/
- artifacts/release-1.8.2-validation/portable-workflow/
- artifacts/release-1.8.2-validation/versions.json
- artifacts/release-1.8.2-validation/bundled-documents.json
- artifacts/release-1.8.2-validation/packages.json
- artifacts/release-1.8.2-validation/SHA256SUMS.txt
- artifacts/release-1.8.2-notes.md
- Previous release validation: artifacts/validation-1.8.1.md

Rebuild locally with scripts/Build.ps1 -Publish or scripts/Build.ps1 -Portable. Run packaged desktop coverage with scripts/Test-Desktop.ps1 and the desired ApplicationPath.
