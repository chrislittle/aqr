namespace Aqr.Core.Model;

public static class Keys
{
    /// <summary>
    /// Canonical family key. Azure returns family names inconsistently across and within APIs
    /// ("standardDSv5Family", "StandardDadsv7Family", "Standard NCASv3_T4 Family", "standard NDAMSv4_A100Family"),
    /// so every table keys on whitespace-stripped lower case. Observed live 2026-10-07 in QuotaResources and Resource SKUs.
    /// </summary>
    public static string Family(string? name) =>
        string.Concat((name ?? "").Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
}
