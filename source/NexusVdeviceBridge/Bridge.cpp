#include "Bridge.h"
#include "MouseProtocol.h"
#include <atomic>
#include <chrono>
#include <cstring>
#include <memory>
#include <mutex>
#include <thread>

namespace {
using namespace std::chrono_literals;
std::atomic<bool> occupied{false}; // SDK output queue is shared: own one mouse per bridge module.
std::uint32_t Message(std::uint32_t code, const char* text, char* out, std::uint32_t capacity) noexcept {
    if (out && capacity) {
        const auto length = std::min(std::strlen(text), static_cast<std::size_t>(capacity - 1));
        std::memcpy(out, text, length);
        out[length] = '\0';
    }
    return code;
}
struct Context {
    Vdevice::Device device;
    std::atomic<bool> stopping{false};
    std::thread pump;
    std::mutex failureMutex;
    Vdevice::Result failure;
    ~Context() {
        stopping = true;
        if (pump.joinable()) pump.join();
        if (device) {
            try { (void)Vdevice::Send_Input(device, 0x81, NexusMouse::Report(0,0,0)); } catch (...) { }
            try { (void)Vdevice::Delete_Device(device); } catch (...) { }
        }
    }
};
Vdevice::Result CheckInfo(const Context& context, bool& ready) {
    ready = false;
    auto info = Vdevice::Get_Info();
    if (!info) return {info.error, info.message};
    if (info.trial_expired) return {1460, "Vdevice demo time has expired. The public demo permits one hour per Windows boot; an unrestricted license is available from the vendor."};
    if (info.removing || info.quiescing) return {1167, "Vdevice is being removed. Complete setup and restart the app."};
    for (const auto& item : info.devices) {
        if (item.id != context.device.Id()) continue;
        if (!item.plugged) return {1167, "The Vdevice mouse disconnected."};
        ready = item.configured && item.powered;
        return {};
    }
    return {1167, "The Vdevice mouse is no longer present."};
}
Vdevice::Result Receive() {
    auto output = Vdevice::Get_Output(50);
    if (output.timed_out) return {};
    if (!output) return {output.error, output.message};
    if (output.kind == Vdevice::Output_Kind::Control && output.request_id) {
        auto response = Vdevice::Respond_Output(output, {}, true);
        if (!response && response.error != 1168) return response;
    }
    return {};
}
void Pump(Context& context) noexcept {
    try {
        while (!context.stopping) {
            auto status = Receive();
            bool ready = false;
            if (status) status = CheckInfo(context, ready);
            if (!status) {
                std::lock_guard<std::mutex> lock(context.failureMutex);
                context.failure = std::move(status);
                return;
            }
            // Suspend/unconfigured states are not treated as permanent removal;
            // Send_Input reports an error if the host cannot currently accept input.
        }
    } catch (...) {
        std::lock_guard<std::mutex> lock(context.failureMutex);
        context.failure.error = 8;
        // Avoid allocating another diagnostic string while handling an allocation failure.
    }
}
}

std::uint32_t NEXUS_CALL NexusVdeviceVersion() noexcept { return 1; }
std::uint32_t NEXUS_CALL NexusVdeviceOpen(void** handle, char* error, std::uint32_t capacity) noexcept {
    if (!handle) return Message(87, "Missing output handle.", error, capacity);
    *handle = nullptr;
    bool expected = false;
    if (!occupied.compare_exchange_strong(expected, true))
        return Message(170, "A Vdevice mouse is already open in this process.", error, capacity);
    try {
        auto context = std::make_unique<Context>();
        auto info = Vdevice::Get_Info();
        if (!info || info.trial_expired) {
            occupied = false;
            return Message(info ? 1460u : info.error, info ? "Vdevice demo time has expired." : info.message.c_str(), error, capacity);
        }
        context->device = Vdevice::Create_Device(NexusMouse::Config());
        if (!context->device) {
            const auto code = context->device.error ? context->device.error : 31u;
            occupied = false;
            return Message(code, context->device.message.c_str(), error, capacity);
        }
        const auto deadline = std::chrono::steady_clock::now() + 5s;
        bool ready = false;
        while (!ready) {
            auto status = Receive();
            if (status) status = CheckInfo(*context, ready);
            if (!status || std::chrono::steady_clock::now() >= deadline) {
                const auto code = status ? 1460u : status.error;
                Message(code, status ? "Timed out waiting for Windows to configure the virtual mouse." : status.message.c_str(), error, capacity);
                context.reset();
                occupied = false;
                return code;
            }
        }
        context->pump = std::thread([ptr = context.get()] { Pump(*ptr); });
        *handle = context.release();
        return Message(0, "", error, capacity);
    } catch (...) {
        occupied = false;
        return Message(8, "Unable to create the Vdevice mouse bridge.", error, capacity);
    }
}
std::uint32_t NEXUS_CALL NexusVdeviceSend(void* handle, int x, int y, std::uint32_t buttons,
    char* error, std::uint32_t capacity) noexcept {
    if (!handle || buttons > 31) return Message(87, "Invalid mouse handle or buttons.", error, capacity);
    try {
        auto& context = *static_cast<Context*>(handle);
        {
            std::lock_guard<std::mutex> lock(context.failureMutex);
            if (!context.failure)
                return Message(context.failure.error, context.failure.message.empty() ? "Vdevice output processing failed." : context.failure.message.c_str(), error, capacity);
        }
        const auto result = Vdevice::Send_Input(context.device, 0x81, NexusMouse::Report(x,y,buttons));
        return Message(result.error, result.message.c_str(), error, capacity);
    } catch (...) {
        return Message(8, "Unable to send the mouse report.", error, capacity);
    }
}
void NEXUS_CALL NexusVdeviceClose(void* handle) noexcept {
    if (!handle) return;
    delete static_cast<Context*>(handle);
    occupied = false;
}
