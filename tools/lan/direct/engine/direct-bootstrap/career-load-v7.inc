// Included inside the isolated bridge's anonymous namespace. Original-byte
// evidence: career-analysis/0049a8c0.asm.txt and 00b2c2d0.asm.txt.
struct NativeString { U32 table, capacity, text, allocator; char local[4]; };
static_assert(sizeof(NativeString) == 0x14);
// Scoped native string allocator, used by stock jobs such as 55F8B0.
// Constructor 463FB0 ret 8; destructor 464000 ret; exact 12-byte layout.
struct NativeAllocationScope {
    U32 words[3]{};
    U32 slot{}, previous{};
    bool active{};
    explicit NativeAllocationScope(U32 allocator) {
        slot=Read(At(0x147D1F8));
        if(slot==0xffffffff) return;
        previous=reinterpret_cast<U32>(TlsGetValue(slot));
        reinterpret_cast<void (__thiscall*)(void*,U32,U32)>(At(0x463FB0))(words,At(0x147D1F8),allocator);
        active=true;
    }
    bool Valid(U32 allocator) const {
        return active && words[0]==At(0x147D1F8) && words[1]==allocator && words[2]==previous &&
            reinterpret_cast<U32>(TlsGetValue(slot))==reinterpret_cast<U32>(words);
    }
    ~NativeAllocationScope() {
        if(!active) return;
        reinterpret_cast<void (__thiscall*)(void*)>(At(0x464000))(words);
        if(reinterpret_cast<U32>(TlsGetValue(slot))!=previous) {
            Log("CAREER REJECT thread allocator restoration failed\r\n");InterlockedExchange(&careerPhase,3);
        }
    }
};
bool CareerSignatures() {
    constexpr unsigned char load[]{0x53,0x55,0x56,0x57,0x8b,0xf9,0x83,0x7f,0x54,0x00};
    constexpr unsigned char empty[]{0x56,0x8b,0xf1,0x6a,0x01,0x8d,0x46,0x10};
    constexpr unsigned char text[]{0x56,0x8b,0xf1,0x8b,0x4c,0x24,0x08,0x57,0x8b,0x7c,0x24,0x10};
    constexpr unsigned char pump[]{0x56,0x8b,0xf1,0x8b,0x4e,0x34};
    constexpr unsigned char destroy[]{0x56,0x8b,0xf1,0x8b,0x46,0x08,0x85,0xc0};
    constexpr unsigned char scope[]{0x8b,0x44,0x24,0x04,0x56,0x8b,0xf1,0x8b,0x4c,0x24,0x0c};
    constexpr unsigned char restore[]{0x8b,0x51,0x08,0x8b,0x09,0x8b,0x01,0x83,0xf8,0xff};
    return trace::ReadableEqual(reinterpret_cast<void*>(At(0x463FB0)),scope,sizeof(scope)) &&
        trace::ReadableEqual(reinterpret_cast<void*>(At(0x464000)),restore,sizeof(restore)) &&
        trace::ReadableEqual(reinterpret_cast<void*>(At(0xB2C2D0)),load,sizeof(load)) &&
        trace::ReadableEqual(reinterpret_cast<void*>(At(0x449BE0)),empty,sizeof(empty)) &&
        trace::ReadableEqual(reinterpret_cast<void*>(At(0x454500)),text,sizeof(text)) &&
        trace::ReadableEqual(reinterpret_cast<void*>(At(0xAE57A0)),pump,sizeof(pump)) &&
        trace::ReadableEqual(reinterpret_cast<void*>(At(0x448340)),destroy,sizeof(destroy));
}
void Write(U32 p,U32 value) { *reinterpret_cast<U32*>(p)=value; }
void Byte(U32 p,unsigned char value) { *reinterpret_cast<unsigned char*>(p)=value; }
bool Text(U32 p,char* out,size_t size) {
    if(p<0x10000 || p>0x7fff0000) return false;
    __try {
        for(size_t i=0;i<size;++i) {
            const auto c=*reinterpret_cast<const unsigned char*>(p+static_cast<U32>(i));
            if(!c){out[i]=0;return true;}
            if(c<32 || c>126){out[0]=0;return false;}
            out[i]=static_cast<char>(c);
        }
    } __except(EXCEPTION_EXECUTE_HANDLER) {}
    out[0]=0;return false;
}
void ProfileIdentity(const char* phase) {
    const U32 begin=Read(loadProfile+0x24),end=Read(loadProfile+0x28);
    if(begin<0x10000 || end<begin || end-begin>0x128*4096 || (end-begin)%0x128) {
        Log("PROFILE unavailable property array\r\n");return;
    }
    char line[512]{};
    sprintf_s(line,"PROFILE phase=%s owner=%08x properties=%u\r\n",phase,loadProfile,(end-begin)/0x128);Log(line);
    for(U32 p=begin;p<end;p+=0x128) {
        char key[65]{};
        if(!Text(p,key,sizeof(key)) || (strcmp(key,"Player_FirstName") && strcmp(key,"Player_LastName") && strcmp(key,"savegame_user_id"))) continue;
        char pointed[81]{},inlined[81]{};
        if ((Read(p+0x40)&0xff)==4) Text(p+0x48,inlined,sizeof(inlined));
        sprintf_s(line,"PROPERTY phase=%s key=%s type=%u value0=%08x value1=%08x pointed=\"%s\" inline=\"%s\"\r\n",
            phase,key,Read(p+0x40)&0xff,Read(p+0x48),Read(p+0x4C),pointed,inlined);Log(line);
    }
}
void CareerTick(void* self) {
    const LONG phase=InterlockedCompareExchange(&careerPhase,0,0);
    if(!phase || phase==3) return;
    if(GetCurrentThreadId()!=static_cast<DWORD>(ownerThread)) {
        InterlockedExchange(&careerPhase,3); Log("CAREER REJECT changed update thread\r\n");return;
    }
    const U32 owner=reinterpret_cast<U32>(self);
    const U32 sdu=Read(owner+0x2294),profile=Read(owner+0x2284),handler=Read(sdu+0x3C);
    const U32 context=Read(Read(owner+0x2250)+8),index=Read(At(0x147DD28));
    const U32 helper=Read(sdu+0x38),control=Read(helper+0xC),allocator=Read(sdu+0x18);
    if(Read(owner)!=At(0x1329A48) || Read(sdu)!=At(0x1328574) || Read(profile)!=At(0x1323BDC) ||
       Read(profile+0x30)!=sdu || Read(profile+0x34)!=allocator || Read(handler)!=At(0x12F1320) ||
       Read(owner+0x228C)!=handler || Read(handler+0x150)!=At(0x1375B40) || Read(handler+0x154)==0xffffffff ||
       context<0x10000 || index>=512 || Read(context+index*4)!=sdu || control<0x10000 || control>0x7fff0000 ||
       Read(helper)!=allocator || Read(helper+4)!=Read(sdu+0x34)) {
        char line[240]{};sprintf_s(line,"CAREER REJECT ownership owner=%08x sdu=%08x profile=%08x handler=%08x context=%08x index=%u helper=%08x control=%08x allocator=%08x\r\n",owner,sdu,profile,handler,context,index,helper,control,allocator);Log(line);
        InterlockedExchange(&careerPhase,3);return;
    }
    NativeAllocationScope nativeScope(allocator);
    if(!nativeScope.Valid(allocator)) {
        Log("CAREER REJECT thread allocator scope unavailable\r\n");InterlockedExchange(&careerPhase,3);return;
    }
    if(phase==1) {
        char scopeLine[160]{};sprintf_s(scopeLine,"CAREER allocator_scope thread=%lu slot=%u previous=%08x owned_allocator=%08x\r\n",GetCurrentThreadId(),nativeScope.slot,nativeScope.previous,allocator);Log(scopeLine);
        if(Read(sdu+0x54)!=0 || (Read(handler+0x158)!=0 && Read(handler+0x158)!=context)) {
            Log("CAREER REJECT busy or handler already owned\r\n");InterlockedExchange(&careerPhase,3);return;
        }
        loadHelper=helper;loadOwner=owner;loadSdu=sdu;loadProfile=profile;loadHandler=handler;loadControl=control;loadContext=context;
        ProfileIdentity("before");
        // Nonvisual handler request context and operation metadata, matching the
        // native PC profile/load/auto request. No ScreenSDU object is created.
        Write(handler+0x158,context);Byte(handler+0x15C,0);Byte(handler+0x15D,1);
        Write(control+0x1C,0);Byte(control+0x28,0);Write(control+0x2C,6);Byte(control+0x44,0);
        Write(control+0x38,0);Write(control+0x34,0);Write(control+0x3C,0);
        NativeString prefix{},collection{};
        reinterpret_cast<void (__thiscall*)(void*,void*)>(At(0x449BE0))(&prefix,reinterpret_cast<void*>(allocator));
        reinterpret_cast<void (__thiscall*)(void*,void*,const char*)>(At(0x454500))(&collection,reinterpret_cast<void*>(allocator),"Autosave");
        using Load=U32(__thiscall*)(void*,const NativeString*,const NativeString*,const NativeString*,const NativeString*,const NativeString*,const NativeString*,U32);
        Log("CALL career-load collection=Autosave prefix=empty operation=load user=0 menu_calls=0\r\n");
        const U32 result=reinterpret_cast<Load>(At(0xB2C2D0))(reinterpret_cast<void*>(sdu),&prefix,&collection,&collection,&collection,&collection,&collection,0);
        reinterpret_cast<void (__thiscall*)(void*)>(At(0x448340))(&collection);
        reinterpret_cast<void (__thiscall*)(void*)>(At(0x448340))(&prefix);
        char line[160]{};sprintf_s(line,"CAREER initiation=%u state=%u control=%u\r\n",result,Read(sdu+0x54),Read(control+0x2C));Log(line);
        loadBegan=GetTickCount64();InterlockedExchange(&careerPhase,result?3:2);return;
    }
    if(owner!=loadOwner || sdu!=loadSdu || profile!=loadProfile || handler!=loadHandler || control!=loadControl || context!=loadContext) {
        Log("CAREER REJECT ownership changed in flight\r\n");InterlockedExchange(&careerPhase,3);return;
    }
    // SDU completion-message pump used by native callers. This does not execute
    // any frontend state, choose a UI response, or retry a failed load.
    const U32 poll=reinterpret_cast<U32 (__thiscall*)(void*)>(At(0xAE57A0))(reinterpret_cast<void*>(sdu));
    const U32 state=Read(sdu+0x54);
    const U32 controlState=Read(control+0x2C);
    if(controlState!=lastControlState) {
        lastControlState=controlState;
        char detail[512]{};
        const U32 vt=Read(control),error=Read(control+0x10),notice=Read(control+0x14);
        sprintf_s(detail,"CAREER pending state=%u control=%u vt=%08x req4=%08x req8=%08x reqC=%08x req10=%08x req14=%08x methods=%08x,%08x,%08x,%08x,%08x,%08x\r\n",
            state,controlState,vt,Read(control+4),Read(control+8),Read(control+0xC),error,notice,Read(vt),Read(vt+4),Read(vt+8),Read(vt+0xC),Read(vt+0x10),Read(vt+0x14));Log(detail);
        if(error) {
            char message[257]{};Text(error+4,message,sizeof(message));
            sprintf_s(detail,"CAREER native_error key=\"%s\" code0=%u code1=%u\r\n",message,Read(error+0x108),Read(error+0x10C));Log(detail);
        }
        if(notice) {
            const U32 descriptor=Read(notice+4); char key[129]{};Text(Read(descriptor+8),key,sizeof(key));
            sprintf_s(detail,"CAREER native_notice key=\"%s\" flags=%08x\r\n",key,Read(notice+8));Log(detail);
        }
    }
    if(state!=2 || GetTickCount64()-loadBegan>20000) {
        char line[200]{};sprintf_s(line,"CAREER completion state=%u poll=%u control=%u elapsed=%llu direct_start=0 current_race_join=0\r\n",state,poll,Read(control+0x2C),GetTickCount64()-loadBegan);Log(line);
        ProfileIdentity("after");InterlockedExchange(&careerPhase,3);
    }
}
