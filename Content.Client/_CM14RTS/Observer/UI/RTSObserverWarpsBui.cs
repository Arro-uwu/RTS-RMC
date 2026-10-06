using Content.Shared._CM14RTS.Observer;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._CM14RTS.Observer.UI;

[UsedImplicitly]
public sealed class RTSObserverWarpsBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private RTSObserverWarpsWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<RTSObserverWarpsWindow>();
        _window.OnWarpSelected += target => SendMessage(new RTSObserverWarpToMessage(target));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not RTSObserverWarpsBuiState warpsState || _window == null)
            return;

        _window.UpdateWarps(warpsState.Warps);
    }
}
