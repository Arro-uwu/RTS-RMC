using Content.Shared._CM14RTS.CCVar;
using Content.Shared._CM14RTS.Observer;
using Content.Shared._CM14RTS.Vision;
using Content.Shared._RMC14.NightVision;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.Client._CM14RTS.Vision;

/// <summary>
/// Manages enemy visibility under the RTS Fog of War and maintains last-known silhouette markers ("ghosts").
/// While an enemy is in active line-of-sight (or sensed via Xeno Night Vision), its visual is updated live.
/// When it enters fog of war, its live sprite is hidden and replaced by a semi-transparent ghost marker at the disappearing spot.
/// When allies re-illuminate the tile: if the enemy ran away, the silhouette dissolves; if still there, it is replaced by the live enemy.
/// </summary>
public sealed class RTSGhostMarkerSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _playerMan = default!;
    [Dependency] private readonly SharedRTSVisionSystem _vision = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;

    private float _updateRate = 1f / 30f;
    private float _accumulator;

    private string? _activeFaction;
    private readonly Dictionary<EntityUid, TrackedEnemyData> _trackedEnemies = new();
    private readonly Dictionary<EntityUid, EntityUid> _ghostToEnemy = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RTSControllableComponent, ComponentStartup>(OnEnemyStartup);
        SubscribeLocalEvent<RTSControllableComponent, EntityTerminatingEvent>(OnEnemyTerminating);
        SubscribeLocalEvent<RTSGhostMarkerComponent, EntityTerminatingEvent>(OnGhostTerminating);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ResetAll();
    }

    private void OnEnemyStartup(Entity<RTSControllableComponent> ent, ref ComponentStartup args)
    {
        if (!_cfg.GetCVar(CM14RTSCVars.RTSFogOfWarEnabled))
            return;

        var localEnt = _playerMan.LocalEntity;
        if (localEnt == null || !_vision.TryGetPlayerFaction(localEnt.Value, out var playerFaction))
            return;

        if (!_vision.IsEnemy(ent.Owner, playerFaction, ent.Comp))
            return;

        if (!TryComp<SpriteComponent>(ent.Owner, out var sprite) ||
            !TryComp(ent.Owner, out TransformComponent? xform))
            return;

        if (!_vision.TryGetTile(ent.Owner, xform, out var gridUid, out var tile))
            return;

        bool hasActiveNightVision = TryComp<NightVisionComponent>(localEnt.Value, out var nv) && nv.State != NightVisionState.Off;
        bool isNightVisionVisible = hasActiveNightVision && HasComp<RMCNightVisionVisibleComponent>(ent.Owner);

        // If newly entering PVS directly inside fog and not seen by Night Vision, hide immediately to prevent 1-frame pop-in
        if (!_vision.IsTileVisible(gridUid, tile) && !isNightVisionVisible)
        {
            _sprite.SetVisible((ent.Owner, sprite), false);
        }
    }

    private void OnEnemyTerminating(Entity<RTSControllableComponent> ent, ref EntityTerminatingEvent args)
    {
        if (_trackedEnemies.TryGetValue(ent.Owner, out var data))
        {
            // If the enemy has no ghost marker, remove tracked entry
            if (data.GhostMarker == null)
            {
                _trackedEnemies.Remove(ent.Owner);
            }
        }
    }

    private void OnGhostTerminating(Entity<RTSGhostMarkerComponent> ent, ref EntityTerminatingEvent args)
    {
        if (_ghostToEnemy.Remove(ent.Owner, out var enemyUid) &&
            _trackedEnemies.TryGetValue(enemyUid, out var data))
        {
            if (data.GhostMarker == ent.Owner)
                data.GhostMarker = null;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // 1. Process active dissolving silhouettes
        var dissolveQuery = EntityQueryEnumerator<RTSGhostDissolveComponent, SpriteComponent>();
        while (dissolveQuery.MoveNext(out var ghostUid, out var dissolve, out var ghostSprite))
        {
            dissolve.Elapsed += frameTime;
            var progress = Math.Clamp(dissolve.Elapsed / dissolve.FadeTime, 0f, 1f);

            if (progress >= 1f)
            {
                QueueDel(ghostUid);
            }
            else
            {
                var alpha = MathHelper.Lerp(dissolve.InitialColor.A, 0f, progress);
                _sprite.SetColor((ghostUid, ghostSprite), dissolve.InitialColor.WithAlpha(alpha));
            }
        }

        // 2. Throttle main vision & ghost tracking loop
        _accumulator -= frameTime;
        if (_accumulator > 0f)
            return;

        _accumulator = _updateRate;

        // If RTS Fog of War is disabled, ensure all sprites are visible and clean up ghosts
        if (!_cfg.GetCVar(CM14RTSCVars.RTSFogOfWarEnabled))
        {
            if (_activeFaction != null)
            {
                _activeFaction = null;
                ResetAll();
            }
            return;
        }

        var localEnt = _playerMan.LocalEntity;
        if (localEnt == null || !_vision.TryGetPlayerFaction(localEnt.Value, out var playerFaction))
        {
            // Only reset if player truly lost RTS faction / disconnected
            if (_activeFaction != null)
            {
                _activeFaction = null;
                ResetAll();
            }
            return;
        }

        // If player faction actually changed (e.g. admin switched sides from USCM to Hive), reset
        if (_activeFaction != null && !RTSFactionHelper.AreFactionsCompatible(_activeFaction, playerFaction))
        {
            ResetAll();
        }
        _activeFaction = playerFaction;

        bool hasActiveNightVision = false;
        if (TryComp<NightVisionComponent>(localEnt.Value, out var nv) && nv.State != NightVisionState.Off)
        {
            hasActiveNightVision = true;
        }

        // 3. Scan all enemy entities currently in PVS on the client
        var enemyQuery = EntityQueryEnumerator<RTSControllableComponent, SpriteComponent, TransformComponent>();
        while (enemyQuery.MoveNext(out var uid, out var controllable, out var sprite, out var xform))
        {
            if (!_vision.IsEnemy(uid, playerFaction, controllable))
                continue;

            if (!_vision.TryGetTile(uid, xform, out var gridUid, out var tile))
                continue;

            bool isTileVisible = _vision.IsTileVisible(gridUid, tile);

            // If local player has active Night Vision (e.g. Xenos) and enemy has thermal visibility,
            // the xeno actively senses the live enemy through walls/fog in real-time.
            bool isNightVisionVisible = hasActiveNightVision && HasComp<RMCNightVisionVisibleComponent>(uid);

            if (!_trackedEnemies.TryGetValue(uid, out var data))
            {
                data = new TrackedEnemyData { Entity = uid };
                _trackedEnemies[uid] = data;
            }

            if (isTileVisible || isNightVisionVisible)
            {
                // In direct LOS or actively sensed live by Night Vision
                data.WasVisible = true;
                data.LastSeenCoordinates = xform.Coordinates;
                data.LastSeenRotation = xform.LocalRotation;
                data.LastSeenGridUid = gridUid;
                data.LastSeenGridTile = tile;

                _sprite.SetVisible((uid, sprite), true);

                // Any existing ghost marker is replaced by the live enemy
                if (data.GhostMarker is { Valid: true } oldGhost)
                {
                    QueueDel(oldGhost);
                    _ghostToEnemy.Remove(oldGhost);
                    data.GhostMarker = null;
                }
            }
            else
            {
                // In Fog of War and not detected by Night Vision: live sprite is hidden
                _sprite.SetVisible((uid, sprite), false);

                // If this enemy was previously visible and just entered the fog,
                // record their disappearing spot and spawn their last-known ghost marker.
                if (data.WasVisible && data.GhostMarker == null)
                {
                    data.WasVisible = false;
                    data.LastSeenCoordinates = xform.Coordinates;
                    data.LastSeenRotation = xform.LocalRotation;
                    data.LastSeenGridUid = gridUid;
                    data.LastSeenGridTile = tile;

                    SpawnGhost(data, (uid, sprite));
                }
            }
        }

        // 4. Check active ghost markers:
        // When an ally re-illuminates the tile (or NightVision senses the enemy):
        // if enemy ran away -> dissolves; if still there -> replaced by live enemy.
        foreach (var (enemyUid, data) in _trackedEnemies)
        {
            if (data.GhostMarker is not { Valid: true } ghost)
                continue;

            bool tileIlluminated = _vision.IsTileVisible(data.LastSeenGridUid, data.LastSeenGridTile);
            bool sensedByNightVision = hasActiveNightVision && Exists(data.Entity) && HasComp<RMCNightVisionVisibleComponent>(data.Entity);

            if (tileIlluminated || sensedByNightVision)
            {
                bool enemyStillHere = false;
                if (Exists(data.Entity) &&
                    TryComp(data.Entity, out TransformComponent? enemyXform) &&
                    _vision.TryGetTile(data.Entity, enemyXform, out var curGrid, out var curTile))
                {
                    if (curGrid == data.LastSeenGridUid && curTile == data.LastSeenGridTile)
                    {
                        enemyStillHere = true;
                    }
                }

                if (enemyStillHere || sensedByNightVision)
                {
                    // Enemy remained on the tile or is actively sensed by NightVision: replaced by live enemy
                    QueueDel(ghost);
                    _ghostToEnemy.Remove(ghost);
                    data.GhostMarker = null;
                }
                else
                {
                    // Enemy moved away or died in the fog: silhouette dissolves
                    DissolveGhost(ghost);
                    _ghostToEnemy.Remove(ghost);
                    data.GhostMarker = null;
                    data.LastSeenCoordinates = EntityCoordinates.Invalid;
                }
            }
        }
    }

    private void SpawnGhost(TrackedEnemyData data, Entity<SpriteComponent> enemy)
    {
        if (!data.LastSeenCoordinates.IsValid(EntityManager))
            return;

        var ghost = Spawn("RTSGhostMarker", data.LastSeenCoordinates);
        var ghostSprite = Comp<SpriteComponent>(ghost);

        // Deep copy all layers, RSI states, and clothes from enemy to ghost
        _sprite.CopySprite(new Entity<SpriteComponent?>(enemy.Owner, enemy.Comp), new Entity<SpriteComponent?>(ghost, ghostSprite));
        _sprite.SetVisible((ghost, ghostSprite), true);

        // Semi-transparent ghostly silhouette appearance
        var ghostColor = new Color(0.75f, 0.85f, 1f, 0.45f);
        _sprite.SetColor((ghost, ghostSprite), ghostColor);

        // Match rotation
        _transform.SetLocalRotationNoLerp(ghost, data.LastSeenRotation);

        _metaData.SetEntityName(ghost, Name(enemy.Owner));
        _metaData.SetEntityDescription(ghost, Description(enemy.Owner));

        var markerComp = EnsureComp<RTSGhostMarkerComponent>(ghost);
        markerComp.TrackedEntity = data.Entity;
        markerComp.GridUid = data.LastSeenGridUid;
        markerComp.GridTile = data.LastSeenGridTile;

        data.GhostMarker = ghost;
        _ghostToEnemy[ghost] = data.Entity;
    }

    private void DissolveGhost(EntityUid ghost)
    {
        if (Deleted(ghost))
            return;

        var dissolve = EnsureComp<RTSGhostDissolveComponent>(ghost);
        dissolve.Elapsed = 0f;
        dissolve.FadeTime = 0.35f;

        if (TryComp<SpriteComponent>(ghost, out var sprite))
        {
            dissolve.InitialColor = sprite.Color;
        }
    }

    private void ResetAll()
    {
        foreach (var data in _trackedEnemies.Values)
        {
            if (data.GhostMarker is { Valid: true } ghost)
            {
                QueueDel(ghost);
            }

            if (Exists(data.Entity) && TryComp<SpriteComponent>(data.Entity, out var sprite))
            {
                _sprite.SetVisible((data.Entity, sprite), true);
            }
        }

        _trackedEnemies.Clear();
        _ghostToEnemy.Clear();
    }

    private sealed class TrackedEnemyData
    {
        public EntityUid Entity;
        public bool WasVisible;
        public EntityCoordinates LastSeenCoordinates;
        public Angle LastSeenRotation;
        public EntityUid LastSeenGridUid;
        public Vector2i LastSeenGridTile;
        public EntityUid? GhostMarker;
    }
}
