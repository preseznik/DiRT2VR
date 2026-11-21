#include "scenery_motion.h"
#include "live_neural.h"
#include "common.h"
#include <MinHook.h>
#include <wrl/client.h>
#include <array>
#include <atomic>
#include <cmath>
#include <cstring>
#include <mutex>
#include <unordered_map>
#include <unordered_set>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace vr {
namespace {
// Private data on a registered vertex buffer: its mover index. It goes with the buffer, so a released
// buffer whose address is reused never matches.
constexpr GUID kMoverTag{0x5d1c3a71,0x2b8e,0x4f0a,{0x9c,0x41,0x6e,0x2d,0x7b,0x13,0x88,0x5f}};
constexpr double kPi=3.14159265358979323846;

// Row-vector 4x4 (world = p * M), as the object shaders read cb0 `model` and as System.Numerics builds them.
struct Mat { double m[4][4]{}; };
Mat Identity() { Mat r; for(int i=0;i<4;++i)r.m[i][i]=1;return r; }
Mat Mul(const Mat& a,const Mat& b) {
    Mat r;
    for(int i=0;i<4;++i)for(int j=0;j<4;++j)for(int k=0;k<4;++k)r.m[i][j]+=a.m[i][k]*b.m[k][j];
    return r;
}
Mat Translation(double x,double y,double z) { Mat r=Identity();r.m[3][0]=x;r.m[3][1]=y;r.m[3][2]=z;return r; }
// System.Numerics Matrix4x4.CreateRotationY / CreateRotationZ / CreateFromAxisAngle.
Mat RotationY(double a) { Mat r=Identity();const double c=std::cos(a),s=std::sin(a);r.m[0][0]=c;r.m[0][2]=-s;r.m[2][0]=s;r.m[2][2]=c;return r; }
Mat RotationZ(double a) { Mat r=Identity();const double c=std::cos(a),s=std::sin(a);r.m[0][0]=c;r.m[0][1]=s;r.m[1][0]=-s;r.m[1][1]=c;return r; }
Mat AxisAngle(double x,double y,double z,double a) {
    const double s=std::sin(a),c=std::cos(a),xx=x*x,yy=y*y,zz=z*z,xy=x*y,xz=x*z,yz=y*z;
    Mat r=Identity();
    r.m[0][0]=xx+c*(1-xx);r.m[0][1]=xy-c*xy+s*z;r.m[0][2]=xz-c*xz-s*y;
    r.m[1][0]=xy-c*xy-s*z;r.m[1][1]=yy+c*(1-yy);r.m[1][2]=yz-c*yz+s*x;
    r.m[2][0]=xz-c*xz+s*y;r.m[2][1]=yz-c*yz-s*x;r.m[2][2]=zz+c*(1-zz);
    return r;
}
// Inverse of rotation followed by translation.
Mat RigidInverse(const Mat& a) {
    Mat r=Identity();
    for(int i=0;i<3;++i)for(int j=0;j<3;++j)r.m[i][j]=a.m[j][i];
    for(int j=0;j<3;++j)r.m[3][j]=-(a.m[3][0]*r.m[0][j]+a.m[3][1]*r.m[1][j]+a.m[3][2]*r.m[2][j]);
    return r;
}

// A closed railway loop in Unreal centimetres; stations are cumulative distances.
struct Railway { std::vector<double> x,y,z,station; double length{}; };
struct Mover {
    int kind{};
    double hub[3]{},axis[3]{},rpm{};
    int motion{};double distance{},wheelbase{},part[3]{},radius{},speed{};
    int railway{-1};Mat reference;
};

std::mutex mutex;
std::vector<Mover> movers;
std::vector<Railway> railways;
std::unordered_set<ID3D11Buffer*> marked;
std::atomic<bool> active{};
std::atomic<bool> drawHooks{};
LARGE_INTEGER started{},frequency{};
ID3D11Buffer* currentVertices{};
// What the game last wrote into each small constant buffer, so a moved draw can keep the rest of cb0.
struct Shadow { std::array<unsigned char,512> data{};UINT size{}; };
std::unordered_map<ID3D11Resource*,Shadow> shadows;
std::unordered_map<ID3D11Resource*,std::pair<void*,UINT>> pending;
std::unordered_map<UINT,ComPtr<ID3D11Buffer>> replacements;
// Set while this module writes its own replacement buffer, so the map observers leave it alone.
thread_local bool writingOwn{};
ComPtr<ID3D11Device> replacementDevice;
// Diagnostics: moved draws, and marked draws left as they are for want of a cb0 copy or a railway.
double Seconds();
unsigned movedDraws{},unshadowedDraws{},unreadyDraws{};
ULONGLONG reported{};
void Report() {
    const auto now=GetTickCount64();
    if(now-reported<10000)return;
    reported=now;
    Log("scenery motion: parts=%zu railways=%zu moved=%u without-cb0=%u not-ready=%u t=%.1fs",movers.size(),railways.size(),movedDraws,unshadowedDraws,unreadyDraws,Seconds());
}

double Seconds() {
    LARGE_INTEGER now{};QueryPerformanceCounter(&now);
    return double(now.QuadPart-started.QuadPart)/double(frequency.QuadPart);
}

// The train's poses, as the pack builder parks it (Structures.Train) and GoRacer moves it.
void Sample(const Railway& r,double d,double out[3]) {
    double s=std::fmod(d,r.length);if(s<0)s+=r.length;
    size_t lo=0,hi=r.station.size()-1;
    while(lo<hi){const size_t mid=(lo+hi+1)/2;if(r.station[mid]<=s)lo=mid;else hi=mid-1;}
    const size_t next=(lo+1)%r.x.size();
    const double end=next==0?r.length:r.station[next],t=(s-r.station[lo])/(end-r.station[lo]);
    out[0]=r.x[lo]+(r.x[next]-r.x[lo])*t;out[1]=r.y[lo]+(r.y[next]-r.y[lo])*t;out[2]=r.z[lo]+(r.z[next]-r.z[lo])*t;
}
struct Pose { double c[3];double yaw; };
Pose PoseAt(const Railway& r,double d,double wheelbase) {
    double f[3],b[3];Sample(r,d+wheelbase/2,f);Sample(r,d-wheelbase/2,b);
    return {{(f[0]+b[0])/2,(f[1]+b[1])/2,(f[2]+b[2])/2},std::atan2(f[1]-b[1],f[0]-b[0])};
}
// A part at (px, py, pz) cm in the pose's frame: rotation by -yaw about Y, then the world position in metres.
Mat Place(const Pose& p,double px,double py,double pz,const Mat& local=Identity()) {
    const double c=std::cos(p.yaw),s=std::sin(p.yaw);
    const double x=p.c[0]+px*c-py*s,y=p.c[1]+px*s+py*c,z=p.c[2]+pz;
    return Mul(Mul(local,RotationY(-p.yaw)),Translation(x/100,z/100,y/100));
}
Mat TrainPart(const Mover& m,const Railway& r,double d) {
    const Pose pose=PoseAt(r,d,m.wheelbase);
    switch(m.motion) {
    case 0: return Place(pose,0,0,0);
    case 2: return Place(pose,m.part[0],m.part[1],m.part[2],RotationZ(-(d-m.distance)/std::max(1.,m.radius)));
    case 3: { const double phase=d/84;return Place(pose,m.part[0]+m.radius*std::cos(phase),m.part[1],m.part[2]-m.radius*std::sin(phase)); }
    case 4: return Place(PoseAt(r,d+m.part[0],146),0,0,m.part[2]);
    default: return Place(pose,m.part[0],m.part[1],m.part[2]);
    }
}
// L: the part's world-space motion since load (world' = world * L).
bool Motion(const Mover& m,double seconds,Mat& out) {
    if(m.kind==1) {
        const double a=seconds*m.rpm*6*kPi/180;
        out=Mul(Mul(Translation(-m.hub[0],-m.hub[1],-m.hub[2]),AxisAngle(m.axis[0],m.axis[1],m.axis[2],a)),Translation(m.hub[0],m.hub[1],m.hub[2]));
        return true;
    }
    if(m.kind!=2 || m.railway<0 || m.railway>=int(railways.size()))return false;
    out=Mul(m.reference,TrainPart(m,railways[m.railway],m.distance+m.speed*seconds));
    return true;
}

// Registers a vertex buffer whose first vertices carry the marker.
void Register(ID3D11Buffer* buffer,const D3D11_BUFFER_DESC& desc,const void* bytes) {
    if(desc.ByteWidth<16)return;
    float v0[3];std::memcpy(v0,bytes,sizeof v0);
    if(v0[0]!=kSceneryMotionMarker)return;
    const UINT stride=UINT(v0[1]);const int kind=int(v0[2]);
    if(stride<12 || stride>256 || float(stride)!=v0[1] || (kind!=1 && kind!=2))return;
    auto vertex=[&](UINT i,double out[3]) {
        if(UINT64(i)*stride+12>desc.ByteWidth)return false;
        float f[3];std::memcpy(f,static_cast<const unsigned char*>(bytes)+UINT64(i)*stride,sizeof f);
        out[0]=f[0];out[1]=f[1];out[2]=f[2];return true;
    };
    Mover m;m.kind=kind;double v[3];
    std::lock_guard lock(mutex);
    if(kind==1) {
        double rate[3];
        if(!vertex(1,m.hub) || !vertex(2,m.axis) || !vertex(3,rate))return;
        m.rpm=rate[0];
    } else {
        double head[3],rate[3],count[3];
        if(!vertex(1,head) || !vertex(2,m.part) || !vertex(3,rate) || !vertex(4,count))return;
        m.motion=int(head[0]);m.distance=head[1];m.wheelbase=head[2];m.radius=rate[0];m.speed=rate[1];
        const UINT points=UINT(count[0]);
        if(points>=3) {
            Railway r;
            for(UINT i=0;i<points;++i) {
                if(!vertex(5+i,v))return;
                if(i){const double dx=v[0]-r.x.back(),dy=v[1]-r.y.back(),dz=v[2]-r.z.back();r.station.push_back(r.station.back()+std::sqrt(dx*dx+dy*dy+dz*dz));}
                else r.station.push_back(0);
                r.x.push_back(v[0]);r.y.push_back(v[1]);r.z.push_back(v[2]);
            }
            const double dx=r.x.front()-r.x.back(),dy=r.y.front()-r.y.back(),dz=r.z.front()-r.z.back();
            r.length=r.station.back()+std::sqrt(dx*dx+dy*dy+dz*dz);
            railways.push_back(std::move(r));
            for(auto& other:movers)if(other.kind==2 && other.railway<0)other.railway=int(railways.size()-1);
        }
        // One railway per track: parts registered before it are linked when it arrives.
        if(!railways.empty())m.railway=int(railways.size()-1);
    }
    const int index=int(movers.size());
    if(FAILED(buffer->SetPrivateData(kMoverTag,sizeof index,&index)))return;
    movers.push_back(m);marked.insert(buffer);
    if(!active.exchange(true)){QueryPerformanceFrequency(&frequency);QueryPerformanceCounter(&started);}
    if(movers.size()<=4 || movers.size()%50==0)Log("scenery motion: registered part %d kind=%d stride=%u motion=%d railways=%zu",index,kind,stride,m.motion,railways.size());
}

// Train parts take their reference pose (as parked at load) once their railway is known.
bool Ready(Mover& m) {
    if(m.kind!=2)return true;
    if(m.railway<0)return false;
    if(m.reference.m[3][3]==0)m.reference=RigidInverse(TrainPart(m,railways[m.railway],m.distance));
    return true;
}

using CreateBufferFn=HRESULT (STDMETHODCALLTYPE*)(ID3D11Device*,const D3D11_BUFFER_DESC*,const D3D11_SUBRESOURCE_DATA*,ID3D11Buffer**);
using SetVerticesFn=void (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,UINT,UINT,ID3D11Buffer* const*,const UINT*,const UINT*);
using UpdateFn=void (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,ID3D11Resource*,UINT,const D3D11_BOX*,const void*,UINT,UINT);
using MapFn=HRESULT (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,ID3D11Resource*,UINT,D3D11_MAP,UINT,D3D11_MAPPED_SUBRESOURCE*);
using UnmapFn=void (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,ID3D11Resource*,UINT);
using DrawIndexedFn=void (STDMETHODCALLTYPE*)(ID3D11DeviceContext*,UINT,UINT,INT);
CreateBufferFn realCreateBuffer{};SetVerticesFn realSetVertices{};UpdateFn realUpdate{};
MapFn realMap{};UnmapFn realUnmap{};DrawIndexedFn realDrawIndexed{};

HRESULT STDMETHODCALLTYPE CreateBuffer(ID3D11Device* device,const D3D11_BUFFER_DESC* desc,const D3D11_SUBRESOURCE_DATA* data,ID3D11Buffer** buffer) {
    const HRESULT hr=realCreateBuffer(device,desc,data,buffer);
    if(SUCCEEDED(hr) && desc && data && data->pSysMem && buffer && *buffer && (desc->BindFlags&D3D11_BIND_VERTEX_BUFFER))
        Register(*buffer,*desc,data->pSysMem);
    return hr;
}
void STDMETHODCALLTYPE SetVertices(ID3D11DeviceContext* context,UINT slot,UINT count,ID3D11Buffer* const* buffers,const UINT* strides,const UINT* offsets) {
    if(slot==0 && count && buffers && context->GetType()==D3D11_DEVICE_CONTEXT_IMMEDIATE)currentVertices=buffers[0];
    realSetVertices(context,slot,count,buffers,strides,offsets);
}
// Small constant buffers only; their whole contents are written each time.
bool Constant(ID3D11Resource* resource,UINT& size) {
    D3D11_RESOURCE_DIMENSION dimension{};resource->GetType(&dimension);
    if(dimension!=D3D11_RESOURCE_DIMENSION_BUFFER)return false;
    D3D11_BUFFER_DESC desc{};static_cast<ID3D11Buffer*>(resource)->GetDesc(&desc);
    size=desc.ByteWidth;return (desc.BindFlags&D3D11_BIND_CONSTANT_BUFFER) && size<=512;
}
void STDMETHODCALLTYPE Update(ID3D11DeviceContext* context,ID3D11Resource* resource,UINT sub,const D3D11_BOX* box,const void* data,UINT pitch,UINT depth) {
    UINT size{};
    if(active && !box && data && Constant(resource,size)) {
        std::lock_guard lock(mutex);auto& s=shadows[resource];std::memcpy(s.data.data(),data,size);s.size=size;
    }
    realUpdate(context,resource,sub,box,data,pitch,depth);
}
HRESULT STDMETHODCALLTYPE OwnMap(ID3D11DeviceContext* c,ID3D11Resource* r,UINT sub,D3D11_MAP mode,UINT flags,D3D11_MAPPED_SUBRESOURCE* mapped) {
    const HRESULT hr=realMap(c,r,sub,mode,flags,mapped);
    if(SUCCEEDED(hr))SceneryMotionMapped(r,mode,mapped);
    return hr;
}
void STDMETHODCALLTYPE OwnUnmap(ID3D11DeviceContext* c,ID3D11Resource* r,UINT sub) { SceneryMotionUnmapped(r);realUnmap(c,r,sub); }
void STDMETHODCALLTYPE OwnDrawIndexed(ID3D11DeviceContext* c,UINT n,UINT start,INT base) {
    // Desktop live neural rendering takes HUD draws into its own texture.
    if(LiveNeuralHudDraw(c,[&]{realDrawIndexed(c,n,start,base);}))return;
    if(!SceneryMotionDraw(c,[&]{realDrawIndexed(c,n,start,base);}))realDrawIndexed(c,n,start,base);
}

template<class F> bool Hook(void* object,unsigned index,void* replacement,F& original) {
    auto target=(*static_cast<void***>(object))[index];
    auto status=MH_CreateHook(target,replacement,reinterpret_cast<void**>(&original));
    if(status==MH_OK)status=MH_EnableHook(target);
    Log("scenery motion: hook slot=%u status=%s",index,MH_StatusToString(status));
    return status==MH_OK;
}
}

