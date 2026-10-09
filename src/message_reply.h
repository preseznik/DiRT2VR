#pragma once
#include <string>
namespace vr {
struct MessageReply {
    static constexpr size_t limit=240;
    std::wstring text;
    bool editing{},pending{};
    void Begin(){editing=true;pending=false;}
    void Cancel(){text.clear();editing=pending=false;}
    void Character(wchar_t value) {
        if(!editing || pending)return;
        if(value==8) {
            if(!text.empty()) {
                const auto last=text.back();text.pop_back();
                if(last>=0xdc00 && last<=0xdfff && !text.empty() && text.back()>=0xd800 && text.back()<=0xdbff)text.pop_back();
            }
        } else if(value>=32 && value!=127 && text.size()<limit)text+=value;
    }
    bool Submit() {
        if(!editing || text.find_first_not_of(L" \t\r\n")==std::wstring::npos)return false;
        return pending=true;
    }
};
}
