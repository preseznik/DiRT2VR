#pragma once
#include <cstddef>
#include <cstdint>

// Pure parser/policy. Catalog compatibility is not native readiness or admission.
namespace rx_choice_v1 {
struct Track { int model, track, location;bool operator==(const Track&)const=default; };
inline constexpr Track Tracks[]{{141,65,9},{142,65,9},{143,61,4},{144,61,4},
                               {145,59,5},{146,59,5},{171,61,4},{182,59,5},
                               {184,81,12}}; // Spa's authored Rallycross circuit
inline constexpr int Race=1,Class=1,Rounds=1,Teams=0,Laps=3,Collision=0,Grade=0;
inline constexpr Track Baseline{143,61,4};
inline constexpr bool Supported(int model,int location) {
 for(const auto& track:Tracks)if(track.model==model)return track.location==location;
 return false;
}
struct Request {
 Track selected=Baseline;
 bool defaulted=true;
 int race=Race, vehicleClass=Class, localCar{}, peerCar{};
 bool operator==(const Request&) const=default;
};
// Game event enums and their database class rows. Mixed events admit only
// their two physical classes. Both role libraries check the live database too.
struct ClassRows {int event, first, second;};
inline constexpr ClassRows Classes[]{{16,16,16},{17,17,17},{18,18,18},{21,21,21},{19,19,19},
                                    {20,20,20},{22,22,22},{23,19,20},{24,21,22}};
inline constexpr bool EventClass(int race,int cls) {
 return race>=0 && race<=4 && cls>=0 && cls<=8 && (race<3 ? race==cls : cls>=3);
}
inline constexpr bool Id(int value){return value>0 && value<=32767;}
inline constexpr bool Valid(const Request& request){
 if(!EventClass(request.race,request.vehicleClass))return false;
 if(request.localCar || request.peerCar)
  return Id(request.localCar) && Id(request.peerCar) && Id(request.selected.model) &&
         Id(request.selected.track) && Id(request.selected.location);
 if(request.race!=Race || request.vehicleClass!=Class)return false;
 for(const auto& track:Tracks)if(track.model==request.selected.model)
  return track.track==request.selected.track && track.location==request.selected.location;
 return false;
}
// Legacy requests contain three fields. A prepared grid supplies all seven.
class Parser {
 const wchar_t* text_;std::size_t size_,at_{};
 void Space(){while(at_<size_ && (text_[at_]==L' ' || text_[at_]==L'\t' || text_[at_]==L'\r' || text_[at_]==L'\n'))++at_;}
 bool Take(wchar_t c){Space();if(at_==size_ || text_[at_]!=c)return false;++at_;return true;}
 int Key(){
  if(!Take(L'"'))return -1;
  const auto start=at_;
  while(at_<size_ && text_[at_]!=L'"'){if(text_[at_]<L'a' || text_[at_]>L'z'){if(text_[at_]!=L'_')return -1;}++at_;}
  if(at_==size_)return -1;
  const auto count=at_++-start;
  constexpr const wchar_t* names[]{L"native_race",L"native_class",L"track_model",L"track_id",L"location_id",L"local_car",L"peer_car"};
  for(int i=0;i<7;++i){std::size_t n{};while(names[i][n])++n;if(n!=count)continue;
   std::size_t j{};while(j<count && text_[start+j]==names[i][j])++j;if(j==count)return i;}
  return -1;
 }
 bool Number(int& out){
  Space();if(at_==size_ || text_[at_]<L'0' || text_[at_]>L'9')return false;
  const bool zero=text_[at_]==L'0';unsigned value{};std::size_t count{};
  while(at_<size_ && text_[at_]>=L'0' && text_[at_]<=L'9'){
   const auto digit=static_cast<unsigned>(text_[at_]-L'0');
   if((zero && count) || value>(32767u-digit)/10u)return false;
   value=value*10u+digit;++at_;++count;
  }
  out=static_cast<int>(value);return true;
 }
public:
 Parser(const wchar_t* text,std::size_t size):text_(text),size_(size){}
 bool Parse(Request& out){
  if(!text_ || !size_ || size_>512 || !Take(L'{'))return false;
  int values[7]{};unsigned seen{};
  do {const int key=Key();
   if(key<0 || (seen&(1u<<key)) || !Take(L':') || !Number(values[key]))return false;
   seen|=1u<<key;
  } while(Take(L','));
  if(!Take(L'}'))return false;Space();
  if(at_!=size_)return false;
  if(seen==127){
   Request request{{values[2],values[3],values[4]},false,values[0],values[1],values[5],values[6]};
   if(!Id(request.localCar) || !Valid(request))return false;
   out=request;return true;
  }
  if(seen!=7 || values[0]!=Race || values[1]!=Class)return false;
  for(const auto& track:Tracks)if(track.model==values[2]){out={track,false};return true;}
  return false;
 }
};
class Frozen {
 Request value_{};bool attempted_{},valid_{};
public:
 bool Capture(const wchar_t* text,std::size_t size,bool present){
  if(attempted_)return false;attempted_=true;
  if(!present){if(size)return false;value_={};return valid_=true;}
  Request parsed{};if(!Parser(text,size).Parse(parsed))return false;
  value_=parsed;return valid_=true;
 }
 bool Valid()const{return valid_;}
 const Request& Value()const{return value_;}
};
}
