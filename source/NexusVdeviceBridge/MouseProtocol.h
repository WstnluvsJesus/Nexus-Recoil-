#pragma once
#include "vendor/Vdevice.h"
#include <algorithm>
#include <utility>

// Mouse descriptors adapted from TitanCSA/Vdevice ExamplesUI/Mouse.cpp.
// Copyright (c) 2026 TitanCSA. MIT license: vendor/LICENSE.
namespace NexusMouse {
inline Vdevice::Descriptor Text(std::uint32_t index, const std::u16string& value) {
    Vdevice::Descriptor descriptor;
    descriptor.index = index;
    descriptor.language = 0x0409;
    descriptor.data = {static_cast<std::uint8_t>(2 + value.size() * 2), 3};
    for (auto ch : value) {
        descriptor.data.push_back(static_cast<std::uint8_t>(ch));
        descriptor.data.push_back(static_cast<std::uint8_t>(ch >> 8));
    }
    return descriptor;
}
inline Vdevice::Device_Config Config() {
    Vdevice::Device_Config config;
    Vdevice::Descriptor device;
    // Vendor example development identity, not a physical vendor's identity.
    device.data = {18,1,0,2,0,0,0,64,0xFF,0xFF,2,0,0,1,1,2,0,1};
    Vdevice::Descriptor report;
    report.kind = Vdevice::Descriptor_Kind::Report;
    report.data = {
        0x05,0x01,0x09,0x02,0xA1,0x01,0x09,0x01,0xA1,0x00,
        0x05,0x09,0x19,0x01,0x29,0x05,0x15,0x00,0x25,0x01,
        0x95,0x05,0x75,0x01,0x81,0x02,0x95,0x01,0x75,0x03,0x81,0x01,
        0x05,0x01,0x09,0x30,0x09,0x31,0x16,0x00,0x80,0x26,0xFF,0x7F,
        0x75,0x10,0x95,0x02,0x81,0x06,
        0x09,0x38,0x15,0x81,0x25,0x7F,0x75,0x08,0x95,0x01,0x81,0x06,
        0x05,0x0C,0x0A,0x38,0x02,0x95,0x01,0x81,0x06,0xC0,0xC0
    };
    Vdevice::Descriptor configuration;
    configuration.data = {
        9,2,34,0,1,1,0,0x80,50, 9,4,0,0,1,3,0,0,0,
        9,0x21,0x11,0x01,0,1,0x22,
        static_cast<std::uint8_t>(report.data.size()),0,
        7,5,0x81,3,7,0,1
    };
    Vdevice::Descriptor languages;
    languages.data = {4,3,0x09,0x04};
    config.descriptors = {std::move(device),std::move(configuration),std::move(report),
        std::move(languages),Text(1,u"Nexus Recoil"),Text(2,u"Nexus Virtual Mouse")};
    return config;
}
inline std::vector<std::uint8_t> Report(int x, int y, std::uint32_t buttons) {
    const auto dx = static_cast<std::uint16_t>(std::clamp(x, -32768, 32767));
    const auto dy = static_cast<std::uint16_t>(std::clamp(y, -32768, 32767));
    return {static_cast<std::uint8_t>(buttons),static_cast<std::uint8_t>(dx),
        static_cast<std::uint8_t>(dx >> 8),static_cast<std::uint8_t>(dy),
        static_cast<std::uint8_t>(dy >> 8),0,0};
}
}
