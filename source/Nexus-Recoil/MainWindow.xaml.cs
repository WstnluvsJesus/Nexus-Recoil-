using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Nexus_Recoil.Core;
using Nexus_Recoil.Models;

namespace Nexus_Recoil;

public partial class MainWindow : Window
{
    // ── State ─────────────────────────────────────────────────────────────
    private readonly AppSettings  _settings;
    private readonly RecoilEngine _engine = new();

    private List<Profile> _customProfiles = new();
    private int _listeningTarget = -1;  // 0=toggle, 1=enable, 2=disable, 3=next, 4=prev

    private static readonly int[] AimKeyVKs = { 0x02, 0x01, 0x04, 0x05, 0x06 };

    // ── Quick-swap ────────────────────────────────────────────────────────
    private IntPtr      _mainHwnd;
    private HwndSource? _hwndSource;

    // ── Watermark ─────────────────────────────────────────────────────────
    private WatermarkOverlay? _watermarkWindow;

    // ── Sound ─────────────────────────────────────────────────────────────
    private SoundManager? _soundManager;

    // ── Update checker ────────────────────────────────────────────────────
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    // ── Tooltips ──────────────────────────────────────────────────────────
    private static bool _tooltipsEnabled = true;

    static MainWindow()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "NexusRecoil/1.0.5");

        // Block tooltip display globally when tooltips are disabled
        EventManager.RegisterClassHandler(
            typeof(System.Windows.FrameworkElement),
            ToolTipService.ToolTipOpeningEvent,
            new ToolTipEventHandler((_, e) => { if (!_tooltipsEnabled) e.Handled = true; }));
    }

    // ── Construction ──────────────────────────────────────────────────────

    internal MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        Left = settings.WindowLeft;
        Top  = settings.WindowTop;

        // Preserve positions on secondary monitors, including negative coordinates.
        double screenLeft = SystemParameters.VirtualScreenLeft;
        double screenTop = SystemParameters.VirtualScreenTop;
        double screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        double screenBottom = screenTop + SystemParameters.VirtualScreenHeight;
        if (settings.WindowLeft < screenLeft || settings.WindowLeft >= screenRight ||
            settings.WindowTop < screenTop || settings.WindowTop >= screenBottom)
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top  = (SystemParameters.PrimaryScreenHeight - Height) / 2;
        }

        ApplyTheme(settings);
        LoadSettingsIntoUi();
        LoadPresets();

        _engine.Start();

        _ = CheckForUpdatesAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _mainHwnd   = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_mainHwnd);

        // Prevent window from stealing focus from a fullscreen game
        int exStyle = NativeMethods.GetWindowLong(_mainHwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_mainHwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

        HotkeyManager.ToggleUiPressed   += OnToggleUi;
        HotkeyManager.EnablePressed     += OnEnable;
        HotkeyManager.DisablePressed    += OnDisable;
        HotkeyManager.CyclePresetPressed += CyclePreset;
        HotkeyManager.RegisterAll(_settings.ToggleUiKey, _settings.EnableKey, _settings.DisableKey);
        HotkeyManager.RegisterQuickSwap(_settings.NextPresetKey, _settings.PrevPresetKey,
            _settings.QuickSwapEnabled);

        _hwndSource?.AddHook(QuickSwapWndProc);

        if (_settings.StreamProof)
            try { NativeMethods.SetWindowDisplayAffinity(_mainHwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE); }
            catch { /* unsupported on this OS version */ }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hwndSource?.RemoveHook(QuickSwapWndProc);
        _engine.Stop();
        _watermarkWindow?.Close();
        _soundManager?.Dispose();
        base.OnClosed(e);
    }

    // ── Settings → UI ─────────────────────────────────────────────────────

    private void LoadSettingsIntoUi()
    {
        DownSlider.Value  = 12;
        UpSlider.Value    = 0;
        RightSlider.Value = 4;
        LeftSlider.Value  = 0;

        AimKeyCombo.SelectedIndex = Math.Clamp(_settings.AimKeyIndex, 0, 4);
        DelaySlider.Value         = Math.Clamp(_settings.DelayMs, 0, 100);

        SensSlider.Value = Math.Clamp(_settings.GlobalSensitivity, 0.0, 2.0);

        // Rapid fire
        RapidFireToggle.IsChecked      = _settings.RapidFireEnabled;
        _engine.RapidFireEnabled       = _settings.RapidFireEnabled;
        FireRateSlider.Value           = Math.Clamp(_settings.RapidFireRateMs, 10, 150);
        _engine.RapidFireRateMs        = _settings.RapidFireRateMs;
        RapidFireAimKeyChk.IsChecked   = _settings.RapidFireRequireAimKey;
        _engine.RapidFireRequireAimKey = _settings.RapidFireRequireAimKey;

        // Burst mode
        BurstModeToggle.IsChecked   = _settings.BurstModeEnabled;
        _engine.BurstModeEnabled    = _settings.BurstModeEnabled;
        BurstShotsSlider.Value      = Math.Clamp(_settings.BurstShots, 1, 15);
        _engine.BurstShots          = _settings.BurstShots;
        BurstAutoResetChk.IsChecked = _settings.BurstAutoReset;
        _engine.BurstAutoReset      = _settings.BurstAutoReset;
        BurstModePanel.Visibility   = _settings.BurstModeEnabled ? Visibility.Visible : Visibility.Collapsed;

        // Quick-swap
        QuickSwapToggle.IsChecked          = _settings.QuickSwapEnabled;
        QuickSwapPillsPanel.Visibility     = _settings.QuickSwapEnabled ? Visibility.Visible : Visibility.Collapsed;
        NextPresetKeyBtn.Content           = VkToName(_settings.NextPresetKey);
        PrevPresetKeyBtn.Content           = VkToName(_settings.PrevPresetKey);

        // Stream proof
        StreamProofToggle.IsChecked = _settings.StreamProof;

        // Recoil curve
        CurveToggle.IsChecked     = _settings.CurveEnabled;
        _engine.CurveEnabled      = _settings.CurveEnabled;
        RampSlider.Value          = Math.Clamp(_settings.CurveRampShots, 1, 30);
        _engine.CurveRampShots    = _settings.CurveRampShots;
        CurveSectionPanel.Visibility = _settings.CurveEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.CurveEnabled)
            UpdateCurveGraph(_settings.CurveRampShots);

        // Humanization
        HumanizationToggle.IsChecked = _settings.HumanizationEnabled;
        _engine.HumanizationEnabled  = _settings.HumanizationEnabled;
        HumanStrengthSlider.Value    = Math.Clamp(_settings.HumanizationStrength, 0, 100);
        _engine.HumanizationStrength = _settings.HumanizationStrength;
        HumanizationPanel.Visibility = _settings.HumanizationEnabled ? Visibility.Visible : Visibility.Collapsed;

        // Watermark
        WatermarkToggle.IsChecked = _settings.WatermarkEnabled;
        if (_settings.WatermarkEnabled)
            ShowWatermark();

        // Tooltips
        TooltipsToggle.IsChecked = _settings.TooltipsEnabled;
        _tooltipsEnabled         = _settings.TooltipsEnabled;

        // Sound effects
        if (_settings.CustomSoundPath != null && !File.Exists(_settings.CustomSoundPath))
        {
            _settings.CustomSoundPath = null;
            _settings.Save();
        }
        _soundManager = new SoundManager(_settings.SoundEnabled, _settings.SoundVolume / 100f, _settings.CustomSoundPath);
        bool soundEnabled = _settings.SoundEnabled && SoundManager.HasAudioDevice;
        _soundManager.Enabled = soundEnabled;
        if (!SoundManager.HasAudioDevice)
        {
            NoAudioDeviceLabel.Visibility = System.Windows.Visibility.Visible;
        }
        SoundToggle.IsChecked       = soundEnabled;
        SoundSubPanel.Visibility    = soundEnabled ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        SoundVolumeSlider.Value     = Math.Clamp(_settings.SoundVolume, 0, 100);
        if (_settings.CustomSoundPath != null)
        {
            var fname = Path.GetFileName(_settings.CustomSoundPath);
            if (fname.Length > 20) fname = fname[..20] + "…";
            CustomSoundNameLabel.Text       = fname;
            CustomSoundNameLabel.Visibility = System.Windows.Visibility.Visible;
            RemoveCustomSoundBtn.Visibility = System.Windows.Visibility.Visible;
        }

        // Hotkey buttons
        ToggleKeyBtn.Content  = VkToName(_settings.ToggleUiKey);
        EnableKeyBtn.Content  = VkToName(_settings.EnableKey);
        DisableKeyBtn.Content = VkToName(_settings.DisableKey);

        // Theme color pickers
        _suppressColorEvents = true;
        AccentPicker.SelectedColor   = ColorFromHex(_settings.ThemeAccent);
        BgPicker.SelectedColor       = ColorFromHex(_settings.ThemeBg);
        SurfacePicker.SelectedColor  = ColorFromHex(_settings.ThemeSurface);
        TextPrimPicker.SelectedColor = ColorFromHex(_settings.ThemeTextPrim);
        TextSecPicker.SelectedColor  = ColorFromHex(_settings.ThemeTextSec);
        _suppressColorEvents = false;
    }

    // ── Theme ─────────────────────────────────────────────────────────────

    private bool _suppressColorEvents = false;

    private void ApplyTheme(AppSettings s)
    {
        SetBrush("AccentBrush",   s.ThemeAccent);
        SetBrush("BgBrush",       s.ThemeBg);
        SetBrush("SurfaceBrush",  s.ThemeSurface);
        SetBrush("TextPrimBrush", s.ThemeTextPrim);
        SetBrush("TextSecBrush",  s.ThemeTextSec);
    }

    private static void SetBrush(string key, string hex) =>
        Application.Current.Resources[key] = new SolidColorBrush(ColorFromHex(hex));

    private static Color ColorFromHex(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Gray; }
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void AccentPicker_SelectedColorChanged(object sender,
        RoutedPropertyChangedEventArgs<Color?> e)
    {
        if (_suppressColorEvents || e.NewValue is not Color c) return;
        var hex = ToHex(c);
        _settings.ThemeAccent = hex;
        SetBrush("AccentBrush", hex);
        _settings.Save();
    }

    private void BgPicker_SelectedColorChanged(object sender,
        RoutedPropertyChangedEventArgs<Color?> e)
    {
        if (_suppressColorEvents || e.NewValue is not Color c) return;
        var hex = ToHex(c);
        _settings.ThemeBg = hex;
        SetBrush("BgBrush", hex);
        _settings.Save();
    }

    private void SurfacePicker_SelectedColorChanged(object sender,
        RoutedPropertyChangedEventArgs<Color?> e)
    {
        if (_suppressColorEvents || e.NewValue is not Color c) return;
        var hex = ToHex(c);
        _settings.ThemeSurface = hex;
        SetBrush("SurfaceBrush", hex);
        _settings.Save();
    }

    private void TextPrimPicker_SelectedColorChanged(object sender,
        RoutedPropertyChangedEventArgs<Color?> e)
    {
        if (_suppressColorEvents || e.NewValue is not Color c) return;
        var hex = ToHex(c);
        _settings.ThemeTextPrim = hex;
        SetBrush("TextPrimBrush", hex);
        _settings.Save();
    }

    private void TextSecPicker_SelectedColorChanged(object sender,
        RoutedPropertyChangedEventArgs<Color?> e)
    {
        if (_suppressColorEvents || e.NewValue is not Color c) return;
        var hex = ToHex(c);
        _settings.ThemeTextSec = hex;
        SetBrush("TextSecBrush", hex);
        _settings.Save();
    }

    private void ThemeResetOne_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var defaults = new AppSettings();
        _suppressColorEvents = true;
        switch (btn.Tag as string)
        {
            case "Accent":
                _settings.ThemeAccent = defaults.ThemeAccent;
                AccentPicker.SelectedColor = ColorFromHex(defaults.ThemeAccent);
                SetBrush("AccentBrush", defaults.ThemeAccent);
                break;
            case "Bg":
                _settings.ThemeBg = defaults.ThemeBg;
                BgPicker.SelectedColor = ColorFromHex(defaults.ThemeBg);
                SetBrush("BgBrush", defaults.ThemeBg);
                break;
            case "Surface":
                _settings.ThemeSurface = defaults.ThemeSurface;
                SurfacePicker.SelectedColor = ColorFromHex(defaults.ThemeSurface);
                SetBrush("SurfaceBrush", defaults.ThemeSurface);
                break;
            case "TextPrim":
                _settings.ThemeTextPrim = defaults.ThemeTextPrim;
                TextPrimPicker.SelectedColor = ColorFromHex(defaults.ThemeTextPrim);
                SetBrush("TextPrimBrush", defaults.ThemeTextPrim);
                break;
            case "TextSec":
                _settings.ThemeTextSec = defaults.ThemeTextSec;
                TextSecPicker.SelectedColor = ColorFromHex(defaults.ThemeTextSec);
                SetBrush("TextSecBrush", defaults.ThemeTextSec);
                break;
        }
        _suppressColorEvents = false;
        _settings.Save();
    }

    private void ThemeResetAll_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        _suppressColorEvents = true;
        _settings.ThemeAccent   = defaults.ThemeAccent;
        _settings.ThemeBg       = defaults.ThemeBg;
        _settings.ThemeSurface  = defaults.ThemeSurface;
        _settings.ThemeTextPrim = defaults.ThemeTextPrim;
        _settings.ThemeTextSec  = defaults.ThemeTextSec;
        AccentPicker.SelectedColor   = ColorFromHex(defaults.ThemeAccent);
        BgPicker.SelectedColor       = ColorFromHex(defaults.ThemeBg);
        SurfacePicker.SelectedColor  = ColorFromHex(defaults.ThemeSurface);
        TextPrimPicker.SelectedColor = ColorFromHex(defaults.ThemeTextPrim);
        TextSecPicker.SelectedColor  = ColorFromHex(defaults.ThemeTextSec);
        _suppressColorEvents = false;
        ApplyTheme(_settings);
        _settings.Save();
    }

    // ── Update checker ────────────────────────────────────────────────────

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var json = await Task.Run(async () =>
            {
                var resp = await _http.GetAsync(
                    "https://api.github.com/repos/n1k0fr/Nexus-Recoil/releases/latest")
                    .ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

            using var doc     = JsonDocument.Parse(json);
            var tagName       = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var body          = doc.RootElement.TryGetProperty("body", out var bp) ? bp.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(tagName) || tagName == _settings.CurrentVersion)
                return;

            var isNewToUser = tagName != _settings.LastSeenVersion;
            if (isNewToUser)
            {
                _settings.LastSeenVersion = tagName;
                _settings.Save();
            }

            Dispatcher.Invoke(() =>
            {
                ShowUpdateBanner(tagName);
                if (isNewToUser)
                    new ChangelogWindow(tagName, body).Show();
            });
        }
        catch { /* best-effort */ }
    }

    private void ShowUpdateBanner(string tagName)
    {
        UpdateText.Text         = $"Update available: {tagName} — click to download";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void UpdateBanner_Click(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo(
            "https://github.com/n1k0fr/Nexus-Recoil/releases/latest")
            { UseShellExecute = true });
    }

    private void UpdateBannerDismiss_Click(object sender, RoutedEventArgs e)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    // ── Quick-swap hotkeys ────────────────────────────────────────────────

    private void RegisterQuickSwapHotkeys()
    {
        HotkeyManager.RegisterQuickSwap(_settings.NextPresetKey, _settings.PrevPresetKey, true);
    }

    private void UnregisterQuickSwapHotkeys()
    {
        HotkeyManager.RegisterQuickSwap(_settings.NextPresetKey, _settings.PrevPresetKey, false);
    }

    private IntPtr QuickSwapWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_NOACTIVATE    = 0x0003;
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    private void CyclePreset(int direction)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => CyclePreset(direction)); return; }

        var combo = GetActiveCombo();
        if (combo == null || combo.Items.Count == 0) return;

        var selectableIndices = new List<int>();
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is PresetItem pi && !pi.IsHeader) selectableIndices.Add(i);
            else if (combo.Items[i] is Profile)                  selectableIndices.Add(i);
        }
        if (selectableIndices.Count == 0) return;

        int pos = selectableIndices.IndexOf(combo.SelectedIndex);
        if (pos < 0) pos = 0;
        else pos = (pos + direction + selectableIndices.Count) % selectableIndices.Count;

        combo.SelectedIndex = selectableIndices[pos];
        LoadBtn_Click(this, new RoutedEventArgs());

        var profile = GetSelectedProfile();
        if (profile != null) ShowPresetToast(profile.Name);
    }

    private void QuickSwapToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = QuickSwapToggle.IsChecked == true;
        _settings.QuickSwapEnabled         = enabled;
        QuickSwapPillsPanel.Visibility      = enabled ? Visibility.Visible : Visibility.Collapsed;
        _settings.Save();

        UnregisterQuickSwapHotkeys();
        if (enabled) RegisterQuickSwapHotkeys();
    }

    private void NextPresetKeyBtn_Click(object sender, RoutedEventArgs e) => StartListening(3);
    private void PrevPresetKeyBtn_Click(object sender, RoutedEventArgs e) => StartListening(4);

    // ── Toast notification ────────────────────────────────────────────────

    private void ShowPresetToast(string presetName)
    {
        var toast = new Window
        {
            WindowStyle        = WindowStyle.None,
            AllowsTransparency = true,
            Background         = Brushes.Transparent,
            Topmost            = true,
            ShowInTaskbar      = false,
            Width              = 220,
            Height             = 44,
            ResizeMode         = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        toast.Left = SystemParameters.PrimaryScreenWidth  - 240;
        toast.Top  = SystemParameters.PrimaryScreenHeight - 80;

        var border = new Border
        {
            Background      = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)),
            BorderBrush     = (Brush)Application.Current.Resources["AccentBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(12, 0, 12, 0)
        };

        var text = new TextBlock
        {
            Text                = presetName,
            Foreground          = Brushes.White,
            FontFamily          = new FontFamily("Segoe UI"),
            FontSize            = 13,
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        border.Child = text;
        toast.Content = border;
        toast.Show();

        // Make click-through
        var hwnd = new WindowInteropHelper(toast).EnsureHandle();
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_TRANSPARENT);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            anim.Completed += (_, _) => toast.Close();
            toast.BeginAnimation(OpacityProperty, anim);
        };
        timer.Start();
    }

    // ── Stream proof ──────────────────────────────────────────────────────

    private void StreamProofToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = StreamProofToggle.IsChecked == true;
        _settings.StreamProof = enabled;
        _settings.Save();

        if (_mainHwnd != IntPtr.Zero)
        {
            try
            {
                NativeMethods.SetWindowDisplayAffinity(_mainHwnd,
                    enabled ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE);

                if (!enabled)
                    // Force DWM to recompose the window so the cleared affinity takes effect
                    NativeMethods.SetWindowPos(_mainHwnd, IntPtr.Zero, 0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
                        NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
            }
            catch { /* unsupported on this OS version */ }
        }
    }

    // ── Recoil curve ──────────────────────────────────────────────────────

    private void CurveToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = CurveToggle.IsChecked == true;
        _engine.CurveEnabled          = enabled;
        _settings.CurveEnabled        = enabled;
        CurveSectionPanel.Visibility  = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled) UpdateCurveGraph((int)RampSlider.Value);
        _settings.Save();
    }

    private void RampSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = (int)e.NewValue;
        if (RampLabel != null) RampLabel.Text = $"Ramp Shots: {v}";
        _engine.CurveRampShots       = v;
        _settings.CurveRampShots     = v;
        _settings.Save();
        UpdateCurveGraph(v);
    }

    private void UpdateCurveGraph(int rampShots)
    {
        if (CurvePolyline == null) return;
        const int shots = 40;
        const double w  = 200.0;
        const double h  = 60.0;

        var points = new System.Windows.Media.PointCollection();
        for (int i = 0; i <= shots; i++)
        {
            double mult = 1.0 - Math.Exp(-i / (double)Math.Max(1, rampShots));
            double x    = i * (w / shots);
            double y    = h - (mult * (h - 2.0));
            points.Add(new Point(x, y));
        }
        CurvePolyline.Points = points;
    }

    // ── Watermark ─────────────────────────────────────────────────────────

    private void WatermarkToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = WatermarkToggle.IsChecked == true;
        _settings.WatermarkEnabled = enabled;
        _settings.Save();
        if (enabled) ShowWatermark();
        else HideWatermark();
    }

    private void ShowWatermark()
    {
        if (_watermarkWindow == null || !_watermarkWindow.IsLoaded)
            _watermarkWindow = new WatermarkOverlay();

        _watermarkWindow.UpdateStatus(_engine.IsEnabled);
        _watermarkWindow.Show();
        _watermarkWindow.ForceTopmost();
    }

    private void HideWatermark() => _watermarkWindow?.Hide();

    // ── Sound Effects ─────────────────────────────────────────────────────

    private void SoundToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = SoundToggle.IsChecked == true;
        _settings.SoundEnabled = enabled;
        _settings.Save();
        bool soundEnabled = enabled && SoundManager.HasAudioDevice;
        if (_soundManager != null) _soundManager.Enabled = soundEnabled;
        SoundSubPanel.Visibility = soundEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SoundVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var vol = (int)e.NewValue;
        SoundVolumeLabel.Text = $"Volume: {vol}%";
        if (_soundManager != null) _soundManager.Volume = vol / 100f;
        _settings.SoundVolume = vol;
        _settings.Save();
    }

    private void CustomSoundBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Audio Files|*.wav;*.mp3",
            Title  = "Select a sound file"
        };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FileName;
        _settings.CustomSoundPath = path;
        _settings.Save();
        if (_soundManager != null) _soundManager.CustomSoundPath = path;

        var fname = Path.GetFileName(path);
        if (fname.Length > 20) fname = fname[..20] + "…";
        CustomSoundNameLabel.Text       = fname;
        CustomSoundNameLabel.Visibility = Visibility.Visible;
        RemoveCustomSoundBtn.Visibility = Visibility.Visible;
    }

    private void RemoveCustomSoundBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.CustomSoundPath = null;
        _settings.Save();
        if (_soundManager != null) _soundManager.CustomSoundPath = null;
        CustomSoundNameLabel.Visibility = Visibility.Collapsed;
        RemoveCustomSoundBtn.Visibility = Visibility.Collapsed;
    }

    private void SoundTestBtn_Click(object sender, RoutedEventArgs e) =>
        _soundManager?.PlayPreview();

    // ── Import / Export ───────────────────────────────────────────────────

    private void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        var p = GetSelectedProfile();
        if (p == null || p.IsBuiltIn) return;

        var dlg = new SaveFileDialog
        {
            Filter   = "JSON files (*.json)|*.json",
            FileName = p.Name
        };
        if (dlg.ShowDialog() != true) return;

        var export = new
        {
            name  = p.Name,
            down  = p.Down,
            up    = p.Up,
            right = p.Right,
            left  = p.Left
        };

        File.WriteAllText(dlg.FileName,
            JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ImportBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "JSON files (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("name",  out var nameProp)  ||
                !root.TryGetProperty("down",  out var downProp)  ||
                !root.TryGetProperty("up",    out var upProp)    ||
                !root.TryGetProperty("right", out var rightProp) ||
                !root.TryGetProperty("left",  out var leftProp))
            {
                MessageBox.Show("Invalid preset file.", "Import Failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var name  = nameProp.GetString() ?? "";
            var down  = downProp.GetDouble();
            var up    = upProp.GetDouble();
            var right = rightProp.GetDouble();
            var left  = leftProp.GetDouble();

            var existing = _customProfiles.FirstOrDefault(p => p.Name == name);
            if (existing != null)
            {
                var result = MessageBox.Show(
                    $"A preset named \"{name}\" already exists. Overwrite it?",
                    "Confirm Overwrite",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                existing.Down  = down;
                existing.Up    = up;
                existing.Right = right;
                existing.Left  = left;
                ProfileManager.SaveCustomProfiles(_customProfiles);
                RefreshCustomCombo();
                CustomCombo.SelectedItem = existing;
            }
            else
            {
                var p = new Profile
                {
                    Name      = name,
                    Down      = down,
                    Up        = up,
                    Right     = right,
                    Left      = left,
                    Side      = "Custom",
                    IsBuiltIn = false
                };
                _customProfiles.Add(p);
                ProfileManager.SaveCustomProfiles(_customProfiles);
                RefreshCustomCombo();
                PresetTabs.SelectedIndex = 2;
                CustomCombo.SelectedItem = p;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            MessageBox.Show("Invalid preset file.", "Import Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Presets ───────────────────────────────────────────────────────────

    private void LoadPresets()
    {
        AttackerCombo.ItemsSource = ProfileManager.GetAttackerItems();
        DefenderCombo.ItemsSource = ProfileManager.GetDefenderItems();

        _customProfiles = ProfileManager.LoadCustomProfiles();
        RefreshCustomCombo();

        RestoreLastSelection();
    }

    private void RestoreLastSelection()
    {
        var side = _settings.LastProfileSide;
        var name = _settings.LastProfileName;

        if (side == "Defender")
        {
            PresetTabs.SelectedIndex = 1;
            SelectComboByName(DefenderCombo, name);
        }
        else if (side == "Custom")
        {
            PresetTabs.SelectedIndex = 2;
            SelectCustomComboByName(name);
        }
        else
        {
            PresetTabs.SelectedIndex = 0;
            SelectComboByName(AttackerCombo, name);
        }
    }

    private void SelectComboByName(ComboBox combo, string name)
    {
        foreach (var item in combo.Items.OfType<PresetItem>())
        {
            if (!item.IsHeader && item.DisplayName == name)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        SelectFirstNonHeader(combo);
    }

    private void SelectFirstNonHeader(ComboBox combo)
    {
        foreach (var item in combo.Items.OfType<PresetItem>())
        {
            if (!item.IsHeader) { combo.SelectedItem = item; return; }
        }
    }

    private void SelectCustomComboByName(string name)
    {
        var profile = _customProfiles.FirstOrDefault(p => p.Name == name);
        if (profile != null) CustomCombo.SelectedItem = profile;
        else if (_customProfiles.Count > 0) CustomCombo.SelectedIndex = 0;
    }

    private void RefreshCustomCombo()
    {
        CustomCombo.ItemsSource = null;
        CustomCombo.ItemsSource = _customProfiles;
    }

    // ── Hotkey handlers ───────────────────────────────────────────────────

    private void OnToggleUi()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OnToggleUi); return; }
        if (Visibility == Visibility.Visible && Opacity > 0.5) FadeOut();
        else FadeIn();
    }

    private void OnEnable()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OnEnable); return; }
        _soundManager?.PlayActivate();
        SetActive(true);
    }

    private void OnDisable()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OnDisable); return; }
        _soundManager?.PlayDeactivate();
        SetActive(false);
    }

    private void SetActive(bool active)
    {
        _engine.IsEnabled     = active;
        StatusText.Text       = active ? "ACTIVE"   : "INACTIVE";
        StatusText.Foreground = active
            ? new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e))
            : new SolidColorBrush(Color.FromRgb(0xe0, 0x30, 0x30));
        _watermarkWindow?.UpdateStatus(active);
    }

    // ── Fade animation ────────────────────────────────────────────────────

    private void FadeIn()
    {
        Opacity    = 0;
        Visibility = Visibility.Visible;
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        BeginAnimation(OpacityProperty, anim);
    }

    private void FadeOut()
    {
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        anim.Completed += (_, _) => Visibility = Visibility.Hidden;
        BeginAnimation(OpacityProperty, anim);
    }

    // ── Header / close ────────────────────────────────────────────────────

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_listeningTarget >= 0) return;
        DragMove();
        _settings.WindowLeft = Left;
        _settings.WindowTop  = Top;
        _settings.Save();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        Application.Current.Shutdown();

    // ── Recoil sliders ────────────────────────────────────────────────────

    private void DownSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = e.NewValue;
        if (DownBox != null) DownBox.Text = v.ToString("F2");
        _engine.Down = (float)v;
    }

    private void UpSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = e.NewValue;
        if (UpBox != null) UpBox.Text = v.ToString("F2");
        _engine.Up = (float)v;
    }

    private void RightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = e.NewValue;
        if (RightBox != null) RightBox.Text = v.ToString("F2");
        _engine.Right = (float)v;
    }

    private void LeftSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = e.NewValue;
        if (LeftBox != null) LeftBox.Text = v.ToString("F2");
        _engine.Left = (float)v;
    }

    private void RecoilBox_GotFocus(object sender, RoutedEventArgs e)
    {
        ((TextBox)sender).SelectAll();
        // Temporarily allow window activation so keyboard reaches the TextBox
        if (_mainHwnd != IntPtr.Zero)
        {
            int ex = NativeMethods.GetWindowLong(_mainHwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(_mainHwnd, NativeMethods.GWL_EXSTYLE,
                ex & ~NativeMethods.WS_EX_NOACTIVATE);
            NativeMethods.SetForegroundWindow(_mainHwnd);
        }
    }

    private void RestoreNoActivate()
    {
        if (_mainHwnd != IntPtr.Zero)
        {
            int ex = NativeMethods.GetWindowLong(_mainHwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(_mainHwnd, NativeMethods.GWL_EXSTYLE,
                ex | NativeMethods.WS_EX_NOACTIVATE);
        }
    }

    private void DownBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(DownBox.Text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v))
        {
            v = Math.Clamp(v, 0, 50);
            DownSlider.Value = v;
            DownBox.Text     = v.ToString("F2");
            _engine.Down     = (float)v;
        }
        else
        {
            DownBox.Text = DownSlider.Value.ToString("F2");
        }
        RestoreNoActivate();
    }

    private void UpBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(UpBox.Text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v))
        {
            v = Math.Clamp(v, 0, 50);
            UpSlider.Value = v;
            UpBox.Text     = v.ToString("F2");
            _engine.Up     = (float)v;
        }
        else
        {
            UpBox.Text = UpSlider.Value.ToString("F2");
        }
        RestoreNoActivate();
    }

    private void RightBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(RightBox.Text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v))
        {
            v = Math.Clamp(v, 0, 50);
            RightSlider.Value = v;
            RightBox.Text     = v.ToString("F2");
            _engine.Right     = (float)v;
        }
        else
        {
            RightBox.Text = RightSlider.Value.ToString("F2");
        }
        RestoreNoActivate();
    }

    private void LeftBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(LeftBox.Text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v))
        {
            v = Math.Clamp(v, 0, 50);
            LeftSlider.Value = v;
            LeftBox.Text     = v.ToString("F2");
            _engine.Left     = (float)v;
        }
        else
        {
            LeftBox.Text = LeftSlider.Value.ToString("F2");
        }
        RestoreNoActivate();
    }

    private void SensBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(SensBox.Text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v))
        {
            v = Math.Clamp(v, 0.0, 2.0);
            SensSlider.Value            = v;
            SensBox.Text                = v.ToString("F2");
            _engine.GlobalSensitivity   = (float)v;
            _settings.GlobalSensitivity = v;
            _settings.Save();
        }
        else
        {
            SensBox.Text = SensSlider.Value.ToString("F2");
        }
        RestoreNoActivate();
    }

    private void DelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = (int)e.NewValue;
        if (DelayLabel != null) DelayLabel.Text = $"Delay: {v}ms";
        _engine.DelayMs  = v;
        _settings.DelayMs = v;
        _settings.Save();
    }

    private void SensSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = e.NewValue;
        if (SensLabel != null) SensLabel.Text = $"Sensitivity: {v:F2}x";
        if (SensBox  != null) SensBox.Text    = v.ToString("F2");
        _engine.GlobalSensitivity    = (float)v;
        _settings.GlobalSensitivity  = v;
        _settings.Save();
    }

    private void AimKeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = AimKeyCombo.SelectedIndex;
        if (idx < 0 || idx >= AimKeyVKs.Length) return;
        _engine.AimKeyVKey    = AimKeyVKs[idx];
        _settings.AimKeyIndex = idx;
        _settings.Save();
    }

    // ── Rapid fire controls ───────────────────────────────────────────────

    private void RapidFireToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = RapidFireToggle.IsChecked == true;
        _engine.RapidFireEnabled   = enabled;
        _settings.RapidFireEnabled = enabled;
        _settings.Save();
    }

    private void FireRateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = (int)e.NewValue;
        if (FireRateLabel != null) FireRateLabel.Text = $"Fire Rate: {v}ms";
        _engine.RapidFireRateMs    = v;
        _settings.RapidFireRateMs  = v;
        _settings.Save();
    }

    private void RapidFireAimKeyChk_Click(object sender, RoutedEventArgs e)
    {
        var req = RapidFireAimKeyChk.IsChecked == true;
        _engine.RapidFireRequireAimKey   = req;
        _settings.RapidFireRequireAimKey = req;
        _settings.Save();
    }

    // ── Humanization controls ─────────────────────────────────────────────

    private void HumanizationToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = HumanizationToggle.IsChecked == true;
        _engine.HumanizationEnabled      = enabled;
        _settings.HumanizationEnabled    = enabled;
        HumanizationPanel.Visibility     = enabled ? Visibility.Visible : Visibility.Collapsed;
        _settings.Save();
    }

    private void HumanStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = (int)e.NewValue;
        if (HumanStrengthLabel != null) HumanStrengthLabel.Text = $"Strength: {v}%";
        _engine.HumanizationStrength    = v;
        _settings.HumanizationStrength  = v;
        _settings.Save();
    }

    // ── Burst mode controls ───────────────────────────────────────────────

    private void BurstModeToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = BurstModeToggle.IsChecked == true;
        _engine.BurstModeEnabled       = enabled;
        _settings.BurstModeEnabled     = enabled;
        BurstModePanel.Visibility      = enabled ? Visibility.Visible : Visibility.Collapsed;
        _settings.Save();
    }

    private void BurstShotsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var v = (int)e.NewValue;
        if (BurstShotsLabel != null) BurstShotsLabel.Text = $"Shots: {v}";
        _engine.BurstShots    = v;
        _settings.BurstShots  = v;
        _settings.Save();
    }

    private void BurstAutoResetChk_Click(object sender, RoutedEventArgs e)
    {
        var autoReset = BurstAutoResetChk.IsChecked == true;
        _engine.BurstAutoReset    = autoReset;
        _settings.BurstAutoReset  = autoReset;
        _settings.Save();
    }

    private void BurstResetBtn_Click(object sender, RoutedEventArgs e)
    {
        _engine.ResetBurstIndex();
    }

    // ── Tooltips ──────────────────────────────────────────────────────────

    private void TooltipsToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = TooltipsToggle.IsChecked == true;
        _tooltipsEnabled          = enabled;
        _settings.TooltipsEnabled = enabled;
        _settings.Save();
    }

    // ── Preset tab / combo events ─────────────────────────────────────────

    private void PresetTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Ignore SelectionChanged events that bubble up from child ComboBoxes
        if (!ReferenceEquals(e.Source, PresetTabs)) return;

        UpdatePresetButtons();
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePresetButtons();
        SaveLastPresetSelection();
    }

    private void UpdatePresetButtons()
    {
        var profile = GetSelectedProfile();
        bool isCustom = profile != null && !profile.IsBuiltIn;
        if (SaveBtn   != null) SaveBtn.IsEnabled   = isCustom;
        if (DeleteBtn != null) DeleteBtn.IsEnabled = isCustom;
        if (ExportBtn != null) ExportBtn.IsEnabled = isCustom;
        if (LoadBtn   != null) LoadBtn.IsEnabled   = profile != null;
    }

    private Profile? GetSelectedProfile()
    {
        if (PresetTabs == null) return null;
        return PresetTabs.SelectedIndex switch
        {
            0 => (AttackerCombo?.SelectedItem as PresetItem)?.Profile,
            1 => (DefenderCombo?.SelectedItem as PresetItem)?.Profile,
            2 => CustomCombo?.SelectedItem as Profile,
            _ => null
        };
    }

    private ComboBox? GetActiveCombo() => PresetTabs?.SelectedIndex switch
    {
        0 => AttackerCombo,
        1 => DefenderCombo,
        2 => CustomCombo,
        _ => null
    };

    private void SaveLastPresetSelection()
    {
        var profile = GetSelectedProfile();
        if (profile == null) return;
        _settings.LastProfileName = profile.Name;
        _settings.LastProfileSide = profile.Side;
        _settings.Save();
    }

    // ── Preset buttons ────────────────────────────────────────────────────

    private void LoadBtn_Click(object sender, RoutedEventArgs e)
    {
        var p = GetSelectedProfile();
        if (p == null) return;
        DownSlider.Value  = p.Down;
        UpSlider.Value    = p.Up;
        RightSlider.Value = p.Right;
        LeftSlider.Value  = p.Left;
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var p = GetSelectedProfile();
        if (p == null || p.IsBuiltIn) return;
        p.Down  = DownSlider.Value;
        p.Up    = UpSlider.Value;
        p.Right = RightSlider.Value;
        p.Left  = LeftSlider.Value;
        ProfileManager.SaveCustomProfiles(_customProfiles);
    }

    private void NewBtn_Click(object sender, RoutedEventArgs e)
    {
        var name = ShowInputDialog("New Preset", "Enter preset name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        var p = new Profile
        {
            Name      = name.Trim(),
            Down      = DownSlider.Value,
            Up        = UpSlider.Value,
            Right     = RightSlider.Value,
            Left      = LeftSlider.Value,
            Side      = "Custom",
            IsBuiltIn = false
        };
        _customProfiles.Add(p);
        ProfileManager.SaveCustomProfiles(_customProfiles);
        RefreshCustomCombo();
        PresetTabs.SelectedIndex = 2;
        CustomCombo.SelectedItem = p;
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        var p = GetSelectedProfile();
        if (p == null || p.IsBuiltIn) return;
        _customProfiles.Remove(p);
        ProfileManager.SaveCustomProfiles(_customProfiles);
        RefreshCustomCombo();
        if (_customProfiles.Count > 0) CustomCombo.SelectedIndex = 0;
        UpdatePresetButtons();
    }

    // ── Hotkey rebinding ──────────────────────────────────────────────────

    private void ToggleKeyBtn_Click(object sender, RoutedEventArgs e)  => StartListening(0);
    private void EnableKeyBtn_Click(object sender, RoutedEventArgs e)  => StartListening(1);
    private void DisableKeyBtn_Click(object sender, RoutedEventArgs e) => StartListening(2);

    private void StartListening(int target)
    {
        if (_listeningTarget >= 0) CancelListening();
        _listeningTarget = target;
        HotkeyManager.IsListening = true;

        var btn = GetHotkeyButton(target);
        if (btn != null)
        {
            btn.Content     = "Press any key...";
            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3b, 0x82, 0xf6));
        }

        HotkeyManager.KeyCaptured += OnKeyCapture;
    }

    private void StopListening()
    {
        _listeningTarget = -1;
        HotkeyManager.KeyCaptured -= OnKeyCapture;

        RestoreHotkeyButtonStyle(0);
        RestoreHotkeyButtonStyle(1);
        RestoreHotkeyButtonStyle(2);
        RestoreHotkeyButtonStyle(3);
        RestoreHotkeyButtonStyle(4);

        HotkeyManager.RegisterAll(_settings.ToggleUiKey, _settings.EnableKey, _settings.DisableKey);
        HotkeyManager.RegisterQuickSwap(_settings.NextPresetKey, _settings.PrevPresetKey,
            _settings.QuickSwapEnabled);
        HotkeyManager.IsListening = false;
    }

    private void CancelListening() => StopListening();

    private void OnKeyCapture(int vk)
    {
        if (vk == 0x1B) { CancelListening(); return; } // Escape cancels

        switch (_listeningTarget)
        {
            case 0: _settings.ToggleUiKey   = vk; break;
            case 1: _settings.EnableKey     = vk; break;
            case 2: _settings.DisableKey    = vk; break;
            case 3: _settings.NextPresetKey = vk; break;
            case 4: _settings.PrevPresetKey = vk; break;
        }
        _settings.Save();
        StopListening();
    }

    private void RestoreHotkeyButtonStyle(int target)
    {
        var btn = GetHotkeyButton(target);
        if (btn == null) return;
        btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        btn.Content = target switch
        {
            0 => VkToName(_settings.ToggleUiKey),
            1 => VkToName(_settings.EnableKey),
            2 => VkToName(_settings.DisableKey),
            3 => VkToName(_settings.NextPresetKey),
            4 => VkToName(_settings.PrevPresetKey),
            _ => "?"
        };
    }

    private Button? GetHotkeyButton(int target) => target switch
    {
        0 => ToggleKeyBtn,
        1 => EnableKeyBtn,
        2 => DisableKeyBtn,
        3 => NextPresetKeyBtn,
        4 => PrevPresetKeyBtn,
        _ => null
    };

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string VkToName(int vk)
    {
        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "PgUp",
            0x22 => "PgDn",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x60 and <= 0x69 => $"Num{vk - 0x60}",
            >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => $"VK{vk:X2}"
        };
    }

    private string? ShowInputDialog(string title, string prompt)
    {
        string? result = null;

        var dlg = new Window
        {
            Title                 = title,
            Width                 = 300,
            Height                = 130,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner                 = this,
            ResizeMode            = ResizeMode.NoResize,
            WindowStyle           = WindowStyle.None,
            AllowsTransparency    = true,
            Background            = Brushes.Transparent,
            Topmost               = true
        };

        var border = new Border
        {
            Background      = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(16)
        };

        var panel = new StackPanel();

        var label = new TextBlock
        {
            Text       = prompt,
            Foreground = new SolidColorBrush(Color.FromRgb(0xaa, 0xaa, 0xaa)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize   = 12,
            Margin     = new Thickness(0, 0, 0, 8)
        };

        var tb = new TextBox
        {
            Background      = new SolidColorBrush(Color.FromRgb(0x2e, 0x2e, 0x2e)),
            Foreground      = Brushes.White,
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(8, 5, 8, 5),
            FontFamily      = new FontFamily("Segoe UI"),
            FontSize        = 12,
            CaretBrush      = Brushes.White,
            Margin          = new Thickness(0, 0, 0, 10)
        };

        var btnRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancelBtn = new Button { Content = "Cancel", Width = 70, Height = 26, Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Style = (Style)Application.Current.Resources["NxButton"];
        cancelBtn.Click += (_, _) => dlg.Close();

        var okBtn = new Button { Content = "OK", Width = 70, Height = 26 };
        okBtn.Style = (Style)Application.Current.Resources["NxButton"];
        okBtn.Click += (_, _) => { result = tb.Text; dlg.Close(); };

        tb.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return) { result = tb.Text; dlg.Close(); }
            if (e.Key == Key.Escape) dlg.Close();
        };

        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);
        panel.Children.Add(label);
        panel.Children.Add(tb);
        panel.Children.Add(btnRow);
        border.Child = panel;
        dlg.Content  = border;

        dlg.Loaded += (_, _) => tb.Focus();
        dlg.ShowDialog();
        return result;
    }
}
