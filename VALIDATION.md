# Video Sync validation

Validated on Windows 11 x64 on 2026-10-24.

- Release builds completed with zero warnings and zero errors using the project-local .NET SDK 10.0.402.
- All 12 deterministic core scenarios passed: timeline placement, overlap rejection, half-open clip boundaries, track types, gain/mute, validation, atomic saves, portable paths, offline references, and malformed projects.
- All 9 native media scenarios passed with MP4, MOV, MKV, MP3, WAV, M4A, and FLAC: real decoding/output, missing/corrupt files, play/pause, paused/playing/rapid seeks, gaps, adjacent videos, external audio tails, and independent volume/mute.
- All 14 visible WPF acceptance checks passed, including actual native video presentation, fullscreen restoration, black gaps, New/Open, project round trips, and keeping missing sources available for relinking.
- The self-contained package loaded coreclr.dll, libvlc.dll, and libvlccore.dll from its own folder with external .NET roots disabled.
- The ten-minute native run passed with one video and two overlapping audio players. It delivered 15,000 decoded frames and 1,200 decoded black/white pulse transitions. Audio and video output counters continued growing throughout.
- Estimated engine clock drift peaked at 24 ms for video and 10 ms for the two external audio players. First/last-minute p95 remained 24/24 ms for video and 9/9 ms for each audio player. Native timestamp updates are coarse; these figures do not measure physical speaker/display latency.

Evidence:
- Core executable: tests/VideoSync.Tests
- Native report: artifacts/media-test-results/media-20261024-170737.json
- Ten-minute samples: artifacts/media-test-results/soak-20261024-170737.csv
- WPF results/layout PNGs/module proof: artifacts/ui-proof/
- Packaged application: artifacts/VideoSync/VideoSync.exe

Validated application DLL SHA-256:
7C2CCF67E2B8B08856A9D5C9D84EDB511B582A8AF0B7E84C300BA800A27F4657

Headless media tests use native memory video output with software decoding and real DirectSound audio output. The separate normal-window WPF test verifies the native GPU video target and presentation counters. Fullscreen PNGs capture the WPF overlay; hardware video itself is verified through native presentation counters.

Known v1 limits: whole-file placement, everyday playback synchronization, no trimming/splitting/fades/automation/export, and no guarantee of seamless video cuts or frame/sample accuracy.
