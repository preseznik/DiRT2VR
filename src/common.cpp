#include "common.h"
#include <bcrypt.h>
#include <array>
#include <cstdarg>
#include <cstdio>
#include <fstream>
#include <mutex>

namespace vr {
std::filesystem::path Output() {
    static const auto path = [] {
        wchar_t value[32768]{};
        if (GetEnvironmentVariableW(L"DIRT2VR_OUTPUT", value, 32768) == 0) {
            GetModuleFileNameW(nullptr, value, 32768);
            return std::filesystem::path(value).parent_path() / L"dirt2vr-trace";
        }
        return std::filesystem::path(value);
    }();
    return path;
}
void Log(const char* format, ...) {
    static std::mutex mutex;
    std::lock_guard lock(mutex);
    std::error_code ec;
    std::filesystem::create_directories(Output(), ec);
    FILE* file{};
    if (_wfopen_s(&file, (Output()/L"trace.log").c_str(), L"ab") || !file) return;
    fprintf(file, "[%llu:%lu] ", GetTickCount64(), GetCurrentThreadId());
    va_list args;
    va_start(args, format);
    vfprintf(file, format, args);
    va_end(args);
    fputc('\n', file);
    fclose(file);
}
bool SupportedHost() {
    // Check bytes on disk before enabling any instrumentation. ASLR is allowed.
    static const bool supported = [] {
        wchar_t path[32768]{};
        GetModuleFileNameW(nullptr, path, 32768);
        std::ifstream file(path, std::ios::binary);
        if (!file) return false;
        BCRYPT_ALG_HANDLE alg{};
        BCRYPT_HASH_HANDLE hash{};
        if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) return false;
        if (BCryptCreateHash(alg, &hash, nullptr, 0, nullptr, 0, 0) < 0) {
            BCryptCloseAlgorithmProvider(alg, 0); return false;
        }
        std::array<char, 65536> buffer{};
        bool ok = true;
        while (file) {
            file.read(buffer.data(), buffer.size());
            if (BCryptHashData(hash, reinterpret_cast<PUCHAR>(buffer.data()),
                               static_cast<ULONG>(file.gcount()), 0) < 0) ok = false;
        }
        std::array<UCHAR, 32> digest{};
        ok = ok && BCryptFinishHash(hash, digest.data(), 32, 0) >= 0;
        BCryptDestroyHash(hash);
        BCryptCloseAlgorithmProvider(alg, 0);
        char hex[65]{};
        for (unsigned i=0; i<32; ++i) sprintf_s(hex+i*2, 3, "%02X", digest[i]);
        const bool match = ok && std::string(hex) == "49B1E00EA1D4BD02E633CEED63390B5CAE07601333F673C4EFD8D2F0EB54FE48";
        Log("host_sha256=%s supported=%d; development prototype, headset acceptance pending", hex, match);
        return match;
    }();
    return supported;
}
}
