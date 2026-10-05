using System.Diagnostics.CodeAnalysis;
using Content.Server.Mind;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._CM14RTS.Observer;
using Content.Shared.Actions;
using Content.Shared.Eye;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Player;

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

        SubscribeNetworkEvent<RTSRequestUnitControlMessage>(OnRequestUnitControl);
    }

    private void OnObserverStartup(Entity<RTSObserverComponent> ent, ref ComponentStartup args)
    {
        UpdateVisibility(ent);
    }

    private void OnObserverMapInit(Entity<RTSObserverComponent> ent, ref MapInitEvent args)
    {
        EnsureControlAction(ent);
    }

    private void OnObserverShutdown(Entity<RTSObserverComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ControlActionEntity != null)
        {
            _actions.RemoveAction(ent.Owner, ent.Comp.ControlActionEntity);
            ent.Comp.ControlActionEntity = null;
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
        EnsureControlAction(ent);
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
            Eye.RefreshVisibilityMask(observer);
            _popup.PopupEntity(Loc.GetString("rts-control-returned"), observer, observer);
        }
    }

    #endregion
}
