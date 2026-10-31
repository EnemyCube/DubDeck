# DubDeck 1.8.2 validation

Built and validated locally on Windows 11 x64 on 2026-10-31 (America/Toronto), from a34fa3ae074b1e8b1d0089c3cb173f2667c2d4d5 with the release version set to 1.8.2. Published to GitHub on 2026-10-31 as [DubDeck 1.8.2](https://github.com/EnemyCube/DubDeck/releases/tag/v1.8.2), tagged at c2841f4201ad26ba8d204b032f02099f8494dc57. The main branch and release tag are pushed; this is the latest release.

## Release contents

- Default zoom is 5%; fullscreen cursor hiding and focused media play/pause are included.
- Audio ducking preserves quiet passages. Auto level and its blocking source analysis are removed; waveform generation runs in the background.
- Only current .dubdeck project fields and the current waveform cache layout are supported.
- Includes main-window shutdown and modern pointer-input fixes.
- Self-contained folder ZIP and portable EXE both include .NET, VLC, notices/licenses and the README image. Native libraries are x64 only.
- Both editions report FileVersion 1.8.2.0 and ProductVersion 1.8.2+a34fa3ae074b1e8b1d0089c3cb173f2667c2d4d5.

## Checks passed

- Solution build: zero warnings/errors. Core: 23/23. Native media: 15/15, with exit code 0.
- Folder and portable workflows: 227/227 each, with actual process exit. Normal media cleanup took 116 ms and 88 ms respectively.
- Folder performance: 13/13 with 10,042 clips, actual playback, decoder fallback, waveform/activity analysis, dragging and repeated close requests. Normal shutdown took 78 ms.
- Reviewed workspace and fullscreen mixer screenshots. Both editions contain eight matching bundled documents/assets.
- All 835 ZIP entries passed CRC and SHA-256 comparison with the tested folder; copied packages match staging.

Ten-minute soak and physical pen/touch checks were not repeated.

## Local release assets

| Asset | Bytes | SHA-256 |
| --- | ---: | --- |
| DubDeck-1.8.2-win.x64-portable.exe | 115503347 | 1b75772d9b921694e77fb125157133b125702ee158da83d8b54467c21acf744b |
| DubDeck-1.8.2-win-x64.zip | 117407618 | 63016a7ee29b41a5497bca7434721df90a7ab443680d516c84a8f1b74d1b25dd |

Folder DubDeck.dll SHA-256: 821fd4b4c7583782f49557f8d43cca007e4ca2687b866b9f33b310fb2718ba19.

Ready files are under artifacts/DubDeck-1.8.2/, artifacts/DubDeck-1.8.2-Portable/, artifacts/DubDeck-1.8.2-win-x64.zip and artifacts/SHA256SUMS.txt.

## Other builds cleanup

Removed 15 build-output targets (23079 files, 8082864642 logical bytes), including the old ZIP, previous isolated builds, extracted portable copies, current temporary staging and the stale build locator. Source, SDK, fixtures, release/debug evidence and the tested 1.8.2 packages are retained.

One obsolete build remains: artifacts/DubDeck-1.9.0-Portable/DubDeck-1.9.0-win.x64-portable.exe. Windows denied deletion through both mapped and canonical share paths and denied resetting its permissions. Deleting that file requires access from its filesystem/share owner. Cleanup is not fully complete while it remains.

## Evidence

artifacts/release-1.8.2-validation/ contains build/publish/core/native logs, native-media.json, folder-workflow/, portable-workflow/, folder-performance/, test-summary.json, versions.json, bundled-documents.json, source-files.json, packages.json, SHA256SUMS.txt and cleanup-summary.json. prior-validation.md preserves the previous development/release validation record. Staging paths and bundle extraction locations in historical evidence were removed during cleanup.

Rebuild with scripts/Build.ps1 -Publish or -Portable. See scripts/Release-Tools.md for packaging and validation helpers.

## GitHub publication

The portable EXE, folder ZIP and SHA256SUMS.txt are published with the release. GitHub-reported SHA-256 digests and sizes match the validated local assets. The public latest-release API and final download URLs were checked after publication. Details are recorded in artifacts/release-1.8.2-validation/github-release.json.
