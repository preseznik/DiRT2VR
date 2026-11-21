#include "../../src/desktop_bridge_protocol.h"
#include <d3d11.h>
#include <wrl/client.h>
#include <string>
#include <cstdio>

using Microsoft::WRL::ComPtr;
using namespace vr::bridge;
struct Handle {
    HANDLE value{};
    ~Handle() { if(value) CloseHandle(value); }
};
int Run(Shared& shared,HANDLE request,HANDLE done,HANDLE stop) {
    auto fail=[&](HRESULT hr) {
        shared.error=hr; InterlockedExchange(&shared.status,static_cast<LONG>(Status::Failed)); SetEvent(done); return 1;
    };
    if(!Valid(shared) || (shared.format!=DXGI_FORMAT_R8G8B8A8_UNORM && shared.format!=DXGI_FORMAT_B8G8R8A8_UNORM))
        return fail(E_INVALIDARG);
    Handle parent{OpenProcess(SYNCHRONIZE,FALSE,shared.parent)};
    if(!parent.value) return fail(HRESULT_FROM_WIN32(GetLastError()));
    ComPtr<IDXGIFactory1> factory; ComPtr<IDXGIAdapter1> adapter;
    HRESULT hr=CreateDXGIFactory1(IID_PPV_ARGS(&factory));
    if(FAILED(hr)) return fail(hr);
    for(UINT i=0;;++i) {
        adapter.Reset();
        if(factory->EnumAdapters1(i,&adapter)!=S_OK) return fail(DXGI_ERROR_NOT_FOUND);
        DXGI_ADAPTER_DESC1 desc{};
        if(SUCCEEDED(adapter->GetDesc1(&desc)) && desc.AdapterLuid.HighPart==shared.adapter.HighPart &&
           desc.AdapterLuid.LowPart==shared.adapter.LowPart) break;
    }
    ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
    const D3D_FEATURE_LEVEL levels[]{D3D_FEATURE_LEVEL_11_0};
    hr=D3D11CreateDevice(adapter.Get(),D3D_DRIVER_TYPE_UNKNOWN,nullptr,0,levels,1,D3D11_SDK_VERSION,&device,nullptr,&context);
    if(FAILED(hr)) return fail(hr);
    ComPtr<ID3D11Texture2D> input,output; ComPtr<IDXGIKeyedMutex> inputLock,outputLock;
    hr=device->OpenSharedResource(reinterpret_cast<HANDLE>(static_cast<uintptr_t>(shared.inputHandle)),IID_PPV_ARGS(&input));
    if(FAILED(hr)) return fail(hr);
    hr=device->OpenSharedResource(reinterpret_cast<HANDLE>(static_cast<uintptr_t>(shared.outputHandle)),IID_PPV_ARGS(&output));
    if(FAILED(hr)) return fail(hr);
    for(auto texture:{input.Get(),output.Get()}) {
        D3D11_TEXTURE2D_DESC desc{}; texture->GetDesc(&desc);
        if(desc.Width!=shared.width || desc.Height!=shared.height || static_cast<uint32_t>(desc.Format)!=shared.format ||
           desc.SampleDesc.Count!=1 || desc.ArraySize!=1 || desc.MipLevels!=1) return fail(E_INVALIDARG);
    }
    if(FAILED(input.As(&inputLock)) || FAILED(output.As(&outputLock))) return fail(E_NOINTERFACE);
    InterlockedExchange(&shared.status,static_cast<LONG>(Status::Ready));
    uint64_t previous{};
    HANDLE waits[]{stop,parent.value,request};
    while(WaitForMultipleObjects(3,waits,FALSE,INFINITE)==WAIT_OBJECT_0+2) {
        const auto sequence=shared.requested;
        if(sequence<=previous) return fail(E_INVALIDARG);
#ifdef DIRT2VR_BRIDGE_TEST
        wchar_t fault[32]{}; GetEnvironmentVariableW(L"DIRT2VR_BRIDGE_TEST_FAULT",fault,32);
        if(wcscmp(fault,L"exit")==0) return 3;
        if(wcscmp(fault,L"stall")==0) { WaitForSingleObject(stop,10000); return 4; }
        if(wcscmp(fault,L"late")==0 && previous==0) Sleep(40);
#endif
        if(inputLock->AcquireSync(1,100)!=S_OK) return fail(DXGI_ERROR_WAIT_TIMEOUT);
        if(outputLock->AcquireSync(0,100)!=S_OK) { inputLock->ReleaseSync(0); return fail(DXGI_ERROR_WAIT_TIMEOUT); }
        context->CopyResource(output.Get(),input.Get()); context->Flush();
#ifdef DIRT2VR_BRIDGE_TEST
        if(wcscmp(fault,L"marker")==0) {
            ComPtr<ID3D11RenderTargetView> view;
            if(SUCCEEDED(device->CreateRenderTargetView(output.Get(),nullptr,&view))) {
                const float color[]{1,0,1,1}; context->ClearRenderTargetView(view.Get(),color); context->Flush();
            }
        }
#endif
        const auto releasedOut=outputLock->ReleaseSync(1), releasedIn=inputLock->ReleaseSync(0);
        if(releasedOut!=S_OK || releasedIn!=S_OK) return fail(E_FAIL);
        previous=sequence; shared.completed=sequence;
#ifdef DIRT2VR_BRIDGE_TEST
        if(wcscmp(fault,L"mismatch")==0) shared.completed=sequence+1;
#endif
        SetEvent(done);
    }
    InterlockedExchange(&shared.status,static_cast<LONG>(Status::Stopped)); return 0;
}
int wmain(int argc,wchar_t** argv) {
    if(argc!=3 || wcscmp(argv[1],L"--transport")!=0 || wcsncmp(argv[2],L"Local\\DiRT2VR-bridge-",21)!=0) {
        std::puts("Transport-only helper. Neural Rendering and Super Resolution are not implemented."); return 2;
    }
    const std::wstring name=argv[2];
    Handle mapping{OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name.c_str())};
    Handle request{OpenEventW(SYNCHRONIZE,FALSE,(name+L"-request").c_str())};
    Handle done{OpenEventW(EVENT_MODIFY_STATE,FALSE,(name+L"-done").c_str())};
    Handle stop{OpenEventW(SYNCHRONIZE,FALSE,(name+L"-stop").c_str())};
    if(!mapping.value || !request.value || !done.value || !stop.value) return 2;
    auto shared=static_cast<Shared*>(MapViewOfFile(mapping.value,FILE_MAP_ALL_ACCESS,0,0,sizeof(Shared)));
    if(!shared) return 2;
    const int result=Run(*shared,request.value,done.value,stop.value);
    UnmapViewOfFile(shared); return result;
}
