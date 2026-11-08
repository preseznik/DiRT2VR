// Bind existing native settings to the typed registry. Never set a driving option.
// Initial isolated load only; successful bindings remain registered until process exit.
#include "native-driver-settings-guards-v1.inc"
namespace driver_settings_v1 {
struct Key { const char* name; U32 imageName; bool boolean; };
constexpr Key keys[]{
 {"driveraid_traction",0x13002E8,false},{"transmissionGated",0x12FFB4C,true},
 {"clutchAsBrake",0x12FFB74,true},{"driveraid_steering",0x13002D4,false},
 {"driveraid_brake",0x13002C4,false},{"realistic_damage",0x13002B0,false},
 {"pro_mode",0x12EEA30,false},{"head_camera",0x13002A4,false},
 {"difficulty",0x12F9ED8,false},{"transmission_type",0x12FFB60,false}};
struct State { bool attempted{},ready{},failed{}; U32 context{},registry{},allocator{},store{},begin{},end{};
 U32 binding[10]{},row[10]{},value[10]{}; bool created[10]{}; } state;
void Fail(const char* why) {if(!state.failed){state.failed=true;char line[220]{};sprintf_s(line,"SETTINGS REJECT %s; no native loading permitted\r\n",why);Log(line);}}
U32 ReaderType(const Key& k){return At(k.boolean?0x147D220:0x147D80C);}
U32 BindingType(const Key& k){return At(k.boolean?0x147D5DC:0x147D160);}
U32 Table(const Key& k){return At(k.boolean?0x1315CB8:0x12F169C);}
U32 Lookup(const Key& k) {
 return reinterpret_cast<U32(__thiscall*)(void*,U32,const char*)>(At(0x484010))(
  reinterpret_cast<void*>(state.registry),ReaderType(k),reinterpret_cast<const char*>(At(k.imageName)));
}
bool Graph(U32 game) {
 using namespace initial_load_v2;
 const U32 frontend=Read(game+0x2250),context=Read(frontend+8),ri=Read(At(0x147DD6C)),ai=Read(At(0x147DD18)),si=Read(At(0x147F43C));
 if(game!=loadOwner || game!=heldOwner || context!=loadContext || !SelectedCareerLoaded() || holdFailed ||
    GetCurrentThreadId()!=static_cast<DWORD>(holdThread) || ri>=512 || ai>=512 || si>=512)return false;
 const U32 registry=Read(context+ri*4),allocator=Read(context+ai*4),store=Read(context+si*4);
 U32 frame{};
 if(Read(registry)!=At(0x12F6378) || Read(allocator)!=At(0x13CC8E0) || allocator!=Read(frontend+0xC) ||
    Read(Read(allocator)+8)!=At(0x11A1740) || Read(Read(allocator)+0x10)!=At(0x119AA30) || !NativeFrame(allocator,frame) || !Ptr(store,0x38) ||
    store!=loadProfile || store!=Read(game+0x2284) || Read(store)!=At(0x1323BDC) ||
    Read(store+0x30)!=loadSdu || loadSdu!=Read(game+0x2294) ||
    Read(store+0x34)!=Read(loadSdu+0x18) || Read(store+0x34)!=allocator)return false;
 const U32 begin=Read(store+0x24),end=Read(store+0x28);
 if(!Ptr(begin,0x128) || end<=begin || (end-begin)%0x128 || (end-begin)/0x128>1024 || !Ptr(begin,end-begin))return false;
 if(!state.context){state.context=context;state.registry=registry;state.allocator=allocator;state.store=store;state.begin=begin;state.end=end;}
 return context==state.context && registry==state.registry && allocator==state.allocator && store==state.store && begin==state.begin && end==state.end;
}
bool StoreValue(unsigned i,bool capture) {
 using namespace initial_load_v2;
 const auto& key=keys[i];U32 match{},matches{};
 for(U32 entry=state.begin;entry<state.end;entry+=0x128) {
  char name[64]{};if(!Bytes(entry,name,sizeof(name)) || !memchr(name,0,sizeof(name)))return false;
  if(strcmp(name,key.name)==0){match=entry+0x40;++matches;}
 }
 if(matches!=1 || !Flag(match,1))return false;
 const U32 actual=reinterpret_cast<U32(__thiscall*)(void*,const char*)>(At(0xC08500))(
  reinterpret_cast<void*>(state.store),reinterpret_cast<const char*>(At(key.imageName)));
 if(actual!=match)return false;
 const U32 value=Read(match+8);
 if(capture){state.row[i]=match;state.value[i]=value;}
 return state.row[i]==match && state.value[i]==value;
}
bool Binding(unsigned i,U32 object) {
 using namespace initial_load_v2;
 const auto& key=keys[i];char name[32]{};
 if(!Ptr(object,0x30) || Read(object)!=Table(key) || Read(object+0xC)!=state.store || !Bytes(object+0x10,name,sizeof(name)) ||
    !memchr(name,0,sizeof(name)) || strcmp(name,key.name)!=0 || Read(Read(object)+0x14)!=At(key.boolean?0x46A6E0:0x46A630))return false;
 if(key.boolean)return reinterpret_cast<bool(__thiscall*)(void*)>(At(0x46A6E0))(reinterpret_cast<void*>(object))==(state.value[i]!=0);
 return reinterpret_cast<U32(__thiscall*)(void*)>(At(0x46A630))(reinterpret_cast<void*>(object))==state.value[i];
}
void ReleaseUnregistered(U32 object,const Key& key) {
 reinterpret_cast<void(__thiscall*)(void*,U32)>(At(key.boolean?0x661000:0x4C6860))(reinterpret_cast<void*>(object),0);
 reinterpret_cast<void(__cdecl*)(U32,U32)>(At(0x441820))(object,state.allocator);
}
bool Ensure(U32 game) {
 if(state.failed)return false;
 if(!DriverSettingsSignaturesV1() || !Graph(game)){Fail("context, settings-store vector or original instruction identity");return false;}
 if(state.ready) {
  for(unsigned i=0;i<10;++i)if(!StoreValue(i,false) || Lookup(keys[i])!=state.binding[i] || !Binding(i,state.binding[i])){Fail("bound native driving setting changed");return false;}
  return true;
 }
 if(state.attempted){Fail("partial transaction cannot be retried");return false;}
 state.attempted=true;
 // Preflight ALL existing store values and typed keys before allocating anything.
 for(unsigned i=0;i<10;++i) {
  if(!StoreValue(i,true)){char reason[100]{};sprintf_s(reason,"missing/invalid native setting %s",keys[i].name);Fail(reason);return false;}
  state.binding[i]=Lookup(keys[i]);
  if(state.binding[i] && !Binding(i,state.binding[i])){Fail("existing typed value is not the matching native settings binding; preserved");return false;}
 }
 for(unsigned i=0;i<10;++i) {
  const auto& key=keys[i];if(state.binding[i])continue;
  if(!Graph(game) || !StoreValue(i,false) || Lookup(key)){Fail("settings ownership changed before insertion");return false;}
  const U32 object=reinterpret_cast<U32(__cdecl*)(U32,U32,U32)>(At(0x441800))(0x30,state.allocator,1);
  if(!initial_load_v2::Ptr(object,0x30)){Fail("native allocation failed");return false;}
  const U32 constructed=reinterpret_cast<U32(__thiscall*)(void*,U32,const char*)>(At(key.boolean?0x660FB0:0x4C6810))(
   reinterpret_cast<void*>(object),state.store,reinterpret_cast<const char*>(At(key.imageName)));
  if(constructed!=object || !Binding(i,object)){ReleaseUnregistered(object,key);Fail("native binding constructor readback");return false;}
  const bool inserted=reinterpret_cast<bool(__thiscall*)(void*,U32,U32,const char*)>(At(0x49E750))(
   reinterpret_cast<void*>(state.registry),object,BindingType(key),reinterpret_cast<const char*>(At(key.imageName))); // AL only.
  if(!inserted){ReleaseUnregistered(object,key);Fail("typed registry insertion rejected; existing key preserved");return false;}
  state.binding[i]=object;state.created[i]=true;
  if(Lookup(key)!=object || !StoreValue(i,false) || !Binding(i,object)){Fail("typed registry readback; registered object retained");return false;}
  char line[180]{};sprintf_s(line,"SETTINGS bound key=%s value=%u native_store=%08x existing_setting_unchanged=1\r\n",key.name,state.value[i],state.store);Log(line);
 }
 for(unsigned i=0;i<10;++i)if(!StoreValue(i,false) || Lookup(keys[i])!=state.binding[i] || !Binding(i,state.binding[i])){Fail("final all-setting readback failed");return false;}
 if(!Graph(game)){Fail("settings owner changed after native bindings");return false;}
 state.ready=true;Log("SETTINGS all ten native driving bindings verified; setting_writes=0 frontend_calls=0\r\n");return true;
}
}
bool NativeDriverSettingsReady(U32 game){return driver_settings_v1::Ensure(game);}
