#pragma once
#include <cstdint>
// Bounded, read-only lookup of one exact type in the native multimap.
// Ancestor keys may repeat. The requested exact key must be unambiguous.
namespace native_components {
using U32=std::uint32_t;
template<class Reader> U32 Find(Reader read,U32 root,U32 key) {
 if(root<0x10000 || root>0x7fffff00) return 0;
 const U32 sentinel=root+0x24,first=read(root+0x28),last=read(root+0x24),count=read(root+0x2C);
 if(!count || count>256) return 0;
 U32 seen[256]{},node=first,result{};
 for(U32 i=0;i<count;++i) {
  if(node<0x10000 || node>0x7ffffff0 || node==sentinel) return 0;
  for(U32 j=0;j<i;++j) if(seen[j]==node) return 0;
  seen[i]=node;
  if(read(node+8)==key) {if(result)return 0;result=read(node+12);if(result<0x10000 || result>0x7ffffffb)return 0;}
  node=read(node+4);
 }
 if(node!=sentinel || read(root+0x28)!=first || read(root+0x24)!=last || read(root+0x2C)!=count)return 0;
 return result;
}
}
