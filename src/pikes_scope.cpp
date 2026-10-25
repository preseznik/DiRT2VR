#include "pikes_scope.h"
#include "pikes_layouts.h"
#include "common.h"
#include <Windows.h>
#include <shellapi.h>
#include <bcrypt.h>
#include <array>
#include <fstream>

namespace vr {
namespace {
const PikesLayout* selectedLayout{};
std::filesystem::path PracticePath(const std::filesystem::path& lab) {
    int count{};auto args=CommandLineToArgvW(GetCommandLineW(),&count);if(!args)return {};
    std::filesystem::path path;unsigned demos{};
    for(int i=1;i<count;++i)if(_wcsicmp(args[i],L"-demo")==0) {
        ++demos;if(i+1<count)path=args[++i];
    }
    LocalFree(args);if(demos!=1 || path.empty())return {};
    if(path.is_relative())path=lab/path;
    path=path.lexically_normal();
    const auto managed=(lab/L"DiRT2VR/p.xml").lexically_normal();
    if(_wcsicmp(path.wstring().c_str(),managed.wstring().c_str()))return {};
    return path;
}
std::string FileHash(const std::filesystem::path& path) {
    std::ifstream file(path,std::ios::binary);if(!file)return {};
    BCRYPT_ALG_HANDLE algorithm{};BCRYPT_HASH_HANDLE hash{};
    if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return {};
    if(BCryptCreateHash(algorithm,&hash,nullptr,0,nullptr,0,0)<0){BCryptCloseAlgorithmProvider(algorithm,0);return {};}
    std::array<char,65536> buffer{};bool ok=true;
    while(file){file.read(buffer.data(),buffer.size());if(BCryptHashData(hash,reinterpret_cast<PUCHAR>(buffer.data()),static_cast<ULONG>(file.gcount()),0)<0)ok=false;}
    std::array<UCHAR,32> digest{};ok=ok&&BCryptFinishHash(hash,digest.data(),32,0)>=0;
    BCryptDestroyHash(hash);BCryptCloseAlgorithmProvider(algorithm,0);
    char hex[65]{};for(unsigned i=0;i<32;++i)sprintf_s(hex+i*2,3,"%02X",digest[i]);return ok?hex:"";
}
}
bool PikesScope() {
    static const bool allowed=[] {
        wchar_t executable[32768]{};GetModuleFileNameW(nullptr,executable,_countof(executable));
        const auto lab=std::filesystem::path(executable).parent_path().lexically_normal();
        const auto config=PracticePath(lab);if(config.empty())return false;
        std::ifstream input(config,std::ios::binary|std::ios::ate);auto size=input.tellg();if(size<=0 || size>4096)return false;
        input.seekg(0);std::string xml(static_cast<size_t>(size),'\0');if(!input.read(xml.data(),xml.size()))return false;
        // Desktop and VR use the same verified collision archive and launch contract.
        wchar_t discipline[16]{};
        const auto disciplineLength=GetEnvironmentVariableW(L"DIRT2VR_STAGGERED_DISCIPLINE",discipline,16);
        const bool race=disciplineLength==5 && !wcscmp(discipline,L"rally");
        if(disciplineLength && !race)return false;
        wchar_t gridValue[8]{};
        const auto gridLength=GetEnvironmentVariableW(L"DIRT2VR_PIKES_GRID_RACE",gridValue,8);
        const bool gridRace=gridLength==1 && gridValue[0]==L'1';
        if((gridLength && !gridRace) || (gridRace && disciplineLength))return false;
        for(const auto& layout:pikesLayouts) {
            auto car=ParsePikesPracticeXml(xml,layout.country,layout.folder);
            std::vector<std::string> cars;
            if(race || gridRace) {
                if(std::string_view(layout.country)!="usa")continue;
                cars=ParsePikesRaceXml(xml,layout.country,layout.folder,gridRace?8:4);
                if(cars.empty())continue;
                car=cars.front();
            } else {
                if(car.empty())continue;
                cars.push_back(car);
            }
            for(const auto& code:cars)
                if(!std::filesystem::is_regular_file(lab/L"cars"/code/L"cameras.xml"))return false;
            const auto track=lab/L"tracks"/layout.country/layout.folder/L"route_0/track.jpk";
            std::ifstream archive(track,std::ios::binary);std::array<unsigned,8> header{};
            if(!archive.read(reinterpret_cast<char*>(header.data()),sizeof(header)) || header[0]!=1262571594 || header[2]!=layout.chunks+1)return false;
            const auto hash=FileHash(track);const bool exact=hash==layout.hash;
            Log("pikes-normal-scope allowed=%d config=%s car=%s track=%s chunks=%u jpk-sha=%s",exact,config.string().c_str(),car.c_str(),layout.folder,layout.chunks,hash.c_str());
            if(!exact) { Log("pikes: unsupported collision archive; stopping before loading terrain"); ExitProcess(ERROR_BAD_EXE_FORMAT); }
            selectedLayout=&layout;
            return exact;
        }
        return false;
    }();
    return allowed;
}
unsigned PikesChunks(){return PikesScope() && selectedLayout ? selectedLayout->chunks:0;}
bool PikesPerformanceAssets(){
    static const bool enabled=[] {
        if(!PikesScope() || !selectedLayout || std::string_view(selectedLayout->country)!="usa")return false;
        wchar_t folder[64]{};
        const auto length=GetEnvironmentVariableW(L"DIRT2VR_PIKES_PERFORMANCE_ASSETS",folder,_countof(folder));
        if(!length || length>=_countof(folder))return false;
        const std::wstring selected(selectedLayout->folder,selectedLayout->folder+strlen(selectedLayout->folder));
        const bool matches=selected==folder;
        if(matches)Log("pikes performance assets: source tree detail transition=250m; full world visibility retained");
        return matches;
    }();
    return enabled;
}
const char* PikesHash(){return PikesScope() && selectedLayout ? selectedLayout->hash:"";}
}
