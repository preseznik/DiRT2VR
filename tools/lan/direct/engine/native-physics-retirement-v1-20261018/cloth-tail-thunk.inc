// Interior continuation at 107EA31, immediately after the original wait CALL.
// There is no function return here. Preserve the complete interrupted machine
// state before the original five relocated instructions/bytes resume.
__declspec(naked) void AggregateTail(){
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
  call AggregateReturned
  mov eax,[esp+218h]
  fxrstor [eax]
  add esp,240h
  popad
  popfd
  jmp dword ptr [aggregateTrampoline]
 }
}
