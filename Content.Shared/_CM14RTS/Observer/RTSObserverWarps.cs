using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._CM14RTS.Observer;

[Serializable, NetSerializable]
public enum RTSObserverWarpsUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class RTSObserverWarpPoint(NetEntity entity, string name, string category)
{
    public NetEntity Entity = entity;
    public string Name = name;
    public string Category = category;
}

[Serializable, NetSerializable]
public sealed class RTSObserverWarpsBuiState(List<RTSObserverWarpPoint> warps) : BoundUserInterfaceState
{
    public readonly List<RTSObserverWarpPoint> Warps = warps;
}

[Serializable, NetSerializable]
public sealed class RTSObserverWarpToMessage(NetEntity target) : BoundUserInterfaceMessage
{
    public readonly NetEntity Target = target;
}

public sealed partial class RTSObserverOpenWarpsActionEvent : InstantActionEvent;
