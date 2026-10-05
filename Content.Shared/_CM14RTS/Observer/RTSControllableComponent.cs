using Robust.Shared.GameStates;

namespace Content.Shared._CM14RTS.Observer;

/// <summary>
/// Marks an entity as controllable by an RTS observer of a matching faction.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RTSControllableComponent : Component
{
    /// <summary>
    /// The faction this unit belongs to (e.g. "Marine", "USCM", "Hive", "Xeno").
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Faction = "Marine";
}
