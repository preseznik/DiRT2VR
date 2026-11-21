# Desktop bridge helper: third-party notices

## ComfyUI-DLSS5-NR caller shim (MIT)

`third_party/nr-caller/caller_shim.cpp` is unmodified from
https://github.com/lisitskyaa/ComfyUI-DLSS5-NR at commit `41dcdfa593cb61b6a98c65bb8ed27606260bb598`
(SHA-256 `5a7a2f50978bbc1aaae6c8c4d732d42fe1cdf345931f97d2be6c2ff74a977535`). Its licence is in
`third_party/nr-caller/LICENSE`. The minimal NGX interface declarations in `neural.cpp` follow that
project's `bridge.cpp`.

## NVIDIA

NVIDIA, DLSS, NGX and GeForce are trademarks of NVIDIA Corporation. This helper contains no NVIDIA
SDK code, headers or binaries. At run time it loads the NGX core (`_nvngx.dll`) that comes with the
NVIDIA display driver, and an `nvngx_dlssnr.dll` that the player supplies. DiRT2VR does not
distribute either file, and its licence grants no rights in NVIDIA software.
