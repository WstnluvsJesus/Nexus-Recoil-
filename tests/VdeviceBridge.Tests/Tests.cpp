#include "../../source/NexusVdeviceBridge/Bridge.h"
#include "../../source/NexusVdeviceBridge/MouseProtocol.h"
#include "FakeSdk.h"
#include <chrono>
#include <iostream>
#include <stdexcept>
#include <thread>

int main() {
    int checks = 0;
    const auto check = [&](bool condition, const char* message) {
        if (!condition) throw std::runtime_error(message);
        ++checks;
    };
    const auto config = NexusMouse::Config();
    check(config.descriptors.size() == 6 && !config.delegate_controls, "Mouse descriptors/controls");
    const auto& usb = config.descriptors[1].data;
    check(usb.size() == 34 && usb[2] == 34 && usb[29] == 0x81 && usb[31] == 7, "Configuration length/endpoint/report size");
    check(usb[25] == config.descriptors[2].data.size(), "HID descriptor length");
    check(NexusMouse::Report(-1,32768,1) == std::vector<std::uint8_t>({1,255,255,255,127,0,0}), "Signed relative report and upper clamp");
    check(NexusMouse::Report(-40000,0,0)[2] == 128, "Lower clamp");
    check(NexusVdeviceVersion() == 1, "ABI version");
    char error[256]{};
    void* handle = nullptr;
    check(NexusVdeviceOpen(nullptr,error,sizeof(error)) == 87, "Invalid output pointer");
    FakeSdk::mode = FakeSdk::Mode::NoDriver;
    check(NexusVdeviceOpen(&handle,error,sizeof(error)) == 2 && !handle, "Missing driver fails");
    FakeSdk::mode = FakeSdk::Mode::CreateFails;
    check(NexusVdeviceOpen(&handle,error,sizeof(error)) == 31 && !handle, "Create failure has no handle");
    FakeSdk::mode = FakeSdk::Mode::Expired;
    check(NexusVdeviceOpen(&handle,error,sizeof(error)) == 1460 && !handle, "Expired license prevents creation");
    FakeSdk::Reset(); FakeSdk::mode = FakeSdk::Mode::Control;
    check(NexusVdeviceOpen(&handle,error,sizeof(error)) == 0 && handle, "Creates and waits for ready");
    check(FakeSdk::responded == 1, "Services delegated control during enumeration");
    void* duplicate = nullptr;
    check(NexusVdeviceOpen(&duplicate,error,sizeof(error)) == 170 && !duplicate, "Rejects competing SDK output consumer");
    check(NexusVdeviceSend(handle,1,-2,1,error,sizeof(error)) == 0, "Sends relative input");
    check(NexusVdeviceSend(handle,1,-2,32,error,sizeof(error)) == 87, "Rejects reserved button bits");
    FakeSdk::mode = FakeSdk::Mode::SendFails;
    check(NexusVdeviceSend(handle,0,0,0,error,sizeof(error)) == 1167, "Propagates send failure");
    FakeSdk::mode = FakeSdk::Mode::Expired;
    std::uint32_t code = 0;
    for(int i = 0; i < 30 && code == 0; ++i) {
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
        code = NexusVdeviceSend(handle,0,0,0,error,sizeof(error));
    }
    check(code == 1460, "Background pump detects demo expiry");
    NexusVdeviceClose(handle); handle = nullptr;
    check(FakeSdk::deleted == 1 && !FakeSdk::alive, "Close disconnects and joins pump");
    { std::lock_guard<std::mutex> lock(FakeSdk::reportMutex);
      check(FakeSdk::reports.back() == std::vector<std::uint8_t>(7,0), "Close submits neutral input"); }
    FakeSdk::Reset(); FakeSdk::mode = FakeSdk::Mode::OutputFails;
    check(NexusVdeviceOpen(&handle,error,sizeof(error)) == 31 && !handle && FakeSdk::deleted == 1, "Receive failure cleans up partially created device");
    FakeSdk::Reset(); FakeSdk::mode = FakeSdk::Mode::NeverReady;
    check(NexusVdeviceOpen(&handle,error,sizeof(error)) == 1460 && !handle && FakeSdk::deleted == 1, "Enumeration timeout cleans up device");
    FakeSdk::Reset();
    check(NexusVdeviceOpen(&handle,error,sizeof(error)) == 0, "Can open after prior failures");
    FakeSdk::mode = FakeSdk::Mode::Removed;
    code = 0;
    for(int i=0; i<30 && code==0; ++i) {
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
        code = NexusVdeviceSend(handle,0,0,0,error,sizeof(error));
    }
    check(code == 1167, "Background pump detects removal");
    NexusVdeviceClose(handle);
    std::cout << "Passed " << checks << " native bridge assertions.\n";
}
