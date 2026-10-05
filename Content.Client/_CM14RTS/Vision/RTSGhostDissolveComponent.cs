using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Client._CM14RTS.Vision;

/// <summary>
/// Attached to a dissolving ghost marker to smoothly fade its opacity away before deletion.
/// </summary>
[RegisterComponent]
public sealed partial class RTSGhostDissolveComponent : Component
{
    public float FadeTime = 0.35f;
    public float Elapsed = 0f;
    public Color InitialColor = new(0.75f, 0.85f, 1f, 0.45f);
}
