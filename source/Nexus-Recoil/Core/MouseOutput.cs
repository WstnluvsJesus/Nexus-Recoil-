using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nexus_Recoil.Core;

internal interface IMouseOutput : IDisposable
{
    void Move(int x, int y);
    void SetLeftButton(bool pressed);
}

internal sealed class WindowsMouseOutput : IMouseOutput
{
    public void Move(int x, int y)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            mi = new NativeMethods.MOUSEINPUT { dx = x, dy = y, dwFlags = NativeMethods.MOUSEEVENTF_MOVE }
        };
        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public void SetLeftButton(bool pressed) =>
        NativeMethods.mouse_event(pressed ? 0x0002u : 0x0004u, 0, 0, 0, UIntPtr.Zero);

    public void Dispose() { }
}

// Matches NexusMouseDriver/Protocol.h. One exclusive connection owns button state.
internal sealed class KernelMouseOutput : IMouseOutput
{
    private const uint SubmitReport = 0x0022A004; // FILE_DEVICE_UNKNOWN, 0x801, buffered, write access
    private readonly SafeFileHandle _handle;
    private readonly object _sync = new();
    private byte _buttons;
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct MouseReport
    {
        public byte ReportId;
        public byte Buttons;
        public short X;
        public short Y;
    }

    public KernelMouseOutput()
    {
        _handle = CreateFile(@"\\.\NexusVirtualMouse", 0x40000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error, "Cannot open Nexus Virtual Mouse. Install its signed driver and run the app as administrator. Close any other driver client.");
        }
    }

    public void Move(int x, int y)
    {
        lock (_sync)
            Send(x, y, _buttons);
    }

    public void SetLeftButton(bool pressed)
    {
        lock (_sync)
        {
            byte buttons = pressed ? (byte)1 : (byte)0;
            Send(0, 0, buttons);
            _buttons = buttons;
        }
    }

    private void Send(int x, int y, byte buttons)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var report = new MouseReport
        {
            ReportId = 1, Buttons = buttons,
            X = (short)Math.Clamp(x, -32767, 32767), Y = (short)Math.Clamp(y, -32767, 32767)
        };
        if (!DeviceIoControl(_handle, SubmitReport, ref report, (uint)Marshal.SizeOf<MouseReport>(),
                IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The virtual mouse driver rejected input.");
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            // Driver file cleanup releases buttons, including after a client crash.
            _disposed = true;
            _handle.Dispose();
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code,
        ref MouseReport input, uint inputSize, IntPtr output, uint outputSize,
        out uint returned, IntPtr overlapped);
}
