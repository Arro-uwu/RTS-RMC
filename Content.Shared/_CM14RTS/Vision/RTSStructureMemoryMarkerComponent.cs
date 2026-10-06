using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._CM14RTS.Vision;

/// <summary>
/// Attached to a client-side structure memory marker representing a snapshot of a structure
/// that was destroyed or removed while hidden under the fog of war.
/// The marker remains visible until an allied unit gains line of sight to the tile, at which point it dissolves.
/// </summary>
[RegisterComponent]
public sealed partial class RTSStructureMemoryMarkerComponent : Component
{
    /// <summary>
    /// The grid where this structure was situated.
    /// </summary>
    [DataField]
    public EntityUid GridUid = EntityUid.Invalid;

    /// <summary>
    /// The tile coordinates where this structure was situated.
    /// </summary>
    [DataField]
    public Vector2i GridTile;
}
