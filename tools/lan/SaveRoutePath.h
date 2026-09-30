#pragma once
#include <windows.h>
#include <string>
#include <cstring>

namespace profile_route {
inline bool EncodePath(const std::wstring& path, char (&output)[MAX_PATH]) {
    BOOL substituted=FALSE;
    const bool utf8=GetACP()==CP_UTF8;
    const int count=WideCharToMultiByte(CP_ACP,utf8 ? WC_ERR_INVALID_CHARS : WC_NO_BEST_FIT_CHARS,
        path.c_str(),-1,output,MAX_PATH,nullptr,utf8 ? nullptr : &substituted);
    return count>0 && count<MAX_PATH-32 && !substituted;
}

// The game opens saves with ANSI paths. A Windows short path is an acceptable
// lossless alias; replacing unrepresentable characters with '?' is not.
inline bool ResolvePath(const wchar_t* requested, char (&output)[MAX_PATH]) {
    output[0]=0;
    if(!requested || wcslen(requested)<3 || requested[1]!=L':' || requested[2]!=L'\\') return false;
    std::wstring full(32768,L'\0');
    const DWORD length=GetFullPathNameW(requested,static_cast<DWORD>(full.size()),full.data(),nullptr);
    if(!length || length>=full.size()) return false;
    full.resize(length);
    while(full.size()>3 && full.back()==L'\\') full.pop_back();
    // Validate all ancestors, not only the final directory.
    for(size_t end=3;;) {
        const auto part=full.substr(0,end);
        const auto attrs=GetFileAttributesW(part.c_str());
        if(attrs==INVALID_FILE_ATTRIBUTES || !(attrs&FILE_ATTRIBUTE_DIRECTORY) || (attrs&FILE_ATTRIBUTE_REPARSE_POINT)) return false;
        if(end==full.size()) break;
        const auto next=full.find(L'\\',end+1);
        end=next==std::wstring::npos ? full.size() : next;
    }
    if(EncodePath(full,output)) return true;
    std::wstring shortPath(32768,L'\0');
    const DWORD shortLength=GetShortPathNameW(full.c_str(),shortPath.data(),static_cast<DWORD>(shortPath.size()));
    if(!shortLength || shortLength>=shortPath.size()) {output[0]=0;return false;}
    shortPath.resize(shortLength);
    if(EncodePath(shortPath,output)) return true;
    output[0]=0;return false;
}
}
