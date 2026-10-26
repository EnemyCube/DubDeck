# DubDeck 1.8.0 validation

Validated on Windows 11 x64 on 2026-10-26 (America/Toronto).

Application code commit: 462292a56d0eec5dc2495b32fb1a600b83b2eec9, following the themes merge into main. Both editions report ProductVersion 1.8.0 and FileVersion 1.8.0.0.

## Results

- Release solution build: zero warnings and errors.
- Core: 21/21 scenarios passed.
- Native media: 13/13 checks passed across MP4, MOV, MKV, MP3, WAV, M4A and FLAC fixtures, including waveform generation, playback, seeking, subtitles, individual track pause/resume, volume/mute and voice ducking.
- Folder edition: 187/187 desktop workflow checks passed; normal shutdown completed in 1,473 ms.
- Folder performance: 13/13 checks passed with 10,042 clips, actual playback, decoder-startup fallback, dragging, voice analysis and repeated close requests; normal shutdown completed in 363 ms.
- Portable edition: 187/187 desktop workflow checks passed; normal shutdown completed in 94 ms.
- Theme coverage includes all eight palettes, persisted preferences, icon refresh, contrast, and stable yellow paused-track indicators.
- Fullscreen coverage includes toggling Track controls closed, retaining it during mixer interaction, outside-click dismissal, dismissal when leaving the application, audio-only playback, and Escape.
- Inspected rendered workspace and fullscreen mixer screenshots, including the palette icon, integrated track rows and yellow resume button.
- Verified the portable bundle contains matching README, MIT license, third-party notices, bundled license texts and documentation image.
- Verified the folder ZIP contains matching executable, application DLL, README, licenses, notices and VLC files.

An initial native run using fixtures on the SMB workspace timed out while reading an MP3. The complete native suite and both packaged desktop suites passed using unchanged fixture copies on the local disk. The ten-minute soak was not repeated for this interface release.

## Release assets

| Asset | Bytes | SHA-256 |
| --- | ---: | --- |
| DubDeck-1.8.0-win.x64-portable.exe | 116356202 | d32da6a0508ad74ef48c335f9715148d5f9a6cd5a0aaff40eaf3f16fbef266e1 |
| DubDeck-1.8.0-win-x64.zip | 209072511 | 527a4a2f312ef3242eab5127523497e0f40757b85d60933e479f5c330b98747f |

Folder DubDeck.dll SHA-256: 68e96ec665c81cc136ba4c560c4e7e7aadc5a4c6fa4086b95871bcf8cc7c7a49.

## Local evidence

- artifacts/media-test-results/media-20261026-112023.json
- artifacts/release-1.8.0-validation/folder-workflow/
- artifacts/release-1.8.0-validation/folder-performance/
- artifacts/release-1.8.0-validation/portable-workflow/
- artifacts/SHA256SUMS.txt
- Previous release validation: artifacts/validation-1.7.0.md

Rebuild with scripts/Build.ps1 -Publish or scripts/Build.ps1 -Portable. Run packaged desktop coverage with scripts/Test-Desktop.ps1 and the desired ApplicationPath.
