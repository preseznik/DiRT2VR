// Physics frames contain two pointers per wheel: the named surface and its
// coefficient block at +0x40. Rebuild only these typed references, never scan
// arbitrary replay bytes for values that happen to resemble addresses.
struct SurfaceTable {unsigned begin{},stride{};std::vector<unsigned> keys;};
std::mutex surfaceLock;
SurfaceTable liveSurfaces, savedSurfaces;
std::map<unsigned,unsigned> restoredSurfaces;
std::map<unsigned,unsigned> restoredScrapeEffects;
bool surfaceCaptureInvalid{};
using SurfaceLookup=void*(__thiscall*)(void*,void*);
SurfaceLookup originalSurfaceLookup{};
std::atomic<void*> surfaceOwner{};
bool ReadSurfaceTable(void* context,SurfaceTable& result) {
 auto bank=Read<void*>(context,0xd4);if(!bank)return false;
 auto dataset=Read<void*>(bank,8);if(!dataset)return false;
 auto facade=Read<void*>(dataset,0x18);if(!facade)return false;
 auto owner=Read<void*>(facade,4);if(!owner)return false;
 auto table=Read<void*>(owner,0x14);if(!table || !Read<unsigned char>(table,0x10))return false;
 const auto count=Read<unsigned>(table,8),stride=Read<unsigned>(table,12),begin=Read<unsigned>(table,4);
 if(!begin || !count || count>4096 || stride!=0x1a0 || begin>0xffffffffu-count*stride)return false;
 result.begin=begin;result.stride=stride;result.keys.clear();
 for(unsigned i=0;i<count;++i){auto key=Read<unsigned>(reinterpret_cast<void*>(begin+i*stride));
  for(unsigned shift=0;shift<32;shift+=8)if(((key>>shift)&255)<32 || ((key>>shift)&255)>126)return false;
  if(std::find(result.keys.begin(),result.keys.end(),key)!=result.keys.end())return false;
  result.keys.push_back(key);
 }
 return true;
}
bool GuardedSurfaceTable(void* context,SurfaceTable& result) {
 __try{return ReadSurfaceTable(context,result);}
 __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
void* __fastcall SurfaceLookupCurrent(void* context,void*,void* surface) {
 auto result=originalSurfaceLookup(context,surface);
 // The table is stable during an event. Recheck when its owner changes.
 if(surfaceOwner.load()!=context){
  SurfaceTable table;
  if(GuardedSurfaceTable(context,table)){
   std::lock_guard lock(surfaceLock);liveSurfaces=std::move(table);surfaceOwner=context;
  }
 }
 return result;
}
bool SurfaceReference(const SurfaceTable& table,unsigned value,unsigned offset,unsigned& key) {
 if(!value){key=0;return true;}
 if(value<table.begin || table.stride!=0x1a0)return false;
 const auto relative=value-table.begin,index=relative/table.stride;
 if(index>=table.keys.size() || relative%table.stride!=offset)return false;
 key=table.keys[index];return true;
}
void CaptureSurfaceFrame(const void* frame) {
 std::lock_guard lock(surfaceLock);
 if(savedSurfaces.keys.empty())savedSurfaces=liveSurfaces;
 for(unsigned wheel=0;wheel<4;++wheel)for(unsigned field=0;field<2;++field){unsigned key{};
  if(!SurfaceReference(savedSurfaces,Read<unsigned>(frame,0x50+wheel*0x1c+field*4),field*0x40,key))surfaceCaptureInvalid=true;
 }
}
void RestoreSurfaceFrame(void* frame) {
 for(unsigned wheel=0;wheel<4;++wheel)for(unsigned field=0;field<2;++field){
  const auto offset=0x50+wheel*0x1c+field*4,value=Read<unsigned>(frame,offset);unsigned key{};
  if(!SurfaceReference(savedSurfaces,value,field*0x40,key))RaiseException(0xe0420130,EXCEPTION_NONCONTINUABLE,0,nullptr);
  if(key){
   auto found=restoredSurfaces.find(key);if(found==restoredSurfaces.end())RaiseException(0xe0420131,EXCEPTION_NONCONTINUABLE,0,nullptr);
   const auto current=found->second+field*0x40;Put(frame,offset,current);
   static unsigned reports{};if(reports++<4)Log("saved replay: surface reference %08x -> %08x key=%08x",value,current,key);
  }
 }
}
struct SurfaceStream {
 void** table;void* source;
 static void __fastcall Destroy(SurfaceStream*,void*){}
 static unsigned __fastcall Write(SurfaceStream* s,void*,void* p,unsigned n){if(n==0xb0)CaptureSurfaceFrame(p);return Method<Io>(s->source,4)(s->source,p,n);}
 static unsigned __fastcall ReadData(SurfaceStream* s,void*,void* p,unsigned n,unsigned flag){auto error=Method<ReadIo>(s->source,8)(s->source,p,n,flag);if(!error && n==0xb0)RestoreSurfaceFrame(p);return error;}
 SurfaceStream(void* input):source(input){static void* methods[]{reinterpret_cast<void*>(&Destroy),reinterpret_cast<void*>(&Write),reinterpret_cast<void*>(&ReadData)};table=methods;}
};
using PhysicsWrite=void(__thiscall*)(void*,void*,unsigned,void*);
using PhysicsRead=void(__thiscall*)(void*,void*,unsigned,void*,void*,unsigned);
PhysicsWrite originalPhysicsWrite{};PhysicsRead originalPhysicsRead{};
void __fastcall PhysicsWritePortable(void* object,void*,void* car,unsigned index,void* stream){SurfaceStream wrapper(stream);originalPhysicsWrite(object,car,index,&wrapper);}
void __fastcall PhysicsReadPortable(void* object,void*,void* car,unsigned index,void* stream,void* frame,unsigned flag){
 if(!archivePlayback){originalPhysicsRead(object,car,index,stream,frame,flag);return;}
 SurfaceStream wrapper(stream);originalPhysicsRead(object,car,index,&wrapper,frame,flag);
}
std::vector<unsigned> SurfaceMetadata() {
 std::lock_guard lock(surfaceLock);
 if(surfaceCaptureInvalid || savedSurfaces.keys.empty())throw std::runtime_error("surface capture incomplete");
 std::vector<unsigned> result{0x46525553,savedSurfaces.begin,savedSurfaces.stride,static_cast<unsigned>(savedSurfaces.keys.size())};
 result.insert(result.end(),savedSurfaces.keys.begin(),savedSurfaces.keys.end());
 // BroadcastTriggerParticleScrape stores a material-effect definition at +0x40.
 // Resolve definitions through the same native material lookup used by contacts.
 // Keep their source identities in metadata; never scan or rewrite frame bytes.
 auto owner=surfaceOwner.load();if(!owner)throw std::runtime_error("surface owner unavailable");
 std::map<unsigned,unsigned> effects;
 for(unsigned i=0;i<liveSurfaces.keys.size();++i) {
  auto effect=originalSurfaceLookup(owner,reinterpret_cast<void*>(liveSurfaces.begin+i*liveSurfaces.stride));
  if(effect)effects.emplace(reinterpret_cast<unsigned>(effect),liveSurfaces.keys[i]);
 }
 result.push_back(0x58454653);result.push_back(static_cast<unsigned>(effects.size()));
 for(const auto& [pointer,key]:effects){result.push_back(pointer);result.push_back(key);}
 Log("saved replay: captured %u material effect identities",static_cast<unsigned>(effects.size()));
 return result;
}
void ResolveSurfaces(const std::vector<unsigned char>& bytes,unsigned cursor,unsigned version) {
 if(cursor>bytes.size() || bytes.size()-cursor<16 || Read<unsigned>(bytes.data()+cursor)!=0x46525553)throw std::runtime_error("surface metadata missing");
 auto data=bytes.data()+cursor;SurfaceTable source;
 source.begin=Read<unsigned>(data,4);source.stride=Read<unsigned>(data,8);const auto count=Read<unsigned>(data,12);
 if(!source.begin || source.stride!=0x1a0 || !count || count>4096 || source.begin>0xffffffffu-count*source.stride || bytes.size()-cursor<16+count*4)throw std::runtime_error("surface metadata bounds");
 std::lock_guard lock(surfaceLock);restoredSurfaces.clear();restoredScrapeEffects.clear();
 if(liveSurfaces.keys.empty())throw std::runtime_error("current surface table unavailable");
 for(unsigned i=0;i<count;++i){const auto key=Read<unsigned>(data,16+i*4);source.keys.push_back(key);
  auto found=std::find(liveSurfaces.keys.begin(),liveSurfaces.keys.end(),key);
  if(found==liveSurfaces.keys.end() || !restoredSurfaces.emplace(key,liveSurfaces.begin+static_cast<unsigned>(found-liveSurfaces.keys.begin())*liveSurfaces.stride).second)throw std::runtime_error("surface identity mismatch");
 }
 cursor+=16+count*4;
 if(version==4){if(cursor!=bytes.size())throw std::runtime_error("surface metadata trailing data");}
 else {
  if(bytes.size()-cursor<8 || Read<unsigned>(bytes.data()+cursor)!=0x58454653)throw std::runtime_error("material effect metadata missing");
  const auto effects=Read<unsigned>(bytes.data()+cursor,4);cursor+=8;
  if(effects>4096 || bytes.size()-cursor!=effects*8)throw std::runtime_error("material effect metadata bounds");
  auto owner=surfaceOwner.load();if(!owner)throw std::runtime_error("surface owner unavailable");
  for(unsigned i=0;i<effects;++i){
   const auto pointer=Read<unsigned>(bytes.data()+cursor+i*8),key=Read<unsigned>(bytes.data()+cursor+i*8,4);
   auto surface=restoredSurfaces.find(key);
   if(!pointer || surface==restoredSurfaces.end())throw std::runtime_error("material effect identity missing");
   auto effect=originalSurfaceLookup(owner,reinterpret_cast<void*>(surface->second));
   if(!effect || !restoredScrapeEffects.emplace(pointer,reinterpret_cast<unsigned>(effect)).second)throw std::runtime_error("material effect identity mismatch");
  }
  Log("saved replay: resolved %u material effect identities",effects);
 }
 savedSurfaces=std::move(source);Log("saved replay: resolved %u named surfaces",count);
}
bool InstallSurfaceHooks(bool load) {
 auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
 const unsigned char lookup[]{0x51,0x53,0x56,0x57,0x8b,0x7c,0x24,0x14};
 const unsigned char write[]{0x55,0x8b,0xec,0x83,0xe4,0xf0,0x81,0xec,0x14,0x09,0,0};
 const unsigned char read[]{0x53,0x33,0xdb,0x38,0x5c,0x24,0x18,0x55,0x56};
 if(std::memcmp(base+0x3439e0,lookup,sizeof lookup) || std::memcmp(base+0x91c5c0,write,sizeof write) || std::memcmp(base+0x9129a0,read,sizeof read))return false;
 if(MH_CreateHook(base+0x3439e0,reinterpret_cast<void*>(SurfaceLookupCurrent),reinterpret_cast<void**>(&originalSurfaceLookup))!=MH_OK || EnableRecordedHook(base+0x3439e0)!=MH_OK)return false;
 auto target=base+(load?0x9129a0:0x91c5c0);
 return MH_CreateHook(target,load?reinterpret_cast<void*>(PhysicsReadPortable):reinterpret_cast<void*>(PhysicsWritePortable),load?reinterpret_cast<void**>(&originalPhysicsRead):reinterpret_cast<void**>(&originalPhysicsWrite))==MH_OK && EnableRecordedHook(target)==MH_OK;
}
