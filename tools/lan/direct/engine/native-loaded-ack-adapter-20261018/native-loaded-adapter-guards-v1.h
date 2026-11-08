#pragma once
#include <cstdint>
#include <cstring>
namespace loaded_adapter_guards_v1 {
template<class Equal> bool Signatures(Equal equal,std::uint32_t imageBase) {
 const auto at=[imageBase](std::uint32_t va){return imageBase+va-0x400000;};
 {unsigned char bytes[]{0x40,0x59,0x8C,0x00};
  {const std::uint32_t value=at(0x8C5940u);std::memcpy(bytes+0,&value,4);}
  if(!equal(at(0x13371B0u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0xB8,0x8C,0x0B,0x49,0x01,0xC3};
  {const std::uint32_t value=at(0x1490B8Cu);std::memcpy(bytes+1,&value,4);}
  if(!equal(at(0x8C5940u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0xD0,0x58,0x8C,0x00};
  {const std::uint32_t value=at(0x8C58D0u);std::memcpy(bytes+0,&value,4);}
  if(!equal(at(0x1337060u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0xB8,0xD4,0x0B,0x49,0x01,0xC3};
  {const std::uint32_t value=at(0x1490BD4u);std::memcpy(bytes+1,&value,4);}
  if(!equal(at(0x8C58D0u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0x20,0x59,0x8C,0x00};
  {const std::uint32_t value=at(0x8C5920u);std::memcpy(bytes+0,&value,4);}
  if(!equal(at(0x1337150u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0xB8,0xB4,0x0B,0x49,0x01,0xC3};
  {const std::uint32_t value=at(0x1490BB4u);std::memcpy(bytes+1,&value,4);}
  if(!equal(at(0x8C5920u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0x80,0x59,0x8C,0x00};
  {const std::uint32_t value=at(0x8C5980u);std::memcpy(bytes+0,&value,4);}
  if(!equal(at(0x1337270u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0xB8,0xF0,0x0B,0x49,0x01,0xC3};
  {const std::uint32_t value=at(0x1490BF0u);std::memcpy(bytes+1,&value,4);}
  if(!equal(at(0x8C5980u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0x90,0x59,0x8C,0x00};
  {const std::uint32_t value=at(0x8C5990u);std::memcpy(bytes+0,&value,4);}
  if(!equal(at(0x13372A0u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0xB8,0xE4,0x0B,0x49,0x01,0xC3};
  {const std::uint32_t value=at(0x1490BE4u);std::memcpy(bytes+1,&value,4);}
  if(!equal(at(0x8C5990u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0x10,0x59,0x8C,0x00};
  {const std::uint32_t value=at(0x8C5910u);std::memcpy(bytes+0,&value,4);}
  if(!equal(at(0x1337120u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0xB8,0xA0,0x0B,0x49,0x01,0xC3};
  {const std::uint32_t value=at(0x1490BA0u);std::memcpy(bytes+1,&value,4);}
  if(!equal(at(0x8C5910u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0x70,0x19,0x8C,0x00};
  {const std::uint32_t value=at(0x8C1970u);std::memcpy(bytes+0,&value,4);}
  if(!equal(at(0x1336D0Cu),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0xB8,0xBC,0x0B,0x49,0x01,0xC3};
  {const std::uint32_t value=at(0x1490BBCu);std::memcpy(bytes+1,&value,4);}
  if(!equal(at(0x8C1970u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0x00,0x1B,0x8C,0x00};
  {const std::uint32_t value=at(0x8C1B00u);std::memcpy(bytes+0,&value,4);}
  if(!equal(at(0x1336E9Cu),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0xB8,0xE0,0x0B,0x49,0x01,0xC3};
  {const std::uint32_t value=at(0x1490BE0u);std::memcpy(bytes+1,&value,4);}
  if(!equal(at(0x8C1B00u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0xF0,0x19,0x8C,0x00};
  {const std::uint32_t value=at(0x8C19F0u);std::memcpy(bytes+0,&value,4);}
  if(!equal(at(0x1336DACu),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0xB8,0xD0,0x0B,0x49,0x01,0xC3};
  {const std::uint32_t value=at(0x1490BD0u);std::memcpy(bytes+1,&value,4);}
  if(!equal(at(0x8C19F0u),bytes,sizeof(bytes)))return false;}
 {unsigned char bytes[]{0x8B,0x51,0x3C,0x8B,0x41,0x38,0x3B,0xD0,0x8B,0x4C,0x24,0x04,0x7C,0x0D,0x3B,0xC8,0x7C,0x04,0x3B,0xCA,0x7E,0x0D,0x33,0xC0,0xC2,0x04,0x00,0x3B,0xC8,0x7D,0x04,0x3B,0xCA,0x7F,0xF3,0xB8,0x01,0x00,0x00,0x00,0xC2,0x04,0x00,0xCC};
  if(!equal(at(0x6CA460u),bytes,sizeof(bytes)))return false;}
 return true;
}
}
