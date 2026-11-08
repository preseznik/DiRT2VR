# Direct multiplayer libraries

Follow the Windows build prerequisites in the LAN source archive README and build the standard LAN library first. Install Python 3.10 or later with `pefile==2024.8.26` into the build interpreter. Set `DIRT2VR_PYTHON` to its executable if needed. In a project checkout, run `tools/bootstrap.ps1 -Direct` to prepare the build dependencies.

Run `tools/lan/direct/build.cmd`. The command builds, binds and verifies the host and client libraries separately. The release packager copies `build/direct-host/payload/xlive.dll` and `build/direct-client/payload/xlive.dll` into the Experimental payload as `direct-host.dll` and `direct-client.dll`. The corresponding SHA-256 digests are recorded in `source-info.json`. The source archive includes the MinHook sources and license under `.deps/minhook`.
