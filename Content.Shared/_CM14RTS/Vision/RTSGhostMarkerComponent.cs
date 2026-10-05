using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._CM14RTS.Vision;

/// <summary>
/// Attached to a client-side ghost marker representing the last known position and appearance of an enemy in the fog of war.
/// </summary>
[RegisterComponent]
public sealed partial class RTSGhostMarkerComponent : Component
{
    /// <summary>
    /// The original enemy entity this ghost marker represents.
    /// </summary>
    [DataField]
    public EntityUid TrackedEntity = EntityUid.Invalid;

    /// <summary>
    /// The grid where this enemy was last confirmed.
    /// </summary>
    [DataField]
    public EntityUid GridUid = EntityUid.Invalid;

    /// <summary>
    /// The tile coordinates where this enemy was last confirmed.
    /// </summary>
    [DataField]
    public Vector2i GridTile;
}
