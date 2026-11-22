#pragma once
#include <d3d11.h>
#include <vector>

namespace vr {
// Cars seen in the picture, from the world matrices the game writes into its constant
// buffers for each draw (cb0 `model`, row vectors: rows 0-2 basis, row 3 position).
// Only works while a replay plays or Photo Mode is open; otherwise every call returns at once.
struct TrackedCar {
    int id{};             // The car's track.
    int number{};         // Car number in order of first sighting; kept when it comes back into view.
    unsigned unseenMs{};  // How long since it was last drawn (0: in the picture now).
    float matrix[16]{};   // World matrix of its largest draw (the body).
    float velocity[3]{};  // m/s, while the replay plays.
    float travelled{};    // m, since first seen.
    float guess[3]{};     // Where it probably is now (its position while it is in the picture).
    float guessForward[3]{}; // Its direction of travel there.
    bool guessFromTrail{};   // The guess follows another car's trail (rather than straight on).
};
bool CarTrackerActive();
// From the constant buffer write hooks (UpdateSubresource, Unmap).
void CarTrackerConstant(ID3D11Resource* resource,const void* data,UINT size);
// From the indexed draw hooks, before the draw.
void CarTrackerDraw(ID3D11DeviceContext* context,UINT indices);
// At Present: closes the frame. playing: the replay clock is advancing.
void CarTrackerFrame(bool active,bool playing);
// Cars seen recently (also those that left the picture, for a while), ordered by car number.
std::vector<TrackedCar> TrackedCars();
}
