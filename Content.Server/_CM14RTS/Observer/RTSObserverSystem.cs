using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.Mind;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._CM14RTS.Observer;
using Content.Shared.Actions;
using Content.Shared.Eye;
using Content.Shared.Follower.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Warps;
using Robust.Server.GameObjects;
using Robust.Server.Physics;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._CM14RTS.Observer;

public sealed class RTSObserverSystem : SharedRTSObserverSystem
{
    [Dependency] private readonly VisibilitySystem _visibility = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NPCSystem _npc = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RTSObserverComponent, ComponentStartup>(OnObserverStartup);
        SubscribeLocalEvent<RTSObserverComponent, ComponentShutdown>(OnObserverShutdown);
        SubscribeLocalEvent<RTSObserverComponent, MapInitEvent>(OnObserverMapInit);
        SubscribeLocalEvent<RTSObserverComponent, PlayerAttachedEvent>(OnPlayerAttached);

        SubscribeLocalEvent<RTSObserverComponent, RTSUnitControlActionEvent>(OnUnitControlAction);
        SubscribeLocalEvent<RTSControlledUnitComponent, RTSReturnToObserverActionEvent>(OnReturnToAction);
        SubscribeLocalEvent<RTSControlledUnitComponent, MobStateChangedEvent>(OnControlledMobStateChanged);
        SubscribeLocalEvent<RTSControlledUnitComponent, ComponentShutdown>(OnControlledShutdown);

