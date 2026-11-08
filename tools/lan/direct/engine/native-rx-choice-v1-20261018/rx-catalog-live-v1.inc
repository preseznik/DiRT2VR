// Included after NativeChoiceOps. Same held owner/native allocator as baseline.
#include "rx-catalog-v1.h"
rx_catalog_v1::Binding rxCatalog{};U32 rxCatalogOwner{},rxCatalogService{};
bool RxCatalogBound(U32 owner,bool capture){
 const U32 network=Read(owner+0x2CF0),service=Read(owner+0x2E84),database=Read(service+0xC);
 const U32 context=Read(Read(owner+0x2250)+8),dbIndex=Read(At(0x147F3D4)),ownerIndex=Read(At(0x147F3D8));
 const U32 slot=Read(At(0x147D1F8)),frame=slot==0xffffffffu?0:reinterpret_cast<U32>(TlsGetValue(slot));
 if(!rxRequest.Valid() || owner!=loadOwner || network!=Read(loadOwner+0x2CF0) ||
    Read(network)!=At(0x1336604) || context!=loadContext || dbIndex>=512 || ownerIndex>=512 ||
    Read(context+dbIndex*4)!=service || Read(context+ownerIndex*4)!=owner ||
    GetCurrentThreadId()!=static_cast<DWORD>(holdThread) || Read(frame)!=At(0x147D1F8) || Read(frame+4)!=Read(network+0x100C) ||
    !SelectedCareerLoaded() || !(Read(service+0x20)&0xff) || database<0x10000 || !DistinctChoiceSignatures())return false;
 NativeChoiceOps ops{};
 if(capture){
  if(rxCatalogOwner)return false;
  rx_catalog_v1::Binding first{},second{};
  if(!rx_catalog_v1::Capture(ops,RxRequest(),database,first) || !rx_catalog_v1::Capture(ops,RxRequest(),database,second) || !(first==second))return false;
  rxCatalog=first;rxCatalogOwner=owner;rxCatalogService=service;
  char text[240]{};sprintf_s(text,"RX_CATALOG bound database=%08x track_model=%d track=%d location=%d rows=6 native_relations=3\r\n",
   database,RxRequest().selected.model,RxRequest().selected.track,RxRequest().selected.location);Log(text);
 }
 return rxCatalogOwner==owner && rxCatalogService==service && rxCatalog.database==database &&
  rx_catalog_v1::Stable(ops,RxRequest(),rxCatalog) && Read(owner+0x2E84)==service && Read(service+0xC)==database &&
  Read(owner+0x2CF0)==network && Read(Read(owner+0x2250)+8)==context && Read(context+dbIndex*4)==service && Read(context+ownerIndex*4)==owner;
}
