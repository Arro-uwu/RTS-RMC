using Content.Shared.Database;
using Content.Shared.Eye;
using Content.Shared.Follower;
using Content.Shared.Ghost;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Radio;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._CM14RTS.Observer;

public abstract class SharedRTSObserverSystem : EntitySystem
{
    [Dependency] private readonly FollowerSystem _follower = default!;
    [Dependency] protected readonly SharedEyeSystem Eye = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
        SubscribeLocalEvent<RTSControllableComponent, GetVerbsEvent<AlternativeVerb>>(OnGetControllableVerbs);

        SubscribeLocalEvent<RTSObserverComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<RTSObserverComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<RTSObserverComponent, UseAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<RTSObserverComponent, PickupAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<RTSObserverComponent, DropAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<RTSObserverComponent, ThrowAttemptEvent>(OnAttempt);

        SubscribeLocalEvent<RTSObserverComponent, GetDefaultRadioChannelEvent>(OnGetDefaultRadioChannel);
        SubscribeLocalEvent<RTSObserverComponent, GetVisMaskEvent>(OnObserverGetVisMask);
        SubscribeLocalEvent<RTSObserverComponent, AfterAutoHandleStateEvent>(OnAfterHandleState);
    }

    private void OnGetAlternativeVerbs(GetVerbsEvent<AlternativeVerb> ev)
    {
        if (ev.User == ev.Target || IsClientSide(ev.Target))
            return;

        if (!TryComp<RTSObserverComponent>(ev.User, out var observer) || !observer.CanFollow)
            return;

        var verb = new AlternativeVerb
        {
            Priority = 10,
            Act = () => _follower.StartFollowingEntity(ev.User, ev.Target),
            Impact = LogImpact.Low,
            Text = Loc.GetString("verb-follow-text"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/open.svg.192dpi.png")),
        };
        ev.Verbs.Add(verb);
    }

    private void OnGetControllableVerbs(Entity<RTSControllableComponent> ent, ref GetVerbsEvent<AlternativeVerb> ev)
    {
        if (ev.User == ent.Owner || IsClientSide(ent.Owner))
            return;

        if (!TryComp<RTSObserverComponent>(ev.User, out var observer))
            return;

        if (!RTSFactionHelper.AreFactionsCompatible(observer.Faction, ent.Comp.Faction))
            return;

        var target = ent.Owner;
        var verb = new AlternativeVerb
        {
            Priority = 20,
            Act = () =>
            {
                RaiseNetworkEvent(new RTSRequestUnitControlMessage(GetNetEntity(target)));
            },
            Impact = LogImpact.High,
            Text = Loc.GetString("rts-control-verb-text"),
            Icon = new SpriteSpecifier.Rsi(new("/Textures/_CM14RTS/Mobs/Observer/observer.rsi"), "eye"),
        };
        ev.Verbs.Add(verb);
    }

    private void OnGetDefaultRadioChannel(Entity<RTSObserverComponent> ent, ref GetDefaultRadioChannelEvent args)
    {
        if (args.Channel != null)
            return;

        var norm = RTSFactionHelper.Normalize(ent.Comp.Faction);
        args.Channel = norm == "hive" ? "Hivemind" : "MarineCommon";
    }

    public bool CanPhysicalInteract(Entity<RTSObserverComponent> ent, EntityUid? target = null)
    {
        if (ent.Comp.BlockPhysicalInteractions)
            return false;

        return true;
    }

    private void OnInteractionAttempt(Entity<RTSObserverComponent> ent, ref InteractionAttemptEvent args)
    {
        if (args.Target != null && args.Target != ent.Owner && !CanPhysicalInteract(ent, args.Target))
            args.Cancelled = true;
    }

    private void OnAttackAttempt(Entity<RTSObserverComponent> ent, ref AttackAttemptEvent args)
    {
        if (!CanPhysicalInteract(ent, args.Target))
            args.Cancel();
    }

    private void OnAttempt(EntityUid uid, RTSObserverComponent component, CancellableEntityEventArgs args)
    {
        if (!CanPhysicalInteract((uid, component)))
            args.Cancel();
    }

    private void OnObserverGetVisMask(Entity<RTSObserverComponent> ent, ref GetVisMaskEvent args)
    {
        if (ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        if (ent.Comp.CanSeeGhosts)
            args.VisibilityMask |= (int)VisibilityFlags.Ghost;

        if (ent.Comp.CanSeeOwnFaction)
        {
            var norm = RTSFactionHelper.Normalize(ent.Comp.Faction);
            if (norm == "marine")
                args.VisibilityMask |= (int)VisibilityFlags.RTSObserverUSCM;
            else if (norm == "hive")
                args.VisibilityMask |= (int)VisibilityFlags.RTSObserverHive;
            else
                args.VisibilityMask |= (int)VisibilityFlags.RTSObserver;
        }

        if (ent.Comp.CanSeeOtherObservers)
        {
            args.VisibilityMask |= (int)(VisibilityFlags.RTSObserverUSCM | VisibilityFlags.RTSObserverHive | VisibilityFlags.RTSObserver);
        }
    }

    private void OnAfterHandleState(Entity<RTSObserverComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Eye.RefreshVisibilityMask(ent.Owner);
    }

    public void SetCanFollow(Entity<RTSObserverComponent> ent, bool canFollow)
    {
        if (ent.Comp.CanFollow == canFollow)
            return;

        ent.Comp.CanFollow = canFollow;
        Dirty(ent);
    }

    public void SetBlockPhysicalInteractions(Entity<RTSObserverComponent> ent, bool block)
    {
        if (ent.Comp.BlockPhysicalInteractions == block)
            return;

        ent.Comp.BlockPhysicalInteractions = block;
        Dirty(ent);
    }

    public void SetHideFromLiving(Entity<RTSObserverComponent> ent, bool hide)
    {
        if (ent.Comp.HideFromLiving == hide)
            return;

        ent.Comp.HideFromLiving = hide;
        Dirty(ent);
        UpdateVisibility(ent);
    }

    public void SetFaction(Entity<RTSObserverComponent> ent, string faction)
    {
        if (ent.Comp.Faction == faction)
            return;

        ent.Comp.Faction = faction;
        Dirty(ent);
        UpdateVisibility(ent);
    }

    public void SetCanSeeGhosts(Entity<RTSObserverComponent> ent, bool canSee)
    {
        if (ent.Comp.CanSeeGhosts == canSee)
            return;

        ent.Comp.CanSeeGhosts = canSee;
        Dirty(ent);
        UpdateVisibility(ent);
    }

    public void SetCanSeeOwnFaction(Entity<RTSObserverComponent> ent, bool canSee)
    {
        if (ent.Comp.CanSeeOwnFaction == canSee)
            return;

        ent.Comp.CanSeeOwnFaction = canSee;
        Dirty(ent);
        UpdateVisibility(ent);
    }

    public void SetCanSeeOtherObservers(Entity<RTSObserverComponent> ent, bool canSee)
    {
        if (ent.Comp.CanSeeOtherObservers == canSee)
            return;

        ent.Comp.CanSeeOtherObservers = canSee;
        Dirty(ent);
        UpdateVisibility(ent);
    }

    public virtual void UpdateVisibility(Entity<RTSObserverComponent> ent)
    {
        Eye.RefreshVisibilityMask(ent.Owner);
    }
}
