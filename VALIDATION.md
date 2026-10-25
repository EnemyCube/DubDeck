# Video Sync 1.6.1 validation

Validated on Windows 11 x64 on 2026-10-25 (America/Toronto).
Application folder: artifacts/VideoSync-1.6.1/VideoSync.exe.

All workspace and fullscreen Stop buttons open a confirmation before issuing any transport command. Cancel is focused after the popup opens; Escape, Cancel, or an outside click dismisses the question. Playback continues in its existing state until confirmed. Confirming Stop returns the playhead to 00:00.

The subtitles menu has rounded dark chrome, a source filename, descriptive rows, grouped embedded/file choices, clear selection and hover indicators, and a pinned Open subtitle file button. The list scrolls independently of the file action. Escape dismisses the menu, and F11 remains available. Subtitles, track controls, and Stop confirmation dismiss each other and stay above native video.

The timeline heading/instruction text was removed. Ducking and zoom controls share one compact row. An empty timeline shows a drag-and-drop prompt and Import media button below the ruler; it disappears during import and while clips are present, and returns for a new/emptied arrangement. Existing lane/ruler alignment remains covered.

- Release solution build and application publish: no warnings or errors.
- Core: 20/20 scenarios passed.
- Final published workflow: 76 checks passed. Coverage includes empty prompt visibility, paused and playing Stop cancellation, default Cancel focus, confirmed rewind, workspace/video/audio-only Stop buttons, embedded and sidecar subtitle choices, selection application, fullscreen menus, and existing waveform/project/alignment behavior. Normal shutdown took 1,750 ms without forced shutdown.
- Performance regression: 13 checks passed with more than 10,000 clips, actual playback, voice analysis, hardware startup fallback, dragging, and repeated close requests. Normal shutdown took 105 ms.
- The final pinned-footer/default-focus adjustments were followed by a new packaged workflow run. The performance scenario was not repeated for those popup-only adjustments.
- Inspected the empty workspace, Stop confirmation, and final subtitle-menu PNGs. The file action remains visible below the scrollable choices.

Evidence:
- artifacts/desktop-tests/20261025-113826/summary.json (final application-folder workflow).
- artifacts/desktop-tests/20261025-112808/summary.json (initial workflow and performance).
- workflow/01-empty.png, workflow/02-stop-confirmation.png, and workflow/05-subtitles-menu.png.

Rebuild with scripts/Build.ps1 -Publish.
Repeat the workflow with scripts/Test-Desktop.ps1 -Runs 0; use -Runs 1 to include performance.
Native media and ten-minute soak suites were not repeated for these UI changes; previous results are preserved in artifacts/validation-1.6.0.md.

Published VideoSync.dll SHA-256: 5168DF86568771B5FAB6EA8FFF950357D73B5B77B32D947017DA5986AC998431.

## Portable edition

Executable: artifacts/VideoSync-1.6.1-Portable/VideoSync.exe.

- The output contains only VideoSync.exe (115141085 bytes).
- All 76 packaged workflow checks passed, including confirmation/default Cancel focus, subtitle grouping and selection, pinned file action, and empty timeline prompt.
- Shutdown completed normally in 164 ms.
- Evidence: artifacts/desktop-tests/20261025-114122/summary.json.
- Rebuild: scripts/Build.ps1 -Portable.
- Repeat: scripts/Test-Desktop.ps1 -Runs 0 -ApplicationPath artifacts/VideoSync-1.6.1-Portable/VideoSync.exe.

Portable VideoSync.exe SHA-256: DCED8CFF009B2873A10382F7A97DDA0B60E41DE51BA72D3295B340FB03E31694.
