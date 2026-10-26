using System.ComponentModel;
using System.Text.Json;
using Nexus_Recoil.Core;
using Nexus_Recoil.Models;

internal static class SwitchableOutputTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var original = new Output();
        var driver = new Output();
        using var mouse = new SwitchableMouseOutput(original, false);
        mouse.SetLeftButton(true);
        mouse.Switch(true, () => driver);
        Check(!original.Held && original.Disposals == 1 && mouse.UsesVdevice, "Switch releases and closes original input");
        mouse.Move(1, 2);
        Check(driver.Moves == 1 && original.Moves == 0, "Movement uses only selected driver");
        try { mouse.Switch(false, () => throw new Win32Exception(126)); throw new Exception("Expected failure"); }
        catch (Win32Exception) { }
        mouse.Move(3, 4);
        Check(mouse.UsesVdevice && driver.Moves == 2 && driver.Disposals == 0, "Failed opening preserves backend");
        driver.Held = true;
        var next = new Output();
        mouse.Switch(false, () => next);
        Check(!mouse.UsesVdevice && !driver.Held && driver.Disposals == 1, "Switch back releases and closes driver");
        mouse.Move(5, 6);
        Check(next.Moves == 1, "Original input restored");
        mouse.Dispose(); mouse.Dispose();
        Check(next.Disposals == 1, "Switching owner disposes once");
        bool opened = false;
        try { mouse.Switch(true, () => { opened = true; return new Output(); }); throw new Exception("Expected disposed"); }
        catch (ObjectDisposedException) { }
        Check(!opened, "Cannot create driver after shutdown");
        Check(!JsonSerializer.Deserialize<AppSettings>("{}")!.UseVdeviceDriver, "Existing settings default to original input");
        var settings = new AppSettings { UseVdeviceDriver = true };
        Check(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.UseVdeviceDriver,
            "Driver preference survives serialization");
        return checks;
    }
    private sealed class Output : IMouseOutput
    {
        public int Moves, Disposals;
        public bool Held;
        public void Move(int x, int y) => Moves++;
        public void SetLeftButton(bool pressed) => Held = pressed;
        public void Dispose() { Held = false; Disposals++; }
    }
}
