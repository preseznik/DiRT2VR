// Intercept only this experiment's lower SDU request callbacks. Never call a
// ScreenSDU method or its UI control sink. Other helper instances pass through.
using SaveCallback=U32(__thiscall*)(void*,void*);
SaveCallback saveOriginal[6]{};
constexpr U32 SaveCallbacks[]{0xAE5D40,0xAE5D70,0xB0AEB0,0xB0AFA0,0xAE5DA0,0xB0B110};
volatile LONG headlessCallbacks{};
U32 selectedSave{};
bool OwnSaveCallback(void* self) {
    const LONG phase=InterlockedCompareExchange(&careerPhase,0,0);
    return loadHelper && reinterpret_cast<U32>(self)==loadHelper && phase>=1 && phase<=2;
}
bool CallbackThread() {
    if(GetCurrentThreadId()==static_cast<DWORD>(ownerThread)) return true;
    Log("HEADLESS REJECT callback thread mismatch\r\n");InterlockedExchange(&headlessFailed,1);return false;
}
U32 HeadlessSave(unsigned kind,void* message) {
    InterlockedIncrement(&headlessCallbacks);
    if(!CallbackThread()) return 0;
    const U32 request=Read(reinterpret_cast<U32>(message)+8);
    if(kind==4) return 0; // Progress notification has no reply or UI effect.
    char line[320]{};
    sprintf_s(line,"HEADLESS callback=%u request=%08x thread=%lu\r\n",kind,request,GetCurrentThreadId());Log(line);
    if(request<0x10000 || Read(request)<0x10000) {
        Log("HEADLESS REJECT invalid request\r\n");InterlockedExchange(&headlessFailed,1);return 0;
    }
    if(kind==0) {
        const U32 list=Read(request+4),begin=Read(list+4),end=Read(list+8);
        const U32 count=begin>=0x10000 && end>=begin && end-begin<=4*10 && (end-begin)%4==0?(end-begin)/4:0;
        const U32 entry=count==1?Read(begin):0;
        char slot[129]{},name[65]{};if(entry) {Text(Read(entry+0x44),slot,sizeof(slot));Text(Read(entry+0x1C),name,sizeof(name));}
        sprintf_s(line,"HEADLESS existing_slot count=%u entry=%08x name=\"%s\" empty=%u\r\n",count,entry,slot,entry?Read(entry+0x68)&0xff:1);Log(line);
        sprintf_s(line,"HEADLESS profile=\"%s\" expected=\"%s\"\r\n",name,expectedProfileName.c_str());Log(line);
        if(count!=1 || !entry || (Read(entry+0x68)&0xff) || strcmp(slot,"Autosave0") || name!=expectedProfileName) {
            Log("HEADLESS REJECT expected exactly one existing Autosave0 slot\r\n");InterlockedExchange(&headlessFailed,1);return 0;
        }
        // Native response copies existing metadata into its owned payload,
        // selects index zero, signals the worker, and clears this local handle.
        // B0B250 ret 0Ch; B520D0 copies a 6Ch metadata record, not UI state.
        U32 pending=request;
        reinterpret_cast<void(__thiscall*)(void*,U32,U32,void*)>(At(0xB0B250))(&pending,0,0,reinterpret_cast<void*>(entry));
        if(pending) {Log("HEADLESS REJECT response not consumed\r\n");InterlockedExchange(&headlessFailed,1);}
        else {selectedSave=entry;Log("HEADLESS selected existing slot; native_ui_callback=0\r\n");}
    } else if(kind==1) {
        // Native request flags: byte 8 is load; byte 9 is create/overwrite;
        // byte 0A is cancellation and byte 0B is the new-entry flag.
        // Accept only a load of the existing identity already selected above.
        const U32 entry=Read(request+4);char slot[129]{},name[65]{};
        if(entry) {Text(Read(entry+0x44),slot,sizeof(slot));Text(Read(entry+0x1C),name,sizeof(name));}
        sprintf_s(line,"HEADLESS load_request flags=%08x slot=\"%s\" profile=\"%s\"\r\n",Read(request+8),slot,name);Log(line);
        if(!selectedSave || !entry || Read(request+8)!=1 || (Read(entry+0x68)&0xff) || strcmp(slot,"Autosave0") || name!=expectedProfileName) {
            Log("HEADLESS REJECT confirmation is not the selected existing profile load\r\n");InterlockedExchange(&headlessFailed,1);return 0;
        }
        U32 pending=request;
        reinterpret_cast<void(__thiscall*)(void*,U32)>(At(0xAE5E60))(&pending,0);
        if(pending) {Log("HEADLESS REJECT load response not consumed\r\n");InterlockedExchange(&headlessFailed,1);}
        else Log("HEADLESS authorized existing profile read; native_ui_callback=0\r\n");
    } else if(kind==5) {
        // Completion notice: retain its result key before acknowledging receipt.
        const U32 descriptor=Read(request+4);char key[129]{};Text(Read(descriptor+8),key,sizeof(key));
        sprintf_s(line,"HEADLESS finish key=\"%s\" flags=%08x\r\n",key,Read(request+8));Log(line);
        U32 pending=request;
        reinterpret_cast<void(__thiscall*)(void*)>(At(0xAE6000))(&pending);
    } else if(kind==3) {
        char key[257]{};Text(request+4,key,sizeof(key));
        sprintf_s(line,"HEADLESS native_error key=\"%s\"\r\n",key);Log(line);
        InterlockedExchange(&headlessFailed,1);
        U32 pending=request;
        reinterpret_cast<void(__thiscall*)(void*)>(At(0xAE5FC0))(&pending);
    } else {
        // Do not accept overwrite/create/confirmation requests as profile load.
        sprintf_s(line,"HEADLESS REJECT unexpected request kind=%u flags=%08x\r\n",kind,Read(request+8));Log(line);
        InterlockedExchange(&headlessFailed,1);
    }
    return 0;
}
#define SAVE_BRIDGE(N) U32 __fastcall SaveBridge##N(void* self,void*,void* message) { \
    return OwnSaveCallback(self)?HeadlessSave(N,message):saveOriginal[N](self,message); }
