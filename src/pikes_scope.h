#pragma once
#include <string>
#include <string_view>
#include <vector>

namespace vr {
// Accept the launcher's XDocument formatting and the compact diagnostic equivalent.
// Preserve quoted values; reject every structure except the authored Pikes launch contract.
inline constexpr std::string ParsePikesPracticeXml(std::string_view xml,std::string_view country="usa",std::string_view folder="d2ve_pikes_tarmac") {
    if(xml.empty() || xml.size()>4096)return {};
    std::string compact;bool quoted=false;
    for(char c:xml) {
        if(c=='"')quoted=!quoted;
        if(!quoted && (c==' ' || c=='\t' || c=='\r' || c=='\n'))continue;
        compact+=c;
    }
    if(quoted)return {};
    const bool replays=compact.starts_with("<configskipreplays=\"false\">");
    const std::string prefix=(replays ? "<configskipreplays=\"false\">" : "<configskipreplays=\"true\">")+std::string("<trackcountry=\""+std::string(country)+"\"name=\""+std::string(folder)+"\"route=\"route_0\"><carname=\"");
    const std::string suffix="\"number=\"1\"/></track></config>";
    if(compact.size()!=prefix.size()+3+suffix.size() || !compact.starts_with(prefix) || !compact.ends_with(suffix))return {};
    auto car=compact.substr(prefix.size(),3);
    return car.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789")==std::string::npos?car:std::string{};
}
// Explicit race formats bound Rally to four cars and the authored grid to eight. Never broaden the
// solo grammar, accept arbitrary XML or silently ignore an unvalidated opponent.
inline std::vector<std::string> ParsePikesRaceXml(std::string_view xml,std::string_view country,std::string_view folder,unsigned maximumCars=4) {
    if(xml.empty() || xml.size()>4096)return {};
    std::string compact;bool quoted=false;
    for(char c:xml) {
        if(c=='"')quoted=!quoted;
        if(!quoted && (c==' ' || c=='\t' || c=='\r' || c=='\n'))continue;
        compact+=c;
    }
    if(quoted)return {};
    const bool replays=compact.starts_with("<configskipreplays=\"false\">");
    const std::string prefix=(replays ? "<configskipreplays=\"false\">" : "<configskipreplays=\"true\">")+
        std::string("<trackcountry=\""+std::string(country)+"\"name=\""+std::string(folder)+"\"route=\"route_0\">");
    constexpr std::string_view suffix="</track></config>",carPrefix="<carname=\"",numberPrefix="\"number=\"";
    if(!compact.starts_with(prefix) || !compact.ends_with(suffix))return {};
    std::string_view entries(compact.data()+prefix.size(),compact.size()-prefix.size()-suffix.size());
    std::vector<std::string> cars;
    while(!entries.empty()) {
        if(!entries.starts_with(carPrefix) || entries.size()<carPrefix.size()+3+numberPrefix.size()+4)return {};
        entries.remove_prefix(carPrefix.size());
        const auto car=entries.substr(0,3);
        if(car.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789")!=std::string_view::npos)return {};
        entries.remove_prefix(3);
        if(!entries.starts_with(numberPrefix))return {};
        entries.remove_prefix(numberPrefix.size());
        if(entries[0]<'1' || entries[0]>'8' || entries.substr(1,3)!="\"/>")return {};
        const unsigned count=entries[0]-'0';
        if(maximumCars<2 || maximumCars>8 || cars.size()+count>maximumCars)return {};
        for(unsigned i=0;i<count;++i)cars.emplace_back(car);
        entries.remove_prefix(4);
    }
    return cars.size()>=2?cars:std::vector<std::string>{};
}
bool PikesScope();
unsigned PikesChunks();
const char* PikesHash();
}

namespace vr { void EnablePikesCockpit(); }
