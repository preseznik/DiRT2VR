#pragma once
namespace vr {
// Optional observers: never change arguments, game state or launch success.
void EnableEventDiagnostics() noexcept;
void ObserveDiagnosticRoute(const char* country,const char* track,const char* route) noexcept;
void ObserveDiagnosticCar(const char* code) noexcept;
}
