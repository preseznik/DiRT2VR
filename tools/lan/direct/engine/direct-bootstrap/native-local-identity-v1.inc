// Pinned provider identity getters; no sign-in mutation or fabricated peer.
bool NativeLocalIdentity(unsigned long long& xuid,U32& instance) {
 const auto module=GetModuleHandleW(L"xlive.dll");if(!module)return false;
 const auto getUser=reinterpret_cast<DWORD(WINAPI*)(DWORD,unsigned long long*)>(GetProcAddress(module,MAKEINTRESOURCEA(5261)));
 const auto getAddress=reinterpret_cast<DWORD(WINAPI*)(void*)>(GetProcAddress(module,MAKEINTRESOURCEA(73)));
 unsigned char address[36]{};if(!getUser || !getAddress || getUser(0,&xuid)!=0 || !xuid || !getAddress(address))return false;
 memcpy(&instance,address+0x18,4);U32 high{};memcpy(&high,address+0x1C,4);
 return instance && instance<=0xffffff && !high;
}
bool peerIdentityLogged{};
void NativeLocalIdentityTick() {
 if(peerIdentityLogged || !SelectedCareerLoaded())return;
 unsigned long long xuid{};U32 instance{};if(!NativeLocalIdentity(xuid,instance))return;
 char line[180]{};sprintf_s(line,"PEER identity xuid=%016llx instance=%08x thread=%lu\r\n",xuid,instance,GetCurrentThreadId());Log(line);peerIdentityLogged=true;
}
