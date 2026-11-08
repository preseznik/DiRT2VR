#pragma once
#include <array>
#include <cstdint>
#include <cstring>
namespace native_peer_address {
using U32=std::uint32_t;
using Token=std::array<unsigned char,38>;
inline U32 Word(const Token& t,unsigned at){U32 v{};std::memcpy(&v,t.data()+at,4);return v;}
inline U32 Instance(const Token& t){return Word(t,0x18);}
inline bool Valid(const Token& t){
 const U32 id=Instance(t),be=((id&255)<<24)|((id&0xff00)<<8)|((id>>8)&0xff00)|(id>>24);
 return id && id<=0xffffff && Word(t,0x1C)==0 && Word(t,0)==be && Word(t,4)==be && (t[36]||t[37]);
}
template<class Reader> bool ReadToken(Reader read,U32 address,Token& out){
 out={};if(address<0x10000 || address>0x7fffffcf)return false;
 for(unsigned i=0;i<9;++i){const U32 value=read(address+4*i);std::memcpy(out.data()+4*i,&value,4);}
 const auto port=static_cast<std::uint16_t>(read(address+36));std::memcpy(out.data()+36,&port,2);
 if(!Valid(out) || read(address+40)!=Instance(out) || read(address+44)!=0)return false;
 for(unsigned i=0;i<9;++i)if(read(address+4*i)!=Word(out,4*i))return false;
 return (read(address+36)&0xffff)==port && read(address+40)==Instance(out) && read(address+44)==0;
}
template<class Reader> bool Matches(Reader read,U32 address,const Token& wanted){Token current{};return Valid(wanted)&&ReadToken(read,address,current)&&current==wanted;}
}
