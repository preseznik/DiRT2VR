#pragma once
namespace vr {
bool EnablePauseObserver();
bool GamePaused();
// Change only pause presentation; native StatePause continues to own simulation.
bool PhotoWorldVisible(bool visible);
}
