using System.Runtime.InteropServices;
using System.Windows;

namespace Nexus_Recoil.Core;

internal static class HotkeyManager
{
    private static IntPtr _hookHandle = IntPtr.Zero;
    private static NativeMethods.LowLevelKeyboardProc? _hookCallback;

    private static int _toggleKey;
    private static int _enableKey;
    private static int _disableKey;
    private static int _nextKey;
    private static int _prevKey;
    private static bool _quickSwapEnabled;

    // Set to true while the UI is capturing a key press to suppress hotkey actions.
    private static bool _isListening;
    private static int _listeningGeneration;
    public static bool IsListening
    {
        get => _isListening;
        set
        {
            if (_isListening == value) return;
            _isListening = value;
            _listeningGeneration++;
        }
    }

    public static event Action? ToggleUiPressed;
    public static event Action? EnablePressed;
    public static event Action? DisablePressed;
    public static event Action<int>? CyclePresetPressed;

    // Fired with the raw VK code when IsListening is true — used by the keybind capture UI.
    public static event Action<int>? KeyCaptured;

    public static void InstallHook()
    {
        _hookCallback = HookCallback;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module  = process.MainModule!;
        _hookHandle = NativeMethods.SetWindowsHookEx(
            13, // WH_KEYBOARD_LL
            _hookCallback,
            NativeMethods.GetModuleHandle(module.ModuleName!),
            0);
    }

    public static void UninstallHook()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    public static void RegisterAll(int toggleKey, int enableKey, int disableKey)
    {
        _toggleKey  = toggleKey;
        _enableKey  = enableKey;
        _disableKey = disableKey;
    }

    public static void RegisterQuickSwap(int nextKey, int prevKey, bool enabled)
    {
        _nextKey           = nextKey;
        _prevKey           = prevKey;
        _quickSwapEnabled  = enabled;
    }

    // No-op kept so call sites that unregister before listening don't need changing.
    public static void UnregisterAll() { }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)0x0100) // WM_KEYDOWN
        {
            var vk = Marshal.ReadInt32(lParam);
            if (IsListening)
            {
                var generation = _listeningGeneration;
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (IsListening && generation == _listeningGeneration)
                        KeyCaptured?.Invoke(vk);
                });
            }
            else
            {
                if (vk == _toggleKey)
                    Application.Current?.Dispatcher.BeginInvoke(() => ToggleUiPressed?.Invoke());
                else if (vk == _enableKey)
                    Application.Current?.Dispatcher.BeginInvoke(() => EnablePressed?.Invoke());
                else if (vk == _disableKey)
                    Application.Current?.Dispatcher.BeginInvoke(() => DisablePressed?.Invoke());
                else if (_quickSwapEnabled)
                {
                    if (vk == _nextKey)
                        Application.Current?.Dispatcher.BeginInvoke(() => CyclePresetPressed?.Invoke(1));
                    else if (vk == _prevKey)
                        Application.Current?.Dispatcher.BeginInvoke(() => CyclePresetPressed?.Invoke(-1));
                }
            }
        }
        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
}
