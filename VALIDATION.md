# DubDeck 1.8.3 release validation

Validated on 2026-11-02 for Windows 11 x64. Both editions include .NET and VLC for offline use. Published as [DubDeck 1.8.3](https://github.com/EnemyCube/DubDeck/releases/tag/v1.8.3). The portable EXE, folder ZIP, and SHA256SUMS.txt were downloaded from GitHub and verified against the local release; ZIP CRCs also pass.

The release fixes timeline scrub synchronization between embedded video audio and external audio, seeks near media ends, final playback status, pending seek positions, initial playback positions, ducking after backward seeks, audio tails, delayed audio waveforms, and retained subtitle-path validation. Release tools now verify source file sets, preserve checks under optimized Python, and reject accidental package overwrites.

## Validation

- Release builds: zero warnings and errors; 24/24 core scenarios.
- Native media: 19/19, including repeated forward/backward scrubs, seven codecs/containers, near-EOF seeks, and ten real minutes of coordinated video plus two audio streams.
- Scrub samples: maximum 2 ms difference between estimated video/audio clock errors. This is a sampled software-clock measurement, not a physical output phase measurement.
- Soak: 15,000 delivered frames and 1,200 decoded audio pulse transitions. Maximum estimated drift: video 12 ms; audio tracks 35 ms each. First/last minute audio p95: 34/34 ms. Coarse native VLC timestamps reached 347 ms deviation; no accumulating estimated drift.
- Folder workflow: 228/228; portable workflow: 228/228.
- Performance and repeated close during playback: 13/13 with 10,042 clips; no shutdown fallback.
- Release-tool regressions: 6/6, with ordinary and optimized Python subprocess coverage.
- ZIP: 835 entries; CRCs and every file match the published folder. All 835 local test staging files match the ZIP. The staged portable checksum matches the distributable.

An initial run directly from the UNC network share timed out opening a MOV and required shutdown fallback. The unchanged packages passed all desktop checks from local disk. Direct execution from this share remains unvalidated; the release checks cover local installation.

## Packages

| Asset | Bytes | SHA-256 |
| --- | ---: | --- |
| DubDeck-1.8.3-win.x64-portable.exe | 115,504,649 | `5ea840aa062f86374c4d5993634feb222cc55c6052c84cad7af6e449b9d10645` |
| DubDeck-1.8.3-win-x64.zip | 117,409,556 | `88eb70db2b117e080b317adcc3357158be153b22612cae059f63285e6384550c` |

Both executables report file version 1.8.3.0. Their informational version records build base 6071451ce51ea3364dc9fa2a290100523ac62946, with the version bump in the working tree at build time. The source snapshot records all 83 source files at 220ae1f824488b037a26148d18e605bb96090e2d; final validation documentation changes do not affect packaged code.

## Cleanup and evidence

Removed 3,299 generated files (922,860,697 bytes), including obsolete 1.8.2 packages, duplicate fixtures/reports/traces, and source/test bin/obj directories. Removed the redundant Test-Desktop.ps1 wrapper and consolidated the old review record. Kept the SDK, canonical media fixtures, current packages, and release evidence.

Local evidence: artifacts/release-1.8.3-validation/ contains build logs, native results and soak CSV, desktop reports, cleanup inventories, package/checksum manifests, source fingerprints, and the prior review record. Release notes: artifacts/release-1.8.3-notes.md.

Recheck with:

    python scripts/Verify-Release.py --check-source

Publication tag v1.8.3 resolves to 9a8203a2652cc2500ee31dd2edb392fe7974fb5f. GitHub upload digests and fresh download hashes match; github-publication.json records the publication evidence.
