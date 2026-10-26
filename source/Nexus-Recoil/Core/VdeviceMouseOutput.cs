using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Nexus_Recoil.Core;

internal interface IVdeviceApi : IDisposable
{
    IntPtr Open();
    void Send(IntPtr handle, int x, int y, uint buttons);
    void Close(IntPtr handle);
}

internal sealed class VdeviceMouseOutput : IMouseOutput
{
    private readonly IVdeviceApi _api;
    private readonly object _sync = new();
    private IntPtr _handle;
    private uint _buttons;

    public VdeviceMouseOutput() : this(new NativeVdeviceApi()) { }

    internal VdeviceMouseOutput(IVdeviceApi api)
    {
        _api = api;
        try
        {
            _handle = api.Open();
            if (_handle == IntPtr.Zero)
                throw new Win32Exception(31, "Vdevice returned no virtual mouse. Check its installation and license status.");
        }
        catch
        {
            api.Dispose();
            throw;
        }
    }

    public void Move(int x, int y)
    {
        lock (_sync) Send(x, y, _buttons);
    }

    public void SetLeftButton(bool pressed)
    {
        lock (_sync)
        {
            uint buttons = pressed ? 1u : 0u;
            Send(0, 0, buttons);
            _buttons = buttons;
        }
    }

    private void Send(int x, int y, uint buttons)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        _api.Send(_handle, Math.Clamp(x, -32768, 32767), Math.Clamp(y, -32768, 32767), buttons);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_handle == IntPtr.Zero) return;
            try
            {
                // Close also sends neutral input and deletes the device. Removal/expiry
                // can reject the release, but must not prevent releasing native ownership.
                try { _api.Send(_handle, 0, 0, 0); }
                catch (Win32Exception) { }
            }
            finally
            {
                try { _api.Close(_handle); }
                finally
                {
                    _handle = IntPtr.Zero;
                    _api.Dispose();
                }
            }
        }
    }
}

internal sealed class NativeVdeviceApi : IVdeviceApi
{
    private IntPtr _module;
    private readonly OpenMouse _open;
    private readonly SendMouse _send;
    private readonly CloseMouse _close;

    public NativeVdeviceApi()
    {
        try
        {
            _module = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "NexusVdeviceBridge.dll"));
            if (Export<Version>("NexusVdeviceVersion")() != 1)
                throw new Win32Exception(1306, "Vdevice bridge version differs. Rebuild the bridge and app together.");
            _open = Export<OpenMouse>("NexusVdeviceOpen");
            _send = Export<SendMouse>("NexusVdeviceSend");
            _close = Export<CloseMouse>("NexusVdeviceClose");
        }
        catch (Exception ex)
        {
            Dispose();
            if (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
                throw new Win32Exception(126, "Cannot load the Vdevice bridge. Build the x64 bridge and put NexusVdeviceBridge.dll beside NexusRecoil.exe. " + ex.Message);
            throw;
        }
    }

    private T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module, name));

    public IntPtr Open()
    {
        var error = new byte[1024];
        Check(_open(out var handle, error, (uint)error.Length), error);
        return handle;
    }
    public void Send(IntPtr handle, int x, int y, uint buttons)
    {
        var error = new byte[1024];
        Check(_send(handle, x, y, buttons, error, (uint)error.Length), error);
    }
    public void Close(IntPtr handle) => _close(handle);
    public void Dispose()
    {
        var module = Interlocked.Exchange(ref _module, IntPtr.Zero);
        if (module != IntPtr.Zero) NativeLibrary.Free(module);
    }
    private static void Check(uint code, byte[] error)
    {
        if (code == 0) return;
        int length = Array.IndexOf(error, (byte)0);
        var message = Encoding.UTF8.GetString(error, 0, length < 0 ? error.Length : length);
        throw new Win32Exception(unchecked((int)code), string.IsNullOrWhiteSpace(message)
            ? "Vdevice operation failed. Check the driver and license status." : message);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint Version();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint OpenMouse(out IntPtr handle, [Out] byte[] error, uint capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint SendMouse(IntPtr handle, int x, int y, uint buttons, [Out] byte[] error, uint capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CloseMouse(IntPtr handle);
}
