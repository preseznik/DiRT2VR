// Captured once before hooks. No request reread or native calls here.
rx_choice_v1::Frozen rxRequest;
const rx_choice_v1::Request& RxRequest(){return rxRequest.Value();}
bool InitializeRxRequest(){
 wchar_t text[513]{};SetLastError(ERROR_SUCCESS);
 const DWORD length=GetEnvironmentVariableW(L"DIRT2VR_ENGINE_RX_REQUEST",text,513),error=GetLastError();
 if(length>=513)return false;
 return rxRequest.Capture(text,length,length!=0 || error!=ERROR_ENVVAR_NOT_FOUND);
}
bool RxCatalogBound(U32 owner,bool capture);
// Data equality only. Callers retain their stronger phase-specific owner/type
// guards. False before initial request validation; no protected value is written.
bool RxEventMatches(U32 event){
 if(!rxRequest.Valid() || !(Read(At(0x147DE98))&1) || !(Read(At(0x147FC70))&1) ||
    Read(event)!=At(0x13366EC))return false;
 const U32 key=Read(At(0x147DE90)),round=event+0xA4;
 const auto value=[&](U32 address){return static_cast<int>(Read(address)^key);};
 return value(event+0x64)==rx_choice_v1::Race && value(event+0x68)==rx_choice_v1::Rounds &&
  value(event+0x6C)==rx_choice_v1::Teams && value(event+0x74)==rx_choice_v1::Class && value(event+0x78)==rx_choice_v1::Grade &&
  value(event+0x1B4)==0 && Read(round)==At(0x1333840) && Read(round+4)==event && value(round+8)==rx_choice_v1::Race &&
  static_cast<short>((Read(round+0xC)^Read(At(0x147FC68)))&0xffff)==RxRequest().selected.model &&
  value(round+0x10)==RxRequest().selected.location && value(round+0x18)==rx_choice_v1::Laps && value(round+0x1C)==rx_choice_v1::Collision;
}
