using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._CM14RTS.Observer;

/// <summary>
/// Raised when an RTS observer uses the direct unit control action targeting a unit.
/// </summary>
public sealed partial class RTSUnitControlActionEvent : EntityTargetActionEvent
{
}

/// <summary>
/// Raised when a controlled unit activates the return action from the hotbar.
/// </summary>
public sealed partial class RTSReturnToObserverActionEvent : InstantActionEvent
{
}

/// <summary>
/// Raised by the client to request direct unit control (e.g. from an alternative verb).
/// </summary>
[Serializable, NetSerializable]
public sealed class RTSRequestUnitControlMessage : EntityEventArgs
{
    public NetEntity Target;

    public RTSRequestUnitControlMessage(NetEntity target)
    {
        Target = target;
    }
}
