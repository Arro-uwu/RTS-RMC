using System.Linq;
using System.Numerics;
using Content.Shared.Database;
using Content.Shared.Eye;
using Content.Shared.Follower;
using Content.Shared.Follower.Components;
using Content.Shared.Ghost;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._CM14RTS.Observer;

public abstract class SharedRTSObserverSystem : EntitySystem
{
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] protected readonly FollowerSystem Follower = default!;
    [Dependency] protected readonly SharedEyeSystem Eye = default!;

    private bool _isReverting;

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

        SubscribeLocalEvent<RTSObserverComponent, MoveEvent>(OnObserverMove);
        SubscribeLocalEvent<FollowedComponent, MoveEvent>(OnFollowedMove);
        SubscribeLocalEvent<RTSObserverComponent, StartedFollowingEntityEvent>(OnStartedFollowing);

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

        if (IsInSpace(Transform(ev.Target).Coordinates))
            return;

        var verb = new AlternativeVerb
        {
            Priority = 10,
            Act = () => Follower.StartFollowingEntity(ev.User, ev.Target),
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

        var user = ev.User;
        var target = ent.Owner;
        var verb = new AlternativeVerb
        {
            Priority = 20,
            Act = () =>
            {
                RequestUnitControl(user, target);
            },
            Impact = LogImpact.High,
            Text = Loc.GetString("rts-control-verb-text"),
            Icon = new SpriteSpecifier.Rsi(new("/Textures/_CM14RTS/Mobs/Observer/observer.rsi"), "eye"),
        };
        ev.Verbs.Add(verb);
    }

    public virtual void RequestUnitControl(EntityUid observer, EntityUid target)
    {
        RaiseNetworkEvent(new RTSRequestUnitControlMessage(GetNetEntity(target)));
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

    #region Space Boundary & Movement Enforcement

    private void OnStartedFollowing(Entity<RTSObserverComponent> ent, ref StartedFollowingEntityEvent args)
    {
        if (IsInSpace(Transform(args.Following).Coordinates))
        {
            Follower.StopFollowingEntity(ent.Owner, args.Following);
            EnsureSafePosition(ent);
            NotifySpaceBlocked(ent);
        }
    }

    private void OnFollowedMove(Entity<FollowedComponent> ent, ref MoveEvent args)
    {
        if (_timing.ApplyingState)
            return;

        if (TerminatingOrDeleted(ent))
            return;

        if (!args.NewPosition.IsValid(EntityManager))
            return;

        if (!IsInSpace(args.NewPosition))
            return;

        if (ent.Comp.Following.Count == 0)
            return;

        var followers = ent.Comp.Following.ToArray();
        foreach (var follower in followers)
        {
            if (!TryComp<RTSObserverComponent>(follower, out var observerComp))
                continue;

            Follower.StopFollowingEntity(follower, ent.Owner);

            EntityCoordinates safeCoords;
            if (observerComp.LastValidCoordinates is { } lastValid && lastValid.IsValid(EntityManager) && !IsInSpace(lastValid))
            {
                safeCoords = lastValid;
            }
            else if (args.OldPosition.IsValid(EntityManager) && !IsInSpace(args.OldPosition))
            {
                safeCoords = args.OldPosition;
            }
            else if (TryFindSafeGridCoordinates((follower, observerComp), out var gridCoords))
            {
                safeCoords = gridCoords;
            }
            else
            {
                continue;
            }

            observerComp.LastValidCoordinates = safeCoords;

            _isReverting = true;
            try
            {
                _transform.SetCoordinates(follower, safeCoords);
                _transform.AttachToGridOrMap(follower);
            }
            finally
            {
                _isReverting = false;
            }

            if (TryComp<PhysicsComponent>(follower, out var physics))
            {
                _physics.SetLinearVelocity(follower, Vector2.Zero, body: physics);
            }

            NotifySpaceBlocked((follower, observerComp));
        }
    }

    private void OnObserverMove(Entity<RTSObserverComponent> ent, ref MoveEvent args)
    {
        if (_timing.ApplyingState)
            return;

        if (_isReverting)
            return;

        if (TerminatingOrDeleted(ent))
            return;

        if (!args.NewPosition.IsValid(EntityManager))
            return;

        if (IsInSpace(args.NewPosition))
        {
            if (TryComp<FollowerComponent>(ent.Owner, out var follower))
                Follower.StopFollowingEntity(ent.Owner, follower.Following);

            EntityCoordinates safeCoords;
            if (args.OldPosition.IsValid(EntityManager) && !IsInSpace(args.OldPosition))
            {
                safeCoords = args.OldPosition;
            }
            else if (ent.Comp.LastValidCoordinates is { } lastValid && lastValid.IsValid(EntityManager) && !IsInSpace(lastValid))
            {
                safeCoords = lastValid;
            }
            else if (TryFindSafeGridCoordinates(ent, out var gridCoords))
            {
                safeCoords = gridCoords;
            }
            else
            {
                return;
            }

            ent.Comp.LastValidCoordinates = safeCoords;

            _isReverting = true;
            try
            {
                _transform.SetCoordinates(ent.Owner, safeCoords);
                _transform.AttachToGridOrMap(ent.Owner);
            }
            finally
            {
                _isReverting = false;
            }

            if (TryComp<PhysicsComponent>(ent.Owner, out var physics))
            {
                _physics.SetLinearVelocity(ent.Owner, Vector2.Zero, body: physics);
            }

            NotifySpaceBlocked(ent);
        }
        else
        {
            ent.Comp.LastValidCoordinates = args.NewPosition;
        }
    }

    public bool IsInSpace(EntityCoordinates coordinates)
    {
        if (!coordinates.IsValid(EntityManager))
            return true;

        var tile = _turf.GetTileRef(coordinates);
        if (tile == null || tile.Value.Tile.IsEmpty)
            return true;

        return _turf.IsSpace(tile.Value);
    }

    public bool IsInSpace(MapCoordinates coordinates)
    {
        return IsInSpace(_transform.ToCoordinates(coordinates));
    }

    public void NotifySpaceBlocked(Entity<RTSObserverComponent> ent)
    {
        var curTime = _timing.CurTime;
        if (curTime - ent.Comp.LastSpacePopupTime < TimeSpan.FromSeconds(2))
            return;

        ent.Comp.LastSpacePopupTime = curTime;
        _popup.PopupPredicted(Loc.GetString("rts-observer-space-blocked"), ent.Owner, ent.Owner, PopupType.MediumCaution);
    }

    public bool EnsureSafePosition(Entity<RTSObserverComponent> ent)
    {
        var xform = Transform(ent.Owner);
        if (!IsInSpace(xform.Coordinates))
        {
            ent.Comp.LastValidCoordinates = xform.Coordinates;
            return true;
        }

        EntityCoordinates safeCoords;
        if (ent.Comp.LastValidCoordinates is { } lastValid && lastValid.IsValid(EntityManager) && !IsInSpace(lastValid))
        {
            safeCoords = lastValid;
        }
        else if (TryFindSafeGridCoordinates(ent, out var gridCoords))
        {
            safeCoords = gridCoords;
        }
        else
        {
            return false;
        }

        ent.Comp.LastValidCoordinates = safeCoords;

        _isReverting = true;
        try
        {
            _transform.SetCoordinates(ent.Owner, safeCoords);
            _transform.AttachToGridOrMap(ent.Owner);
        }
        finally
        {
            _isReverting = false;
        }

        if (TryComp<PhysicsComponent>(ent.Owner, out var physics))
        {
            _physics.SetLinearVelocity(ent.Owner, Vector2.Zero, body: physics);
        }

        return true;
    }

    public bool TryFindSafeGridCoordinates(Entity<RTSObserverComponent> ent, out EntityCoordinates safeCoordinates)
    {
        safeCoordinates = default;
        var xform = Transform(ent.Owner);
        var mapId = xform.MapID;
        if (mapId == MapId.Nullspace)
            return false;

        if (xform.GridUid is { Valid: true } currentGrid && TryComp<MapGridComponent>(currentGrid, out var currentMapGrid))
        {
            if (TryGetAnyFloorCoordinates(currentGrid, currentMapGrid, out safeCoordinates))
                return true;
        }

        var gridQuery = AllEntityQuery<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var gridUid, out var mapGrid, out var gridXform))
        {
            if (gridXform.MapID != mapId)
                continue;

            if (TryGetAnyFloorCoordinates(gridUid, mapGrid, out safeCoordinates))
                return true;
        }

        return false;
    }

    private bool TryGetAnyFloorCoordinates(EntityUid gridUid, MapGridComponent mapGrid, out EntityCoordinates coords)
    {
        coords = default;
        var tiles = _mapSystem.GetAllTilesEnumerator(gridUid, mapGrid);
        while (tiles.MoveNext(out var tileRefNullable))
        {
            if (tileRefNullable == null)
                continue;

            var tileRef = tileRefNullable.Value;
            if (tileRef.Tile.IsEmpty || _turf.IsSpace(tileRef))
                continue;

            coords = _turf.GetTileCenter(tileRef);
            return true;
        }

        return false;
    }

    #endregion
}

