# DubDeck 1.7.0 validation

Validated on Windows 11 x64 on 2026-10-25 (America/Toronto).
Application folder: artifacts/DubDeck-1.7.0/DubDeck.exe.

The application is branded DubDeck in the workspace, both fullscreen OSDs, window titles, project and error dialogs, Windows executable metadata, launchers, source namespaces/projects, solution, notices, and README. The new vector icon depicts stacked video/audio decks and is rendered into a seven-size Windows ICO.

New projects use .dubdeck. Existing .videosync and .dubsync projects are recognized on open, drop, and command-line launch; saving an existing project retains its filename. JSON remains version 1. Start-VideoSync.ps1 and Start-DubSync.ps1 forward to Start-DubDeck.ps1. Waveform caches use the DubDeck data directory and reuse compatible caches from the previous application data directories without decoding again.

- Renamed solution restore/build: zero warnings and errors.
- Core: 21/21 scenarios passed, including extension detection and legacy-to-DubDeck project round trips that preserve arrangement and playhead.
- Focused native media: 2/2 scenarios passed. All seven supported codec/container fixtures were probed and decoded into waveforms. Existing-cache migration, cache recovery/invalidation, source silence, and active cancellation were verified.
- Published application-folder workflow: 78 checks passed, including all visible branding labels and the executable assembly name, opening .dubdeck and legacy projects, and existing playback/mixer/subtitle/confirmation/waveform behavior. Normal shutdown took 646 ms.
- Published performance: 13 checks passed with more than 10,000 clips, actual playback, dragging, voice analysis, and hardware-startup fallback. Normal shutdown took 93 ms without forced shutdown.
- Inspected the rendered icon plus workspace and fullscreen screenshots. Verified the executable's Windows ProductName and FileDescription are DubDeck.

Evidence:
- artifacts/media-test-results/media-20261025-122443.json.
- artifacts/desktop-tests/20261025-122623/summary.json and workflow/performance folders.

Rebuild: scripts/Build.ps1 -Publish.
Repeat desktop coverage: scripts/Test-Desktop.ps1 -Runs 1.
Focused native check: .tools/dotnet/dotnet.exe run --project tests/DubDeck.MediaTests -c Release -- --waveform-only --fixtures artifacts/media-fixtures.
The full native transport/subtitle suite and ten-minute soak were not repeated for the rebrand; prior reports remain available. Prior release validation is preserved in artifacts/validation-1.6.1.md.

Published DubDeck.dll SHA-256: BB8E669D3DD6233F0F82F702552B7DC4404899ADC94F6A01CC1BA7DB19B09E58.

## Portable edition

Executable: artifacts/DubDeck-1.7.0-Portable/DubDeck.exe.

- The output contains only DubDeck.exe (115130287 bytes).
- All 78 packaged workflow checks passed, including DubDeck branding, new and legacy project round trips, playback, and previous UI features.
- Shutdown completed normally in 226 ms.
- Evidence: artifacts/desktop-tests/20261025-123100/summary.json.
- Rebuild: scripts/Build.ps1 -Portable.
- Repeat: scripts/Test-Desktop.ps1 -Runs 0 -ApplicationPath artifacts/DubDeck-1.7.0-Portable/DubDeck.exe.

Portable DubDeck.exe SHA-256: EEA5F3EA4BBF00A717187B1C1CE04580D971C80D3D0866828728AFCB00FE4043.
