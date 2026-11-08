cmake_minimum_required(VERSION 3.24)
project(DiRT2VRDirect LANGUAGES C CXX)
if(NOT ROLE MATCHES "^(host|client)$")
  message(FATAL_ERROR "Set ROLE to host or client")
endif()
get_filename_component(ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../.." ABSOLUTE)
set(CMAKE_MSVC_RUNTIME_LIBRARY MultiThreaded)
set(DXSDK_DIR "${ROOT}/.deps/dxsdk-jun10/DXSDK")
# Reuse dependency sources from the normal LAN build when available.
if(EXISTS "${ROOT}/build/lan/cmake/CPM_0.35.1.cmake")
  file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/cmake")
  configure_file("${ROOT}/build/lan/cmake/CPM_0.35.1.cmake" "${CMAKE_BINARY_DIR}/cmake/CPM_0.35.1.cmake" COPYONLY)
endif()
foreach(dep opus rapidjson rapidxml)
  if(IS_DIRECTORY "${ROOT}/build/lan/_deps/${dep}-src")
    string(TOUPPER "${dep}" upper)
    set(FETCHCONTENT_SOURCE_DIR_${upper} "${ROOT}/build/lan/_deps/${dep}-src" CACHE PATH "")
  endif()
endforeach()
add_subdirectory("${ROOT}/tools/lan" lan EXCLUDE_FROM_ALL)
set_target_properties(XLiveLessNess PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/payload"
  LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/payload" ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/payload"
  PDB_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/payload")
set_source_files_properties("${ROOT}/tools/lan/integration.cpp" TARGET_DIRECTORY XLiveLessNess
  PROPERTIES COMPILE_DEFINITIONS "DiRT2VRLanInitialize=DirectLabPrepareLan")
set(RX "${CMAKE_CURRENT_SOURCE_DIR}/engine/native-rx-choice-v1-20261018")
if(ROLE STREQUAL "host")
  set(ROLE_SOURCE "${RX}/native-pair-host-rx-v1.cpp")
else()
  set(ROLE_SOURCE "${RX}/native-client-rx-v1.cpp")
endif()
set(MH "${ROOT}/.deps/minhook")
target_sources(XLiveLessNess PRIVATE "${ROLE_SOURCE}" "${CMAKE_CURRENT_SOURCE_DIR}/entry.cpp"
  "${MH}/src/buffer.c" "${MH}/src/hook.c" "${MH}/src/trampoline.c" "${MH}/src/hde/hde32.c")
set_source_files_properties("${CMAKE_CURRENT_SOURCE_DIR}/entry.cpp" TARGET_DIRECTORY XLiveLessNess
  PROPERTIES INCLUDE_DIRECTORIES "${CMAKE_BINARY_DIR}")
target_include_directories(XLiveLessNess PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}" "${MH}/include"
  "${CMAKE_CURRENT_SOURCE_DIR}/engine/direct-bootstrap")
