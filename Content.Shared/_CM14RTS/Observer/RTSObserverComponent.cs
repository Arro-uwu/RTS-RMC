using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._CM14RTS.Observer;

/// <summary>
/// Controls RTS observer features: follow mode, physical world interaction blocking,
/// invisibility to living beings, faction visibility isolation, and direct unit control.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedRTSObserverSystem))]
public sealed partial class RTSObserverComponent : Component
{
    /// <summary>
    /// Whether this observer is allowed to follow other entities.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CanFollow = true;

    /// <summary>
    /// Whether physical interactions (attacks, using items, pickup, drop, throw, world interactions) are blocked.
    /// Actions and UI remain functional.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool BlockPhysicalInteractions = true;

    /// <summary>
    /// Whether this observer is invisible to normal living beings.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool HideFromLiving = true;

    /// <summary>
    /// The faction this observer belongs to (e.g. "USCM", "Marine", "Hive", "Xeno").
    /// Observers of different factions cannot see each other by default and can only control friendly units.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Faction = "USCM";

    /// <summary>
    /// Prototype for the action used to directly control units.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId ControlAction = "CM14RTSActionUnitControl";

    /// <summary>
    /// Granted control action entity instance.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ControlActionEntity;

    /// <summary>
    /// Prototype for the return action granted to units while controlled.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId ReturnAction = "CM14RTSActionReturnToObserver";

    /// <summary>
    /// Whether this observer can see ghosts.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CanSeeGhosts = true;

    /// <summary>
    /// Whether this observer can see observers belonging to its own faction (including oneself).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CanSeeOwnFaction = true;

    /// <summary>
    /// Whether this observer can see observers belonging to other factions.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CanSeeOtherObservers = false;
}

[Serializable, NetSerializable]
public enum RTSObserverFaction : byte
{
    None = 0,
    USCM = 1,
    Hive = 2,
}
