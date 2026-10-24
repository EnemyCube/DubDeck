# Video Sync 1.2.0 validation

Validated on Windows 11 x64 on 2026-10-24. The executable is artifacts/VideoSync-1.2.0/VideoSync.exe. Keep the complete published folder together.

## Functionality

- Application and window icon are included.
- Embedded MKV and sidecar MP4 subtitles are selected, rendered into real decoded pixels, and disabled. Automatic selection and switching an ongoing subtitle cue are covered.
- Fullscreen exposes every track volume, mute, Pause/Resume, and Voice control, plus master transport/volume, subtitles, and ducking settings. Mixer values stay synchronized with the workspace and audio-only fullscreen mode.
- Independent video and audio pause preserve the frozen source while other players continue. Resume catches up to the timeline, including clip boundaries and gaps. Stop clears individual pauses.
- Voice-triggered ducking lowers other tracks by the selected percentage, leaves Voice tracks at their own levels, and restores levels during silence. Tests cover mute, pause, amount changes, paused/playing seeks, global pause/resume, and rapid seeks. Known-amplitude PCM verifies the detector reads actual sound levels.
- Ducking and subtitle preferences round-trip through version 1 projects; runtime track pauses are not saved. Older projects retain defaults.

## Results

- Release build: zero warnings and errors.
- Core: 15/15 scenarios passed.
- Native media: 12/12 scenarios passed, including MP4, MOV, MKV, MP3, WAV, M4A, FLAC, corrupt/missing input, transitions, gaps, audio tails, subtitles, track pause, and voice activity.
- Packaged workflow: 23 checks passed; normal shutdown 416 ms.
- Three packaged performance runs passed with 10,042 clips, a timeline over six hours, video playback, external audio, and active voice analysis. The first deliberately stalls hardware startup and verifies software recovery. Each closes during actual playback with repeated close requests.

| Metric | Run 1 (software recovery) | Run 2 | Run 3 |
| --- | ---: | ---: | ---: |
| Drag redraw p95 | 0.138 ms | 0.120 ms | 0.209 ms |
| Input callback interval p95 | 34.6 ms | 34.2 ms | 34.6 ms |
| Two committed moves | 20.8 ms | 20.0 ms | 27.5 ms |
| Normal close during playback | 96 ms | 630 ms | 676 ms |
| Forced shutdown | No | No | No |

Only three clips and eleven ruler labels required drawing in the tested viewport. One thousand playhead updates and continuous drag feedback did not rebuild static timeline text/ruler geometry. Clip moves retained controls and native decoders. Voice analysis measured the sine fixture at -21.07 dBFS and attenuated non-Voice players to 40% for a 60% duck amount.

The packaged process loaded coreclr.dll, libvlc.dll, and libvlccore.dll from the release folder, confirming the bundled runtime and native media libraries were used. Window and fullscreen layout screenshots were reviewed. WPF screenshots do not capture native hardware video; native output/pixel checks verify it separately.

## Evidence and reproduction

- Desktop results, shutdown reports, screenshots, and loaded-module paths: artifacts/desktop-tests/20261024-191315/summary.json and its workflow/performance folders.
- Full native suite: artifacts/media-test-results/media-20261024-191056.json.
- Final mixer/startup-pause regressions (including an immediate global pause while the detector opens): artifacts/media-test-results/media-20261024-191449.json. The tested app binary matches the published DLL.
- Reproduce: scripts/Build.ps1 -MediaTests -Publish, then scripts/Test-Desktop.ps1 -Runs 3.
- Earlier performance validation is preserved in artifacts/validation-1.0.1.md.

This release did not repeat the earlier ten-minute timing soak. Run scripts/Build.ps1 -Soak to repeat it. Synchronization targets everyday playback; these measurements do not establish frame/sample accuracy or physical speaker/display timing. Voice detection responds to sound above its threshold on marked Voice tracks, including background noise.

Published VideoSync.dll SHA-256: F9D0E44B41CDF4FF0D3943841D539C1541B119B7C60488CD73FB7AC1CAAACFEB.
