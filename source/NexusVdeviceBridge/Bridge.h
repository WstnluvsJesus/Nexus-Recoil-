#pragma once
#include <cstdint>
#ifdef _WIN32
#define NEXUS_EXPORT extern "C" __declspec(dllexport)
#define NEXUS_CALL __cdecl
#else
#define NEXUS_EXPORT extern "C" __attribute__((visibility("default")))
#define NEXUS_CALL
#endif
NEXUS_EXPORT std::uint32_t NEXUS_CALL NexusVdeviceVersion() noexcept;
NEXUS_EXPORT std::uint32_t NEXUS_CALL NexusVdeviceOpen(void** handle, char* error, std::uint32_t capacity) noexcept;
NEXUS_EXPORT std::uint32_t NEXUS_CALL NexusVdeviceSend(void* handle, int x, int y, std::uint32_t buttons,
    char* error, std::uint32_t capacity) noexcept;
NEXUS_EXPORT void NEXUS_CALL NexusVdeviceClose(void* handle) noexcept;
