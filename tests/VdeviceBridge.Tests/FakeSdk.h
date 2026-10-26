#pragma once
#include <atomic>
#include <cstdint>
#include <mutex>
#include <vector>
namespace FakeSdk {
enum class Mode { Normal, NoDriver, CreateFails, NeverReady, Expired, Removed, SendFails, Control, OutputFails };
inline std::atomic<Mode> mode{Mode::Normal};
inline std::atomic<bool> alive{false};
inline std::atomic<int> created{0}, deleted{0}, receives{0}, responded{0};
inline std::mutex reportMutex;
inline std::vector<std::vector<std::uint8_t>> reports;
void Reset();
}
