using Content.Shared._CM14RTS.CCVar;
using Content.Shared._CM14RTS.Observer;
using Content.Shared._CM14RTS.Vision;
using Content.Shared._RMC14.Entrenching;
using Content.Shared._RMC14.Sentry;
using Content.Shared._RMC14.Xenonids.Construction;
using Content.Shared._RMC14.Xenonids.Construction.Tunnel;
using Content.Shared._RMC14.Xenonids.Hive;
using Content.Shared._RMC14.Xenonids.Weeds;
using Content.Shared.Doors.Components;
using Content.Shared.Tag;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._CM14RTS.Vision;

/// <summary>
/// Manages map memory and structure snapshots under the RTS Fog of War.
/// Pre-existing colony map structures (walls, doors, fortifications) are known to the commander.
/// In fogged areas, a terrain snapshot is displayed.
/// If enemies build barricades, resin walls, or tunnels in the fog, their live sprites remain hidden until scouted by an ally.
/// If walls or structures are breached/destroyed in the fog, a snapshot marker remains until an ally illuminates the tile.
/// </summary>
public sealed class RTSMapMemorySystem : EntitySystem
{
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

    // 1) Dependencies
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _playerMan = default!;
    [Dependency] private readonly SharedRTSVisionSystem _vision = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    // 2) Constants + static readonly
    private const float UpdateInterval = 1f / 10f; // 10 Hz is optimal for map memory scouting updates

    // 3) Runtime state / cache
    private float _accumulator;
    private bool _initialized;
    private string? _activeFaction;

    private readonly HashSet<EntityUid> _undiscoveredStructures = new();
    private readonly Dictionary<EntityUid, RTSStructureMemoryMarkerComponent> _activeMemoryMarkers = new();
    private readonly List<EntityUid> _toDiscover = new();
    private readonly List<EntityUid> _toDissolve = new();

