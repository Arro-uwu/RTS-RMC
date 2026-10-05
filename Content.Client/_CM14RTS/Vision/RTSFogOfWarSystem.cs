using Robust.Client.Graphics;
using Robust.Shared.GameObjects;

namespace Content.Client._CM14RTS.Vision;

public sealed class RTSFogOfWarSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;

    private RTSFogOfWarOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new RTSFogOfWarOverlay();
        _overlayManager.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay(_overlay);
    }
}
