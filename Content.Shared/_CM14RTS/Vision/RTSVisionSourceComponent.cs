using Robust.Shared.GameStates;

namespace Content.Shared._CM14RTS.Vision;

/// <summary>
/// Attached to entities (units, structures, cameras) that emit vision for their RTS faction.
/// By default, RangeOverride is null, which uses the default player view radius from engine/Eye.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedRTSVisionSystem))]
public sealed partial class RTSVisionSourceComponent : Component
{
    /// <summary>
    /// The faction this vision source provides vision to (e.g. "Marine", "Hive").
    /// If left empty, will automatically adopt RTSControllableComponent's faction if present.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Faction = string.Empty;

    /// <summary>
    /// Explicit override for vision radius. If null, the default view radius that a player sees
    /// when controlling this entity is used (from NetMaxUpdateRange and EyeComponent.PvsScale).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float? RangeOverride;

    /// <summary>
    /// Whether this vision source is currently active.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;
}
