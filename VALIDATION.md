# DubDeck 1.9.0 validation

## Legacy compatibility removed ? 2026-10-31

Current source supports only .dubdeck project paths and the current complete project schema. Open-file filters and drag/drop detection no longer recognize .videosync or .dubsync; ProjectStore rejects unsupported extensions on both load and save. Missing current fields and unknown/obsolete settings are rejected instead of supplying older-project defaults or ignoring removed options. Current projects retain portable relative/absolute media paths, subtitle paths, offline relinking, mix settings and saved playhead support.

Waveforms use one cache layout from the current DubDeck cache directory. Removed old application-name cache directories, cache migration, the migration constructor parameter and the former level-bearing cache reader. Unsupported or damaged payloads regenerate in the background; current caches are reused. Existing user media and project files were not deleted by this code change.

Validation: isolated Release solution build passed with zero warnings/errors; 23/23 core scenarios and 227/227 desktop checks passed, including current-project save/reopen and normal process exit (178 ms cleanup). The first native run passed cache regeneration but failed the immediate track-mute assertion while the desktop suite was also running. An unchanged standalone rerun passed 15/15 checks and exited with code 0; the cause of the initial mute failure was not established. Both reports/logs are preserved under artifacts/remove-legacy-validation/ (native-first-failure.json, native-media.json, native*.log, core.log, build.log and workflow/). The earlier Auto level removal compatibility notes below describe that earlier snapshot and are superseded by this change.

No release assets were rebuilt or published by this change.

## Auto level removed ? 2026-10-31

Current source removes Auto level from the workspace and both fullscreen mixers, project settings and playback. Transport preparation no longer waits for full-source loudness analysis. Waveforms still generate in the background, without calculating loudness statistics. Existing peak-only and former level-bearing waveform caches are reused without decoding; older projects' autoLevelEnabled fields are ignored, and newly saved projects omit that setting. Manual master/track volumes and quiet-passage-aware ducking remain active.

Validation: isolated Release solution build passed with zero warnings/errors; 23/23 core scenarios, 15/15 real native media checks and 227/227 desktop workflow checks passed. The native process exited with code 0; the desktop process exited normally with media cleanup in 154 ms. Reviewed the updated workspace and fullscreen track controls screenshots. Evidence: artifacts/remove-auto-level-validation/ (build.log, core.log, native-media.json, native-exit-code.txt and workflow/).

The existing 1.9.0 release downloads were not rebuilt or published by this change; the release results below describe their historical source snapshot.

Validated locally on Windows 11 x64 on 2026-10-31 (America/Toronto). Nothing was pushed or published to GitHub.

Built from the local source tree based on 01fed29824891e1ee29d4d6eb3872130beb2eec7, including the release shutdown fixes. Exact fingerprints for 85 source/document files are recorded in source-files.json.
Both editions report ProductVersion 1.9.0+01fed29824891e1ee29d4d6eb3872130beb2eec7 and FileVersion 1.9.0.0.

## Changes

- Added Auto level in the workspace and both fullscreen mixers, enabled by default for new projects.
- Existing projects keep their previous mix until Auto level is enabled; the choice is saved.
- Fixed per-file corrections use the 95th percentile of active 400 ms stereo RMS blocks, targeting -20 dBFS. Boosts are capped at 12 dB and decoded sample peaks retain 3 dB of headroom.
- Corrections preserve within-file dynamics and quiet dialogue, without moving the track faders. Automatic gain is accounted for before ducking.
- Full-rate 48 kHz stereo analysis shares the waveform decoder and cached results. Legacy peak-only caches are upgraded once.
- Quiet detection and level corrections account for the cubic DirectSound volume curve.
- Shutdown follows the main window after asynchronous media cleanup, even if auxiliary WPF windows remain.
- Modern Windows pointer input avoids a legacy WPF WISP pen-service deadlock during dispatcher shutdown.

## Results

