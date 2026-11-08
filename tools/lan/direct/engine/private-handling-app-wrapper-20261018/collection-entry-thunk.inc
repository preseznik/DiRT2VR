// Shared by the actual full-context candidate and x86 ABI fixture. Native input:
// ECX=collection, [ESP]=return address, [ESP+4]=delta, ESI=App for69471F.
// EDX is volatile and unused by the original thiscall ABI. Reuse only that
// fastcall shim argument; do not touch the original return address or delta.
// The normal compiled Collection body performs the original RET4 cleanup.
__declspec(naked) void __fastcall CollectionEntry(void*,void*,const double*) {
 __asm {
  mov edx,esi
  jmp Collection
 }
}