    // 4) Lifecycle
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RTSDiscoveredStructureComponent, EntityTerminatingEvent>(OnDiscoveredTerminating);
        SubscribeLocalEvent<RTSStructureMemoryMarkerComponent, EntityTerminatingEvent>(OnMarkerTerminating);
        SubscribeLocalEvent<TransformComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ResetAll();
    }

    // 5) Event handlers

    private void OnAnchorStateChanged(EntityUid uid, TransformComponent xform, ref AnchorStateChangedEvent args)
    {
        if (!_cfg.GetCVar(CM14RTSCVars.RTSFogOfWarEnabled) || _activeFaction == null)
            return;

        if (!IsTrackedStructure(uid))
            return;

        if (args.Anchored)
        {
            if (TryComp<SpriteComponent>(uid, out var sprite))
            {
                ProcessStructure(uid, xform, sprite, _activeFaction, isInitialLoad: false);
            }
        }
        else
        {
            // Structure unanchored / deconstructed
            if (HasComp<RTSDiscoveredStructureComponent>(uid))
            {
                if (_vision.TryGetTile(uid, xform, out var gridUid, out var tile) &&
                    !_vision.IsTileVisible(gridUid, tile))
                {
                    SpawnMemoryMarker(uid, xform, null, gridUid, tile);
                }
                RemComp<RTSDiscoveredStructureComponent>(uid);
            }

            _undiscoveredStructures.Remove(uid);
        }
    }

    private void OnDiscoveredTerminating(Entity<RTSDiscoveredStructureComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!_cfg.GetCVar(CM14RTSCVars.RTSFogOfWarEnabled) || _activeFaction == null)
            return;

        if (HasComp<RTSStructureMemoryMarkerComponent>(ent.Owner))
            return;

        if (!IsTrackedStructure(ent.Owner))
            return;

        if (!TryComp(ent.Owner, out TransformComponent? xform) ||
            !_vision.TryGetTile(ent.Owner, xform, out var gridUid, out var tile))
            return;

        // If destroyed while hidden inside the fog, leave a memory snapshot marker
        if (!_vision.IsTileVisible(gridUid, tile))
        {
            SpawnMemoryMarker(ent.Owner, xform, null, gridUid, tile);
        }
    }

    private void OnMarkerTerminating(Entity<RTSStructureMemoryMarkerComponent> ent, ref EntityTerminatingEvent args)
    {
        _activeMemoryMarkers.Remove(ent.Owner);
    }

    // 6) Main Logic
    public void ProcessStructure(EntityUid uid, TransformComponent xform, SpriteComponent sprite, string playerFaction, bool isInitialLoad)
    {
        if (HasComp<RTSStructureMemoryMarkerComponent>(uid))
            return;

        if (!IsTrackedStructure(uid))
            return;

        if (!_vision.TryGetTile(uid, xform, out var gridUid, out var tile))
            return;

        // If a real live structure exists on this tile, remove any phantom memory marker on it
        EntityUid? toRemoveMarker = null;
        foreach (var (markerUid, markerComp) in _activeMemoryMarkers)
        {
            if (markerComp.GridUid == gridUid && markerComp.GridTile == tile)
            {
                toRemoveMarker = markerUid;
                break;
            }
        }
        if (toRemoveMarker != null)
        {
            QueueDel(toRemoveMarker.Value);
            _activeMemoryMarkers.Remove(toRemoveMarker.Value);
        }

        if (HasComp<RTSDiscoveredStructureComponent>(uid) || _undiscoveredStructures.Contains(uid))
            return;

        bool isVisible = _vision.IsTileVisible(gridUid, tile);

        if (isVisible)
        {
            EnsureComp<RTSDiscoveredStructureComponent>(uid);
            _sprite.SetVisible((uid, sprite), true);
        }
        else
        {
            if (isInitialLoad)
            {
                if (IsEnemyOrTacticalStructure(uid, playerFaction))
                {
                    _undiscoveredStructures.Add(uid);
                    _sprite.SetVisible((uid, sprite), false);
                }
                else
                {
                    // Standard neutral colony blueprint (walls, doors, fixtures)
                    EnsureComp<RTSDiscoveredStructureComponent>(uid);
                    _sprite.SetVisible((uid, sprite), true);
                }
            }
            else
            {
                // Dynamic structure constructed / anchored in the fog
                _undiscoveredStructures.Add(uid);
                _sprite.SetVisible((uid, sprite), false);
            }
        }
    }

    // 7) Periodic Update
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_cfg.GetCVar(CM14RTSCVars.RTSFogOfWarEnabled))
        {
            if (_activeFaction != null)
            {
                ResetAll();
            }
            return;
        }

        var localEnt = _playerMan.LocalEntity;
        if (localEnt == null || !_vision.TryGetPlayerFaction(localEnt.Value, out var playerFaction))
        {
            if (_activeFaction != null)
            {
                ResetAll();
            }
            return;
        }

        if (_activeFaction != null && !RTSFactionHelper.AreFactionsCompatible(_activeFaction, playerFaction))
        {
            ResetAll();
        }
        _activeFaction = playerFaction;

        // Perform initial map blueprint scan once faction is known
        if (!_initialized)
        {
            var query = EntityQueryEnumerator<TransformComponent, SpriteComponent>();
            while (query.MoveNext(out var uid, out var xform, out var sprite))
            {
                if (!xform.Anchored || !IsTrackedStructure(uid))
                    continue;

                ProcessStructure(uid, xform, sprite, playerFaction, isInitialLoad: true);
            }
            _initialized = true;
        }

        _accumulator -= frameTime;
        if (_accumulator > 0f)
            return;

        _accumulator = UpdateInterval;

        // 1. Check undiscovered structures: if an ally illuminates the tile, discover and reveal them
        _toDiscover.Clear();
        foreach (var uid in _undiscoveredStructures)
        {
            if (!Exists(uid) ||
                !TryComp(uid, out TransformComponent? xform) ||
                !TryComp(uid, out SpriteComponent? sprite))
            {
                _toDiscover.Add(uid);
                continue;
            }

            if (!_vision.TryGetTile(uid, xform, out var gridUid, out var tile))
                continue;

            if (_vision.IsTileVisible(gridUid, tile))
            {
                EnsureComp<RTSDiscoveredStructureComponent>(uid);
                _sprite.SetVisible((uid, sprite), true);
                _toDiscover.Add(uid);
            }
        }

        foreach (var uid in _toDiscover)
        {
            _undiscoveredStructures.Remove(uid);
        }

        // 2. Check active memory markers: if an ally illuminates the tile, dissolve marker to reveal the breach
        _toDissolve.Clear();
        foreach (var (markerUid, markerComp) in _activeMemoryMarkers)
        {
            if (Deleted(markerUid))
            {
                _toDissolve.Add(markerUid);
                continue;
            }

            if (_vision.IsTileVisible(markerComp.GridUid, markerComp.GridTile))
            {
                DissolveMarker(markerUid);
                _toDissolve.Add(markerUid);
            }
        }

        foreach (var markerUid in _toDissolve)
        {
            _activeMemoryMarkers.Remove(markerUid);
        }
    }

    // 8) Helpers

    public bool IsTrackedStructure(EntityUid uid)
    {
        if (_tag.HasTag(uid, WallTag) ||
            _tag.HasTag(uid, WindowTag) ||
            HasComp<DoorComponent>(uid) ||
            HasComp<BarricadeComponent>(uid) ||
            HasComp<XenoWeedsComponent>(uid) ||
            HasComp<XenoConstructionSupportComponent>(uid) ||
            HasComp<XenoTunnelComponent>(uid) ||
            HasComp<HiveMemberComponent>(uid))
        {
            return true;
        }

        return false;
    }

    private bool IsEnemyOrTacticalStructure(EntityUid uid, string playerFaction)
    {
        if (_vision.IsEnemy(uid, playerFaction))
            return true;

        if (!RTSFactionHelper.AreFactionsCompatible(playerFaction, "Hive"))
        {
            if (HasComp<XenoWeedsComponent>(uid) ||
                HasComp<XenoConstructionSupportComponent>(uid) ||
                HasComp<XenoTunnelComponent>(uid) ||
                HasComp<HiveMemberComponent>(uid))
            {
                return true;
            }
        }

        if (RTSFactionHelper.AreFactionsCompatible(playerFaction, "Hive"))
        {
            if (HasComp<BarricadeComponent>(uid) ||
                HasComp<SentryComponent>(uid))
            {
                return true;
            }
        }

        if (HasComp<BarricadeComponent>(uid))
            return true;

        return false;
    }

    private void SpawnMemoryMarker(EntityUid sourceUid, TransformComponent xform, SpriteComponent? sprite, EntityUid gridUid, Vector2i tile)
    {
        if (sprite == null && !TryComp(sourceUid, out sprite))
            return;

        if (!xform.Coordinates.IsValid(EntityManager))
            return;

        // Avoid duplicate overlapping markers on the same tile
        foreach (var markerComp in _activeMemoryMarkers.Values)
        {
            if (markerComp.GridUid == gridUid && markerComp.GridTile == tile)
                return;
        }

        // Verify there is not already another live tracked structure on this tile
        if (TryComp<MapGridComponent>(gridUid, out var gridComp))
        {
            var enumerator = _maps.GetAnchoredEntitiesEnumerator(gridUid, gridComp, tile);
            while (enumerator.MoveNext(out var ent))
            {
                if (ent.Value != sourceUid && !TerminatingOrDeleted(ent.Value) && IsTrackedStructure(ent.Value))
                    return;
            }
        }

        var marker = Spawn("RTSStructureMemoryMarker", xform.Coordinates);
        var markerSprite = Comp<SpriteComponent>(marker);

        _sprite.CopySprite(new Entity<SpriteComponent?>(sourceUid, sprite), new Entity<SpriteComponent?>(marker, markerSprite));
        _sprite.SetVisible((marker, markerSprite), true);
        _transform.SetLocalRotationNoLerp(marker, xform.LocalRotation);

        _metaData.SetEntityName(marker, Name(sourceUid));
        _metaData.SetEntityDescription(marker, Description(sourceUid));

        var memoryComp = Comp<RTSStructureMemoryMarkerComponent>(marker);
        memoryComp.GridUid = gridUid;
        memoryComp.GridTile = tile;

        _activeMemoryMarkers[marker] = memoryComp;
    }

    private void DissolveMarker(EntityUid marker)
    {
        if (Deleted(marker))
            return;

        var dissolve = EnsureComp<RTSGhostDissolveComponent>(marker);
        dissolve.Elapsed = 0f;
        dissolve.FadeTime = 0.35f;

        if (TryComp<SpriteComponent>(marker, out var sprite))
        {
            dissolve.InitialColor = sprite.Color;
        }
    }

    private void ResetAll()
    {
        foreach (var marker in _activeMemoryMarkers.Keys)
        {
            if (Exists(marker))
                QueueDel(marker);
        }
        _activeMemoryMarkers.Clear();

        foreach (var uid in _undiscoveredStructures)
        {
            if (Exists(uid) && TryComp<SpriteComponent>(uid, out var sprite))
            {
                _sprite.SetVisible((uid, sprite), true);
            }
        }
        _undiscoveredStructures.Clear();

        var query = EntityQueryEnumerator<RTSDiscoveredStructureComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemComp<RTSDiscoveredStructureComponent>(uid);
        }

        _initialized = false;
        _activeFaction = null;
    }
}
