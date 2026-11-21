cmake_minimum_required(VERSION 3.24)
project(DiRT2VRDesktopBridge LANGUAGES CXX)
if(NOT WIN32 OR NOT MSVC OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "The desktop bridge helper requires MSVC x64")
endif()
# x64 helper for the Win32 game: frame transport, and DLSS 5 Neural Rendering with a
# player-supplied runtime. No NVIDIA SDK code is built in (see THIRD_PARTY_NOTICES.md).
add_executable(dirt2vr-desktop-bridge main.cpp neural.cpp)
target_compile_features(dirt2vr-desktop-bridge PRIVATE cxx_std_20)
target_compile_definitions(dirt2vr-desktop-bridge PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(dirt2vr-desktop-bridge PRIVATE /W4 /EHsc)
target_link_libraries(dirt2vr-desktop-bridge PRIVATE d3d11 d3d12 dxgi d3dcompiler)
set_property(TARGET dirt2vr-desktop-bridge PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
# MIT caller shim (ComfyUI-DLSS5-NR, pinned commit 41dcdfa); the runtime checks the calling module's name.
add_library(dirt2vr-nr-caller SHARED third_party/nr-caller/caller_shim.cpp)
set_target_properties(dirt2vr-nr-caller PROPERTIES OUTPUT_NAME "nvngx.dll_dirt2vr"
  MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
add_dependencies(dirt2vr-desktop-bridge dirt2vr-nr-caller)
option(DIRT2VR_BRIDGE_LAB "Also build the local-only lab tools from tests/" ON)
if(DIRT2VR_BRIDGE_LAB AND EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/../../tests/DesktopBridgeHost.cmake")
  include("${CMAKE_CURRENT_SOURCE_DIR}/../../tests/DesktopBridgeHost.cmake")
endif()
