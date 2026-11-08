#pragma once
#include "processor-gate.h"
#include <windows.h>
namespace vehicle_processor {
using Process=U32(__thiscall*)(void*,void*);
// Caller has acquired one Counter entry. Native exceptions propagate, but the
// entry is released even during SEH unwinding. No game exception is consumed.
inline U32 InvokeCounted(Counter&counter,Process nativeProcessor,void*self,void*envelope,DWORD incomingError){
 U32 result{};
 __try {SetLastError(incomingError);result=nativeProcessor(self,envelope);}
 __finally {const DWORD originalError=GetLastError();if(AbnormalTermination())counter.Fault();counter.Leave();SetLastError(originalError);}
 return result;
}
using ScopeBody=void(*)(void*);
inline void InvokeClosed(Counter&counter,ScopeBody body,void*context,bool&inside,bool&returned,bool&failed){
 __try {body(context);returned=true;}
 __finally {if(AbnormalTermination()){counter.Fault();failed=true;}counter.Release();inside=false;}
}
}
