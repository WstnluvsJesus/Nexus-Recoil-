using System.ComponentModel;

namespace Nexus_Recoil.Core;

internal sealed class RecoilEngine : IDisposable
{
    // ── Recoil state ──────────────────────────────────────────────────────
    public volatile float Down       = 12f;
    public volatile float Up         = 0f;
    public volatile float Right      = 4f;
    public volatile float Left       = 0f;
    public volatile int   DelayMs    = 0;
    public volatile int   AimKeyVKey = 0x02;
    public volatile bool  IsEnabled  = false;
    public volatile float GlobalSensitivity = 1.0f;

    // ── Recoil curve ──────────────────────────────────────────────────────
    public volatile bool CurveEnabled   = false;
    public volatile int  CurveRampShots = 10;

    // ── Rapid fire state ──────────────────────────────────────────────────
    public volatile bool RapidFireEnabled       = false;
    public volatile int  RapidFireRateMs        = 60;
    public volatile bool RapidFireRequireAimKey = false;

    // ── Humanization ──────────────────────────────────────────────────────
    public volatile bool HumanizationEnabled  = false;
    public volatile int  HumanizationStrength = 30;

    // ── Burst mode ────────────────────────────────────────────────────────
    public volatile bool BurstModeEnabled = false;
    public volatile int  BurstShots       = 5;
    public volatile bool BurstAutoReset   = true;

    // ── Thread management ─────────────────────────────────────────────────
    private Thread? _recoilThread;
    private Thread? _rapidFireThread;
    private volatile bool _running = false;
    private readonly object _rapidFireLock = new();

    // ── Curve state (only accessed from RecoilLoop thread) ────────────────
    private int    _shotIndex       = 0;
    private double _curveMultiplier = 0;

    // ── Burst shot counter (persists across sprays when auto-reset is off) ─
    private volatile int _burstShotIndex = 0;

    // ── Humanization RNG ──────────────────────────────────────────────────
    private Random _humanRng = new Random();

    private readonly IMouseOutput _mouse;
    private int _failureReported;
    public event Action<string>? OutputFailed;

    public RecoilEngine(IMouseOutput? mouse = null) => _mouse = mouse ?? new WindowsMouseOutput();

