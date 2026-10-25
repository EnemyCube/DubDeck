# Third-party components

DubDeck dynamically links these unmodified components:

- LibVLCSharp and LibVLCSharp.WPF 3.10.2 — VideoLAN contributors, LGPL-2.1-or-later. Source: https://code.videolan.org/videolan/LibVLCSharp/-/tree/3.10.2
- VideoLAN.LibVLC.Windows / LibVLC 3.0.24 — VideoLAN contributors, LGPL-2.1-or-later. Source and build scripts: https://code.videolan.org/videolan/vlc/-/tree/3.0.24 and https://code.videolan.org/videolan/libvlc-nuget
- Microsoft .NET 10 Windows Desktop runtime — Microsoft and contributors, MIT and component-specific notices. See licenses/DOTNET-LICENSE.txt and licenses/DOTNET-ThirdPartyNotices.txt distributed alongside the application.

The LGPL text is included in licenses/LGPL-2.1.txt. The bundled native libraries and plugins remain separate files and may be replaced with compatible builds. In the portable single-exe edition, these files and this notice are extracted automatically under %TEMP%/.net/DubDeck (or DOTNET_BUNDLE_EXTRACT_BASE_DIR when set); compatible replacement libraries can be placed in that extracted libvlc/win-x64 directory while the app is closed. Refer to the upstream repositories for corresponding component sources and notices.

- Captions icon: Lucide Icons and Contributors, ISC License. Downloaded from https://github.com/lucide-icons/lucide/blob/5d36179bf459e730c4580c5101b85addb64dc19f/icons/captions.svg and converted to a WPF vector drawing. Original SVG: src/DubDeck.App/Assets/captions-lucide.svg. License: licenses/LUCIDE-LICENSE.txt. Icon page: https://lucide.dev/icons/captions.

Additional button icons use Lucide SVGs pinned at 7faf87628a1ad6175975bc595344337ce14a7db4 (https://github.com/lucide-icons/lucide/tree/7faf87628a1ad6175975bc595344337ce14a7db4/icons). Sources are bundled in Assets/Icons as WPF resources. The same licenses/LUCIDE-LICENSE.txt applies.
