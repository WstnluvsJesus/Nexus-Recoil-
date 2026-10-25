using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus_Recoil.Models;

internal sealed class AppSettings
{
    // ── Hotkeys ───────────────────────────────────────────────────────────
    public int ToggleUiKey { get; set; } = 45;   // Insert
    public int EnableKey   { get; set; } = 112;  // F1
    public int DisableKey  { get; set; } = 113;  // F2

    // ── Recoil ────────────────────────────────────────────────────────────
    public int    AimKeyIndex       { get; set; } = 0;
    public int    DelayMs           { get; set; } = 0;
    public double GlobalSensitivity { get; set; } = 1.0;

    // ── Recoil curve ──────────────────────────────────────────────────────
    public bool CurveEnabled   { get; set; } = false;
    public int  CurveRampShots { get; set; } = 10;

    // ── Quick-swap ────────────────────────────────────────────────────────
    public bool QuickSwapEnabled { get; set; } = false;
    public int  NextPresetKey    { get; set; } = 0x22; // Page Down
    public int  PrevPresetKey    { get; set; } = 0x21; // Page Up

    // ── Stream proof ──────────────────────────────────────────────────────
    public bool StreamProof { get; set; } = false;

    // ── Watermark ─────────────────────────────────────────────────────────
    public bool WatermarkEnabled { get; set; } = false;

    // ── Update tracking ───────────────────────────────────────────────────
    public string LastSeenVersion { get; set; } = "";

    // ── Window ────────────────────────────────────────────────────────────
    public double WindowLeft { get; set; } = 100;
    public double WindowTop  { get; set; } = 100;

    // ── Last preset ───────────────────────────────────────────────────────
    public string LastProfileName { get; set; } = "R4-C";
    public string LastProfileSide { get; set; } = "Attacker";

    // ── Misc ──────────────────────────────────────────────────────────────
    public bool DisclaimerShown { get; set; } = false;

    // ── Rapid fire ────────────────────────────────────────────────────────
    public bool RapidFireEnabled      { get; set; } = false;
    public int  RapidFireRateMs       { get; set; } = 60;
    public bool RapidFireRequireAimKey { get; set; } = false;

    // ── Humanization ──────────────────────────────────────────────────────
    public bool HumanizationEnabled  { get; set; } = false;
    public int  HumanizationStrength { get; set; } = 30;

    // ── Burst mode ────────────────────────────────────────────────────────
    public bool BurstModeEnabled { get; set; } = false;
    public int  BurstShots       { get; set; } = 5;
    public bool BurstAutoReset   { get; set; } = true;

    // ── Sound ─────────────────────────────────────────────────────────────
    public bool    SoundEnabled    { get; set; } = true;
    public int     SoundVolume     { get; set; } = 70;
    public string? CustomSoundPath { get; set; } = null;

    // ── Tooltips ──────────────────────────────────────────────────────────
    public bool TooltipsEnabled { get; set; } = true;

    // ── Theme colors ──────────────────────────────────────────────────────
    public string ThemeAccent   { get; set; } = "#e03030";
    public string ThemeBg       { get; set; } = "#1a1a1a";
    public string ThemeSurface  { get; set; } = "#242424";
    public string ThemeTextPrim { get; set; } = "#ffffff";
    public string ThemeTextSec  { get; set; } = "#aaaaaa";

    // ── Version (read-only, not serialized) ───────────────────────────────
    [JsonIgnore]
    public string CurrentVersion { get; } = "v1.0.5";

    // ── Persistence ───────────────────────────────────────────────────────
    [JsonIgnore]
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NexusRecoil", "settings.json");

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static AppSettings Load()
    {
        try
        {
            // Keep existing portable-install preferences until the first per-user save.
            var loadPath = File.Exists(FilePath) ? FilePath
                : Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (File.Exists(loadPath))
            {
                var json = File.ReadAllText(loadPath);
                return JsonSerializer.Deserialize<AppSettings>(json, _opts) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, _opts));
        }
        catch { }
    }
}
