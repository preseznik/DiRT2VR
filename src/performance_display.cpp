#include "performance_display.h"
#include "common.h"
#include <dxgi.h>
#include <pdh.h>
#include <pdhmsg.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cstdio>
#include <cstring>

namespace vr {
struct GpuCounterState {
    HANDLE stop{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
    std::atomic<double> percent{-1};
    std::wstring adapter;
    ~GpuCounterState() {if(stop)CloseHandle(stop);}
};
namespace {
struct GpuWorker {std::shared_ptr<GpuCounterState> state;HMODULE module;};
constexpr unsigned width=512,height=64;
// Small bitmap font keeps the panel independent of desktop DPI/font services.
std::array<unsigned char,7> Glyph(char c) {
    switch(c) {
    case '0':return {14,17,19,21,25,17,14};case '1':return {4,12,4,4,4,4,14};
    case '2':return {14,17,1,2,4,8,31};case '3':return {30,1,1,14,1,1,30};
    case '4':return {2,6,10,18,31,2,2};case '5':return {31,16,16,30,1,1,30};
    case '6':return {14,16,16,30,17,17,14};case '7':return {31,1,2,4,8,8,8};
    case '8':return {14,17,17,14,17,17,14};case '9':return {14,17,17,15,1,1,14};
    case 'F':return {31,16,16,30,16,16,16};case 'P':return {30,17,17,30,16,16,16};
    case 'S':return {15,16,16,14,1,1,30};case 'G':return {14,17,16,23,17,17,14};
    case 'U':return {17,17,17,17,17,17,14};case 'N':return {17,25,25,21,19,19,17};
    case 'A':return {14,17,17,31,17,17,17};case '/':return {1,2,2,4,8,8,16};
    case '.':return {0,0,0,0,0,12,12};case '%':return {25,25,2,4,8,19,19};
    default:return {};
    }
}
std::vector<uint32_t> Pixels(double fps,double gpu) {
    std::vector<uint32_t> pixels(width*height,0xff181818);
    for(unsigned y=0;y<height;++y)for(unsigned x=0;x<4;++x)pixels[y*width+x]=0xff3030df;
    char text[64],a[24],b[24];
    if(fps<0)strcpy_s(a,"N/A");else sprintf_s(a,"%.1f",fps);
    if(gpu<0)strcpy_s(b,"N/A");else sprintf_s(b,"%.0f%%",gpu);
    sprintf_s(text,"FPS %s  GPU %s",a,b);
    unsigned left=16;
    for(char c:text) {if(!c || left+20>=width)break;const auto rows=Glyph(c);
        for(unsigned y=0;y<7;++y)for(unsigned x=0;x<5;++x)if(rows[y]&(1u<<(4-x)))
            for(unsigned dy=0;dy<4;++dy)for(unsigned dx=0;dx<4;++dx)pixels[(18+y*4+dy)*width+left+x*4+dx]=0xffeeeeee;
        left+=24;
    }
    return pixels;
}
}
static void CollectGpu(GpuCounterState& self) {
    PDH_HQUERY query{};PDH_HCOUNTER counter{};
    if(PdhOpenQueryW(nullptr,0,&query)!=ERROR_SUCCESS)return;
    if(PdhAddEnglishCounterW(query,L"\\GPU Engine(*)\\Utilization Percentage",0,&counter)!=ERROR_SUCCESS) {PdhCloseQuery(query);return;}
    PdhCollectQueryData(query);
    try { while(WaitForSingleObject(self.stop,1000)==WAIT_TIMEOUT) {
        double value=-1;DWORD bytes{},count{};
        if(PdhCollectQueryData(query)==ERROR_SUCCESS &&
           PdhGetFormattedCounterArrayW(counter,PDH_FMT_DOUBLE,&bytes,&count,nullptr)==PDH_MORE_DATA && bytes<=4*1024*1024) {
            std::vector<unsigned char> storage(bytes);
            auto* items=reinterpret_cast<PDH_FMT_COUNTERVALUE_ITEM_W*>(storage.data());
            if(PdhGetFormattedCounterArrayW(counter,PDH_FMT_DOUBLE,&bytes,&count,items)==ERROR_SUCCESS &&
               count<=storage.size()/sizeof(*items)) {
                GpuEngineUsage usage;
                for(DWORD i=0;i<count;++i)if(items[i].szName)usage.Add(items[i].szName,self.adapter,items[i].FmtValue.doubleValue,
                    items[i].FmtValue.CStatus==PDH_CSTATUS_VALID_DATA || items[i].FmtValue.CStatus==PDH_CSTATUS_NEW_DATA);
                value=usage.Percent();
            }
        }
        self.percent.store(value);
    } } catch(...) {self.percent.store(-1);}
    PdhCloseQuery(query);
}
static DWORD WINAPI SampleGpu(void* raw) {
    auto* worker=static_cast<GpuWorker*>(raw);const auto module=worker->module;
    CollectGpu(*worker->state);
    delete worker;
    // The counter owns no D3D/XR objects. Keep its code loaded until it exits,
    // without making game Quit wait for a slow Windows performance provider.
    FreeLibraryAndExitThread(module,0);
}
bool GpuUsageSampler::Start(ID3D11Device* device) {
    if(state_ || !device)return false;
    Microsoft::WRL::ComPtr<IDXGIDevice> dxgi;Microsoft::WRL::ComPtr<IDXGIAdapter> adapter;DXGI_ADAPTER_DESC desc{};
    if(FAILED(device->QueryInterface(IID_PPV_ARGS(&dxgi))) || FAILED(dxgi->GetAdapter(&adapter)) || FAILED(adapter->GetDesc(&desc)))return false;
    wchar_t name[64]{};swprintf_s(name,L"luid_0x%08x_0x%08x",unsigned(desc.AdapterLuid.HighPart),desc.AdapterLuid.LowPart);
    auto state=std::make_shared<GpuCounterState>();state->adapter=name;HMODULE module{};
    if(!state->stop || !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,reinterpret_cast<LPCWSTR>(&SampleGpu),&module))return false;
    auto* worker=new GpuWorker{state,module};const auto handle=CreateThread(nullptr,0,SampleGpu,worker,0,nullptr);
    if(!handle) {delete worker;FreeLibrary(module);return false;}
    CloseHandle(handle);state_=std::move(state);return true;
}
double GpuUsageSampler::Percent() const {return state_?state_->percent.load():-1.;}
void GpuUsageSampler::Stop() {if(state_) {SetEvent(state_->stop);state_.reset();}}
bool PerformanceDisplay::Requested() {
    wchar_t value[8]{};
    return GetEnvironmentVariableW(L"DIRT2VR_PERFORMANCE_DISPLAY",value,8)==1 && value[0]==L'1';
}
bool PerformanceDisplay::Initialize(XrSession session,ID3D11Device* device) {
    if(thread_ || !session || !device)return false;
    thread_=GetCurrentThreadId();device->GetImmediateContext(&context_);
    XrReferenceSpaceCreateInfo reference{XR_TYPE_REFERENCE_SPACE_CREATE_INFO};
    reference.referenceSpaceType=XR_REFERENCE_SPACE_TYPE_VIEW;reference.poseInReferenceSpace.orientation.w=1;
    if(xrCreateReferenceSpace(session,&reference,&view_)!=XR_SUCCESS)return false;
    uint32_t count{};if(xrEnumerateSwapchainFormats(session,0,&count,nullptr)!=XR_SUCCESS || !count || count>128)return false;
    std::vector<int64_t> formats(count);
    if(xrEnumerateSwapchainFormats(session,count,&count,formats.data())!=XR_SUCCESS || count!=formats.size() ||
       std::find(formats.begin(),formats.end(),DXGI_FORMAT_R8G8B8A8_UNORM_SRGB)==formats.end())return false;
    XrSwapchainCreateInfo create{XR_TYPE_SWAPCHAIN_CREATE_INFO};create.format=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    create.usageFlags=XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT;create.sampleCount=create.faceCount=create.arraySize=create.mipCount=1;create.width=width;create.height=height;
    if(xrCreateSwapchain(session,&create,&chain_)!=XR_SUCCESS)return false;
    if(xrEnumerateSwapchainImages(chain_,0,&count,nullptr)!=XR_SUCCESS || !count || count>16)return false;
    images_.resize(count,{XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR});
    if(xrEnumerateSwapchainImages(chain_,count,&count,reinterpret_cast<XrSwapchainImageBaseHeader*>(images_.data()))!=XR_SUCCESS || count!=images_.size())return false;
    for(const auto& image:images_) {
        if(!image.texture)return false;
        Microsoft::WRL::ComPtr<ID3D11Device> actual;image.texture->GetDevice(&actual);D3D11_TEXTURE2D_DESC d{};image.texture->GetDesc(&d);
        if(actual.Get()!=device || d.Width!=width || d.Height!=height || d.ArraySize!=1 || d.MipLevels!=1 || d.SampleDesc.Count!=1 ||
           (d.Format!=DXGI_FORMAT_R8G8B8A8_UNORM_SRGB && d.Format!=DXGI_FORMAT_R8G8B8A8_TYPELESS))return false;
    }
    const D3D11_QUERY_DESC completion{D3D11_QUERY_EVENT,0};if(FAILED(device->CreateQuery(&completion,&completion_)))return false;
    // Below the centre of view, like GRID2VR's default Follow view placement.
    layer_.space=view_;layer_.eyeVisibility=XR_EYE_VISIBILITY_BOTH;layer_.subImage.swapchain=chain_;
    layer_.subImage.imageRect.extent={width,height};layer_.pose.orientation.w=1;
    layer_.pose.position={0,-.45f,-1.5f};layer_.size={.6f,.075f};enabled_=true;
    const bool gpu=gpu_.Start(device);
    Log("performance display enabled gpu_counter=%d",gpu);
    return true;
}
bool PerformanceDisplay::Upload(uint64_t now) {
    if(ready_ && now-updated_<1000)return true;
    if(!acquired_) {
        const XrSwapchainImageAcquireInfo acquire{XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO};
        if(xrAcquireSwapchainImage(chain_,&acquire,&index_)!=XR_SUCCESS)return false;
        acquired_=true;
    }
    const XrSwapchainImageWaitInfo wait{XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO,nullptr,0};
    // An optional meter must not stall rendering. A pending image is retried on
    // the next frame, and is never released before a successful wait.
    const auto result=xrWaitSwapchainImage(chain_,&wait);
    if(result==XR_TIMEOUT_EXPIRED)return true;
    if(result!=XR_SUCCESS)return false;
    waited_=true;
    if(index_>=images_.size())return false;
    const auto pixels=Pixels(rate_.Fps(),gpu_.Percent());
    Microsoft::WRL::ComPtr<ID3D11Predicate> predicate;BOOL predicateValue{};
    context_->GetPredication(&predicate,&predicateValue);context_->SetPredication(nullptr,FALSE);
    context_->UpdateSubresource(images_[index_].texture,0,nullptr,pixels.data(),width*4,0);
    context_->SetPredication(predicate.Get(),predicateValue);used_=true;
    const XrSwapchainImageReleaseInfo release{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};
    if(xrReleaseSwapchainImage(chain_,&release)!=XR_SUCCESS)return false;
    acquired_=waited_=false;updated_=now;ready_=true;return true;
}
const XrCompositionLayerQuad* PerformanceDisplay::Layer(uint64_t now) {
    if(!enabled_ || thread_!=GetCurrentThreadId())return nullptr;
    if(!Upload(now)) {enabled_=false;Log("performance display stopped after a swapchain error");return nullptr;}
    return ready_?&layer_:nullptr;
}
bool PerformanceDisplay::Shutdown() {
    if(!thread_)return true;
    if(thread_!=GetCurrentThreadId()) {gpu_.Stop();return false;}
    if(enabled_)Log("performance display last fps=%.1f gpu=%.0f",rate_.Fps(),gpu_.Percent());
    enabled_=false;
    gpu_.Stop();
    if(acquired_) {
        const XrSwapchainImageWaitInfo wait{XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO,nullptr,100000000};
        if(!waited_ && xrWaitSwapchainImage(chain_,&wait)!=XR_SUCCESS)return false;
        waited_=true;
        const XrSwapchainImageReleaseInfo release{XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO};
        if(xrReleaseSwapchainImage(chain_,&release)!=XR_SUCCESS)return false;acquired_=waited_=false;
    }
    if(used_) {
        context_->End(completion_.Get());context_->Flush();const auto deadline=GetTickCount64()+2000;HRESULT status;
        do {status=context_->GetData(completion_.Get(),nullptr,0,0);if(status==S_FALSE)Sleep(1);}while(status==S_FALSE && GetTickCount64()<deadline);
        if(status!=S_OK)return false;used_=false;
    }
    if(chain_ && xrDestroySwapchain(chain_)!=XR_SUCCESS)return false;
    if(view_ && xrDestroySpace(view_)!=XR_SUCCESS)return false;
    chain_=XR_NULL_HANDLE;view_=XR_NULL_HANDLE;images_.clear();completion_.Reset();context_.Reset();
    ready_=false;return true;
}
}
