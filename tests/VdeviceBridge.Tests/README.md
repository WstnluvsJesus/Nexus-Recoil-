# Native bridge tests

Uses the production Bridge.cpp and mouse descriptors with a fake SDK. Does not
install a driver or inject real input. From the repository root on Linux:

```sh
g++ -std=c++17 -Wall -Wextra -Werror -pthread source/NexusVdeviceBridge/Bridge.cpp tests/VdeviceBridge.Tests/FakeSdk.cpp tests/VdeviceBridge.Tests/Tests.cpp -o /tmp/nexus-vdevice-tests
/tmp/nexus-vdevice-tests
```

These checks do not compile the vendor's Windows implementation. Build the real
bridge using the documented Windows CMake command before installing/running it.
