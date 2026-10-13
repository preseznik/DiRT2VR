// The native animation stream stores a constructor function pointer.
// Relocate only that typed field and only the eight inspected constructors.
unsigned savedImageBase{};
bool AcceptSavedImageBase(unsigned source) {
 if(source<0x10000 || source%0x10000 || source>0x7e000000 || (savedImageBase && savedImageBase!=source))return false;
 savedImageBase=source;return true;
}
using AnimationRead=void(__cdecl*)(void*,unsigned,void*);
AnimationRead originalAnimationRead{};
using AnimationBlend=void(__cdecl*)(void*,double,unsigned,void*);
AnimationBlend originalAnimationBlend{};
struct AnimationStream {
 void** table;void* source;unsigned field{};
 static void __fastcall Destroy(AnimationStream*,void*){}
 static unsigned __fastcall Write(AnimationStream* s,void*,void* p,unsigned n){return Method<Io>(s->source,4)(s->source,p,n);}
 static unsigned __fastcall ReadData(AnimationStream* s,void*,void* p,unsigned n,unsigned flag){
  auto error=Method<ReadIo>(s->source,8)(s->source,p,n,flag);if(error)return error;
  constexpr unsigned sizes[]{16,8,2,1,4,4};
  if(s->field<6 && n!=sizes[s->field])RaiseException(0xe0420110,EXCEPTION_NONCONTINUABLE,0,nullptr);
  if(s->field++==4){
   const auto old=Read<unsigned>(p);
   constexpr unsigned allowed[]{0x6ee380,0x7292e0,0x729310,0x7307c0,0x7307f0,0x730820,0x730850,0x918de0};
   if(!savedImageBase){
    // Empty name tables occur in valid recordings. These constructors have
    // distinct low 16 bits; accept only one exact RVA + aligned image match.
    unsigned candidate{};
    for(auto known:allowed){const auto source=old-known;
     if(source>=0x10000 && !(source%0x10000) && source<=0x7e000000){
      if(candidate)RaiseException(0xe0420111,EXCEPTION_NONCONTINUABLE,0,nullptr);candidate=source;
     }
    }
    if(!AcceptSavedImageBase(candidate))RaiseException(0xe0420111,EXCEPTION_NONCONTINUABLE,0,nullptr);
   }
   const auto rva=old-savedImageBase;
   if(!savedImageBase || std::find(std::begin(allowed),std::end(allowed),rva)==std::end(allowed)){Log("archive animation: unknown constructor %08x source=%08x",old,savedImageBase);RaiseException(0xe0420111,EXCEPTION_NONCONTINUABLE,0,nullptr);}
   Put(p,0,reinterpret_cast<unsigned>(GetModuleHandleW(nullptr))+rva);
   static unsigned reports{};if(reports++<8)Log("archive animation: constructor relocated rva=%08x",rva);
  }return 0;
 }
 AnimationStream(void* input):source(input){static void* methods[]{reinterpret_cast<void*>(&Destroy),reinterpret_cast<void*>(&Write),reinterpret_cast<void*>(&ReadData)};table=methods;}
};
void __cdecl AnimationLoad(void* stream,unsigned flag,void* state){
 if(!archivePlayback){originalAnimationRead(stream,flag,state);return;}
 AnimationStream wrapper(stream);originalAnimationRead(&wrapper,flag,state);
}
void __cdecl AnimationInterpolate(void* stream,double amount,unsigned flag,void* state){
 if(!archivePlayback){originalAnimationBlend(stream,amount,flag,state);return;}
 AnimationStream wrapper(stream);originalAnimationBlend(&wrapper,amount,flag,state);
}
bool InstallAnimationProbe(){
 auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
 const unsigned char expected[]{0x83,0xec,0x14,0x53,0x55,0x56,0x57};
 if(std::memcmp(base+0x6eb640,expected,sizeof expected))return false;
 if(MH_CreateHook(base+0x715eb0,reinterpret_cast<void*>(AnimationInterpolate),reinterpret_cast<void**>(&originalAnimationBlend))!=MH_OK || EnableRecordedHook(base+0x715eb0)!=MH_OK)return false;
 return MH_CreateHook(base+0x6eb640,reinterpret_cast<void*>(AnimationLoad),reinterpret_cast<void**>(&originalAnimationRead))==MH_OK && EnableRecordedHook(base+0x6eb640)==MH_OK;
}
// Typed string
// headers carry the same known vtable in every record; require agreement.
unsigned ReadSavedImageBase(const std::vector<unsigned char>& bytes,unsigned types){
 unsigned cursor=0;savedImageBase=0;
 for(unsigned i=0;i<types;++i){
  if(cursor>bytes.size() || bytes.size()-cursor<4)throw std::runtime_error("metadata size");
  auto n=Read<unsigned>(bytes.data()+cursor);cursor+=4;
  if(n>bytes.size()-cursor)throw std::runtime_error("metadata record size");
  if(i==1 || i==2){
   unsigned prefix=i==1?6:4;if(n<prefix || (n-prefix)%80)throw std::runtime_error("metadata names size");
   for(unsigned at=prefix;at<n;at+=80){auto source=Read<unsigned>(bytes.data()+cursor+at)-0xeedfb0;
    if(!AcceptSavedImageBase(source))throw std::runtime_error("metadata image identity");}
  }cursor+=n;
 }
 // Cars without named animation records have no image pointer in this metadata.
 // A typed message or a uniquely matched, allowlisted animation constructor
 // establishes it later. Subsequent records must agree with that same base.
 Log("archive animation: saved image base=%08x current=%p",savedImageBase,GetModuleHandleW(nullptr));
 return cursor;
}