- Release solution builds successfully with zero warnings and errors in the final isolated build; final folder and portable publishing succeeded.
- Core: 24/24 scenarios passed, including level matching, stereo PCM, dynamics, peak headroom, saved settings and legacy-project defaults.
- Native media: 15/15 checks passed across MP4, MOV, MKV, MP3, WAV, M4A and FLAC, including automatic matching, quiet dialogue, ducking, seeks, opt-out and enabling levels during playback.
- Folder edition: 230/230 desktop workflow checks passed; normal media cleanup completed in 128 ms and the process exited normally.
- Portable edition: 230/230 desktop workflow checks passed; normal media cleanup completed in 103 ms and the process exited normally.
- Both workflow runs deliberately leave an auxiliary WPF window open to verify actual main-window shutdown.
- Folder performance: 13/13 checks passed with 10,042 clips, actual playback, decoder-startup fallback, waveform/level analysis, dragging and repeated close requests. Normal shutdown completed in 83 ms.
- Inspected the rendered workspace and fullscreen mixer screenshots.
- Both editions contain eight matching README, license, third-party notice, bundled-license and workspace-image files. The portable bundle was extracted and inspected.
- All 1666 ZIP entries passed CRC verification and SHA-256 comparison with the tested folder. Copies in artifacts match staging.

Generated fixtures were staged locally for repeatable timing. The ten-minute soak and physical pen/touch hardware tests were not repeated.

## Resolved release blockers

The initial portable build encountered a running development EXE; isolated build outputs preserved that session. Early packaged workflow runs completed their checks and media cleanup but did not exit. Managed stacks identified a wait in WPF PenThreadWorker.WorkerGetTabletsInfo during mouse-capture teardown. The main-window lifetime policy and modern pointer input were enabled, then both packaged workflows and the performance run passed with actual process exit. Earlier failed-run reports and shutdown-stacks.txt are retained as diagnostic evidence.

## Local release assets

| Asset | Bytes | SHA-256 |
| --- | ---: | --- |
| DubDeck-1.9.0-win.x64-portable.exe | 115507809 | c41ce2a9dbb83bb9b6644ee5fff47e14d3ab5f86944247bd35272556d4a3411c |
| DubDeck-1.9.0-win-x64.zip | 214180597 | 027216b59007a57032134b5d5383590b121c0d77e1dc23900fb1c4527005146b |

Folder DubDeck.dll SHA-256: b57289c30c39ed37f4b8514448b742edbeb7f37dd6937e04dddbfc8170a59b40.

## Local evidence

- artifacts/release-1.9.0-validation/test-summary.json
- artifacts/release-1.9.0-validation/core.log
- artifacts/release-1.9.0-validation/native-media.json
- artifacts/release-1.9.0-validation/folder-workflow-pointer/
- artifacts/release-1.9.0-validation/folder-performance/
- artifacts/release-1.9.0-validation/portable-workflow/
- artifacts/release-1.9.0-validation/source-files.json
- artifacts/release-1.9.0-validation/versions.json
- artifacts/release-1.9.0-validation/bundled-documents.json
- artifacts/release-1.9.0-validation/packages.json
- artifacts/release-1.9.0-validation/SHA256SUMS.txt
- artifacts/release-1.9.0-validation/shutdown-stacks.txt
- artifacts/release-1.9.0-notes.md
- Previous release validation: archive/2026-10-31-cleanup/artifacts/validation-1.8.2.md

Rebuild locally with scripts/Build.ps1 -Publish or scripts/Build.ps1 -Portable. Use an isolated BaseOutputPath while a development EXE is running. Run packaged desktop coverage with scripts/Test-Desktop.ps1 and the desired ApplicationPath.

## Repository archival ? 2026-10-31

Historical artifacts, build outputs and unused assets were moved into archive/2026-10-31-cleanup/ with a file manifest and restore instructions. The complete expanded 1.9.0 distribution and matching symbols are there; its ZIP, portable EXE, checksums and release evidence remain in artifacts/. See docs/repository-cleanup-audit.md and scripts/Release-Tools.md for the current layout and retained reusable tools. No GitHub push or publication was performed.

The retained ZIP matches the release checksum and passes all ZIP CRC checks. The portable EXE was found to differ before the move: 115507800 bytes and SHA-256 d747c377e215cc48c711b96658ed52100fc6215f5c79d8529e4f9f467ef3d1c5. The earlier release manifest expects 115507809 bytes and the original hash listed above. The file and historical evidence were left untouched. Current integrity results are recorded in artifacts/archive-validation/release-integrity.json; this discrepancy must be resolved before relying on that EXE for a release.

Post-archive validation: solution rebuild passed with zero warnings/errors; core scenarios passed 24/24; native media checks passed 15/15; rebuilt desktop workflow passed 230/230 and exited normally after 148 ms of media cleanup. App and media-test output contain only libvlc/win-x64. Evidence is under artifacts/archive-validation/ (post-archive-build.log, core.log, native-media.json and post-archive-workflow/).
