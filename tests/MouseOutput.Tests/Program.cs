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
