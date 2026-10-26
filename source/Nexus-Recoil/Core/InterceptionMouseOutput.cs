using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Nexus_Recoil.Core;

[StructLayout(LayoutKind.Sequential)]
internal struct InterceptionMouseStroke
{
    public ushort State;
    public ushort Flags;
    public short Rolling;
    public int X;
    public int Y;
    public uint Information;
}

internal interface IInterceptionApi : IDisposable
{
    IntPtr CreateContext();
    void DestroyContext(IntPtr context);
    uint GetHardwareId(IntPtr context, int device, byte[] buffer);
    int Send(IntPtr context, int device, ref InterceptionMouseStroke stroke);
}

// Sends through the documented Interception library API. No input filters are set.
internal sealed class InterceptionMouseOutput : IMouseOutput
{
    private readonly IInterceptionApi _api;
    private readonly object _sync = new();
    private IntPtr _context;
    private readonly int _device;
    private bool _leftDown;

    public InterceptionMouseOutput() : this(new NativeInterceptionApi()) { }

    internal InterceptionMouseOutput(IInterceptionApi api)
    {
        _api = api;
        try
        {
            _context = api.CreateContext();
            if (_context == IntPtr.Zero)
                throw new Win32Exception(31, "Cannot connect to Interception. Install its official driver, restart Windows, and close other Interception clients.");

            var buffer = new byte[1024];
            // interception.h assigns mouse devices 11 through 20.
            for (int device = 11; device <= 20; device++)
            {
                if (api.GetHardwareId(_context, device, buffer) == 0) continue;
                _device = device;
                break;
            }
            if (_device == 0)
                throw new Win32Exception(1167, "Interception found no connected mouse. Connect a mouse and restart the app.");
        }
        catch
        {
            if (_context != IntPtr.Zero) api.DestroyContext(_context);
            _context = IntPtr.Zero;
            api.Dispose();
            throw;
        }
    }

    public void Move(int x, int y)
    {
        lock (_sync)
        {
            var stroke = new InterceptionMouseStroke { X = x, Y = y }; // relative, no button transition
            Send(ref stroke);
        }
    }

    public void SetLeftButton(bool pressed)
    {
        lock (_sync)
        {
            // Remember a possible press even if the native call fails, for best-effort cleanup.
            if (pressed) _leftDown = true;
            var stroke = new InterceptionMouseStroke { State = pressed ? (ushort)1 : (ushort)2 };
            Send(ref stroke);
            _leftDown = pressed;
        }
    }

    private void Send(ref InterceptionMouseStroke stroke)
    {
        ObjectDisposedException.ThrowIf(_context == IntPtr.Zero, this);
        if (_api.Send(_context, _device, ref stroke) != 1)
            throw new Win32Exception(31, "Interception could not send mouse input. Check the driver and mouse connection, then restart the app.");
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_context == IntPtr.Zero) return;
            try
            {
                if (_leftDown)
                {
                    var release = new InterceptionMouseStroke { State = 2 };
                    _api.Send(_context, _device, ref release);
                }
            }
            finally
            {
                _api.DestroyContext(_context);
                _context = IntPtr.Zero;
                _api.Dispose();
            }
        }
    }
}

internal sealed class NativeInterceptionApi : IInterceptionApi
{
    private readonly IntPtr _module;
    private readonly Create _create;
    private readonly Destroy _destroy;
    private readonly HardwareId _hardwareId;
    private readonly SendStroke _send;

    public NativeInterceptionApi()
    {
        try
        {
            // Load only the user-supplied DLL beside the executable, not from PATH/CWD.
            _module = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "interception.dll"));
            _create = Export<Create>("interception_create_context");
            _destroy = Export<Destroy>("interception_destroy_context");
            _hardwareId = Export<HardwareId>("interception_get_hardware_id");
            _send = Export<SendStroke>("interception_send");
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            if (_module != IntPtr.Zero) NativeLibrary.Free(_module);
            throw new Win32Exception(126, "Cannot load Interception. Put the official x64 interception.dll beside NexusRecoil.exe. " + ex.Message);
        }
    }

    private T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module, name));

    public IntPtr CreateContext() => _create();
    public void DestroyContext(IntPtr context) => _destroy(context);
    public uint GetHardwareId(IntPtr context, int device, byte[] buffer) =>
        _hardwareId(context, device, buffer, (uint)buffer.Length);
    public int Send(IntPtr context, int device, ref InterceptionMouseStroke stroke) =>
        _send(context, device, ref stroke, 1);
    public void Dispose() => NativeLibrary.Free(_module);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr Create();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Destroy(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint HardwareId(IntPtr context, int device, [Out] byte[] buffer, uint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SendStroke(IntPtr context, int device, ref InterceptionMouseStroke stroke, uint count);
}
