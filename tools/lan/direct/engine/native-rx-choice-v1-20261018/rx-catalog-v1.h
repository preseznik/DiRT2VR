#pragma once
// Uses the existing distinct_cars native schema ABI; no new database call ABI.
namespace rx_catalog_v1 {
using U32=std::uint32_t;
struct Binding { U32 database{};distinct_cars::Row rows[6]{};bool operator==(const Binding&)const=default; };
inline constexpr const char* Names[]{"track_model","track","location","net_session_restrictions","net_race_tracks","net_round_restrictions"};
template<class Ops> bool Find(Ops& ops,U32 database,const char* tableName,const char* field,int value,
                             const char* secondField,int second,distinct_cars::Row& out) {
 const U32 table=ops.Table(database,tableName);U32 count{},stride{},data{};
 if(table<0x10000 || table>0x7fffffbc || !ops.Read(table+0x2C,stride) || !ops.Read(table+0x30,count) || !ops.Read(table+0x38,data) ||
    !count || count>1024 || stride<8 || stride>0x100000 || data<0x10000 || std::uint64_t(data)+std::uint64_t(stride)*count>0x80000000ull)return false;
 unsigned matches{};
 for(U32 i=0;i<count;++i){
  distinct_cars::Row row{};int first{},other{};
  if(!distinct_cars::Capture(ops,database,table,data+stride*i+4,row) || !distinct_cars::Integer(ops,row,field,first))return false;
  if(first!=value)continue;
  if(secondField){if(!distinct_cars::Integer(ops,row,secondField,other))return false;if(other!=second)continue;}
  out=row;++matches;
 }
 return matches==1 && out.count==count && out.stride==stride && out.data==data &&
  distinct_cars::Stable(ops,out) && ops.Table(database,tableName)==table;
}
template<class Ops> bool Stable(Ops& ops,const rx_choice_v1::Request& request,const Binding& b){
 const auto& t=request.selected;const char* fields[]{"id","id","id","net_race_type_id","net_race_type","net_vehicle_class_id"};
 const int values[]{t.model,t.track,t.location,2,2,17};
 if(!rx_choice_v1::Valid(request) || !b.database)return false;
 for(unsigned i=0;i<6;++i){int value{};
  if(b.rows[i].database!=b.database || ops.Table(b.database,Names[i])!=b.rows[i].table ||
     !distinct_cars::Integer(ops,b.rows[i],fields[i],value) || value!=values[i])return false;
 }
 int value{};distinct_cars::Row track{},location{};
 if(!distinct_cars::Linked(ops,b.rows[0],"track_id",b.rows[1].table,track) || !(track==b.rows[1]) ||
    !distinct_cars::Linked(ops,b.rows[1],"location_id",b.rows[2].table,location) || !(location==b.rows[2]) ||
    !distinct_cars::Integer(ops,b.rows[3],"net_vehicle_class_id",value) || value!=17 ||
    !distinct_cars::Integer(ops,b.rows[4],"track_model_id",value) || value!=t.model ||
    !distinct_cars::Integer(ops,b.rows[5],"track_model_id",value) || value!=t.model)return false;
 for(const auto& row:b.rows)if(!distinct_cars::Stable(ops,row))return false;
 return true;
}
template<class Ops> bool Capture(Ops& ops,const rx_choice_v1::Request& request,U32 database,Binding& out){
 Binding b{};b.database=database;const auto& t=request.selected;
 if(!rx_choice_v1::Valid(request) ||
    !Find(ops,database,Names[0],"id",t.model,nullptr,0,b.rows[0]) ||
    !Find(ops,database,Names[1],"id",t.track,nullptr,0,b.rows[1]) ||
    !Find(ops,database,Names[2],"id",t.location,nullptr,0,b.rows[2]) ||
    !Find(ops,database,Names[3],"net_race_type_id",2,"net_vehicle_class_id",17,b.rows[3]) ||
    !Find(ops,database,Names[4],"net_race_type",2,"track_model_id",t.model,b.rows[4]) ||
    !Find(ops,database,Names[5],"net_vehicle_class_id",17,"track_model_id",t.model,b.rows[5]) ||
    !Stable(ops,request,b))return false;
 out=b;return true;
}
}