void AttachSceneryMotion(ID3D11Device* device,ID3D11DeviceContext* context,bool ownDrawHooks) {
    static std::once_flag once;
    std::call_once(once,[&] {
        const auto status=MH_Initialize();
        if(status!=MH_OK && status!=MH_ERROR_ALREADY_INITIALIZED){Log("scenery motion: MinHook unavailable");return;}
        ComPtr<ID3D11DeviceContext> owned;
        if(!context){device->GetImmediateContext(&owned);context=owned.Get();}
        Hook(device,3,reinterpret_cast<void*>(CreateBuffer),realCreateBuffer);
        Hook(context,18,reinterpret_cast<void*>(SetVertices),realSetVertices);
        Hook(context,48,reinterpret_cast<void*>(Update),realUpdate);
        if(ownDrawHooks) {
            Hook(context,14,reinterpret_cast<void*>(OwnMap),realMap);
            Hook(context,15,reinterpret_cast<void*>(OwnUnmap),realUnmap);
            Hook(context,12,reinterpret_cast<void*>(OwnDrawIndexed),realDrawIndexed);
        }
        drawHooks=true;
    });
}

void SceneryMotionMapped(ID3D11Resource* resource,D3D11_MAP mode,const D3D11_MAPPED_SUBRESOURCE* mapped) {
    UINT size{};
    if(writingOwn || !active || !mapped || !mapped->pData || (mode!=D3D11_MAP_WRITE_DISCARD && mode!=D3D11_MAP_WRITE_NO_OVERWRITE) || !Constant(resource,size))return;
    std::lock_guard lock(mutex);pending[resource]={mapped->pData,size};
}
void SceneryMotionUnmapped(ID3D11Resource* resource) {
    if(writingOwn || !active)return;
    std::lock_guard lock(mutex);
    const auto found=pending.find(resource);
    if(found==pending.end())return;
    auto& s=shadows[resource];std::memcpy(s.data.data(),found->second.first,found->second.second);s.size=found->second.second;
    pending.erase(found);
}

