using Robust.Shared.GameObjects;

namespace Content.Shared._CM14RTS.Vision;

/// <summary>
/// Attached to a structure on the client that has been discovered by the RTS commander.
/// Discovered structures remain visible under the fog of war as part of map memory.
/// If destroyed or dismantled in the fog, a snapshot memory marker is spawned until scouted.
/// </summary>
[RegisterComponent]
public sealed partial class RTSDiscoveredStructureComponent : Component
{
}
