#include "common.h"
#include "replay_storage.h"
#include <windows.h>
#include <array>
#include <algorithm>
#include <vector>
#include <stdexcept>
#include <cstring>
#include <cmath>
#include <cstdio>
#include <MinHook.h>
#include "gfwl_compat.h"
#include <mutex>
#include <map>
#include <atomic>

namespace vr {
namespace {
bool loading{}, configured{}, hooksReady{}, playbackFailed{};
bool loaded{};
void* lastInner{};unsigned lastBytes{};double lastEnd{};
std::wstring directory;
unsigned sequence{};
void ReportFailure(const char* message) {
 Log("saved replay: %s",message);
 if(directory.empty())return;
 const auto path=directory+L"\\error.txt";
 HANDLE file=CreateFileW(path.c_str(),GENERIC_WRITE,FILE_SHARE_READ,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
 if(file!=INVALID_HANDLE_VALUE){DWORD written{};WriteFile(file,message,static_cast<DWORD>(strlen(message)),&written,nullptr);CloseHandle(file);}
}
template<class T> T Read(const void* p,unsigned n=0){T v{};std::memcpy(&v,static_cast<const unsigned char*>(p)+n,sizeof v);return v;}
template<class T> void Put(void* p,unsigned n,T v){std::memcpy(static_cast<unsigned char*>(p)+n,&v,sizeof v);}
template<class F> F Method(void* p,unsigned n){return reinterpret_cast<F>(Read<void**>(p)[n/4]);}
using Tell=unsigned(__thiscall*)(void*);
using Seek=unsigned(__thiscall*)(void*,unsigned,unsigned);
using Io=unsigned(__thiscall*)(void*,void*,unsigned);
using ReadIo=unsigned(__thiscall*)(void*,void*,unsigned,unsigned);
#include "replay_storage_audio.inl"
#include "replay_storage_animation.inl"
#include "replay_storage_messages.inl"
#include "replay_storage_surfaces.inl"
#pragma pack(push,1)
struct Header {char magic[8];unsigned version,types,snapshot,bytes;double start,end;unsigned startOffset,metadata,audio;};
#pragma pack(pop)
struct File {
 HANDLE handle=INVALID_HANDLE_VALUE;
 File(const wchar_t* path,bool output){handle=CreateFileW(path,output?GENERIC_WRITE:GENERIC_READ,FILE_SHARE_READ,nullptr,output?CREATE_NEW:OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);if(handle==INVALID_HANDLE_VALUE)throw std::runtime_error("archive open");}
 ~File(){if(handle!=INVALID_HANDLE_VALUE)CloseHandle(handle);}
 void Transfer(void* p,unsigned n,bool output){DWORD done{};if(!(output?WriteFile(handle,p,n,&done,nullptr):ReadFile(handle,p,n,&done,nullptr))||done!=n)throw std::runtime_error("archive transfer");}
};
struct Buffer {
 void** table;
 std::vector<unsigned char> data;
 unsigned cursor{};bool failed{},namedRecords{};
 static void __fastcall Destroy(Buffer*,void*){}
 static unsigned __fastcall Write(Buffer* b,void*,void* p,unsigned n){
  if(n>32*1024*1024 || b->data.size()+n>32*1024*1024){b->failed=true;return 14;}
  auto bytes=static_cast<unsigned char*>(p);b->data.insert(b->data.end(),bytes,bytes+n);return 0;
 }
 static unsigned __fastcall ReadData(Buffer* b,void*,void* p,unsigned n){
  if(n>b->data.size()-b->cursor){b->failed=true;return 14;}
  if(b->namedRecords && n && !(n%80)){
   auto dest=static_cast<unsigned char*>(p);auto source=b->data.data()+b->cursor;
   auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
   for(unsigned at=0;at<n;at+=80){auto item=dest+at;if(Read<void*>(item)!=base+0xeedfb0 || Read<unsigned>(item,4)!=64 || Read<void*>(item,8)!=item+12 || !std::memchr(source+at+12,0,64)){b->failed=true;return 14;}}
   for(unsigned at=0;at<n;at+=80){std::memcpy(dest+at+12,source+at+12,64);Put<unsigned>(dest+at,76,0);}
  }else if(n)std::memcpy(p,b->data.data()+b->cursor,n);
  b->cursor+=n;return 0;
 }
 Buffer(){static void* methods[]{reinterpret_cast<void*>(&Destroy),reinterpret_cast<void*>(&Write),reinterpret_cast<void*>(&ReadData)};table=methods;}
};
using Serialize=unsigned(__thiscall*)(void*,void*);
void TransferRecording(unsigned char* inner,bool load,const wchar_t* path) {
 auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
 auto writer=Read<unsigned char*>(inner,0x1a4);auto stream=writer+0x90;
 auto snapshot=Read<unsigned char*>(inner,0x274);
 if(!writer||!snapshot||Read<void*>(snapshot)!=base+0xf57210)throw std::runtime_error("snapshot type");
 auto begin=Read<unsigned*>(inner,0x1cc),end=Read<unsigned*>(inner,0x1d0);
 if(!begin||end<begin||end-begin>128)throw std::runtime_error("serializer list");
 std::vector<unsigned> types;for(auto i=begin;i!=end;++i){auto table=Read<unsigned>(*reinterpret_cast<void**>(i));if(table<reinterpret_cast<unsigned>(base)||table>=reinterpret_cast<unsigned>(base)+0x1100000)throw std::runtime_error("serializer type");types.push_back(table-reinterpret_cast<unsigned>(base));}
 Header h{{'D','2','V','R','L','A','B',0},4,static_cast<unsigned>(types.size()),Read<unsigned>(snapshot,0x10),Read<unsigned>(inner,0x288),Read<double>(inner,0x260),Read<double>(inner,0x280),Read<unsigned>(inner,0x268),0,0};
 Log("saved replay: %s snapshot=%u capacity=%u types=%u bytes=%u stream_table=%x",load?"load":"save",h.snapshot,Read<unsigned>(snapshot,8),h.types,h.bytes,Read<unsigned>(stream)-reinterpret_cast<unsigned>(base));
 for(unsigned i=0;i<h.types;++i)Log("saved replay: serializer[%u]=%x",i,types[i]);
 std::vector<AudioIdentity> audio;
 if(!load){std::lock_guard<std::mutex> lock(audioLock);if(audioCaptureInvalid)throw std::runtime_error("audio capture incomplete");for(auto& pair:recordedAudio)audio.push_back(pair.second);h.audio=static_cast<unsigned>(audio.size());}
 Buffer metadata;
 if(!load){for(auto item=begin;item!=end;++item){Buffer record;auto object=reinterpret_cast<void*>(*item);if(Method<Serialize>(object,0x24)(object,&record)||record.failed)throw std::runtime_error("serializer save");unsigned n=static_cast<unsigned>(record.data.size());Buffer::Write(&metadata,nullptr,&n,4);if(n)Buffer::Write(&metadata,nullptr,record.data.data(),n);Log("saved replay: saved serializer %u bytes=%u",static_cast<unsigned>(item-begin),n);}h.metadata=static_cast<unsigned>(metadata.data.size());}
 if(!load){auto surfaces=SurfaceMetadata();Buffer::Write(&metadata,nullptr,surfaces.data(),static_cast<unsigned>(surfaces.size()*4));h.metadata=static_cast<unsigned>(metadata.data.size());}
 File file(path,!load);
 if(load){
  file.Transfer(&h,sizeof h,false);
  if(std::memcmp(h.magic,"D2VRLAB",8)||h.version!=4||h.audio>4096||h.types!=types.size()||h.snapshot>8*1024*1024||!h.snapshot||h.snapshot>Read<unsigned>(snapshot,8)||!h.bytes||h.bytes>=Read<unsigned>(inner+0x20,0xc)||!std::isfinite(h.start)||h.start<0||h.start>=0.1||!std::isfinite(h.end)||h.end<=h.start||h.end>86400||h.startOffset>=h.bytes||h.metadata>32*1024*1024||h.metadata<h.types*4)throw std::runtime_error("archive metadata");
  LARGE_INTEGER length{};if(!GetFileSizeEx(file.handle,&length)||static_cast<unsigned long long>(length.QuadPart)!=sizeof h+4ull*h.types+h.metadata+h.snapshot+h.bytes+static_cast<unsigned long long>(h.audio)*sizeof(AudioIdentity))throw std::runtime_error("archive length");
  std::vector<unsigned> saved(h.types);file.Transfer(saved.data(),h.types*4,false);if(saved!=types)throw std::runtime_error("serializer mismatch");
 }else{if(!h.snapshot||h.snapshot>Read<unsigned>(snapshot,8)||h.snapshot>8*1024*1024)throw std::runtime_error("snapshot length");file.Transfer(&h,sizeof h,true);file.Transfer(types.data(),h.types*4,true);}
 if(load){audio.resize(h.audio);if(h.audio)file.Transfer(audio.data(),h.audio*sizeof(AudioIdentity),false);for(auto& key:audio)if(!std::memchr(key.name,0,32)||!std::memchr(key.bank,0,16))throw std::runtime_error("audio name");ResolveAudio(audio);}
 else if(h.audio)file.Transfer(audio.data(),h.audio*sizeof(AudioIdentity),true);
 if(load){metadata.data.resize(h.metadata);file.Transfer(metadata.data.data(),h.metadata,false);const auto nativeBytes=ReadSavedImageBase(metadata.data,h.types);ResolveSurfaces(metadata.data,nativeBytes);metadata.data.resize(nativeBytes);}else file.Transfer(metadata.data.data(),h.metadata,true);
 std::vector<unsigned char> snap(h.snapshot);
 if(load)file.Transfer(snap.data(),h.snapshot,false);else{std::memcpy(snap.data(),Read<void*>(snapshot,4),h.snapshot);file.Transfer(snap.data(),h.snapshot,true);}
 Log("saved replay: header and snapshot read bytes=%u metadata=%u",h.bytes,h.metadata);
 using Flush=void(__thiscall*)(void*);auto flush=reinterpret_cast<Flush>(base+0x7f1270);flush(writer);
 auto tell=Method<Tell>(stream,0x10);auto seek=Method<Seek>(stream,0xc);auto write=Method<Io>(stream,0x24);auto read=Method<ReadIo>(stream,0x28);
 const auto previous=tell(stream);seek(stream,0,0);std::array<unsigned char,65536> block{};
 try {
  for(unsigned offset=0;offset<h.bytes;){unsigned count=(std::min)(static_cast<unsigned>(block.size()),h.bytes-offset);if(load)file.Transfer(block.data(),count,false);if(!(offset%(4*1024*1024)))Log("saved replay: transfer offset=%u",offset);auto error=load?write(stream,block.data(),count):read(stream,block.data(),count,0);if(error)throw std::runtime_error("native stream transfer");if(!load)file.Transfer(block.data(),count,true);offset+=count;}
  if(load){
   flush(writer);Log("saved replay: stream restored");
   LARGE_INTEGER payload{};payload.QuadPart=sizeof h+4ull*h.types+h.metadata+h.snapshot+static_cast<unsigned long long>(h.audio)*sizeof(AudioIdentity);
   if(!SetFilePointerEx(file.handle,payload,nullptr,FILE_BEGIN))throw std::runtime_error("verification seek");
   seek(stream,0,0);std::array<unsigned char,65536> expected{};
   for(unsigned offset=0;offset<h.bytes;){unsigned count=(std::min)(static_cast<unsigned>(block.size()),h.bytes-offset);file.Transfer(expected.data(),count,false);if(read(stream,block.data(),count,0)||std::memcmp(expected.data(),block.data(),count)){Log("saved replay: verification failed offset=%u",offset);throw std::runtime_error("native restore differs");}offset+=count;}
   Log("saved replay: restored recording byte-identical");
   for(auto item=begin;item!=end;++item){unsigned n{};if(Buffer::ReadData(&metadata,nullptr,&n,4)||n>h.metadata-metadata.cursor)throw std::runtime_error("serializer size");Buffer record;record.namedRecords=(item-begin==1 || item-begin==2);record.data.resize(n);if(n)Buffer::ReadData(&metadata,nullptr,record.data.data(),n);auto object=reinterpret_cast<void*>(*item);Log("saved replay: restoring serializer %u bytes=%u",static_cast<unsigned>(item-begin),n);if(Method<Serialize>(object,0x20)(object,&record)||record.failed||record.cursor!=n)throw std::runtime_error("serializer load");}
   if(metadata.cursor!=metadata.data.size())throw std::runtime_error("serializer trailing data");
   // Discard the new attempt's seek checkpoints before restoring the saved start.
   reinterpret_cast<void(__thiscall*)(void*)>(base+0x92e6d0)(inner);
   Log("saved replay: checkpoints discarded");std::memcpy(Read<void*>(snapshot,4),snap.data(),snap.size());Put<unsigned>(snapshot,0xc,0);Put<unsigned>(snapshot,0x10,h.snapshot);
   Put(inner,0x260,h.start);Put(inner,0x268,h.startOffset);Put(inner,0x280,h.end);Put(inner,0x288,h.bytes);Put(inner,0x1c0,h.end);Put(inner,0x1b8,h.end);archivePlayback=true;
  }
 }catch(...){seek(stream,previous,0);throw;}
 seek(stream,load?h.bytes:previous,0);
 if(!load && !FlushFileBuffers(file.handle))throw std::runtime_error("archive flush");
 Log("saved replay: %s complete start=%.6f end=%.6f snapshot=%u bytes=%u",load?"load":"save",h.start,h.end,h.snapshot,h.bytes);
}
}
bool ReplayStorageRequested() {
 wchar_t path[MAX_PATH]{};
 const auto length=GetEnvironmentVariableW(L"DIRT2VR_REPLAY_DIRECTORY",path,MAX_PATH);
 return length>0 && length<MAX_PATH;
}
bool EnableReplayStorage() {
 if(!ReplayStorageRequested())return true;
 wchar_t path[MAX_PATH]{},full[MAX_PATH]{},mode[12]{};
 GetEnvironmentVariableW(L"DIRT2VR_REPLAY_DIRECTORY",path,MAX_PATH);
 const auto length=GetFullPathNameW(path,MAX_PATH,full,nullptr);
 GetEnvironmentVariableW(L"DIRT2VR_REPLAY_MODE",mode,12);
 const auto attributes=GetFileAttributesW(full);
 if(!SupportedHost() || !length || length>=MAX_PATH || wcscmp(path,full) ||
    attributes==INVALID_FILE_ATTRIBUTES || !(attributes&FILE_ATTRIBUTE_DIRECTORY) ||
    (attributes&FILE_ATTRIBUTE_REPARSE_POINT) || (wcscmp(mode,L"save") && wcscmp(mode,L"load")))return false;
 auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
 {constexpr unsigned char bytes[]{0x8b,0x44,0x24,0xc,0x56,0x8b,0xf1,0x8b,0x4c,0x24,0x8,0x89};if(std::memcmp(base+0x5cac30,bytes,sizeof bytes))return false;}
 // The next instruction contains an ASLR-relocated address; guard its prefix only.
 {constexpr unsigned char bytes[]{0xf6,0x44,0x24,0x4,0x1,0x56,0x8b,0xf1};if(std::memcmp(base+0x642eb0,bytes,sizeof bytes))return false;}
 {constexpr unsigned char bytes[]{0x51,0x55,0x8b,0xe9,0x8b,0x8d,0xc8,0xc,0x0,0x0,0x57,0xc6};if(std::memcmp(base+0x38e040,bytes,sizeof bytes))return false;}
 {constexpr unsigned char bytes[]{0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x54,0x53,0x56,0x57};if(std::memcmp(base+0x394d20,bytes,sizeof bytes))return false;}
 {constexpr unsigned char bytes[]{0x83,0xec,0x14,0x53,0x55,0x56,0x57,0x8d,0x4c,0x24,0x1c,0xe8};if(std::memcmp(base+0x6eb640,bytes,sizeof bytes))return false;}
 {constexpr unsigned char bytes[]{0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0xd4,0x0,0x0,0x0};if(std::memcmp(base+0x715eb0,bytes,sizeof bytes))return false;}
 {constexpr unsigned char bytes[]{0x55,0x8b,0xec,0x83,0xe4,0xf0,0x83,0xec,0x64,0x53,0x56,0x57};if(std::memcmp(base+0x394b30,bytes,sizeof bytes))return false;}
 {constexpr unsigned char bytes[]{0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0x34,0x1,0x0,0x0};if(std::memcmp(base+0x394c20,bytes,sizeof bytes))return false;}
 {constexpr unsigned char bytes[]{0x83,0xec,0x24,0x53,0x55,0x56,0x57,0x8b,0xf9,0x8b,0x8f,0x60};if(std::memcmp(base+0x394e10,bytes,sizeof bytes))return false;}
 {constexpr unsigned char bytes[]{0x83,0xec,0x18,0x53,0x55,0x56,0x57,0x8b,0xf9,0x8b,0x8f,0xe8};if(std::memcmp(base+0x394ef0,bytes,sizeof bytes))return false;}
 directory=full;loading=!wcscmp(mode,L"load");configured=true;
 hooksReady=InstallAudioProbe() && InstallSurfaceHooks(loading) && (!loading || (InstallAnimationProbe() && InstallMessageProbe()));
 if(!hooksReady)ReportFailure("Saved replay hooks are unavailable for this game build.");
 return hooksReady;
}
int StorageFault(EXCEPTION_POINTERS* error) {
 if(error->ExceptionRecord->ExceptionCode==0xe06d7363)return EXCEPTION_CONTINUE_SEARCH; // C++ unwinding retains file/lock cleanup.
 Log("saved replay: fault=%08lx address=%p",error->ExceptionRecord->ExceptionCode,error->ExceptionRecord->ExceptionAddress);
 return EXCEPTION_EXECUTE_HANDLER;
}
bool GuardedTransfer(unsigned char* inner,bool load,const wchar_t* path) {
 __try {TransferRecording(inner,load,path);return true;}
 __except(StorageFault(GetExceptionInformation())) {return false;}
}
bool ReplayStorageComplete(unsigned char* inner) {
 if(!configured)return true;
 if(!hooksReady || playbackFailed)return !loading;
 if(loading && loaded)return true;
 const auto bytes=Read<unsigned>(inner,0x288);const auto end=Read<double>(inner,0x280);
 if(!loading && lastInner==inner && lastBytes==bytes && lastEnd==end)return true;
 lastInner=inner;lastBytes=bytes;lastEnd=end;
 const auto path=directory+(loading?L"\\recording.bin":L"\\attempt-"+std::to_wstring(++sequence)+L".tmp");
 bool success{};
 try {
  success=GuardedTransfer(inner,loading,path.c_str());
  if(success && !loading) {
   const auto complete=directory+L"\\attempt-"+std::to_wstring(sequence)+L".bin";
   success=MoveFileExW(path.c_str(),complete.c_str(),MOVEFILE_WRITE_THROUGH)!=FALSE;
  }
 }catch(const std::exception& e){Log("saved replay: %s win32=%lu",e.what(),GetLastError());}
 if(!success) {
  ReportFailure(loading?"The saved replay could not be restored. Return to menus and choose another replay.":"This completed run could not be saved. Its in-game Full Replay is still available.");
  if(loading)playbackFailed=true;
 }
 loaded=loading && success;
 return loading?success:true;
}
void ReplayStorageBegin() {
 if(!configured)return;
 {std::lock_guard lock(surfaceLock);savedSurfaces={};restoredSurfaces.clear();surfaceCaptureInvalid=false;surfaceOwner=nullptr;}
 std::lock_guard<std::mutex> lock(audioLock);
 recordedAudio.clear();restoredAudio.clear();audioCaptureInvalid=false;
 archivePlayback=false;loaded=false;lastInner=nullptr;lastBytes=0;lastEnd=0;
 Log("saved replay: new attempt; previous recording identities discarded");
}
// An incompatible typed record must never continue through partially restored
// native state. Shut down normally so the session manager can restore its files.
BOOL CALLBACK CloseReplayWindow(HWND window,LPARAM) {
 DWORD pid{};GetWindowThreadProcessId(window,&pid);
 if(pid==GetCurrentProcessId() && IsWindowVisible(window))PostMessageW(window,WM_CLOSE,0,0);
 return TRUE;
}
void ReplayStorageUpdate(void(__thiscall* original)(void*,const double*),void* root,const double* dt) {
 if(playbackFailed){EnumWindows(CloseReplayWindow,0);return;}
 if(!archivePlayback){original(root,dt);return;}
 __try {original(root,dt);}
 __except(StorageFault(GetExceptionInformation())) {
  playbackFailed=true;
  ReportFailure("This saved replay contains an unsupported native record. Playback was stopped and the game is closing.");
  EnumWindows(CloseReplayWindow,0);
 }
}
}
