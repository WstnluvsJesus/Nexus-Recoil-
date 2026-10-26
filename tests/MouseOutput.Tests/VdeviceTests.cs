using System.ComponentModel;
using Nexus_Recoil.Core;

internal static class VdeviceTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var api = new FakeVdeviceApi();
        var mouse = new VdeviceMouseOutput(api);
        mouse.Move(int.MinValue, int.MaxValue);
        Check(api.Reports.Last() == (-32768,32767,0u), "Vdevice clamps coordinates to HID range");
        mouse.SetLeftButton(true);
        mouse.Move(-1, 20);
        Check(api.Reports.Last() == (-1,20,1u), "Movement preserves held Vdevice button state");
        mouse.SetLeftButton(false);
        Check(api.Reports.Last() == (0,0,0u), "Button release has no stale movement");
        mouse.Dispose();
        Check(api.Closed == 1 && api.Disposed == 1, "Native context and module released");
        Check(api.Reports.Last() == (0,0,0u), "Neutral input sent on disposal");
        mouse.Dispose();
        Check(api.Closed == 1 && api.Disposed == 1, "Disposal is idempotent");
        try { mouse.Move(1,1); Check(false, "Send after dispose must fail"); }
        catch (ObjectDisposedException) { Check(true, "Send after disposal rejected"); }

        var missing = new FakeVdeviceApi { Handle = IntPtr.Zero };
        try { using var unused = new VdeviceMouseOutput(missing); Check(false, "Null device must fail"); }
        catch (Win32Exception) { Check(missing.Disposed == 1, "Null device frees module"); }
        var expired = new FakeVdeviceApi { FailOpen = true };
        try { using var unused = new VdeviceMouseOutput(expired); Check(false, "Expired demo must fail"); }
        catch (Win32Exception ex) { Check(ex.NativeErrorCode == 1460 && expired.Disposed == 1, "Open expiry propagated and module freed"); }

        var rejected = new FakeVdeviceApi();
        using (var failed = new VdeviceMouseOutput(rejected))
        {
            rejected.FailSend = true;
            try { failed.Move(1,1); Check(false, "Send failure must propagate"); }
            catch (Win32Exception ex) { Check(ex.NativeErrorCode == 1167, "Disconnection propagated"); }
        }
        Check(rejected.Closed == 1 && rejected.Disposed == 1, "Failed neutral send still frees native resources");

        var disconnect = new FakeVdeviceApi();
        using (var engine = new RecoilEngine(new VdeviceMouseOutput(disconnect)) { IsEnabled = true })
        {
            int errors = 0;
            using var failedEvent = new ManualResetEventSlim();
            engine.OutputFailed += _ => { Interlocked.Increment(ref errors); failedEvent.Set(); };
            disconnect.FailSend = true;
            engine.Start();
            Check(failedEvent.Wait(3000), "Vdevice transport failure reaches engine error event");
            Check(!engine.IsEnabled && errors == 1, "Engine disabled after Vdevice failure");
        }
        Check(disconnect.Closed == 1 && disconnect.Disposed == 1, "Engine failure releases Vdevice once");
        return checks;
    }
}

internal sealed class FakeVdeviceApi : IVdeviceApi
{
    public IntPtr Handle = new(123);
    public bool FailOpen;
    public volatile bool FailSend;
    public int Closed, Disposed;
    public readonly List<(int,int,uint)> Reports = new();
    public IntPtr Open()
    {
        if (FailOpen) throw new Win32Exception(1460, "Demo expired");
        return Handle;
    }
    public void Send(IntPtr handle, int x, int y, uint buttons)
    {
        if (FailSend) throw new Win32Exception(1167, "Disconnected");
        Reports.Add((x,y,buttons));
    }
    public void Close(IntPtr handle) => Closed++;
    public void Dispose() => Disposed++;
}
