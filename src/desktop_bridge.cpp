#include "desktop_bridge.h"
#include <bcrypt.h>
#include <filesystem>
#include <new>
#include <cstdio>
#include <chrono>
#include <algorithm>

namespace vr {
using Microsoft::WRL::ComPtr;
DesktopBridge::DesktopBridge(std::wstring helper,Report report,DWORD waitMilliseconds,std::wstring mode,std::wstring arguments):
    helper_(std::move(helper)),mode_(std::move(mode)),arguments_(std::move(arguments)),report_(report),
    waitMilliseconds_(std::clamp<DWORD>(waitMilliseconds,1,2000)) {}
DesktopBridge::~DesktopBridge() { Stop(); }
void DesktopBridge::Note(const char* message) const { if(report_) report_(message); }
void DesktopBridge::Stop() {
    pending_=pendingReady_=inFlight_=false;
    if(stop_) SetEvent(stop_);
    // The job owns only the helper we created; closing it never touches the game.
    if(job_) CloseHandle(job_);
    job_=nullptr;
    if(shared_) UnmapViewOfFile(shared_);
    shared_=nullptr;
    for(auto handle:{mapping_,request_,done_,stop_,process_}) if(handle) CloseHandle(handle);
    mapping_=request_=done_=stop_=process_=nullptr; processId_=0;
    inputLock_.Reset(); outputLock_.Reset(); input_.Reset(); output_.Reset();
    context_.Reset(); device_.Reset();
}
bool DesktopBridge::Fail(const char* message) {
    Note(message); disabled_=true; Stop(); return false;
}
bool DesktopBridge::Start(ID3D11Texture2D* frame,const D3D11_TEXTURE2D_DESC& desc) {
    if(!std::filesystem::path(helper_).is_absolute() ||
       GetFileAttributesW(helper_.c_str())==INVALID_FILE_ATTRIBUTES)
        return Fail("unavailable: helper path must name an existing absolute executable");
    if(desc.SampleDesc.Count!=1) return Fail("unavailable: transport requires a single-sample backbuffer");
    if(!desc.Width || !desc.Height || desc.Width>8192 || desc.Height>8192 ||
       desc.MipLevels!=1 || desc.ArraySize!=1 ||
       (desc.Format!=DXGI_FORMAT_R8G8B8A8_UNORM && desc.Format!=DXGI_FORMAT_B8G8R8A8_UNORM))
        return Fail("unavailable: transport supports single RGBA8/BGRA8 surfaces only");
    frame->GetDevice(&device_); device_->GetImmediateContext(&context_);
    ComPtr<IDXGIDevice> dxgi; ComPtr<IDXGIAdapter> adapter; DXGI_ADAPTER_DESC adapterDesc{};
    if(FAILED(device_.As(&dxgi)) || FAILED(dxgi->GetAdapter(&adapter)) || FAILED(adapter->GetDesc(&adapterDesc)))
        return Fail("unavailable: could not identify game adapter");
    auto sharedDesc=desc; sharedDesc.SampleDesc={1,0};
    sharedDesc.Usage=D3D11_USAGE_DEFAULT; sharedDesc.CPUAccessFlags=0;
    sharedDesc.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET;
    sharedDesc.MiscFlags=D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;
    if(FAILED(device_->CreateTexture2D(&sharedDesc,nullptr,&input_)) ||
       FAILED(device_->CreateTexture2D(&sharedDesc,nullptr,&output_)) ||
       FAILED(input_.As(&inputLock_)) || FAILED(output_.As(&outputLock_)))
        return Fail("unavailable: shared GPU texture allocation failed");
    ComPtr<IDXGIResource> inResource,outResource; HANDLE inHandle{},outHandle{};
    if(FAILED(input_.As(&inResource)) || FAILED(output_.As(&outResource)) ||
       FAILED(inResource->GetSharedHandle(&inHandle)) || FAILED(outResource->GetSharedHandle(&outHandle)))
        return Fail("unavailable: GPU texture handles could not be exported");
    ULONG random[4]{};
    if(BCryptGenRandom(nullptr,reinterpret_cast<PUCHAR>(random),sizeof(random),BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0)
        return Fail("unavailable: session identifier allocation failed");
    wchar_t name[128]{};
    swprintf_s(name,L"Local\\DiRT2VR-bridge-%lu-%08lx%08lx%08lx%08lx",GetCurrentProcessId(),random[0],random[1],random[2],random[3]);
    const std::wstring base=name;
    mapping_=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(bridge::Shared),base.c_str());
    if(!mapping_ || GetLastError()==ERROR_ALREADY_EXISTS) return Fail("unavailable: session mapping creation failed");
    shared_=static_cast<bridge::Shared*>(MapViewOfFile(mapping_,FILE_MAP_ALL_ACCESS,0,0,sizeof(bridge::Shared)));
    if(!shared_) return Fail("unavailable: session mapping could not be opened");
    new(shared_) bridge::Shared{};
    shared_->parent=GetCurrentProcessId(); shared_->adapter=adapterDesc.AdapterLuid;
    shared_->width=desc.Width; shared_->height=desc.Height; shared_->format=desc.Format;
    shared_->inputHandle=reinterpret_cast<uintptr_t>(inHandle); shared_->outputHandle=reinterpret_cast<uintptr_t>(outHandle);
    request_=CreateEventW(nullptr,FALSE,FALSE,(base+L"-request").c_str());
    done_=CreateEventW(nullptr,FALSE,FALSE,(base+L"-done").c_str());
    stop_=CreateEventW(nullptr,TRUE,FALSE,(base+L"-stop").c_str());
    job_=CreateJobObjectW(nullptr,nullptr);
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags=JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if(!request_ || !done_ || !stop_ || !job_ ||
       !SetInformationJobObject(job_,JobObjectExtendedLimitInformation,&limits,sizeof(limits)))
        return Fail("unavailable: helper lifecycle setup failed");
    std::wstring command=L"\""+helper_+L"\" "+mode_+L" \""+base+L"\""+(arguments_.empty()?L"":L" "+arguments_);
    STARTUPINFOW startup{}; startup.cb=sizeof(startup); PROCESS_INFORMATION pi{};
    if(!CreateProcessW(helper_.c_str(),command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW|CREATE_SUSPENDED,
                       nullptr,nullptr,&startup,&pi)) return Fail("unavailable: helper launch failed");
    process_=pi.hProcess; processId_=pi.dwProcessId;
    if(!AssignProcessToJobObject(job_,process_)) {
        TerminateProcess(process_,1); CloseHandle(pi.hThread);
        return Fail("unavailable: helper could not be assigned to its private job");
    }
    const bool resumed=ResumeThread(pi.hThread)!=static_cast<DWORD>(-1); CloseHandle(pi.hThread);
    if(!resumed) return Fail("unavailable: helper could not start");
    desc_=desc; started_=GetTickCount64();
    char started[96]{};sprintf_s(started,"starting x64 helper (%ls) with %lu ms wait budget",mode_.c_str(),waitMilliseconds_);
    Note(started); return true;
}
// Shared checks: (re)start the helper for this frame shape; true once it is ready.
bool DesktopBridge::Ready(ID3D11Texture2D* frame,const D3D11_TEXTURE2D_DESC& desc) {
    ComPtr<ID3D11Device> device; frame->GetDevice(&device);
    if(shared_ && (device.Get()!=device_.Get() || desc.Width!=desc_.Width || desc.Height!=desc_.Height ||
                   desc.Format!=desc_.Format || desc.SampleDesc.Count!=desc_.SampleDesc.Count)) Stop();
    if(!shared_) { Start(frame,desc); return false; } // Startup never waits on Present.
    if(WaitForSingleObject(process_,0)!=WAIT_TIMEOUT) return Fail("disabled: helper exited; original frame retained");
    const auto status=static_cast<bridge::Status>(InterlockedCompareExchange(&shared_->status,0,0));
    if(status==bridge::Status::Starting) {
        if(GetTickCount64()-started_>StartupLimit()) return Fail("disabled: helper startup timed out; original frame retained");
        return false;
    }
    if(status!=bridge::Status::Ready) return Fail("disabled: helper reported failure; original frame retained");
    return true;
}
// Finishes an outstanding pipelined request and discards its result.
bool DesktopBridge::Drain() {
    if(!inFlight_) return true;
    HANDLE waits[]{stop_,process_,done_};
    if(WaitForMultipleObjects(3,waits,FALSE,DWORD(StallLimit()))!=WAIT_OBJECT_0+2) return Fail("disabled: helper stalled; original frame retained");
    if(outputLock_->AcquireSync(1,100)!=S_OK || outputLock_->ReleaseSync(0)!=S_OK) return Fail("disabled: output ownership failed");
    inFlight_=false; return true;
}
bool DesktopBridge::TransferPipelined(ID3D11Texture2D* frame) {
    if(disabled_ || !frame) return false;
    D3D11_TEXTURE2D_DESC desc{}; frame->GetDesc(&desc);
    if(!Ready(frame,desc)) return false;
    if(pending_) {
        // A late synchronous (Photo Mode) frame: let it finish and discard it.
        HANDLE waits[]{stop_,process_,done_};
        if(!pendingReady_ && WaitForMultipleObjects(3,waits,FALSE,DWORD(StallLimit()))!=WAIT_OBJECT_0+2)
            return Fail("disabled: helper stalled; original frame retained");
        if(outputLock_->AcquireSync(1,100)!=S_OK || outputLock_->ReleaseSync(0)!=S_OK) return Fail("disabled: late output ownership failed");
        pending_=pendingReady_=false;
    }
    bool previous=false;
    if(inFlight_) {
        // The helper had a whole frame; waiting here paces the game to the helper.
        HANDLE waits[]{stop_,process_,done_};
        const auto waited=WaitForMultipleObjects(3,waits,FALSE,waitMilliseconds_);
        if(waited==WAIT_TIMEOUT) {
            ++dropped_;
            if(GetTickCount64()-pendingSince_>StallLimit()) return Fail("disabled: helper stalled; original frame retained");
            return false;
        }
        if(waited!=WAIT_OBJECT_0+2) return Fail("disabled: helper exited; original frame retained");
        if(InterlockedCompareExchange(&shared_->status,0,0)!=static_cast<LONG>(bridge::Status::Ready) ||
           shared_->completed!=sequence_) return Fail("disabled: mismatched frame; original frame retained");
        if(outputLock_->AcquireSync(1,100)!=S_OK) return Fail("disabled: output ownership unavailable; original frame retained");
        previous=true;
    }
    // Send this frame before replacing it with the previous result.
    if(inputLock_->AcquireSync(0,100)!=S_OK) {
        if(previous) outputLock_->ReleaseSync(0);
        return Fail("disabled: input ownership unavailable; original frame retained");
    }
    context_->CopyResource(input_.Get(),frame);
    if(inputLock_->ReleaseSync(1)!=S_OK) return Fail("disabled: input ownership release failed");
    if(previous) {
        context_->CopyResource(frame,output_.Get());
        if(outputLock_->ReleaseSync(0)!=S_OK) return Fail("disabled: output ownership release failed");
        ++completed_;
        if(completed_==1) Note("pipelined neural frames active (one frame of latency)");
    }
    context_->Flush();
    shared_->requested=++sequence_; pendingSince_=GetTickCount64();
    ResetEvent(done_); SetEvent(request_); inFlight_=true;
    return previous;
}
bool DesktopBridge::Transfer(ID3D11Texture2D* frame) {
    if(disabled_ || !frame) return false;
    D3D11_TEXTURE2D_DESC desc{}; frame->GetDesc(&desc);
    if(!Ready(frame,desc) || !Drain()) return false;
    if(pending_) {
        if(GetTickCount64()-pendingSince_>StallLimit()) return Fail("disabled: helper stalled; original frame retained");
        pendingReady_=pendingReady_ || WaitForSingleObject(done_,0)==WAIT_OBJECT_0;
        if(!pendingReady_) return false;
        if(shared_->completed!=sequence_) return Fail("disabled: mismatched late frame; original frame retained");
        const auto acquired=outputLock_->AcquireSync(1,0);
        if(acquired==WAIT_TIMEOUT) return false;
        if(acquired!=S_OK) return Fail("disabled: late output ownership failed");
        if(outputLock_->ReleaseSync(0)!=S_OK) return Fail("disabled: late output release failed");
        // Never present a previous frame: release its resources and try a fresh
        // capture on the next Present. A scheduling hitch need not end a session.
        pending_=pendingReady_=false;
        return false;
    }
    // Timeout is a positive HRESULT, so SUCCEEDED is deliberately not used.
    const auto deadline=std::chrono::steady_clock::now()+std::chrono::milliseconds(waitMilliseconds_);
    const auto acquired=inputLock_->AcquireSync(0,0);
    if(acquired==WAIT_TIMEOUT) return false;
    if(acquired!=S_OK) return Fail("disabled: input ownership unavailable; original frame retained");
    context_->CopyResource(input_.Get(),frame); context_->Flush();
    if(inputLock_->ReleaseSync(1)!=S_OK) return Fail("disabled: input ownership release failed");
    const auto requested=++sequence_; shared_->requested=requested;
    pendingSince_=GetTickCount64();
    ResetEvent(done_); SetEvent(request_);
    HANDLE waits[]{stop_,process_,done_};
    const auto remaining=[&]() {
        const auto left=deadline-std::chrono::steady_clock::now();
        return left>decltype(left)::zero() ? static_cast<DWORD>(std::chrono::ceil<std::chrono::milliseconds>(left).count()) : 0u;
    };
    const auto waited=WaitForMultipleObjects(3,waits,FALSE,remaining());
    if(waited==WAIT_TIMEOUT) {
        pending_=true; ++dropped_;
        if(dropped_==1) Note("frame deadline missed; original retained, late output will be discarded");
        return false;
    }
    if(waited!=WAIT_OBJECT_0+2) return Fail("disabled: helper exited; original frame retained");
    if(InterlockedCompareExchange(&shared_->status,0,0)!=static_cast<LONG>(bridge::Status::Ready) ||
       shared_->completed!=requested) return Fail("disabled: mismatched frame; original frame retained");
    const auto acquiredOutput=outputLock_->AcquireSync(1,remaining());
    if(acquiredOutput==WAIT_TIMEOUT) { pending_=pendingReady_=true; ++dropped_; return false; }
    if(acquiredOutput!=S_OK) return Fail("disabled: output ownership unavailable; original frame retained");
    context_->CopyResource(frame,output_.Get()); context_->Flush();
    if(outputLock_->ReleaseSync(0)!=S_OK) return Fail("disabled: output ownership release failed");
    ++completed_;
    if(completed_==1) Note("matching-frame x86/x64 GPU transport active");
    return true;
}
bool DesktopBridgeRequested(const wchar_t* mode,const wchar_t* headset,bool logging) {
    return logging && mode && wcscmp(mode,L"transport")==0 && (!headset || wcscmp(headset,L"1")!=0);
}
}
