namespace Content.Shared._CM14RTS.Observer;

public static class RTSFactionHelper
{
    public static bool AreFactionsCompatible(string? f1, string? f2)
    {
        if (string.IsNullOrWhiteSpace(f1) || string.IsNullOrWhiteSpace(f2))
            return false;

        return string.Equals(Normalize(f1), Normalize(f2), StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string faction)
    {
        var trimmed = faction.Trim().ToLowerInvariant();
        if (trimmed is "marine" or "uscm" or "cm")
            return "marine";
        if (trimmed is "hive" or "xeno" or "xenonid")
            return "hive";
        return trimmed;
    }
}