bool SceneryMotionPrepare(ID3D11DeviceContext* context,ID3D11Buffer*& original,ID3D11Buffer*& moved) {
    if(!active || !currentVertices)return false;
    std::array<unsigned char,512> data;UINT size{};
    ComPtr<ID3D11Buffer> globals,replacement;
    {
    std::lock_guard lock(mutex);
    ID3D11Buffer* vertices=currentVertices;
    if(!marked.contains(vertices))return false;
    int index=-1;UINT bytes=sizeof index;
    if(FAILED(vertices->GetPrivateData(kMoverTag,&bytes,&index)) || bytes!=sizeof index || index<0 || index>=int(movers.size())) {
        marked.erase(vertices);return false;
    }
    Mover& mover=movers[index];
    Report();
    Mat motion;
    if(!Ready(mover) || !Motion(mover,Seconds(),motion)){++unreadyDraws;return false;}
    context->VSGetConstantBuffers(0,1,&globals);
    if(!globals){++unshadowedDraws;return false;}
    const auto shadow=shadows.find(globals.Get());
    if(shadow==shadows.end() || shadow->second.size<64){++unshadowedDraws;return false;}
    ComPtr<ID3D11Device> device;context->GetDevice(&device);
    if(replacementDevice.Get()!=device.Get()){replacements.clear();replacementDevice=device;}
    size=shadow->second.size;
    auto& cached=replacements[size];
    // Dynamic, rewritten with WRITE_DISCARD per draw: the driver renames it (UpdateSubresource into a
    // default buffer cost about 1 ms of GPU time per eye pair in the fake-headset lab run).
    if(!cached) {
        D3D11_BUFFER_DESC desc{size,D3D11_USAGE_DYNAMIC,D3D11_BIND_CONSTANT_BUFFER,D3D11_CPU_ACCESS_WRITE,0,0};
        if(FAILED(device->CreateBuffer(&desc,nullptr,&cached)))return false;
    }
    replacement=cached;
    data=shadow->second.data;
    // cb0 `model` at 0 and, in the velocity passes, `prevModel` at 256 (no motion blur for moved parts).
    for(UINT offset:{0u,256u}) {
        if(offset+64>size)break;
        float f[16];std::memcpy(f,data.data()+offset,sizeof f);
        Mat m;for(int i=0;i<16;++i)m.m[i/4][i%4]=f[i];
        m=Mul(m,motion);
        for(int i=0;i<16;++i)f[i]=float(m.m[i/4][i%4]);
        std::memcpy(data.data()+offset,f,sizeof f);
    }
    ++movedDraws;
    }
    // Outside the lock: the Map/Unmap observers take it, and skip this buffer anyway.
    D3D11_MAPPED_SUBRESOURCE mapped{};
    writingOwn=true;
    const HRESULT hr=context->Map(replacement.Get(),0,D3D11_MAP_WRITE_DISCARD,0,&mapped);
    if(SUCCEEDED(hr)){std::memcpy(mapped.pData,data.data(),size);context->Unmap(replacement.Get(),0);}
    writingOwn=false;
    if(FAILED(hr))return false;
    original=globals.Detach();moved=replacement.Get();
    context->VSSetConstantBuffers(0,1,&moved);
    return true;
}
void SceneryMotionRestore(ID3D11DeviceContext* context,ID3D11Buffer* original) {
    context->VSSetConstantBuffers(0,1,&original);
    if(original)original->Release();
}
}
