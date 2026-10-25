# Media integration checks

These checks load the deployed LibVLC runtime and decode real synthetic media.
Video goes to native memory callbacks with actual pixel buffers; no video window
is created or automated. Audio uses the real Windows output device. The separate
visible application UI smoke checks WPF/native GPU rendering. Native playback or
output failures fail this run rather than being reported as passes.

Run from the repository root with a Windows desktop audio session and ffmpeg
available to generate the developer fixtures:

```powershell
./scripts/Generate-Fixtures.ps1 -IncludeSoak
./.tools/dotnet/dotnet.exe run --project tests/DubDeck.MediaTests -c Release -- --initialize-vlc-cache
./.tools/dotnet/dotnet.exe run --project tests/DubDeck.MediaTests -c Release
./.tools/dotnet/dotnet.exe run --project tests/DubDeck.MediaTests -c Release -- --soak
```

The smoke run covers MP4, MOV, MKV, MP3, WAV, M4A and FLAC imports and actual
native decode/output; corrupt and missing paths; pause/resume and paused/playing
seeks; rapid seeks; gaps and adjacent video transitions; overlapping audio and
its tail after video ends; track/master gain and mute; project end and stop.
Coordinated preroll can freeze the common transport while newly starting files
become ready, so boundary checks allow bounded native readiness waits.

`--soak` adds ten real minutes with one video and two independent audio players.
Each second records the common transport, raw native timestamps, extrapolated
native timestamps, and actual decoded/output counters. The run checks bounded
drift and its growth, continued native output every minute, delivered video
frames, and real black/white pulse pixel transitions. The soak starts explicitly
at zero and keeps all output muted. Gain arithmetic is checked while paused;
mute isolation is checked briefly during the smoke playback.

JSON reports and the soak's CSV samples go to `artifacts/media-test-results`.
Native timestamps are coarse. These measurements cannot establish speaker
latency, audible phase alignment, or the instant a frame appears on a physical
display. The pulse fixtures also support a separate manual A/V output check.

Use `--fixtures <path>` for an alternate generated fixture directory. The cache
command is optional: it scans deployed native plugins once and writes VLC's
plugin cache beside them, improving repeated startup from a network drive.
Regenerate the cache after replacing native plugin binaries. ffmpeg is only a
fixture-generation dependency and is not required by the application.
