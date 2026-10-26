using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Nexus_Recoil.Core;

int assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    assertions++;
}
bool JoinWorkers(RecoilEngine engine)
{
    foreach (string name in new[] { "_recoilThread", "_rapidFireThread" })
    {
        var thread = (Thread?)typeof(RecoilEngine).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(engine);
        if (thread != null && !thread.Join(3000)) return false;
    }
    return true;
}

Check(Marshal.SizeOf<KernelMouseOutput.MouseReport>() == 6, "Wire report size");
Check(Marshal.OffsetOf<KernelMouseOutput.MouseReport>("Buttons").ToInt32() == 1, "Button offset");
Check(Marshal.OffsetOf<KernelMouseOutput.MouseReport>("X").ToInt32() == 2, "X offset");
Check(Marshal.OffsetOf<KernelMouseOutput.MouseReport>("Y").ToInt32() == 4, "Y offset");

var output = new FakeOutput();
using (var engine = new RecoilEngine(output) { IsEnabled = true, RapidFireEnabled = true, RapidFireRateMs = 10 })
{
    int errors = 0;
    engine.OutputFailed += _ => Interlocked.Increment(ref errors);
    engine.Start();
    Check(output.Moved.Wait(3000), "Injected backend receives recoil movement");
    Check(output.Pressed.Wait(3000), "Injected backend receives button presses");
    engine.Stop();
    Check(JoinWorkers(engine), "Stop ends both loops while physical keys remain held");
    Check(!output.Down, "Stop releases virtual button");
    Check(engine.RapidFireEnabled, "Stop preserves selected rapid-fire setting");
    engine.Dispose();
    Check(output.Disposed, "Dispose closes mouse backend");
    Check(errors == 0, "Normal shutdown does not report an output failure");
}

var failing = new FakeOutput { Fail = true };
using (var engine = new RecoilEngine(failing) { IsEnabled = true, RapidFireEnabled = true })
{
    int errors = 0;
    engine.OutputFailed += _ => Interlocked.Increment(ref errors);
    engine.Start();
    Check(JoinWorkers(engine), "Output failure terminates both workers");
    Check(!engine.IsEnabled, "Output failure disables engine");
    Check(failing.Disposed, "Output failure closes backend for driver cleanup");
    Check(errors == 1, "Concurrent worker failures produce one notification");
}

var interrupted = new FakeOutput { BlockMove = true };
using (var engine = new RecoilEngine(interrupted) { IsEnabled = true })
{
    int errors = 0;
    engine.OutputFailed += _ => Interlocked.Increment(ref errors);
    engine.Start();
    Check(interrupted.Moved.Wait(3000), "Movement entered before disposal race");
    engine.Dispose();
    interrupted.Continue.Set();
    Check(JoinWorkers(engine), "Disposed backend cannot keep recoil worker alive");
    Check(errors == 0, "Intentional disposal race does not show failure dialog");
}
Check(Marshal.SizeOf<InterceptionMouseStroke>() == 20, "Interception stroke size matches native header");
Check(Marshal.OffsetOf<InterceptionMouseStroke>("X").ToInt32() == 8, "Interception X alignment");
Check(Marshal.OffsetOf<InterceptionMouseStroke>("Y").ToInt32() == 12, "Interception Y alignment");
Check(Marshal.OffsetOf<InterceptionMouseStroke>("Information").ToInt32() == 16, "Interception information alignment");

var api = new FakeInterceptionApi();
var intercepted = new InterceptionMouseOutput(api);
Check(api.Queried.SequenceEqual(new[] { 11, 12, 13 }), "Selects first reported connected mouse");
intercepted.Move(-20, 15);
Check(api.Reports[0].Device == 13 && api.Reports[0].Stroke.X == -20 && api.Reports[0].Stroke.Y == 15,
    "Relative movement targets detected mouse");
