// Original CALL10D03C9 already returned. Save full state before relocated
// MOV EAX,[modelCount] overwrites the original native aggregate result.
__declspec(naked) void HandlingTail(){
 __asm {
  pushfd
  pushad
  mov ecx,esp
  lea edx,[esp+24h]
  sub esp,240h
  mov [esp+210h],ecx
  mov [esp+214h],edx
  lea eax,[esp+0Fh]
  and eax,0FFFFFFF0h
  mov [esp+218h],eax
  fxsave [eax]
  push edx
  push ecx
  call HandlingReturned
  mov eax,[esp+218h]
  fxrstor [eax]
  add esp,240h
  popad
  popfd
  jmp dword ptr [handlingTrampoline]
 }
}
