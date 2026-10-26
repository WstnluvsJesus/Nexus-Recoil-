using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Nexus_Recoil.Core;

namespace Nexus_Recoil;

public partial class WatermarkOverlay : Window
{
    private System.Windows.Threading.DispatcherTimer? _topmostTimer;

    public WatermarkOverlay()
    {
        InitializeComponent();
        Loaded      += (_, _) => { PositionWatermark(); StartTopmostTimer(); ForceTopmost(); };
        SizeChanged += (_, _) => PositionWatermark();
    }

    public void ForceTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // Set as tool window so Windows never hides it on focus changes
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);

        // Apply always-on-top via SetWindowPos for reliable topmost
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        // Always exclude from screen capture
        NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
    }

    private void StartTopmostTimer()
    {
        _topmostTimer = new System.Windows.Threading.DispatcherTimer();
        _topmostTimer.Interval = TimeSpan.FromMilliseconds(500);
        _topmostTimer.Tick += (s, e) => ForceTopmost();
        _topmostTimer.Start();
    }

    private void PositionWatermark()
    {
        // Get the physical→logical scale for this monitor (handles per-monitor DPI)
        var source      = PresentationSource.FromVisual(this);
        double dpiScale = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;

        // PrimaryScreenWidth is in system-DPI logical units; multiply by per-monitor
        // dpiScale (96 / monitorDPI) to get the width in this window's WPF unit space.
        double screenWidth = SystemParameters.PrimaryScreenWidth * dpiScale;
        Left = screenWidth - ActualWidth - 20;
        Top  = 20;
    }

    public void UpdateStatus(bool active)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => UpdateStatus(active)); return; }
        WmStatusText.Text       = active ? "Status: ACTIVE"   : "Status: INACTIVE";
        WmStatusText.Foreground = active
            ? new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e))
            : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    }
}