        SubscribeLocalEvent<RTSObserverComponent, RTSObserverOpenWarpsActionEvent>(OnOpenWarpsAction);
        Subs.BuiEvents<RTSObserverComponent>(RTSObserverWarpsUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnWarpsUiOpened);
            subs.Event<RTSObserverWarpToMessage>(OnWarpToMessage);
        });

        SubscribeNetworkEvent<RTSRequestUnitControlMessage>(OnRequestUnitControl);
    }

    private void OnObserverStartup(Entity<RTSObserverComponent> ent, ref ComponentStartup args)
    {
        EnsureSafePosition(ent);
        UpdateVisibility(ent);
    }

    private void OnObserverMapInit(Entity<RTSObserverComponent> ent, ref MapInitEvent args)
    {
        EnsureSafePosition(ent);
        EnsureControlAction(ent);
        EnsureWarpsAction(ent);
    }

    private void OnObserverShutdown(Entity<RTSObserverComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ControlActionEntity != null)
        {
            _actions.RemoveAction(ent.Owner, ent.Comp.ControlActionEntity);
            ent.Comp.ControlActionEntity = null;
        }

        if (ent.Comp.WarpsActionEntity != null)
        {
            _actions.RemoveAction(ent.Owner, ent.Comp.WarpsActionEntity);
            ent.Comp.WarpsActionEntity = null;
        }

        if (Terminating(ent))
            return;

        if (TryComp<VisibilityComponent>(ent, out var vis))
        {
            _visibility.RemoveLayer((ent.Owner, vis), (ushort)(VisibilityFlags.RTSObserverUSCM | VisibilityFlags.RTSObserverHive | VisibilityFlags.RTSObserver), false);
            _visibility.AddLayer((ent.Owner, vis), (ushort)VisibilityFlags.Normal, false);
            _visibility.RefreshVisibility(ent.Owner, visibilityComponent: vis);
        }

        Eye.RefreshVisibilityMask(ent.Owner);
    }

    private void OnPlayerAttached(Entity<RTSObserverComponent> ent, ref PlayerAttachedEvent args)
    {
        EnsureSafePosition(ent);
        EnsureControlAction(ent);
        EnsureWarpsAction(ent);
        Eye.RefreshVisibilityMask(ent.Owner);
    }

    private void EnsureControlAction(Entity<RTSObserverComponent> ent)
    {
        if (ent.Comp.ControlActionEntity == null && !string.IsNullOrEmpty(ent.Comp.ControlAction))
        {
            _actions.AddAction(ent.Owner, ref ent.Comp.ControlActionEntity, ent.Comp.ControlAction);
            Dirty(ent);
        }
    }

    private void EnsureWarpsAction(Entity<RTSObserverComponent> ent)
    {
        if (ent.Comp.WarpsActionEntity == null && !string.IsNullOrEmpty(ent.Comp.WarpsAction))
        {
            _actions.AddAction(ent.Owner, ref ent.Comp.WarpsActionEntity, ent.Comp.WarpsAction);
            Dirty(ent);
        }
    }

    public override void UpdateVisibility(Entity<RTSObserverComponent> ent)
    {
        base.UpdateVisibility(ent);

        var vis = EnsureComp<VisibilityComponent>(ent);

        ushort layer = 0;
        if (!ent.Comp.HideFromLiving)
            layer |= (ushort)VisibilityFlags.Normal;

        var norm = RTSFactionHelper.Normalize(ent.Comp.Faction);
        if (norm == "marine")
            layer |= (ushort)VisibilityFlags.RTSObserverUSCM;
        else if (norm == "hive")
            layer |= (ushort)VisibilityFlags.RTSObserverHive;
        else
            layer |= (ushort)VisibilityFlags.RTSObserver;

        _visibility.SetLayer((ent.Owner, vis), layer, false);
        _visibility.RefreshVisibility(ent.Owner, visibilityComponent: vis);
        Eye.RefreshVisibilityMask(ent.Owner);
    }

    #region Unit Control (Direct Control)

    private void OnUnitControlAction(Entity<RTSObserverComponent> ent, ref RTSUnitControlActionEvent args)
    {
        if (args.Handled)
            return;

        if (TryControlUnit(ent, args.Target))
            args.Handled = true;
    }

    private void OnReturnToAction(Entity<RTSControlledUnitComponent> ent, ref RTSReturnToObserverActionEvent args)
    {
        if (args.Handled)
            return;

        if (TryReturnToObserver(ent))
            args.Handled = true;
    }

    public override void RequestUnitControl(EntityUid observer, EntityUid target)
    {
        if (TryComp<RTSObserverComponent>(observer, out var obsComp))
            TryControlUnit((observer, obsComp), target);
    }

    private void OnRequestUnitControl(RTSRequestUnitControlMessage msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } attached)
            return;

        if (!TryComp<RTSObserverComponent>(attached, out var observer))
            return;

        var target = GetEntity(msg.Target);
        TryControlUnit((attached, observer), target);
    }

    private void OnControlledMobStateChanged(Entity<RTSControlledUnitComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            _popup.PopupEntity(Loc.GetString("rts-control-unit-died"), ent.Owner, PopupType.LargeCaution);
            TryReturnToObserver(ent);
        }
    }

    private void OnControlledShutdown(Entity<RTSControlledUnitComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Observer is { Valid: true } obs && !TerminatingOrDeleted(obs))
        {
            if (_mind.TryGetMind(obs, out var mindId, out var mind) && mind.VisitingEntity == ent.Owner)
            {
                _mind.UnVisit(mindId, mind);
                Eye.RefreshVisibilityMask(obs);
            }
        }
    }

    public bool CanControlUnit(Entity<RTSObserverComponent> observer, EntityUid target, [NotNullWhen(false)] out string? reason, bool quiet = false)
    {
        reason = null;

        if (TerminatingOrDeleted(observer.Owner) || TerminatingOrDeleted(target))
        {
            reason = string.Empty;
            return false;
        }

        if (!TryComp<RTSControllableComponent>(target, out var controllable))
        {
            reason = Loc.GetString("rts-control-cant-control");
            if (!quiet)
                _popup.PopupEntity(reason, observer.Owner, observer.Owner, PopupType.MediumCaution);
            return false;
        }

        if (!RTSFactionHelper.AreFactionsCompatible(observer.Comp.Faction, controllable.Faction))
        {
            reason = Loc.GetString("rts-control-wrong-faction");
            if (!quiet)
                _popup.PopupEntity(reason, observer.Owner, observer.Owner, PopupType.MediumCaution);
            return false;
        }

        if (TryComp<MobStateComponent>(target, out var mobState) && _mobState.IsDead(target, mobState))
        {
            reason = Loc.GetString("rts-control-target-dead");
            if (!quiet)
                _popup.PopupEntity(reason, observer.Owner, observer.Owner, PopupType.MediumCaution);
            return false;
        }

        if (HasComp<VisitingMindComponent>(target))
        {
            reason = Loc.GetString("rts-control-already-controlled");
            if (!quiet)
                _popup.PopupEntity(reason, observer.Owner, observer.Owner, PopupType.MediumCaution);
            return false;
        }

        if (TryComp<ActorComponent>(target, out var actor))
        {
            if (TryComp<ActorComponent>(observer.Owner, out var obsActor) && actor.PlayerSession != obsActor.PlayerSession)
            {
                reason = Loc.GetString("rts-control-already-controlled");
                if (!quiet)
                    _popup.PopupEntity(reason, observer.Owner, observer.Owner, PopupType.MediumCaution);
                return false;
            }
        }

        if (!_mind.TryGetMind(observer.Owner, out _, out var mind))
        {
            reason = Loc.GetString("rts-control-no-mind");
            if (!quiet)
                _popup.PopupEntity(reason, observer.Owner, observer.Owner, PopupType.MediumCaution);
            return false;
        }

        if (mind.VisitingEntity != null)
        {
            reason = Loc.GetString("rts-control-already-visiting");
            if (!quiet)
                _popup.PopupEntity(reason, observer.Owner, observer.Owner, PopupType.MediumCaution);
            return false;
        }

        return true;
    }

    public bool TryControlUnit(Entity<RTSObserverComponent> observer, EntityUid target)
    {
        if (!CanControlUnit(observer, target, out _))
            return false;

        DoControlUnit(observer, target);
        return true;
    }

    public void DoControlUnit(Entity<RTSObserverComponent> observer, EntityUid target)
    {
        if (!_mind.TryGetMind(observer.Owner, out var mindId, out var mind))
            return;

        // Stop following if observer was following something
        if (TryComp<FollowerComponent>(observer.Owner, out var follower))
            Follower.StopFollowingEntity(observer.Owner, follower.Following);

        // Move observer entity to target coordinates before visiting
        _transform.SetCoordinates(observer.Owner, _transform.GetMoverCoordinates(target));

        // Sleep NPC AI if target has HTN
        if (TryComp<HTNComponent>(target, out var htn))
            _npc.SleepNPC(target, htn);

        // Visit unit
        _mind.Visit(mindId, target, mind);

        // Track controlled unit
        var controlledComp = EnsureComp<RTSControlledUnitComponent>(target);
        controlledComp.Observer = observer.Owner;

        // Add return action to unit's hotbar
        _actions.AddAction(target, ref controlledComp.ReturnAction, observer.Comp.ReturnAction);

        // Feedback popup
        _popup.PopupEntity(Loc.GetString("rts-control-success", ("unit", target)), target, target, PopupType.Medium);
    }

    public bool CanReturnToObserver(Entity<RTSControlledUnitComponent> target, out EntityUid observer)
    {
        observer = EntityUid.Invalid;
        if (target.Comp.Observer is not { Valid: true } obs || TerminatingOrDeleted(obs))
            return false;

        observer = obs;
        return true;
    }

    public bool TryReturnToObserver(Entity<RTSControlledUnitComponent> target)
    {
        if (!CanReturnToObserver(target, out var observer))
            return false;

        DoReturnToObserver(target, observer);
        return true;
    }

    public void DoReturnToObserver(Entity<RTSControlledUnitComponent> target, EntityUid observer)
    {
        // Move observer to target's position
        if (!TerminatingOrDeleted(observer))
            _transform.SetCoordinates(observer, _transform.GetMoverCoordinates(target.Owner));

        // Wake NPC AI back up if target has HTN
        if (TryComp<HTNComponent>(target.Owner, out var htn))
            _npc.WakeNPC(target.Owner, htn);

        // Remove return action from unit
        if (target.Comp.ReturnAction != null)
        {
            _actions.RemoveAction(target.Owner, target.Comp.ReturnAction);
            target.Comp.ReturnAction = null;
        }

        // Remove tracking component
        RemCompDeferred<RTSControlledUnitComponent>(target.Owner);

        // Return mind
        if (_mind.TryGetMind(observer, out var mindId, out var mind))
        {
            if (mind.VisitingEntity == target.Owner)
            {
                _mind.UnVisit(mindId, mind);
            }

            if (mind.UserId != null && _players.TryGetSessionById(mind.UserId.Value, out var session))
            {
                if (session.AttachedEntity != observer)
                    _players.SetAttachedEntity(session, observer);
            }
        }

        if (!TerminatingOrDeleted(observer))
        {
            if (TryComp<RTSObserverComponent>(observer, out var obsComp))
                EnsureSafePosition((observer, obsComp));

            Eye.RefreshVisibilityMask(observer);
            _popup.PopupEntity(Loc.GetString("rts-control-returned"), observer, observer);
        }
    }

    #endregion

    #region Observer Warps (Fast Travel)

    private void OnOpenWarpsAction(Entity<RTSObserverComponent> ent, ref RTSObserverOpenWarpsActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _ui.TryToggleUi(ent.Owner, RTSObserverWarpsUiKey.Key, ent.Owner);
    }

    private void OnWarpsUiOpened(Entity<RTSObserverComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateWarpsState(ent);
    }

    private void OnWarpToMessage(Entity<RTSObserverComponent> ent, ref RTSObserverWarpToMessage args)
    {
        if (!TryGetEntity(args.Target, out var target))
            return;

        TryWarpTo(ent, target.Value);
    }

    public void UpdateWarpsState(Entity<RTSObserverComponent> ent)
    {
        var warps = new List<RTSObserverWarpPoint>();
        var seenLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Warp Points (map landmarks, rooms, vehicles, shuttles, etc.)
        var warpQuery = AllEntityQuery<WarpPointComponent, MetaDataComponent, TransformComponent>();
        while (warpQuery.MoveNext(out var uid, out var warp, out var meta, out var xform))
        {
            if (IsInSpace(xform.Coordinates))
                continue;

            var name = warp.Location;
            if (string.IsNullOrWhiteSpace(name))
                name = meta.EntityName;

            if (string.IsNullOrWhiteSpace(name) || name.Equals("warp point", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!seenLocations.Add(name))
                continue;

            warps.Add(new RTSObserverWarpPoint(GetNetEntity(uid), name, "Warp"));
        }

        // Sort alphabetically by Name
        warps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        _ui.SetUiState(ent.Owner, RTSObserverWarpsUiKey.Key, new RTSObserverWarpsBuiState(warps));
    }

    public bool TryWarpTo(Entity<RTSObserverComponent> ent, EntityUid target)
    {
        if (!CanWarpTo(ent, target))
            return false;

        DoWarpTo(ent, target);
        return true;
    }

    public bool CanWarpTo(Entity<RTSObserverComponent> ent, EntityUid target, bool quiet = false)
    {
        if (!Exists(ent) || !Exists(target))
            return false;

        if (!HasComp<TransformComponent>(ent) || !HasComp<TransformComponent>(target))
            return false;

        if (IsInSpace(Transform(target).Coordinates))
        {
            if (!quiet)
                _popup.PopupEntity(Loc.GetString("rts-observer-space-warp-blocked"), ent.Owner, ent.Owner, PopupType.MediumCaution);
            return false;
        }

        return true;
    }

    public void DoWarpTo(Entity<RTSObserverComponent> ent, EntityUid target)
    {
        var xform = Transform(ent.Owner);
        var targetCoords = Transform(target).Coordinates;
        if (IsInSpace(targetCoords))
            return;

        _transform.SetCoordinates(ent.Owner, xform, targetCoords);
        _transform.AttachToGridOrMap(ent.Owner, xform);

        ent.Comp.LastValidCoordinates = targetCoords;

        if (TryComp<PhysicsComponent>(ent.Owner, out var physics))
            _physics.SetLinearVelocity(ent.Owner, Vector2.Zero, body: physics);

        var targetName = Name(target);
        if (TryComp<WarpPointComponent>(target, out var warp) && !string.IsNullOrWhiteSpace(warp.Location))
            targetName = warp.Location;

        _popup.PopupEntity(Loc.GetString("rts-observer-warped-to", ("target", targetName)), ent.Owner, ent.Owner);
    }

    #endregion
}
