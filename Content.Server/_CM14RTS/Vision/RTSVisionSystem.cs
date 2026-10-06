using System.Collections.Generic;
using System.Linq;
using Content.Server._RMC14.Chat.Chat;
using Content.Server.Mind;
using Content.Shared._CM14RTS.Observer;
using Content.Shared._CM14RTS.Vision;
using Content.Shared.Chat;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._CM14RTS.Vision;

public sealed class RTSVisionSystem : SharedRTSVisionSystem
{
    [Dependency] private readonly ViewSubscriberSystem _viewSubscriber = default!;
    [Dependency] private readonly MindSystem _mind = default!;

    private readonly List<ICommonSession> _chatRemoveSessions = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);

        SubscribeLocalEvent<RTSVisionSourceComponent, ComponentShutdown>(OnVisionSourceShutdown);
        SubscribeLocalEvent<RTSVisionSourceComponent, MobStateChangedEvent>(OnVisionMobStateChanged);

        SubscribeLocalEvent<TransformComponent, ChatMessageAfterGetRecipientsEvent>(OnChatGetRecipients);
    }

    private void OnChatGetRecipients(Entity<TransformComponent> ent, ref ChatMessageAfterGetRecipientsEvent args)
    {
        if ((args.Channel & (ChatChannel.Local | ChatChannel.Whisper | ChatChannel.Emotes)) == 0)
            return;

        _chatRemoveSessions.Clear();
        foreach (var (session, _) in args.Recipients)
        {
            if (session.AttachedEntity is not { Valid: true } attached)
                continue;

            if (!TryComp<RTSObserverComponent>(attached, out var observer))
                continue;

            // If the speaker is an enemy to this observer's faction, check if any allied unit has vision on the speaker
            if (IsEnemy(ent.Owner, observer.Faction))
            {
                if (!CanFactionSeeEntity(ent.Owner, ent.Comp, observer.Faction))
                {
                    _chatRemoveSessions.Add(session);
                }
            }
        }

        foreach (var session in _chatRemoveSessions)
        {
            args.Recipients.Remove(session);
        }
    }

    private bool CanFactionSeeEntity(EntityUid target, TransformComponent targetXform, string faction)
    {
        var targetPos = targetXform.Coordinates;
        var query = EntityQueryEnumerator<RTSVisionSourceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var vision, out var xform))
        {
            if (!vision.Enabled || !IsVisionActive(uid, vision))
                continue;

            var sourceFaction = ResolveFaction((uid, vision));
            if (!RTSFactionHelper.AreFactionsCompatible(faction, sourceFaction))
                continue;

            if (xform.Coordinates.TryDistance(EntityManager, targetPos, out var dist))
            {
                var radius = GetVisionRadius(uid, vision);
                if (dist <= radius)
                    return true;
            }
        }

        return false;
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (!TryComp<RTSObserverComponent>(args.Entity, out var observer))
            return;

        SubscribeObserverToAllAllies(args.Entity, observer, args.Player);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        if (!TryComp<RTSObserverComponent>(args.Entity, out _))
            return;

        // If the player detached because they are visiting a controlled unit, keep subscriptions active.
        if (_mind.TryGetMind(args.Entity, out _, out var mind) && mind.VisitingEntity != null)
            return;

        UnsubscribeSessionFromAll(args.Player);
    }

    protected override void OnVisionSourceStartup(Entity<RTSVisionSourceComponent> ent, ref ComponentStartup args)
    {
        base.OnVisionSourceStartup(ent, ref args);

        if (!IsVisionActive(ent.Owner, ent.Comp))
            return;

        var faction = ResolveFaction(ent);
        if (string.IsNullOrWhiteSpace(faction))
            return;

        var sessions = GetCommanderSessionsForFaction(faction);
        foreach (var session in sessions)
        {
            _viewSubscriber.AddViewSubscriber(ent.Owner, session);
        }
    }

    private void OnVisionSourceShutdown(Entity<RTSVisionSourceComponent> ent, ref ComponentShutdown args)
    {
        var faction = ResolveFaction(ent);
        if (string.IsNullOrWhiteSpace(faction))
            return;

        var sessions = GetCommanderSessionsForFaction(faction);
        foreach (var session in sessions)
        {
            _viewSubscriber.RemoveViewSubscriber(ent.Owner, session);
        }
    }

    private void OnVisionMobStateChanged(Entity<RTSVisionSourceComponent> ent, ref MobStateChangedEvent args)
    {
        var faction = ResolveFaction(ent);
        if (string.IsNullOrWhiteSpace(faction))
            return;

        var sessions = GetCommanderSessionsForFaction(faction);
        if (args.NewMobState == MobState.Dead)
        {
            foreach (var session in sessions)
            {
                _viewSubscriber.RemoveViewSubscriber(ent.Owner, session);
            }
        }
        else if (args.OldMobState == MobState.Dead && args.NewMobState != MobState.Dead && ent.Comp.Enabled)
        {
            foreach (var session in sessions)
            {
                _viewSubscriber.AddViewSubscriber(ent.Owner, session);
            }
        }
    }

    public override void SetEnabled(Entity<RTSVisionSourceComponent?> ent, bool enabled)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false))
            return;

        if (ent.Comp.Enabled == enabled)
            return;

        base.SetEnabled(ent, enabled);

        var faction = ResolveFaction((ent.Owner, ent.Comp));
        if (string.IsNullOrWhiteSpace(faction))
            return;

        var sessions = GetCommanderSessionsForFaction(faction);
        foreach (var session in sessions)
        {
            if (enabled && IsVisionActive(ent.Owner, ent.Comp))
                _viewSubscriber.AddViewSubscriber(ent.Owner, session);
            else
                _viewSubscriber.RemoveViewSubscriber(ent.Owner, session);
        }
    }

    private void SubscribeObserverToAllAllies(EntityUid observerUid, RTSObserverComponent observer, ICommonSession session)
    {
        var faction = observer.Faction;
        var query = AllEntityQuery<RTSVisionSourceComponent>();
        while (query.MoveNext(out var uid, out var vision))
        {
            var sourceFaction = ResolveFaction((uid, vision));
            if (RTSFactionHelper.AreFactionsCompatible(faction, sourceFaction) &&
                IsVisionActive(uid, vision))
            {
                _viewSubscriber.AddViewSubscriber(uid, session);
            }
        }
    }

    private void UnsubscribeSessionFromAll(ICommonSession session)
    {
        foreach (var eye in session.ViewSubscriptions.ToArray())
        {
            if (HasComp<RTSVisionSourceComponent>(eye))
            {
                _viewSubscriber.RemoveViewSubscriber(eye, session);
            }
        }
    }

    private List<ICommonSession> GetCommanderSessionsForFaction(string faction)
    {
        var sessions = new List<ICommonSession>();

        // 1. Observers with attached players
        var obsQuery = AllEntityQuery<RTSObserverComponent, ActorComponent>();
        while (obsQuery.MoveNext(out _, out var observer, out var actor))
        {
            if (RTSFactionHelper.AreFactionsCompatible(observer.Faction, faction) && !sessions.Contains(actor.PlayerSession))
            {
                sessions.Add(actor.PlayerSession);
            }
        }

        // 2. Commanders visiting/controlling a unit
        var controlledQuery = AllEntityQuery<RTSControlledUnitComponent, ActorComponent>();
        while (controlledQuery.MoveNext(out _, out var controlled, out var actor))
        {
            if (controlled.Observer is { Valid: true } obs &&
                TryComp<RTSObserverComponent>(obs, out var observer) &&
                RTSFactionHelper.AreFactionsCompatible(observer.Faction, faction) &&
                !sessions.Contains(actor.PlayerSession))
            {
                sessions.Add(actor.PlayerSession);
            }
        }

        return sessions;
    }
}
