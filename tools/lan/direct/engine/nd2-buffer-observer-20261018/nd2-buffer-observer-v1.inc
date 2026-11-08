// Passive native delivery snapshot. Never frees or changes borrowed bytes.
namespace nd2_observer {
constexpr U32 Capacity=8,MaxBytes=0xA000;
struct Record{volatile LONG state{};U32 ordinal{},manager{},game{},job{},envelope{},payload{},allocator{},size{},thread{},caller{};char path[260]{};unsigned char bytes[MaxBytes]{};};
Record records[Capacity]{};volatile LONG reserved{},rejected{};LONG lastRejected{};wchar_t outputPrefix[512]{};
using Insert=void(__thiscall*)(void*,const void*);Insert originalInsert{};
bool Range(U32 p,U32 n){return p>=0x10000&&n&&uint64_t(p)+n<=0x80000000ull;}
Record* Capture(U32 manager,U32 envelope,U32 caller){
 if(caller!=At(0x6DCF6B))return nullptr;
 __try{
  const U32 loader=Read(At(0x1482D8C)),game=Read(loader+0xC),job=envelope-0x18C;
  if(!Range(envelope,0x24)||!Range(job,0x1D0)||Read(loader)!=At(0x1325688)||Read(game)!=At(0x1329A48)||Read(game+0x222C)!=manager||Read(envelope)!=At(0x1356E28)||Read(job)!=At(0x1359FF0)||Read(job+0x44)!=At(0x1321620)||Read(job+0x48)!=260||Read(job+0x4C)!=job+0x50||Read(envelope+4)!=job+0x50||Read(envelope+0x14)!=job+0x160||Read(envelope+0x18)!=job+0x168||Read(envelope+0x1C)!=job+0x170||Read(job+0x16C)||Read(job+0x170)!=1||Read(job+0x174)||!Read(manager+0x7C)||Read(manager+0xF8)>=10)return nullptr;
  const U32 size=Read(job+0x168),payload=Read(job+0x160),kind=Read(envelope+8),allocator=Read(envelope+0x20);
  if(!size||size>MaxBytes||!Range(payload,size)||!Range(kind,7)||memcmp(reinterpret_cast<const void*>(kind),"damage",7)||!Range(allocator,4))return nullptr;
  const char*path=reinterpret_cast<const char*>(job+0x50);U32 n{};
  for(;n<260&&path[n];++n)if(static_cast<unsigned char>(path[n])<32||static_cast<unsigned char>(path[n])>126)return nullptr;
  if(!n||n==260)return nullptr;
  const LONG index=InterlockedIncrement(&reserved)-1;if(index<0||index>=static_cast<LONG>(Capacity))return nullptr;
  auto&r=records[index];if(InterlockedCompareExchange(&r.state,1,0))return nullptr;
  r.ordinal=static_cast<U32>(index);r.manager=manager;r.game=game;r.job=job;r.envelope=envelope;r.payload=payload;r.allocator=allocator;r.size=size;r.thread=GetCurrentThreadId();r.caller=caller;
  memcpy(r.path,path,n+1);memcpy(r.bytes,reinterpret_cast<const void*>(payload),size);
  if(Read(job+0x160)!=payload||Read(job+0x168)!=size||Read(job+0x16C)||Read(envelope+0x20)!=allocator||Read(game+0x222C)!=manager||Read(envelope)!=At(0x1356E28)||memcmp(r.path,path,n+1)||memcmp(r.bytes,reinterpret_cast<const void*>(payload),size)){InterlockedExchange(&r.state,-1);return nullptr;}
  InterlockedExchange(&r.state,5);return &r; // Copied, but not consumable before original return.
 }__except(EXCEPTION_EXECUTE_HANDLER){return nullptr;}
}
void __fastcall Observe(void*self,void*,const void*envelope){
 const DWORD incoming=GetLastError();const U32 caller=reinterpret_cast<U32>(_ReturnAddress());
 Record*const record=Capture(reinterpret_cast<U32>(self),reinterpret_cast<U32>(envelope),caller);
 if(!record)InterlockedIncrement(&rejected);
 SetLastError(incoming);originalInsert(self,envelope); // Exactly once, unchanged arguments; original faults are not swallowed.
 const DWORD returned=GetLastError();if(record)InterlockedExchange(&record->state,2); // Final record access by this producer.
 SetLastError(returned);
}
bool WriteEvidence(const wchar_t*name,const void*bytes,DWORD size){
 HANDLE file=CreateFileW(name,GENERIC_WRITE,FILE_SHARE_READ,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);if(file==INVALID_HANDLE_VALUE)return false;
 DWORD written{};const bool ok=WriteFile(file,bytes,size,&written,nullptr)&&written==size&&FlushFileBuffers(file);CloseHandle(file);return ok;
}
void Drain(){
 if(GetCurrentThreadId()!=static_cast<DWORD>(holdThread))return;
 const DWORD saved=GetLastError();
 for(U32 i=0;i<Capacity;++i){auto&r=records[i];if(InterlockedCompareExchange(&r.state,3,2)!=2)continue;
  wchar_t path[600]{};swprintf_s(path,L"%s-%02u.bin",outputPrefix,i);bool ok=WriteEvidence(path,r.bytes,r.size);
  char escaped[521]{};U32 n{};for(U32 j=0;j<260&&r.path[j];++j){if(r.path[j]=='\\'||r.path[j]=='"')escaped[n++]='\\';escaped[n++]=r.path[j];}
  char json[1500]{};const int length=sprintf_s(json,"{\"schema\":1,\"ordinal\":%u,\"pid\":%lu,\"image_base\":%u,\"game\":%u,\"manager\":%u,\"job\":%u,\"envelope\":%u,\"payload_telemetry\":%u,\"allocator_telemetry\":%u,\"thread\":%u,\"caller\":%u,\"preferred_caller\":7196523,\"size\":%u,\"native_path\":\"%s\",\"original_returned\":true,\"native_bytes_freed_by_observer\":false,\"admission_proven\":false}\n",r.ordinal,GetCurrentProcessId(),base,r.game,r.manager,r.job,r.envelope,r.payload,r.allocator,r.thread,r.caller,r.size,escaped);
  swprintf_s(path,L"%s-%02u.json",outputPrefix,i);ok=ok&&length>0&&WriteEvidence(path,json,static_cast<DWORD>(length));
  char note[380]{};sprintf_s(note,"ND2_CAPTURE written=%u ordinal=%u bytes=%u path=%s original_returned=1\r\n",unsigned(ok),i,r.size,r.path);Log(note);InterlockedExchange(&r.state,ok?4:-1);
 }
 const LONG failures=InterlockedCompareExchange(&rejected,0,0);if(failures!=lastRejected){char note[100]{};sprintf_s(note,"ND2_CAPTURE rejected=%ld; originals preserved\r\n",failures);Log(note);lastRejected=failures;}
 SetLastError(saved);
}
bool SourceSignatures(){
 {const unsigned char bytes[]{0x56,0x8B,0xF1,0x8B,0x4E,0x7C,0x85,0xC9,0x0F,0x84,0xE8,0x00,0x00,0x00,0x8B,0x44,0x24,0x08,0x8B,0x50,0x18,0x53,0x8B,0x1A,0x55,0x57,0x8B,0x78,0x04,0x8B,0x40,0x14,0x8B,0x10,0x8B,0x86,0xF8,0x00,0x00,0x00,0x69,0xC0,0x00,0xA0,0x00,0x00,0x53,0x52,0x03,0xC1,0x50,0x8B,0xEF,0xE8,0xB6,0x6C,0x43,0x00,0x8B,0x8E,0xF8,0x00,0x00,0x00,0x8B,0xC7,0x83,0xC4,0x0C,0x89,0x9C,0x8E,0xFC,0x00,0x00,0x00,0x8D,0x50,0x01,0x90,0x8A,0x08,0x83,0xC0,0x01,0x84,0xC9,0x75,0xF7,0x2B,0xC2,0x33,0xC9,0x85,0xC0,0x7E,0x18,0x8A,0x14,0x39,0x80,0xFA,0x5C,0x74,0x05,0x80,0xFA,0x2F,0x75,0x04,0x8D,0x6C,0x39,0x01,0x83,0xC1,0x01,0x3B,0xC8,0x7C,0xE8,0x8B,0x86,0xF8,0x00,0x00,0x00,0x6A,0x0C,0x8D,0x14,0x40,0x55,0x8D,0x84,0x96,0x80,0x00,0x00,0x00,0x50,0xE8,0xBE,0xB3,0x3B,0x00,0x8B,0x86,0xF8,0x00,0x00,0x00,0x8D,0x0C,0x40,0x03,0xC9,0x03,0xC9,0x8D,0xBC,0x31,0x80,0x00,0x00,0x00,0x8B,0xC7,0x8D,0x58,0x01,0xEB,0x03,0x8D,0x49,0x00,0x8A,0x10,0x83,0xC0,0x01,0x84,0xD2,0x75,0xF7,0x2B,0xC3,0x33,0xD2,0x85,0xC0,0x7E,0x1B,0x80,0x3F,0x2E,0x74,0x0C,0x83,0xC2,0x01,0x83,0xC7,0x01,0x3B,0xD0,0x7C,0xF1,0xEB,0x0A,0x03,0xCA,0xC6,0x84,0x31,0x80,0x00,0x00,0x00,0x00,0x8B,0x86,0xF8,0x00,0x00,0x00,0x83,0xC0,0x01,0x99,0xB9,0x0A,0x00,0x00,0x00,0xF7,0xF9,0x5F,0x5D,0x5B,0x89,0x96,0xF8,0x00,0x00,0x00,0x5E,0xC2,0x04,0x00};if(!trace::ReadableEqual(reinterpret_cast<void*>(At(0xD9F240)),bytes,sizeof(bytes)))return false;}
 {const unsigned char bytes[]{0xE8,0xD5,0x22,0x6C,0x00};if(!trace::ReadableEqual(reinterpret_cast<void*>(At(0x6DCF66)),bytes,sizeof(bytes)))return false;}
 {const unsigned char bytes[]{0x8B,0x4E,0x20,0x8B,0x56,0x14,0x8B,0x01,0x8B,0x12,0x8B,0x40,0x10,0x52,0xFF,0xD0,0x84,0xDB};if(!trace::ReadableEqual(reinterpret_cast<void*>(At(0x6DD01F)),bytes,sizeof(bytes)))return false;}
 return true;
}
bool Install(){
 const auto log=trace::Environment(L"DIRT2VR_ENGINE_CAREER_LOG");const auto slash=log.find_last_of(L"\\/");if(slash==std::wstring::npos||slash+12>=512)return false;
 if(wcsncpy_s(outputPrefix,log.c_str(),slash+1))return false;if(wcscat_s(outputPrefix,L"native-nd2"))return false;
 if(!SourceSignatures())return false;
 return MH_CreateHook(reinterpret_cast<void*>(At(0xD9F240)),reinterpret_cast<void*>(Observe),reinterpret_cast<void**>(&originalInsert))==MH_OK;
}
}
