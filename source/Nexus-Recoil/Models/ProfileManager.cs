using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Nexus_Recoil.Models;

internal static class ProfileManager
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string ProfilesPath =
        Path.Combine(AppContext.BaseDirectory, "profiles.json");

    // ── Attacker presets ──────────────────────────────────────────────────────

    public static IReadOnlyList<PresetItem> GetAttackerItems()
    {
        var list = new List<PresetItem>();

        list.Add(PresetItem.Header("Assault Rifles"));
        foreach (var p in AttackerARs) list.Add(PresetItem.ForProfile(p));

        list.Add(PresetItem.Header("SMGs"));
        foreach (var p in AttackerSMGs) list.Add(PresetItem.ForProfile(p));

        list.Add(PresetItem.Header("LMGs"));
        foreach (var p in AttackerLMGs) list.Add(PresetItem.ForProfile(p));

        list.Add(PresetItem.Header("Marksman Rifles"));
        foreach (var p in AttackerDMRs) list.Add(PresetItem.ForProfile(p));

        list.Add(PresetItem.Header("Shotguns"));
        foreach (var p in AttackerSGs) list.Add(PresetItem.ForProfile(p));

        return list;
    }

    // ── Defender presets ──────────────────────────────────────────────────────

    public static IReadOnlyList<PresetItem> GetDefenderItems()
    {
        var list = new List<PresetItem>();

        list.Add(PresetItem.Header("SMGs"));
        foreach (var p in DefenderSMGs) list.Add(PresetItem.ForProfile(p));

        list.Add(PresetItem.Header("Assault Rifles"));
        foreach (var p in DefenderARs) list.Add(PresetItem.ForProfile(p));

        list.Add(PresetItem.Header("LMGs"));
        foreach (var p in DefenderLMGs) list.Add(PresetItem.ForProfile(p));

        list.Add(PresetItem.Header("Marksman Rifles"));
        foreach (var p in DefenderDMRs) list.Add(PresetItem.ForProfile(p));

        list.Add(PresetItem.Header("Shotguns"));
        foreach (var p in DefenderSGs) list.Add(PresetItem.ForProfile(p));

        return list;
    }

    // ── Custom profiles (persisted) ───────────────────────────────────────────

    public static List<Profile> LoadCustomProfiles()
    {
        try
        {
            if (File.Exists(ProfilesPath))
            {
                var json = File.ReadAllText(ProfilesPath);
                var root = JsonSerializer.Deserialize<ProfilesRoot>(json, _opts);
                if (root?.Profiles != null)
                {
                    foreach (var p in root.Profiles)
                    {
                        p.Side = "Custom";
                        p.IsBuiltIn = false;
                    }
                    return root.Profiles;
                }
            }
        }
        catch { }
        return new List<Profile>();
    }

    public static void SaveCustomProfiles(IEnumerable<Profile> profiles)
    {
        try
        {
            var root = new ProfilesRoot { Profiles = new List<Profile>(profiles) };
            File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(root, _opts));
        }
        catch { }
    }

    private sealed class ProfilesRoot
    {
        public List<Profile> Profiles { get; set; } = new();
    }

    // ── Built-in data ─────────────────────────────────────────────────────────

    private static Profile AR(string name, double d, double u, double r, double l) =>
        new() { Name = name, Down = d, Up = u, Right = r, Left = l, Side = "Attacker", Category = "Assault Rifles", IsBuiltIn = true };
    private static Profile ASMG(string name, double d, double u, double r, double l) =>
        new() { Name = name, Down = d, Up = u, Right = r, Left = l, Side = "Attacker", Category = "SMGs", IsBuiltIn = true };
    private static Profile ALMG(string name, double d, double u, double r, double l) =>
        new() { Name = name, Down = d, Up = u, Right = r, Left = l, Side = "Attacker", Category = "LMGs", IsBuiltIn = true };
    private static Profile ADMR(string name, double d, double u, double r, double l) =>
        new() { Name = name, Down = d, Up = u, Right = r, Left = l, Side = "Attacker", Category = "Marksman Rifles", IsBuiltIn = true };
    private static Profile ASG(string name, double d, double u, double r, double l) =>
        new() { Name = name, Down = d, Up = u, Right = r, Left = l, Side = "Attacker", Category = "Shotguns", IsBuiltIn = true };

    private static Profile DSMG(string name, double d, double u, double r, double l) =>
        new() { Name = name, Down = d, Up = u, Right = r, Left = l, Side = "Defender", Category = "SMGs", IsBuiltIn = true };
    private static Profile DAR(string name, double d, double u, double r, double l) =>
        new() { Name = name, Down = d, Up = u, Right = r, Left = l, Side = "Defender", Category = "Assault Rifles", IsBuiltIn = true };
    private static Profile DLMG(string name, double d, double u, double r, double l) =>
        new() { Name = name, Down = d, Up = u, Right = r, Left = l, Side = "Defender", Category = "LMGs", IsBuiltIn = true };
    private static Profile DDMR(string name, double d, double u, double r, double l) =>
        new() { Name = name, Down = d, Up = u, Right = r, Left = l, Side = "Defender", Category = "Marksman Rifles", IsBuiltIn = true };
    private static Profile DSG(string name, double d, double u, double r, double l) =>
        new() { Name = name, Down = d, Up = u, Right = r, Left = l, Side = "Defender", Category = "Shotguns", IsBuiltIn = true };

    private static readonly Profile[] AttackerARs =
    [
        AR("R4-C",          15, 0, 3, 0),
        AR("AK-12",         18, 0, 4, 0),
        AR("SC3000K",       13, 0, 2, 0),
        AR("C8-SFW",        14, 0, 3, 0),
        AR("M4",            12, 0, 2, 0),
        AR("C7E",           14, 0, 3, 0),
        AR("Spear .308",    11, 0, 2, 0),
        AR("M762",          19, 0, 5, 0),
        AR("556XI",         13, 0, 2, 0),
        AR("F2",            20, 0, 5, 0),
        AR("552 Commando",  13, 0, 3, 0),
        AR("Type-89",       16, 0, 3, 0),
        AR("L85A2",         12, 0, 2, 0),
        AR("G36C",          11, 0, 2, 0),
        AR("416-C Carbine", 15, 0, 4, 0),
        AR("PARA-308",      17, 0, 4, 0),
        AR("Mk17 CQB",      17, 0, 5, 0),
        AR("V308",          15, 0, 3, 0),
        AR("AUG A2",        14, 0, 3, 0),
        AR("ARX200",        10, 0, 2, 0),
        AR("AR33",          11, 0, 2, 0),
        AR("AK-74M",        16, 0, 3, 0),
        AR("F90",           13, 0, 2, 0),
        AR("Commando 9",    10, 0, 1, 0),
        AR("POF-9",         12, 0, 2, 0),
        AR("PCX-33",        11, 0, 2, 0),
    ];

    private static readonly Profile[] AttackerSMGs =
    [
        ASMG("MP7",     9,  0, 2, 0),
        ASMG("FMG-9",   9,  0, 1, 0),
        ASMG("MPX",     8,  0, 1, 0),
        ASMG("PDW9",    9,  0, 2, 0),
        ASMG("9mm C1",  8,  0, 1, 0),
        ASMG("P90",     10, 0, 2, 0),
    ];

    private static readonly Profile[] AttackerLMGs =
    [
        ALMG("LMG-E",    19, 0, 4, 0),
        ALMG("G8A1",     18, 0, 4, 0),
        ALMG("M249 SAW", 20, 0, 5, 0),
        ALMG("M249",     20, 0, 5, 0),
    ];

    private static readonly Profile[] AttackerDMRs =
    [
        ADMR("417",       6, 0, 1, 0),
        ADMR("OTs-03",    6, 0, 1, 0),
        ADMR("CAMRS",     5, 0, 1, 0),
        ADMR("SR-25",     5, 0, 1, 0),
        ADMR("Mk 14 EBR", 7, 0, 1, 0),
        ADMR("AR-15.50",  4, 0, 0, 0),
        ADMR("CSRX 300",  3, 0, 0, 0),
        ADMR("PMR90A2",   5, 0, 1, 0),
    ];

    private static readonly Profile[] AttackerSGs =
    [
        ASG("M1014",   4, 0, 0, 0),
        ASG("M590A1",  4, 0, 0, 0),
        ASG("SG-CQB",  4, 0, 0, 0),
        ASG("M870",    4, 0, 0, 0),
        ASG("SASG-12", 5, 0, 0, 0),
        ASG("Super 90",4, 0, 0, 0),
        ASG("SPAS-12", 4, 0, 0, 0),
        ASG("SPAS-15", 4, 0, 0, 0),
    ];

    private static readonly Profile[] DefenderSMGs =
    [
        DSMG("T-5 SMG",           9,  0, 1, 0),
        DSMG("MP7",               9,  0, 2, 0),
        DSMG("Vector .45 ACP",    12, 0, 3, 0),
        DSMG("MP5K",              7,  0, 1, 0),
        DSMG("MP5",               8,  0, 1, 0),
        DSMG("416-C Carbine",     15, 0, 4, 0),
        DSMG("UZK50GI",           10, 0, 2, 0),
        DSMG("Scorpion EVO 3 A1", 11, 0, 2, 0),
        DSMG("9x19VSN",           11, 0, 2, 0),
        DSMG("K1A",               9,  0, 1, 0),
        DSMG("MP5SD",             7,  0, 1, 0),
        DSMG("Mx4 Storm",         8,  0, 1, 0),
        DSMG("AUG A3",            10, 0, 2, 0),
        DSMG("UMP45",             10, 0, 2, 0),
        DSMG("FMG-9",             9,  0, 1, 0),
        DSMG("M12",               9,  0, 1, 0),
        DSMG("P10 RONI",          8,  0, 1, 0),
        DSMG("MPX",               8,  0, 1, 0),
    ];

    private static readonly Profile[] DefenderARs =
    [
        DAR("Commando 9",    10, 0, 1, 0),
        DAR("416-C Carbine", 15, 0, 4, 0),
        DAR("Spear .308",    11, 0, 2, 0),
        DAR("AUG A2",        14, 0, 3, 0),
    ];

    private static readonly Profile[] DefenderLMGs =
    [
        DLMG("ALDA 5.56", 21, 0, 5, 0),
        DLMG("T-95 LSW",  16, 0, 3, 0),
        DLMG("6P41",      22, 0, 5, 0),
        DLMG("DP27",      18, 0, 4, 0),
    ];

    private static readonly Profile[] DefenderDMRs =
    [
        DDMR("Mk 14 EBR", 7, 0, 1, 0),
        DDMR("TCSG12",    4, 0, 0, 0),
        DDMR("OTs-03",    6, 0, 1, 0),
    ];

    private static readonly Profile[] DefenderSGs =
    [
        DSG("M590A1",   4, 0, 0, 0),
        DSG("SASG-12",  5, 0, 0, 0),
        DSG("ACS12",    5, 0, 0, 0),
        DSG("SuperNova",4, 0, 0, 0),
        DSG("ITA12L",   4, 0, 0, 0),
        DSG("SIX12",    4, 0, 0, 0),
        DSG("SIX12 SD", 4, 0, 0, 0),
        DSG("FO-12",    5, 0, 0, 0),
        DSG("BOSG.12.2",3, 0, 0, 0),
        DSG("SPAS-15",  4, 0, 0, 0),
        DSG("M1014",    4, 0, 0, 0),
    ];
}
