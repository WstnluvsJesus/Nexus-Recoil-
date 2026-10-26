using System.ComponentModel;

namespace Nexus_Recoil.Core;

// Serializes backend replacement with both output workers and shutdown.
internal sealed class SwitchableMouseOutput : IMouseOutput
{
    private readonly object _sync = new();
    private IMouseOutput? _current;
    public bool UsesVdevice { get; private set; }

    public SwitchableMouseOutput(IMouseOutput current, bool usesVdevice)
    {
        _current = current;
        UsesVdevice = usesVdevice;
    }

    internal void Switch(bool useVdevice, Func<IMouseOutput> create)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_current is null, this);
            // Opening can fail; keep the existing backend until the replacement is ready.
            var next = create();
            try
            {
                try { _current.SetLeftButton(false); }
                catch (Win32Exception) { /* Disposal also releases the old device. */ }
                _current.Dispose();
            }
            catch
            {
                next.Dispose();
                throw;
            }
            _current = next;
            UsesVdevice = useVdevice;
        }
    }

    public void Move(int x, int y)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_current is null, this);
            _current.Move(x, y);
        }
    }

    public void SetLeftButton(bool pressed)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_current is null, this);
            _current.SetLeftButton(pressed);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            var current = _current;
            _current = null;
            current?.Dispose();
        }
    }
}
