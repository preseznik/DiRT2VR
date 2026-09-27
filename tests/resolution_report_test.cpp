#include "resolution_report.h"
#include <string>
#include <vector>
#include <stdexcept>

std::vector<std::string> messages;
void Require(bool value,const char* message) { if(!value) throw std::runtime_error(message); }
int wmain(int argc,wchar_t** argv) {
    try {
        if(argc==8) {
            vr::ResolutionReport writer(argv[1]);
            Require(writer.Active(),"cross-process resolution mapping unavailable");
            std::array<unsigned,6> actual{};
            for(unsigned i=0;i<6;++i) actual[i]=std::stoul(argv[i+2]);
            writer.Observe(actual);
            return 0;
        }
        const auto name=L"Local\\DiRT2VR.Resolution.Test."+std::to_wstring(GetCurrentProcessId());
        const auto mapping=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,44,name.c_str());
        Require(mapping!=nullptr,"create mapping failed");
        auto shared=static_cast<vr::ResolutionReport::Shared*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,44));
        Require(shared!=nullptr,"map view failed");
        {
            vr::ResolutionReport invalid(name.c_str());
            Require(!invalid.Active(),"invalid protocol must be ignored");
        }
        shared->magic=0x32565252; shared->version=1; shared->requestedWidth=2400; shared->requestedHeight=1800;
        {
            vr::ResolutionReport writer(name.c_str());
            const auto log=+[](const char* message) { messages.emplace_back(message); };
            writer.Observe({1280,720,0,0,0,0},log);
            Require(shared->sequence==2 && messages[0].find("source_match=0")!=std::string::npos,"startup mismatch must be reported");
            writer.Observe({1280,720,0,0,0,0},log);
            Require(shared->sequence==2 && messages.size()==1,"unchanged dimensions must do no writes or logging");
            writer.Observe({2400,1800,1700,1734,1710,1744},log);
            Require(shared->sequence==4 && shared->actual[4]==1710 && messages.back().find("source_match=1")!=std::string::npos,"resize and asymmetric eyes must be observed");
            for(unsigned i=0;i<100;++i) writer.Observe({1600+i,1200,1700,1734,1700,1734},log);
            Require(messages.size()==33 && messages.back().find("capped")!=std::string::npos,"change logging must be bounded");
            Require(shared->actual[0]==1699 && !(shared->sequence&1),"latest summary must continue after log cap");
            writer.Observe({4800,3600,3400,3468,3400,3468});
            Require(messages.size()==33 && shared->actual[0]==4800,"logging off still updates summary");
        }
        UnmapViewOfFile(shared); CloseHandle(mapping);
        vr::ResolutionReport absent(name.c_str());
        Require(!absent.Active(),"closed session mapping must not be recreated");
        puts("resolution reporting passed"); return 0;
    } catch(const std::exception& error) { puts(error.what()); return 1; }
}
