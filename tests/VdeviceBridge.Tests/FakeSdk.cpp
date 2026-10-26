#include "FakeSdk.h"
#include "../../source/NexusVdeviceBridge/vendor/Vdevice.h"
#include <chrono>
#include <thread>

namespace FakeSdk {
void Reset() {
    mode = Mode::Normal; created = 0; deleted = 0; receives = 0; responded = 0; alive = false;
    std::lock_guard<std::mutex> lock(reportMutex); reports.clear();
}
}
namespace Vdevice {
namespace Detail { struct Device_State { ~Device_State() { FakeSdk::alive = false; } }; }
Device::~Device() = default;
Device::Device(Device&&) noexcept = default;
Device& Device::operator=(Device&&) noexcept = default;
Device::operator bool() const noexcept { return state_ != nullptr && !error; }
std::uint64_t Device::Id() const noexcept { return state_ ? 1 : 0; }
Result Validate_Device(const Device_Config&) { return {}; }
Device Create_Device(const Device_Config&) {
    Device device;
    if (FakeSdk::mode == FakeSdk::Mode::CreateFails) { device.error = 31; device.message = "Creation failed"; return device; }
    device.state_ = std::make_shared<Detail::Device_State>();
    FakeSdk::alive = true; ++FakeSdk::created; return device;
}
Result Delete_Device(Device& device) {
    ++FakeSdk::deleted; device.state_.reset(); return {};
}
Info Get_Info() {
    Info info;
    if (FakeSdk::mode == FakeSdk::Mode::NoDriver) { info.error = 2; info.message = "Driver missing"; return info; }
    info.trial_expired = FakeSdk::mode == FakeSdk::Mode::Expired;
    if (FakeSdk::alive && FakeSdk::mode != FakeSdk::Mode::Removed) {
        Device_Details details; details.id = 1; details.plugged = true;
        details.configured = FakeSdk::mode != FakeSdk::Mode::NeverReady;
        details.powered = true; info.devices.push_back(details);
    }
    return info;
}
Result Send_Input(const Device&, std::uint8_t endpoint, const std::vector<std::uint8_t>& data) {
    if (endpoint != 0x81 || data.size() != 7) return {87, "Invalid mouse report"};
    if (FakeSdk::mode == FakeSdk::Mode::SendFails) return {1167, "Disconnected"};
    std::lock_guard<std::mutex> lock(FakeSdk::reportMutex);
    FakeSdk::reports.push_back(data); return {};
}
Output Get_Output(std::uint32_t timeout) {
    ++FakeSdk::receives;
    Output output;
    if (FakeSdk::mode == FakeSdk::Mode::OutputFails) { output.error = 31; output.message = "Receive failed"; return output; }
    if (FakeSdk::mode == FakeSdk::Mode::Control && FakeSdk::responded == 0) {
        output.kind = Output_Kind::Control; output.request_id = 5; output.device_id = 1; return output;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(timeout));
    output.timed_out = true; output.error = 1460; return output;
}
Result Respond_Output(const Output& output, const std::vector<std::uint8_t>& data, bool stall) {
    if (output.request_id != 5 || !data.empty() || !stall) return {87, "Unexpected control response"};
    ++FakeSdk::responded; return {};
}
}