    private static bool IsHeld(int vk) =>
        (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    private void RunWorker(ThreadStart worker)
    {
        try { worker(); }
        catch (Exception ex) when (ex is Win32Exception or ObjectDisposedException)
        {
            HandleOutputFailure(ex);
        }
    }

    private void HandleOutputFailure(Exception ex)
    {
        bool wasRunning = _running;
        _running = false;
        _mouse.Dispose();
        if (wasRunning)
        {
            IsEnabled = false;
            if (Interlocked.Exchange(ref _failureReported, 1) == 0)
                OutputFailed?.Invoke(ex.Message);
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    public void Start()
    {
        _running = true;

        _recoilThread = new Thread(() => RunWorker(RecoilLoop))
        {
            IsBackground = true,
            Name         = "RecoilLoop",
            Priority     = ThreadPriority.BelowNormal
        };
        _recoilThread.Start();

        _rapidFireThread = new Thread(() => RunWorker(RapidFireLoop))
        {
            IsBackground = true,
            Name         = "RapidFireLoop",
            Priority     = ThreadPriority.BelowNormal
        };
        _rapidFireThread.Start();
    }

    public void Stop()
    {
        lock (_rapidFireLock)
        {
            _running = false;
            ReleaseButton();
        }
    }

    private void ReleaseButton()
    {
        try { _mouse.SetLeftButton(false); }
        catch (Exception ex) when (ex is Win32Exception or ObjectDisposedException)
        {
            HandleOutputFailure(ex);
        }
    }

    public void Dispose()
    {
        Stop();
        // Closing the handle also releases driver state if a worker is sleeping.
        _mouse.Dispose();
    }

    // ── Recoil loop ───────────────────────────────────────────────────────

    public void ResetBurstIndex() => _burstShotIndex = 0;

    private (int x, int y) ApplyHumanization(int x, int y, int strengthPercent)
    {
        if (strengthPercent == 0) return (x, y);
        double maxOffset = (strengthPercent / 100.0) * 2.0;
        double offsetX = (_humanRng.NextDouble() * 2 - 1) * maxOffset;
        double offsetY = (_humanRng.NextDouble() * 2 - 1) * maxOffset;
        return (x + (int)Math.Round(offsetX), y + (int)Math.Round(offsetY));
    }

    private void RecoilLoop()
    {
        while (_running)
        {
            if (!IsEnabled || !IsHeld(AimKeyVKey) || !IsHeld(0x01))
            {
                _shotIndex = 0;
                Thread.Sleep(1);
                continue;
            }

            _recoilThread!.Priority = ThreadPriority.AboveNormal;
            _shotIndex       = 0;
            _curveMultiplier = 0;
            _humanRng = new Random();

            while (_running && IsEnabled && IsHeld(AimKeyVKey) && IsHeld(0x01))
            {
                if (BurstModeEnabled && _burstShotIndex >= BurstShots)
                {
                    Thread.Sleep(1);
                    continue;
                }

                // Fix 4: explicitly track and reset curve multiplier at spray start
                if (CurveEnabled && CurveRampShots > 0)
                    _curveMultiplier = (_shotIndex == 0) ? 0.0 : 1.0 - Math.Exp(-_shotIndex / (double)CurveRampShots);
                else
                    _curveMultiplier = 1.0;

                int actualDown  = (int)Math.Round(Down  * GlobalSensitivity * _curveMultiplier);
                int actualUp    = (int)Math.Round(Up    * GlobalSensitivity * _curveMultiplier);
                int actualRight = (int)Math.Round(Right * GlobalSensitivity * _curveMultiplier);
                int actualLeft  = (int)Math.Round(Left  * GlobalSensitivity * _curveMultiplier);

                var (finalX, finalY) = HumanizationEnabled
                    ? ApplyHumanization(actualRight - actualLeft, actualDown - actualUp, HumanizationStrength)
                    : (actualRight - actualLeft, actualDown - actualUp);

                // Fix 5: safety clamp — prevents wild mouse jumps from extreme settings
                finalX = Math.Clamp(finalX, -20, 20);
                finalY = Math.Clamp(finalY, -20, 20);

                _mouse.Move(finalX, finalY);

                _shotIndex++;
                _burstShotIndex++;
                Thread.Sleep(Math.Max(1, DelayMs));
            }

            if (BurstAutoReset)
                _burstShotIndex = 0;
            _shotIndex = 0;
            _recoilThread!.Priority = ThreadPriority.BelowNormal;
        }
    }

    // ── Rapid fire loop ───────────────────────────────────────────────────

    private void RapidFireLoop()
    {
        while (_running)
        {
            if (!IsEnabled || !RapidFireEnabled || !IsHeld(0x01) ||
                (RapidFireRequireAimKey && !IsHeld(AimKeyVKey)))
            {
                Thread.Sleep(1);
                continue;
            }

            _rapidFireThread!.Priority = ThreadPriority.Normal;

            try
            {
                while (_running && IsEnabled && RapidFireEnabled && IsHeld(0x01) &&
                       (!RapidFireRequireAimKey || IsHeld(AimKeyVKey)))
                {
                    lock (_rapidFireLock)
                    {
                        // Stop must not release the button just before another press.
                        if (!_running) break;
                        _mouse.SetLeftButton(false);
                        _mouse.SetLeftButton(true);
                    }
                    Thread.Sleep(Math.Max(10, RapidFireRateMs));
                }
            }
            finally
            {
                lock (_rapidFireLock)
                    ReleaseButton();
                _rapidFireThread!.Priority = ThreadPriority.BelowNormal;
            }
        }
    }
}
