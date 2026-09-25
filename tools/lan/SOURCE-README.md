# DiRT2VR LAN library source

This archive contains the modified XLiveLessNess source used for the matching
DiRT2VR release, together with integration code, build scripts and tests.
It is not needed to run the launcher or game. `source-info.json` identifies
the source revisions, integration patch and distributed DLL SHA-256.

The upstream source is under `.deps/xlivelessness`, with our changes already
applied. Do not apply `tools/lan/xlln-integration.patch` again. Its license is
`.deps/xlivelessness/LICENSE.md` (LGPL 2.1); retain upstream notices.

## Build on Windows

Install Visual Studio C++ x86 tools, a Windows SDK, CMake and Ninja. Set
`DIRT2VR_VS_ROOT` if Visual Studio is not installed at the default path in
`tools/lan/build.cmd`. The build also needs the legacy DirectX June 2010 SDK
headers at `.deps/dxsdk-jun10/DXSDK`; proprietary SDK files are not included.

Run `tools/lan/build.cmd` from a command prompt. CMake downloads the dependencies
declared in `.deps/xlivelessness/cmake/packages.cmake`, builds the x86 DLL and
runs the native LAN tests. Network access is required on the first build.
The DLL is written to `.deps/xlivelessness/bin/xlive.dll`; DiRT2VR distributes
it as `DiRT2VR/payload/xlive-lan.dll`.

Do not run CMake's install target: the upstream default can target Windows
system directories. This source archive does not install or modify game files.
