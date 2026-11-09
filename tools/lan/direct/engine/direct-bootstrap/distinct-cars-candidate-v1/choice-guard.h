#pragma once
// Native-free policy and schema inspector. Production and fixtures share this code.
#include <cstdint>
#include <cstring>
#include "../../native-rx-choice-v1-20261018/rx-request-v1.h"
namespace distinct_cars {
using Address=std::uint32_t;
struct Request {
 int vehicle{};bool initialized{},valid{},defaulted{};
 // Absence alone selects the established baseline. Empty or malformed input fails.
 bool Initialize(const wchar_t* text,bool present) {
  if(initialized)return false;
  initialized=true;defaulted=!present;
  if(!present){vehicle=88;return valid=true;}
  if(!text || !*text)return false;
  std::uint32_t value{};
  for(unsigned i=0;text[i];++i) {
   if(i>=10 || text[i]<L'0' || text[i]>L'9')return false;
   const unsigned digit=static_cast<unsigned>(text[i]-L'0');
   if(value>(0x7fffffffu-digit)/10)return false;
   value=value*10+digit;
  }
  if(!value)return false;
  vehicle=static_cast<int>(value);return valid=true;
 }
};
struct View {Address row{},table{};};
struct Row {
 Address table{},row{},stride{},count{},data{},database{},type{},header{};
 bool operator==(const Row&) const=default;
};
struct Choice {
 int vehicle{},livery{},carClass{},grade{},gradeOrder{};
 Row rows[4]{}; // livery, vehicle, class, grade; all borrowed.
 bool operator==(const Choice&) const=default;
};
inline bool EventAllows(const Choice& c,int eventClass=1) {
 if(eventClass<0 || eventClass>8)return false;
 const auto classes=rx_choice_v1::Classes[eventClass];
 return c.vehicle>0 && c.livery>0 && (c.carClass==classes.first || c.carClass==classes.second) && c.grade>0 && c.gradeOrder>=0;
}
template<class Ops> bool Capture(Ops& ops,Address database,Address table,Address row,Row& out) {
 if(database<0x10000 || table<0x10000 || table>0x7fffffbc || row<0x10004 || row>0x7ffffffb || row==ops.Sentinel())return false;
 Row r{};r.table=table;r.row=row;
 if(!ops.Read(table+0x2C,r.stride) || !ops.Read(table+0x30,r.count) || !ops.Read(table+0x38,r.data) ||
    !ops.Read(table+0x3C,r.database) || !ops.Read(table+0x40,r.type) || !ops.Read(row-4,r.header))return false;
 if(r.stride<8 || r.stride>0x100000 || !r.count || r.count>0x100000 || r.data<0x10000 || r.data>0x7ffffffb ||
    r.database!=database || r.type>255 || std::uint64_t(r.data)+std::uint64_t(r.stride)*r.count>0x80000000ull ||
    row<r.data+4 || (row-r.data-4)%r.stride || (row-r.data-4)/r.stride>=r.count ||
    (r.header&0xFFFFFF)!=0x4D5449 || (r.header>>24)!=r.type)return false;
 out=r;return true;
}
template<class Ops> bool Stable(Ops& ops,const Row& row) {
 Row current{};return Capture(ops,row.database,row.table,row.row,current) && current==row;
}
template<class Ops> bool Integer(Ops& ops,const Row& row,const char* field,int& out) {
 if(!Stable(ops,row))return false;
 out=-1;
 return ops.Integer(row.table,row.row,field,out)==0 && out>=0 && Stable(ops,row);
}
template<class Ops> bool Linked(Ops& ops,const Row& source,const char* field,Address targetTable,Row& out) {
 if(!Stable(ops,source))return false;
 const Address linked=ops.Linked(source.table,source.row,field);
 return Stable(ops,source) && Capture(ops,source.database,targetTable,linked,out);
}
template<class Ops> bool InspectOnce(Ops& ops,Address context,Address database,int livery,Choice& out,int eventClass=1) {
 if(livery<=0)return false;
 const char* names[]{"vehicle_livery","vehicle","vehicle_class","vehicle_grade"};
 Address tables[4]{};for(unsigned i=0;i<4;++i) {tables[i]=ops.Table(database,names[i]);if(tables[i]<0x10000)return false;}
 View view{};if(!ops.Resolve(context,livery,view) || view.table!=tables[0])return false;
 Choice c{};
 if(!Capture(ops,database,tables[0],view.row,c.rows[0]) || !Integer(ops,c.rows[0],"id",c.livery) || c.livery!=livery ||
    !Linked(ops,c.rows[0],"vehicle_id",tables[1],c.rows[1]) || !Integer(ops,c.rows[1],"id",c.vehicle) ||
    !Linked(ops,c.rows[1],"vehicle_class_id",tables[2],c.rows[2]) || !Integer(ops,c.rows[2],"id",c.carClass) ||
    !Linked(ops,c.rows[1],"vehicle_grade_id",tables[3],c.rows[3]) || !Integer(ops,c.rows[3],"id",c.grade) ||
    !Integer(ops,c.rows[3],"order_index",c.gradeOrder) || !EventAllows(c,eventClass))return false;
 for(unsigned i=0;i<4;++i)if(!Stable(ops,c.rows[i]) || ops.Table(database,names[i])!=tables[i])return false;
 out=c;return true;
}
template<class Ops> bool Inspect(Ops& ops,Address context,Address database,int livery,Choice& out,int eventClass=1) {
 Choice first{},second{};
 if(!InspectOnce(ops,context,database,livery,first,eventClass) || !InspectOnce(ops,context,database,livery,second,eventClass) || !(first==second))return false;
 out=first;return true;
}
template<std::size_t N> inline bool LocalEligible(int request,const int (&candidates)[N],int count,bool resolved,bool available,int profileLivery,const Choice& choice,int eventClass=1) {
 if(request<=0 || count<0 || static_cast<std::size_t>(count)>N || !resolved || !available || profileLivery<=0 ||
    choice.vehicle!=request || choice.livery!=profileLivery || !EventAllows(choice,eventClass))return false;
 for(int i=0;i<count;++i)if(candidates[i]==request)return true;
 return false;
}
// Keep the existing loader's `vehicle` field: it is a COMPONENT POINTER, never an ID.
struct Participant {Address root{},identity{},vehicle{},nativeId{},record{};int livery{};Choice choice{};};
inline bool Participants(const Participant (&players)[2],const Address (&roster)[2],Address localRoot,Address localOwner,const Choice& local,int eventClass=1) {
 if(roster[0]>255 || roster[1]>255 || roster[0]==roster[1] || !localRoot ||
    players[0].root==players[1].root || players[0].identity==players[1].identity || players[0].vehicle==players[1].vehicle ||
    players[0].record==players[1].record || players[0].nativeId==players[1].nativeId)return false;
 unsigned locals{};
 for(const auto& p:players) {
  if(!p.root || !p.identity || !p.vehicle || !p.record || p.nativeId>255 ||
     (p.nativeId!=roster[0] && p.nativeId!=roster[1]) || p.livery!=p.choice.livery || !EventAllows(p.choice,eventClass))return false;
  if(p.root==localRoot){++locals;if(p.vehicle!=localOwner || !(p.choice==local))return false;}
 }
 return locals==1;
}
inline bool EntryMatches(const Participant& p,Address localRoot,Address role,Address row,const Choice& actual) {
 return role==(p.root==localRoot?4u:2u) && row==p.choice.rows[0].row && actual==p.choice;
}
}
