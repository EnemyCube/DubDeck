# Video Sync 1.6.0 validation

Validated on Windows 11 x64 on 2026-10-25 (America/Toronto).
Application folder: artifacts/VideoSync-1.6.0/VideoSync.exe.

Faint peak waveforms appear behind the clip text on each timeline lane, including embedded movie audio. Sound and silence follow source time; moving clips or applying track-pause alignment moves their waveform with them. Timeline gaps remain empty. Source waveforms are independent of mixer volume and mute.

Generation runs one silent, unpaced VLC PCM decoder at a time on a background worker, using the existing LibVLC instance. Importing and transport do not wait for it. Decoding retains bounded peak bins rather than PCM, and drawing queries only the visible viewport at the current zoom. Source-path/size/modification-time/duration keys identify a per-user disk cache. Missing sources retain their offline appearance, corrupt caches regenerate, and source relinking or opening a different project cancels obsolete work. Shutdown cancels and joins waveform decoding before releasing LibVLC.

- Release solution build: zero warnings and errors.
- Core: 20/20 scenarios passed, including stereo signed PCM peaks, silence/timing, transient preservation at different zoom ranges, and bounded allocation for long files.
- Native playback: 13/13 scenarios passed. New coverage decodes MP4, MOV, MKV, MP3, WAV, M4A, and FLAC waveforms; checks repeated voice/silence cues through the file end, faster-than-realtime extraction, cache reuse/recovery/invalidation, missing/corrupt sources, and cancellation after real PCM delivery. Existing transport, track pause, volume, subtitles, and ducking tests still pass.
- Published workflow: 61 checks passed, including waveforms on embedded and external audio, visible-background drawing, and existing ruler alignment and project workflows. Normal shutdown took 605 ms.
- Published performance: 13 checks passed with 10,042 clips, actual video playback, voice analysis, and simulated hardware startup recovery. Drag rendering p95 was 0.177 ms; input callback p95 was 34.25 ms. Normal shutdown took 132 ms, without forced shutdown.
- Inspected workflow/02-timeline.png: faint waveforms remain behind readable labels, and gated narration shows distinct audible and silent sections.

Evidence:
- artifacts/media-test-results/media-20261025-104615.json.
- artifacts/desktop-tests/20261025-104922/summary.json and workflow/performance folders.

Reproduce with scripts/Build.ps1 -Publish -MediaTests, then scripts/Test-Desktop.ps1 -Runs 1.
For focused decoding checks, run tests/VideoSync.MediaTests with --waveform-only --fixtures artifacts/media-fixtures.
The ten-minute playback soak was not repeated; previous reports remain available. Prior layout validation is preserved in artifacts/validation-1.5.3.md.

Published VideoSync.dll SHA-256: 4A823689FF71A3F92DB14D5CC166D7F85BCFD1A47CD7F1CE512E0F70EC9972E4.

## Portable edition

Executable: artifacts/VideoSync-1.6.0-Portable/VideoSync.exe.

- The output contains only VideoSync.exe (115136231 bytes).
- All 61 packaged workflow checks passed, including embedded/external audio waveforms, native video presentation, controls, and project round trips.
- Shutdown completed normally in 112 ms.
- Evidence: artifacts/desktop-tests/20261025-105135/summary.json.
- Rebuild: scripts/Build.ps1 -Portable.
- Repeat: scripts/Test-Desktop.ps1 -Runs 0 -ApplicationPath artifacts/VideoSync-1.6.0-Portable/VideoSync.exe.

Portable VideoSync.exe SHA-256: 796C1D6587213E4ED29C4B5A09A6FE67ECF51407E1C7FA996DD3BE7B84DF0EEE.
