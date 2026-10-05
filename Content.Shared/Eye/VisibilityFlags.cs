using Robust.Shared.Serialization;

namespace Content.Shared.Eye
{
    [Flags]
    [FlagsFor(typeof(VisibilityMaskLayer))]
    public enum VisibilityFlags : int
    {
        None   = 0,
        Normal = 1 << 0,
        Ghost  = 1 << 1,
        Subfloor = 1 << 2,
        // cm14-rts-edit start
        RTSObserverUSCM = 1 << 3,
        RTSObserverHive = 1 << 4,
        RTSObserver = 1 << 5,
        // cm14-rts-edit end
        ImaginaryFriend = 1 << 14,
        Xeno = 1 << 15,
    }
}
