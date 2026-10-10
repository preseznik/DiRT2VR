// Native message records have a typed vtable header and a transient queue link.
MessageRead originalOtherMessages[4]{};
struct MessageStream {
 void** table;void* source;unsigned type,remaining{};bool count=true;
 static void __fastcall Destroy(MessageStream*,void*){}
 static unsigned __fastcall Write(MessageStream* s,void*,void* p,unsigned n){return Method<Io>(s->source,4)(s->source,p,n);}
 static unsigned __fastcall ReadData(MessageStream* s,void*,void* p,unsigned n,unsigned flag){
  auto error=Method<ReadIo>(s->source,8)(s->source,p,n,flag);if(error)return error;
  if(s->count){if(n!=4 || Read<unsigned>(p)>50)RaiseException(0xe0420120,EXCEPTION_NONCONTINUABLE,0,nullptr);s->count=false;s->remaining=Read<unsigned>(p);}
  else {
   constexpr unsigned sizes[]{80,288,28,16},tables[]{0xf28748,0xf28738,0xeeec74,0xf28250};
   if(n!=sizes[s->type] || !s->remaining-- || Read<unsigned>(p)!=savedImageBase+tables[s->type])RaiseException(0xe0420121,EXCEPTION_NONCONTINUABLE,0,nullptr);
   Put(p,0,reinterpret_cast<unsigned>(GetModuleHandleW(nullptr))+tables[s->type]);Put(p,4,0u);
   static unsigned seen[4]{};if(!seen[s->type]++)Log("archive message: relocated type=%u size=%u",s->type,n);
  }return 0;
 }
 MessageStream(void* input,unsigned index):source(input),type(index){static void* methods[]{reinterpret_cast<void*>(&Destroy),reinterpret_cast<void*>(&Write),reinterpret_cast<void*>(&ReadData)};table=methods;}
};
template<unsigned Index> void __fastcall OtherMessageRead(void* object,void*,void* stream,unsigned flag){
 if(!archivePlayback){originalOtherMessages[Index](object,stream,flag);return;}
 MessageStream wrapper(stream,Index);originalOtherMessages[Index](object,&wrapper,flag);
}
bool InstallMessageProbe(){
 auto base=reinterpret_cast<unsigned char*>(GetModuleHandleW(nullptr));
 unsigned offsets[]{0x394b30,0x394c20,0x394e10,0x394ef0};
 void* functions[]{reinterpret_cast<void*>(OtherMessageRead<0>),reinterpret_cast<void*>(OtherMessageRead<1>),reinterpret_cast<void*>(OtherMessageRead<2>),reinterpret_cast<void*>(OtherMessageRead<3>)};
 for(unsigned i=0;i<4;++i)if(MH_CreateHook(base+offsets[i],functions[i],reinterpret_cast<void**>(&originalOtherMessages[i]))!=MH_OK || EnableRecordedHook(base+offsets[i])!=MH_OK)return false;
 return true;
}
