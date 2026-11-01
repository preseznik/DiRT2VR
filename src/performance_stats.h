#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <map>
#include <string>
#include <string_view>

namespace vr {
// Ported from GRID2VR. Adapter-wide utilization: sum processes sharing one
// engine, then select the busiest engine. Summing different engines would
// incorrectly exceed 100%.
class GpuEngineUsage {
public:
    void Add(std::wstring_view name,std::wstring_view adapter,double percent,bool valid) {
        if(!valid || !std::isfinite(percent) || percent<0)return;
        const auto start=name.find(adapter);
        if(start==std::wstring_view::npos)return;
        const auto key=name.substr(start+adapter.size());
        if(!key.starts_with(L"_phys_") || key.find(L"_eng_")==std::wstring_view::npos)return;
        totals_[std::wstring(key)]+=percent;
    }
    double Percent() const {
        if(totals_.empty())return -1;
        double busiest{};for(const auto& [key,value]:totals_)busiest=std::max(busiest,value);
        return std::clamp(busiest,0.,100.);
    }
private:
    std::map<std::wstring,double> totals_;
};
// Submitted application frames per second, refreshed about once a second.
class SubmittedFrameRate {
public:
    void Frame(uint64_t milliseconds) {
        if(!started_) {started_=true;start_=milliseconds;return;}
        if(milliseconds<start_) {start_=milliseconds;count_=0;fps_=-1;return;}
        ++count_;
        const auto elapsed=milliseconds-start_;
        if(elapsed>=1000) {fps_=double(count_)*1000/double(elapsed);count_=0;start_=milliseconds;}
    }
    double Fps() const {return fps_;}
private:
    uint64_t start_{},count_{};
    double fps_{-1};bool started_{};
};
}