Check(api.Reports[0].Stroke.State == 0 && api.Reports[0].Stroke.Flags == 0, "Movement has no button transition or absolute flags");
intercepted.SetLeftButton(true);
intercepted.Move(1, 2);
intercepted.SetLeftButton(false);
Check(api.Reports[1].Stroke.State == 1 && api.Reports[3].Stroke.State == 2, "Uses native left-down and left-up transition codes");
Check(api.Reports[2].Stroke.State == 0, "Movement does not repeat a held-button transition");
intercepted.SetLeftButton(true);
intercepted.Dispose();
Check(api.Reports.Last().Stroke.State == 2, "Dispose releases held button");
Check(api.Destroyed == 1 && api.Disposed == 1, "Dispose destroys context and unloads library once");
intercepted.Dispose();
Check(api.Destroyed == 1 && api.Disposed == 1, "Interception disposal is idempotent");
try { intercepted.Move(1, 1); Check(false, "Disposed send must throw"); }
catch (ObjectDisposedException) { Check(true, "Disposed sends rejected"); }

var unavailable = new FakeInterceptionApi { Context = IntPtr.Zero };
try { using var unused = new InterceptionMouseOutput(unavailable); Check(false, "Absent driver must fail"); }
catch (Win32Exception) { Check(unavailable.Disposed == 1 && unavailable.Destroyed == 0, "Absent driver fails and unloads library"); }
var noMouse = new FakeInterceptionApi { Mouse = 0 };
try { using var unused = new InterceptionMouseOutput(noMouse); Check(false, "Absent mouse must fail"); }
catch (Win32Exception) { Check(noMouse.Destroyed == 1 && noMouse.Disposed == 1, "Absent mouse cleans up created context"); }
Check(noMouse.Queried.SequenceEqual(Enumerable.Range(11, 10)), "Discovery stays within documented mouse ID range");
var rejected = new FakeInterceptionApi();
using (var backend = new InterceptionMouseOutput(rejected))
{
    rejected.Accept = false;
    try { backend.SetLeftButton(true); Check(false, "Failed send must throw"); }
    catch (Win32Exception) { Check(true, "Native send failure reported"); }
}
Check(rejected.Reports.Last().Stroke.State == 2 && rejected.Destroyed == 1,
    "Rejected press still attempts release and frees context");

var disconnected = new FakeInterceptionApi();
using (var backend = new InterceptionMouseOutput(disconnected))
using (var engine = new RecoilEngine(backend) { IsEnabled = true })
{
    disconnected.Accept = false;
    int errors = 0;
    engine.OutputFailed += _ => Interlocked.Increment(ref errors);
    engine.Start();
    Check(JoinWorkers(engine), "Interception send failure stops engine workers");
    Check(!engine.IsEnabled && errors == 1, "Interception disconnection reports one engine error");
}
Check(disconnected.Destroyed == 1 && disconnected.Disposed == 1, "Engine failure closes Interception context");

assertions += VdeviceTests.Run();
Console.WriteLine($"Passed {assertions} mouse output assertions.");

sealed class FakeOutput : IMouseOutput
{
    public bool Fail;
    public bool BlockMove;
    public volatile bool Disposed;
    public volatile bool Down;
    public readonly ManualResetEventSlim Moved = new();
    public readonly ManualResetEventSlim Pressed = new();
    public readonly ManualResetEventSlim Continue = new();
    public void Move(int x, int y)
    {
        Moved.Set();
        if (BlockMove && !Continue.Wait(3000)) throw new TimeoutException();
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (Fail) throw new Win32Exception(5);
    }
    public void SetLeftButton(bool pressed)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        if (Fail) throw new Win32Exception(5);
        Down = pressed;
        if (pressed) Pressed.Set();
    }
    public void Dispose() { Disposed = true; Down = false; }
}

sealed class FakeInterceptionApi : IInterceptionApi
{
    public IntPtr Context = new(1);
    public int Mouse = 13;
    public volatile bool Accept = true;
    public int Destroyed;
    public int Disposed;
    public readonly List<int> Queried = new();
    public readonly List<(int Device, InterceptionMouseStroke Stroke)> Reports = new();
    public IntPtr CreateContext() => Context;
    public void DestroyContext(IntPtr context) => Destroyed++;
    public uint GetHardwareId(IntPtr context, int device, byte[] buffer)
    {
        Queried.Add(device);
        return device == Mouse ? 8u : 0;
    }
    public int Send(IntPtr context, int device, ref InterceptionMouseStroke stroke)
    {
        Reports.Add((device, stroke));
        return Accept ? 1 : 0;
    }
    public void Dispose() => Disposed++;
}
