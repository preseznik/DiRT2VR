#pragma once
#include <windows.h>
#include <array>
#include <cstdio>

namespace vr {
// One render-thread writer, launcher reader. Fixed 32-bit fields also work across x86/x64.
class ResolutionReport {
public:
    struct Shared {
        unsigned magic,version,requestedWidth,requestedHeight;
        volatile LONG sequence;
        std::array<unsigned,6> actual; // backbuffer, left eye, right eye (width/height)
    };
    static_assert(sizeof(Shared)==44);
    using Logger=void(*)(const char*);
    explicit ResolutionReport(const wchar_t* name) {
        if(!name || !*name) return;
        mapping_=OpenFileMappingW(FILE_MAP_READ|FILE_MAP_WRITE,FALSE,name);
        if(mapping_) shared_=static_cast<Shared*>(MapViewOfFile(mapping_,FILE_MAP_READ|FILE_MAP_WRITE,0,0,sizeof(Shared)));
        if(shared_ && (shared_->magic!=0x32565252 || shared_->version!=1)) {
            UnmapViewOfFile(shared_); shared_=nullptr;
        }
    }
    ~ResolutionReport() { if(shared_) UnmapViewOfFile(shared_); if(mapping_) CloseHandle(mapping_); }
    ResolutionReport(const ResolutionReport&)=delete;
    ResolutionReport& operator=(const ResolutionReport&)=delete;
    bool Active() const { return shared_!=nullptr; }
    void Observe(const std::array<unsigned,6>& actual,Logger log=nullptr) {
        if(!shared_ || actual==shared_->actual) return;
        InterlockedIncrement(&shared_->sequence);
        shared_->actual=actual;
        InterlockedIncrement(&shared_->sequence);
        if(log && reports_<32) {
            char message[256]{};
            snprintf(message,sizeof(message),"VR resolution requested=%ux%u backbuffer=%ux%u left=%ux%u right=%ux%u source_match=%d",
                shared_->requestedWidth,shared_->requestedHeight,actual[0],actual[1],actual[2],actual[3],actual[4],actual[5],
                shared_->requestedWidth==actual[0] && shared_->requestedHeight==actual[1]);
            log(message);
            if(++reports_==32) log("VR resolution change logging capped at 32 reports; latest dimensions remain available");
        }
    }
private:
    HANDLE mapping_{};
    Shared* shared_{};
    unsigned reports_{};
};
}
