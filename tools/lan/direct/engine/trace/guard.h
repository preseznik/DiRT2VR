#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <bcrypt.h>
#include <array>
#include <cstdio>
#include <cstring>
#include <string>

namespace trace {
inline constexpr char OriginalHash[] = "49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48";
inline constexpr char LaaHash[] = "274A798EF4DC609C24690434616E42FFC6D381EB90982BFFA326DC336C58F6B4";
// Complete functions, including their ret 4; all instructions are guarded.
inline constexpr unsigned char FlowBytes[]{0x8b,0x41,0x2c,0x0f,0x57,0xc0,0x89,0x41,0x30,0x8b,0x41,0x24,0xa8,0x02,0xf3,0x0f,0x11,0x41,0x34,0x75,0x06,0x83,0xc8,0x02,0x89,0x41,0x24,0xc2,0x04,0x00};
inline constexpr unsigned char BaseBytes[]{0x8b,0x41,0x24,0xa8,0x02,0x75,0x06,0x83,0xc8,0x02,0x89,0x41,0x24,0xc2,0x04,0x00};
inline constexpr DWORD FlowRva=0x62d60, BaseRva=0xb6de0;
inline constexpr unsigned char CreateBytes[]{0x56,0x8b,0xf1,0x8b,0x46,0x24,0xa8,0x02,0x75,0x06,0x83,0xc8,0x02,0x89,0x46,0x24};
inline constexpr unsigned char SettingsBytes[]{0x53,0x56,0x8b,0xf1,0x8b,0x46,0x24,0xa8,0x02,0x57,0x75,0x06,0x83,0xc8,0x02,0x89,0x46,0x24};
inline constexpr unsigned char FindBytes[]{0x83,0xec,0x3c,0x56,0x8b,0xf1,0x8b,0x46,0x24,0xa8,0x02,0x57,0x75,0x06,0x83,0xc8,0x02,0x89,0x46,0x24};
struct Guard { DWORD rva;const unsigned char* bytes;size_t size; };
inline constexpr Guard Guards[]{
    {FlowRva,FlowBytes,sizeof(FlowBytes)},{BaseRva,BaseBytes,sizeof(BaseBytes)},
    {0x236d10,CreateBytes,sizeof(CreateBytes)},{0x2392a0,SettingsBytes,sizeof(SettingsBytes)},
    {0x1d1d60,FindBytes,sizeof(FindBytes)}};
enum Result : DWORD { Ok=0, NoOptIn=10, BadLogPath=11, UnsupportedHost=12, BadSignature=13,
    LogUnavailable=14, HookFailure=15, AlreadyUsed=16, StopFailure=17, BadImage=18 };

inline bool AllowedHash(const std::string& hash) { return hash==OriginalHash || hash==LaaHash; }
inline bool AbsolutePath(const std::wstring& p) {
    // Require an explicit drive path, excluding device/UNC namespaces.
    return p.size()>3 && ((p[0]>=L'A'&&p[0]<=L'Z')||(p[0]>=L'a'&&p[0]<=L'z')) &&
        p[1]==L':' && (p[2]==L'\\'||p[2]==L'/');
}
inline std::wstring Environment(const wchar_t* name) {
    wchar_t value[32768]{}; DWORD n=GetEnvironmentVariableW(name,value,32768);
    return n>0&&n<32768 ? std::wstring(value,n) : std::wstring{};
}
inline std::wstring ModulePath(HMODULE module=nullptr) {
    wchar_t path[32768]{}; DWORD n=GetModuleFileNameW(module,path,32768);
    return n>0&&n<32768 ? std::wstring(path,n) : std::wstring{};
}
inline std::wstring Canonical(HANDLE file) {
    wchar_t path[32768]{}; DWORD n=GetFinalPathNameByHandleW(file,path,32768,FILE_NAME_NORMALIZED|VOLUME_NAME_DOS);
    if(!n)n=GetFinalPathNameByHandleW(file,path,32768,FILE_NAME_OPENED|VOLUME_NAME_DOS);
    if(!n||n>=32768) return {};
    std::wstring result(path,n);
    if(result.rfind(L"\\\\?\\",0)==0) result.erase(0,4);
    return result;
}
inline std::wstring FullPath(const std::wstring& value) {
    wchar_t path[32768]{};DWORD n=GetFullPathNameW(value.c_str(),32768,path,nullptr);
    return n>0&&n<32768 ? std::wstring(path,n) : std::wstring{};
}
inline bool SameFile(HANDLE a,HANDLE b) {
    BY_HANDLE_FILE_INFORMATION x{},y{};
    return GetFileInformationByHandle(a,&x)&&GetFileInformationByHandle(b,&y)&&
        x.dwVolumeSerialNumber==y.dwVolumeSerialNumber&&x.nFileIndexHigh==y.nFileIndexHigh&&x.nFileIndexLow==y.nFileIndexLow;
}
inline bool Digest(HANDLE file,std::string& result) {
    LARGE_INTEGER zero{}; if(!SetFilePointerEx(file,zero,nullptr,FILE_BEGIN))return false;
    BCRYPT_ALG_HANDLE alg{}; BCRYPT_HASH_HANDLE hash{};
    if(BCryptOpenAlgorithmProvider(&alg,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return false;
    bool ok=BCryptCreateHash(alg,&hash,nullptr,0,nullptr,0,0)>=0;
    std::array<unsigned char,65536> buffer{}; DWORD read{};
    while(ok) {
        if(!ReadFile(file,buffer.data(),static_cast<DWORD>(buffer.size()),&read,nullptr)){ok=false;break;}
        if(!read)break;
        ok=BCryptHashData(hash,buffer.data(),read,0)>=0;
    }
    std::array<unsigned char,32> digest{};
    if(ok)ok=BCryptFinishHash(hash,digest.data(),static_cast<ULONG>(digest.size()),0)>=0;
    if(hash)BCryptDestroyHash(hash); BCryptCloseAlgorithmProvider(alg,0);
    if(!ok)return false;
    char hex[65]{}; for(unsigned i=0;i<32;++i)sprintf_s(hex+i*2,3,"%02X",digest[i]);
    result=hex; return true;
}
inline bool FileDigest(const std::wstring& path,std::string& digest) {
    HANDLE file=CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return false;
    bool ok=Digest(file,digest); CloseHandle(file); return ok;
}
inline bool ReadableEqual(const void* address,const void* expected,size_t length) {
    __try {
        if (memcmp(address,expected,length)==0) return true;
        extern void ReportMismatch(const void*, const void*, size_t);
        ReportMismatch(address, expected, length);
        return false;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) { return false; }
}
}
