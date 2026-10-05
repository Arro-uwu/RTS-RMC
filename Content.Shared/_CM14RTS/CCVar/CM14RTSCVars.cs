using Robust.Shared.Configuration;

namespace Content.Shared._CM14RTS.CCVar;

[CVarDefs]
public sealed class CM14RTSCVars
{
    /// <summary>
    /// Whether the RTS Fog of War overlay is enabled for RTS commanders.
    /// </summary>
    public static readonly CVarDef<bool> RTSFogOfWarEnabled =
        CVarDef.Create("rts.fow_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Alpha transparency of the Fog of War veil outside allied vision.
    /// </summary>
    public static readonly CVarDef<float> RTSFogOfWarAlpha =
        CVarDef.Create("rts.fow_alpha", 0.88f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
