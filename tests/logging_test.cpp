#include "common.h"
#include <cstdio>
int wmain(int argc,wchar_t** argv) {
    const bool enabled=argc==2 && wcscmp(argv[1],L"on")==0;
    auto folder=std::filesystem::temp_directory_path()/(L"dirt2vr-logging-test-"+std::to_wstring(GetCurrentProcessId()));
    if(std::filesystem::exists(folder)) return 1;
    SetEnvironmentVariableW(L"DIRT2VR_OUTPUT",folder.c_str());
    SetEnvironmentVariableW(L"DIRT2VR_LOGGING",enabled ? L"1" : (argc==2 ? L"0" : nullptr));
    if(vr::LoggingEnabled()!=enabled) return 2;
    vr::Log("test log");
    { auto csv=vr::TraceFile(folder/L"frames.csv"); csv << "test frame\n"; }
    { auto binary=vr::TraceFile(folder/L"camera.bin",std::ios::binary); binary.write("abc",3); }
    if(!enabled) {
        if(std::filesystem::exists(folder)) return 3;
        // Disabling logging must also leave old logs untouched.
        std::filesystem::create_directory(folder);
        { std::ofstream existing(folder/L"frames.csv"); existing << "preserve"; }
        { auto csv=vr::TraceFile(folder/L"frames.csv"); csv << "replace"; }
        std::ifstream existing(folder/L"frames.csv"); std::string value; existing >> value;
        if(value!="preserve") return 4;
    } else if(!std::filesystem::exists(folder/L"trace.log") || std::filesystem::file_size(folder/L"camera.bin")!=3) return 5;
    for(auto file:{L"trace.log",L"frames.csv",L"camera.bin"}) std::filesystem::remove(folder/file);
    std::filesystem::remove(folder);
    puts(enabled ? "Enabled logging writes diagnostics." : "Disabled logging creates nothing and preserves existing logs.");
    return 0;
}
