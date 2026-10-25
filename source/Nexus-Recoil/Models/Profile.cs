namespace Nexus_Recoil.Models;

internal sealed class Profile
{
    public string Name     { get; set; } = "";
    public double Down     { get; set; }
    public double Up       { get; set; }
    public double Right    { get; set; }
    public double Left     { get; set; }
    public string Side     { get; set; } = "Custom";   // "Attacker" | "Defender" | "Custom"
    public string Category { get; set; } = "";

    public bool IsBuiltIn { get; set; } = false;

    public override string ToString() => Name;
}

internal sealed class PresetItem
{
    public bool     IsHeader    { get; init; }
    public string   DisplayName { get; init; } = "";
    public Profile? Profile     { get; init; }

    public static PresetItem Header(string name) =>
        new() { IsHeader = true, DisplayName = $"── {name.ToUpperInvariant()} ──" };

    public static PresetItem ForProfile(Profile p) =>
        new() { IsHeader = false, DisplayName = p.Name, Profile = p };
}
