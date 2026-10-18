# Third-party software and licenses

EasyHotspot's original source code and documentation are licensed under the
[MIT License](LICENSE). This does not replace the licenses of third-party code,
libraries, runtimes, or tools. Their copyright notices and license terms remain
in effect. The MSI includes this document, the project license, and the complete
notices in `ThirdParty/`. They can also be read offline in **About → Documentation
and licenses** in the application.

## WinDivert 2.2.2 — LGPL-3.0

Copyright ReQrypt and contributors. Upstream:
<https://github.com/basil00/WinDivert/tree/v2.2.2>.

This distribution elects the GNU Lesser General Public License version 3 option
offered by upstream. EasyHotspot dynamically loads the unmodified x64
`WinDivert.dll` and the signed `WinDivert64.sys` in `Native/`. Compatible modified
versions may replace these files. Reverse engineering for debugging modifications
to this library is permitted; the project license adds no restriction on doing so.

- Full LGPLv3, GPLv3, and upstream alternative GPLv2 texts:
  [ThirdParty/WinDivert/LICENSE](ThirdParty/WinDivert/LICENSE).
- Distribution details and binary checksums:
  [ThirdParty/WinDivert/NOTICE.txt](ThirdParty/WinDivert/NOTICE.txt).
- Corresponding upstream source is included in
  [ThirdParty/WinDivert/WinDivert-2.2.2-source.tar.gz](ThirdParty/WinDivert/WinDivert-2.2.2-source.tar.gz).

The EasyHotspot application sources and build scripts are available in this
repository and in each GitHub Release's source archives. No signature or
integrity check in EasyHotspot prevents replacing an interface-compatible
WinDivert library. Windows' own driver signing requirements still apply.

## Packet stack and Go dependencies

`EasyHotspot.PacketStack.exe` is built from `PacketStack/`, using the exact module
versions in `go.mod` and `go.sum`. It includes third-party code under its original
licenses, including:

- Go runtime and standard library 1.27.2 — BSD-3-Clause.
- tun2socks v2.6.0, copyright Jason Lyu — MIT.
- gVisor `eede7a881b20`, copyright The gVisor Authors — Apache-2.0.
- go-gost/relay v0.5.0 — MIT.
- google/btree v1.1.3 — Apache-2.0.
- google/uuid v1.6.0 — BSD-3-Clause.
- Uber atomic v1.11.0, multierr v1.11.0, and zap v1.27.0 — MIT.
- golang.org/x/crypto v0.38.0, x/sys v0.33.0, and x/time v0.11.0 — BSD-3-Clause.

Complete copyright notices and license texts, including retained notices for
build/test dependencies, are in [ThirdParty/PacketStack/](ThirdParty/PacketStack/).
[README.txt](ThirdParty/PacketStack/README.txt) records dependency versions and
upstream locations. The Apache license and BSD/MIT notices apply to the respective
components of the combined executable; they are not relicensed as MIT by this
project's license.

## .NET, Windows App SDK, and TraceEvent

- Microsoft .NET 10 runtime and System.ServiceProcess.ServiceController 10.0.0 —
  [MIT license](ThirdParty/DotNet-LICENSE.txt), with additional component notices
  in [DotNet-THIRD-PARTY-NOTICES.txt](ThirdParty/DotNet-THIRD-PARTY-NOTICES.txt).
- Microsoft Windows App SDK WinUI 1.8.260803003 and its runtime components —
  [Microsoft Windows App SDK Software License Terms](ThirdParty/WindowsAppSDK-LICENSE.txt) and
  [notices](ThirdParty/WindowsAppSDK-NOTICE.txt).
- Microsoft WebView2 SDK loader 1.0.3179.45 —
  [BSD-style redistribution license](ThirdParty/WebView2-LICENSE.txt).
- Microsoft.Diagnostics.Tracing.TraceEvent 3.2.8 —
  [MIT license](ThirdParty/TraceEvent-LICENSE.txt).

The Windows App SDK binary packages are distributed under Microsoft's terms;
their open-source components retain the licenses identified in the accompanying
notices. EasyHotspot's MIT license does not relicense these Microsoft binaries.
The separately installed WebView2 browser runtime, if used by WinUI, has its own
Microsoft terms and is not included as a browser runtime in this MSI.

Microsoft Windows and its SDK/build tools are separate products subject to their
own terms. Their installation is a prerequisite for building or running the app,
not a grant under EasyHotspot's MIT license. WiX and the .NET/Go SDKs used to build
the MSI are not installed as tools by the EasyHotspot installer.

## Redistribution

Keep the project license, this notice, and applicable third-party license and
notice files with redistributed copies. Preserve WinDivert's corresponding
source and replacement rights when redistributing the bundled binaries. Changes
to third-party components may create additional notice or source obligations
under those components' licenses.
