// Typed relocation for locally recorded collision-audio messages.
#include <MinHook.h>
#include "gfwl_compat.h"
#include <mutex>
#include <map>
struct AudioIdentity {unsigned pointer,id;char name[32],bank[16];};
std::mutex audioLock;
std::map<unsigned,AudioIdentity> liveAudio,recordedAudio;
std::map<unsigned,unsigned> restoredAudio;
bool archivePlayback{};bool audioCaptureInvalid{};
using AudioCtor=void*(__thiscall*)(void*,void*,void*,unsigned,void*);
using AudioDtor=void*(__thiscall*)(void*,unsigned);
AudioCtor originalAudioCtor{};AudioDtor originalAudioDtor{};
void* __fastcall AudioConstruct(void* object,void*,void* name,void* bank,unsigned id,void* config){
 auto result=originalAudioCtor(object,name,bank,id,config);
 AudioIdentity key{};key.pointer=reinterpret_cast<unsigned>(object);key.id=id;
 std::memcpy(key.name,static_cast<char*>(object)+0x18,32);std::memcpy(key.bank,static_cast<char*>(object)+0x44,16);
 if(!std::memchr(key.name,0,32)||!std::memchr(key.bank,0,16)){std::lock_guard<std::mutex> lock(audioLock);audioCaptureInvalid=true;Log("archive audio: invalid live identity; saving disabled");return result;}
 {std::lock_guard<std::mutex> lock(audioLock);liveAudio[key.pointer]=key;}
 return result;
}
void* __fastcall AudioDestroy(void* object,void*,unsigned flags){
 {std::lock_guard<std::mutex> lock(audioLock);liveAudio.erase(reinterpret_cast<unsigned>(object));}
 return originalAudioDtor(object,flags);
}
using MessageWrite=void(__thiscall*)(void*,void*);
using MessageRead=void(__thiscall*)(void*,void*,unsigned);
MessageWrite originalMessageWrite{};MessageRead originalMessageRead{};
struct AudioStream {
 void** table;void* source;unsigned remaining{};bool count=true;
 static void __fastcall Destroy(AudioStream*,void*){}
 static unsigned __fastcall Write(AudioStream* s,void*,void* p,unsigned n){
  if(n==64){auto key=Read<unsigned>(p,8);if(key){std::lock_guard<std::mutex> lock(audioLock);auto found=liveAudio.find(key);if(found==liveAudio.end()){if(!audioCaptureInvalid)Log("archive audio: unregistered sound %08x; saving disabled",key);audioCaptureInvalid=true;}
   else {auto previous=recordedAudio.find(key);if(previous!=recordedAudio.end() && (previous->second.id!=found->second.id || strcmp(previous->second.name,found->second.name) || strcmp(previous->second.bank,found->second.bank)))audioCaptureInvalid=true;
    if(recordedAudio.size()>=4096)audioCaptureInvalid=true;else recordedAudio[key]=found->second;}
  }}
  return Method<Io>(s->source,4)(s->source,p,n);
 }
 static unsigned __fastcall ReadData(AudioStream* s,void*,void* p,unsigned n,unsigned flag){
  auto error=Method<ReadIo>(s->source,8)(s->source,p,n,flag);if(error)return error;
  if(s->count){if(n!=4 || Read<unsigned>(p)>50)RaiseException(0xe0420103,EXCEPTION_NONCONTINUABLE,0,nullptr);s->remaining=Read<unsigned>(p);s->count=false;}
  else {
   if(n!=64 || !s->remaining--)RaiseException(0xe0420104,EXCEPTION_NONCONTINUABLE,0,nullptr);
   auto old=Read<unsigned>(p,8);if(old){auto found=restoredAudio.find(old);if(found==restoredAudio.end())RaiseException(0xe0420105,EXCEPTION_NONCONTINUABLE,0,nullptr);Put(p,8,found->second);}
   Put(p,0,reinterpret_cast<unsigned>(GetModuleHandleW(nullptr))+0xf28d10);Put(p,4,0u);
  }return 0;
 }
 AudioStream(void* input):source(input){static void* methods[]{reinterpret_cast<void*>(&Destroy),reinterpret_cast<void*>(&Write),reinterpret_cast<void*>(&ReadData)};table=methods;}
};
void __fastcall AudioWrite(void* object,void*,void* stream){AudioStream wrapper(stream);originalMessageWrite(object,&wrapper);}
void __fastcall AudioRead(void* object,void*,void* stream,unsigned flag){if(!archivePlayback){originalMessageRead(object,stream,flag);return;}AudioStream wrapper(stream);originalMessageRead(object,&wrapper,flag);}
void ResolveAudio(const std::vector<AudioIdentity>& saved){
 std::lock_guard<std::mutex> lock(audioLock);restoredAudio.clear();
 for(auto& key:saved){unsigned resolved{},matches{};for(auto& entry:liveAudio){auto& candidate=entry.second;if(candidate.id==key.id && !strcmp(candidate.name,key.name) && !strcmp(candidate.bank,key.bank)){resolved=entry.first;++matches;}}
  if(matches!=1){Log("archive audio: unresolved %s/%s id=%u matches=%u",key.name,key.bank,key.id,matches);throw std::runtime_error("audio identity mismatch");}
  if(!restoredAudio.emplace(key.pointer,resolved).second)throw std::runtime_error("duplicate audio identity");
 }
 Log("archive audio: resolved %u saved sounds against %u current sounds",static_cast<unsigned>(saved.size()),static_cast<unsigned>(liveAudio.size()));
}
bool InstallAudioProbe(){
 auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
 struct Hook{unsigned offset;void* detour;void** original;};
 Hook hooks[]{{0x5cac30,reinterpret_cast<void*>(AudioConstruct),reinterpret_cast<void**>(&originalAudioCtor)},
 {0x642eb0,reinterpret_cast<void*>(AudioDestroy),reinterpret_cast<void**>(&originalAudioDtor)},
 {0x38e040,reinterpret_cast<void*>(AudioWrite),reinterpret_cast<void**>(&originalMessageWrite)},
 {0x394d20,reinterpret_cast<void*>(AudioRead),reinterpret_cast<void**>(&originalMessageRead)}};
 for(auto& hook:hooks){if(MH_CreateHook(base+hook.offset,hook.detour,hook.original)!=MH_OK || EnableRecordedHook(base+hook.offset)!=MH_OK)return false;}
 Log("archive audio: portability hooks installed base=%p",base);return true;
}
