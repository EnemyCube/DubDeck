# Video Sync 1.0.1 validation

Validated on Windows 11 x64 on 2026-10-24.

## Fixes

- Retain the visible timeline drawing and render playhead/drag feedback separately.
- Cull clips and ruler labels using the actual scroll viewport; cache metadata/source availability.
- Coalesce playback UI notifications and disable continuous native diagnostic polling in normal use.
- Keep existing track controls and decoders when clips move; skip unrelated seeks.
- Stop native outputs asynchronously while the video HWND remains attached. Repeated close requests wait for cleanup. Unsaved-project confirmation remains in place.
- Retry a stalled hardware decoder with software decoding. A deterministic startup-stall regression exercises this recovery.
- Publish correctly from the network share whose path contains an apostrophe.

## Current results

- Release solution build: zero warnings and errors.
- Core scenarios: 12/12 passed.
- Native media scenarios: 9/9 passed, including MP4/MOV/MKV/MP3/WAV/M4A/FLAC decode/output, seeking, transitions, audio tails, independent volume and mute.
- Packaged workflow: 14 checks passed.
- Three packaged performance runs loaded a timeline longer than six hours with 10,042 clips. Each checked real playback, viewport culling, 1,000 playhead updates without rebuilding the ruler/text, drag feedback, real clip moves, decoder reuse, and repeated close requests while video was still playing.
- The first performance run deliberately simulated hardware startup failure and verified actual software-decoded playback.

| Metric | Run 1 (software recovery) | Run 2 | Run 3 |
| --- | ---: | ---: | ---: |
| Drag redraw p95 | 0.155 ms | 0.142 ms | 0.155 ms |
| Input callback interval p95 | 34.9 ms | 35.5 ms | 34.9 ms |
| Two committed moves | 19.4 ms | 22.9 ms | 22.3 ms |
| Normal close while playing | 312 ms | 578 ms | 605 ms |
| Forced shutdown | No | No | No |

Only three clips and eleven ruler labels needed drawing in the test viewport. All three playback-close runs received repeated close requests and completed normal cleanup without using the five-second last-resort watchdog.

These measurements cover this machine and generated fixtures. The release did not repeat the original ten-minute timing soak; its original report is preserved separately. Native output counters and decoded-frame checks do not measure physical display/speaker latency.

## Evidence and reproduction

- Packaged executable: artifacts/VideoSync/VideoSync.exe (1.0.1).
- Current packaged desktop results: artifacts/desktop-tests/20261024-175941/summary.json.
- Native media results: artifacts/media-test-results/media-20261024-180112.json.
- Reproduce desktop regressions: scripts/Test-Desktop.ps1 -Runs 3.
- Build/publish: scripts/Build.ps1 -Publish.

Packaged VideoSync.dll SHA-256:
0E2A49C1592B311B1745BE627257307AE15CA5FE6288F92B86E8C496965561EF
