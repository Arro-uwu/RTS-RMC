using Robust.Shared.GameStates;

namespace Content.Shared._CM14RTS.Observer;

/// <summary>
/// Added to an entity while it is being directly controlled by an RTS observer.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RTSControlledUnitComponent : Component
{
    /// <summary>
    /// The observer entity whose mind is visiting this unit.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Observer;

    /// <summary>
    /// The Return to Observer action entity granted to this unit's hotbar.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ReturnAction;
}