SAVE_BRIDGE(0)
SAVE_BRIDGE(1)
SAVE_BRIDGE(2)
SAVE_BRIDGE(3)
SAVE_BRIDGE(4)
SAVE_BRIDGE(5)
#undef SAVE_BRIDGE
bool InstallSaveHooks() {
    constexpr unsigned char reply[]{0x56,0x8b,0xf1,0x8b,0x06,0x85,0xc0,0x74,0x30};
    constexpr unsigned char copy[]{0x56,0x8b,0x74,0x24,0x08,0x57,0x8b,0xf9,0x3b,0xf7};
    constexpr unsigned char confirm[]{0x56,0x8b,0xf1,0x8b,0x06,0x85,0xc0,0x74,0x18};
    constexpr unsigned char acknowledge[]{0x56,0x8b,0xf1,0x8b,0x06,0x85,0xc0,0x74,0x0f};
    if(!trace::ReadableEqual(reinterpret_cast<void*>(At(0xAE5E60)),confirm,sizeof(confirm)) ||
       !trace::ReadableEqual(reinterpret_cast<void*>(At(0xB0B250)),reply,sizeof(reply)) ||
       !trace::ReadableEqual(reinterpret_cast<void*>(At(0xB520D0)),copy,sizeof(copy)) ||
       !trace::ReadableEqual(reinterpret_cast<void*>(At(0xAE5FC0)),acknowledge,sizeof(acknowledge)) ||
       !trace::ReadableEqual(reinterpret_cast<void*>(At(0xAE6000)),acknowledge,sizeof(acknowledge))) return false;
    void* bridges[]{reinterpret_cast<void*>(SaveBridge0),reinterpret_cast<void*>(SaveBridge1),reinterpret_cast<void*>(SaveBridge2),reinterpret_cast<void*>(SaveBridge3),reinterpret_cast<void*>(SaveBridge4),reinterpret_cast<void*>(SaveBridge5)};
    for(unsigned i=0;i<6;++i) {
        if(MH_CreateHook(reinterpret_cast<void*>(At(SaveCallbacks[i])),bridges[i],reinterpret_cast<void**>(&saveOriginal[i]))!=MH_OK) return false;
    }
    Log("HEADLESS installed six lower save-service callback replacements\r\n");return true;
}
