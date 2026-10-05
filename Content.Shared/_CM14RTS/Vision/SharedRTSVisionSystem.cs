using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._CM14RTS.Observer;
using Content.Shared._RMC14.Marines;
using Content.Shared._RMC14.Xenonids;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Threading;

namespace Content.Shared._CM14RTS.Vision;

public abstract class SharedRTSVisionSystem : EntitySystem
{
    [Dependency] private readonly IParallelManager _parallel = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    private SeedJob _seedJob;
    private ViewJob _job;

    private readonly HashSet<Entity<OccluderComponent>> _occluders = new();
    private readonly HashSet<Entity<RTSVisionSourceComponent>> _seeds = new();
    private readonly HashSet<Entity<RTSControllableComponent>> _controllableSeeds = new();
    private readonly HashSet<Vector2i> _viewportTiles = new();
    private readonly HashSet<Vector2i> _opaque = new();

    /// <summary>
    /// Active visible tiles cached per grid, populated each vision update.
    /// </summary>
    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _visibleTilesByGrid = new();

    private EntityQuery<EyeComponent> _eyeQuery;
    private EntityQuery<OccluderComponent> _occluderQuery;

    public override void Initialize()
    {
        base.Initialize();

        _eyeQuery = GetEntityQuery<EyeComponent>();
        _occluderQuery = GetEntityQuery<OccluderComponent>();

        _seedJob = new SeedJob
        {
            System = this,
        };

        _job = new ViewJob
        {
            EntManager = EntityManager,
            Maps = _maps,
            System = this,
            VisibleTiles = new HashSet<Vector2i>(),
        };

        SubscribeLocalEvent<RTSVisionSourceComponent, ComponentStartup>(OnVisionSourceStartup);
    }

    protected virtual void OnVisionSourceStartup(Entity<RTSVisionSourceComponent> ent, ref ComponentStartup args)
    {
        ResolveFaction(ent);
    }

    /// <summary>
    /// Checks whether a specific grid tile is currently within line-of-sight of any allied RTS unit.
    /// </summary>
    public bool IsTileVisible(EntityUid gridUid, Vector2i tile)
    {
        if (_visibleTilesByGrid.TryGetValue(gridUid, out var set))
            return set.Contains(tile);

        return false;
    }

    /// <summary>
    /// Gets or creates the visible tiles set for a specific grid.
    /// </summary>
    public HashSet<Vector2i> GetOrCreateVisibleTiles(EntityUid gridUid)
    {
        if (!_visibleTilesByGrid.TryGetValue(gridUid, out var set))
        {
            set = new HashSet<Vector2i>();
            _visibleTilesByGrid[gridUid] = set;
        }

        return set;
    }

    /// <summary>
    /// Clears cached visible tiles across all grids.
    /// </summary>
    public void ClearAllVisibleTiles()
    {
        foreach (var set in _visibleTilesByGrid.Values)
        {
            set.Clear();
        }
    }

    /// <summary>
    /// Resolves grid and tile coordinates for any entity on a map grid.
    /// </summary>
    public bool TryGetTile(EntityUid uid, TransformComponent? xform, out EntityUid gridUid, out Vector2i tile)
    {
        gridUid = default;
        tile = default;

        if (!Resolve(uid, ref xform, false))
            return false;

        if (xform.GridUid is not { Valid: true } gridId)
            return false;

        if (!TryComp<MapGridComponent>(gridId, out var gridComp))
            return false;

        gridUid = gridId;
        tile = _maps.TileIndicesFor(gridId, gridComp, xform.Coordinates);
        return true;
    }

    /// <summary>
    /// Checks whether the tile beneath this entity is currently illuminated by allied vision.
    /// </summary>
    public bool IsEntityTileVisible(EntityUid uid, TransformComponent? xform = null)
    {
        if (!TryGetTile(uid, xform, out var gridUid, out var tile))
            return false;

        return IsTileVisible(gridUid, tile);
    }

    /// <summary>
    /// Resolves the RTS commander faction for a player entity (commander observer or directly-controlled unit).
    /// </summary>
    public bool TryGetPlayerFaction(EntityUid playerEnt, [NotNullWhen(true)] out string? faction)
    {
        faction = null;

        if (TryComp<RTSObserverComponent>(playerEnt, out var observer))
        {
            faction = observer.Faction;
        }
        else if (TryComp<RTSControlledUnitComponent>(playerEnt, out var controlled) &&
                 controlled.Observer is { Valid: true } obs &&
                 TryComp<RTSObserverComponent>(obs, out var obsComp))
        {
            faction = obsComp.Faction;
        }

        return !string.IsNullOrWhiteSpace(faction);
    }

    /// <summary>
    /// Checks whether a given entity belongs to an enemy faction relative to ourFaction.
    /// </summary>
    public bool IsEnemy(EntityUid uid, string ourFaction, RTSControllableComponent? controllable = null)
    {
        var theirFaction = ResolveFaction(uid);
        if (string.IsNullOrWhiteSpace(theirFaction))
            return false;

        return !RTSFactionHelper.AreFactionsCompatible(ourFaction, theirFaction);
    }

    /// <summary>
    /// Ensures faction is populated from RTSControllableComponent if not explicitly set.
    /// </summary>
    public string ResolveFaction(EntityUid uid, RTSVisionSourceComponent? comp = null)
    {
        if (Resolve(uid, ref comp, false) && !string.IsNullOrWhiteSpace(comp.Faction))
            return comp.Faction;

        if (TryComp<RTSControllableComponent>(uid, out var controllable) &&
            !string.IsNullOrWhiteSpace(controllable.Faction))
        {
            if (comp != null)
            {
                comp.Faction = controllable.Faction;
                Dirty(uid, comp);
            }
            return controllable.Faction;
        }

        // Fallbacks for standard RMC factions
        if (HasComp<XenoComponent>(uid))
            return "Hive";

        if (HasComp<MarineComponent>(uid))
            return "Marine";

        return string.Empty;
    }

    public string ResolveFaction(Entity<RTSVisionSourceComponent> ent)
    {
        return ResolveFaction(ent.Owner, ent.Comp);
    }

    /// <summary>
    /// Returns the active vision radius for this entity.
    /// If RangeOverride is set, returns RangeOverride.
    /// Otherwise, returns the default view radius that a player sees when controlling the entity,
    /// derived from NetMaxUpdateRange and EyeComponent.PvsScale.
    /// </summary>
    public float GetVisionRadius(EntityUid uid, RTSVisionSourceComponent? vision = null, EyeComponent? eye = null)
    {
        if (Resolve(uid, ref vision, false) && vision.RangeOverride is { } overrideRadius)
            return overrideRadius;

        return GetDefaultVisionRadius(uid, eye);
    }

    /// <summary>
    /// Calculates the default view radius that a player sees when controlling the entity.
    /// Based on the engine NetMaxUpdateRange (half-extent) and EyeComponent.PvsScale.
    /// </summary>
    public float GetDefaultVisionRadius(EntityUid uid, EyeComponent? eye = null)
    {
        var baseRange = _cfg.GetCVar(CVars.NetMaxUpdateRange) / 2f;
        if (baseRange <= 0.1f)
            baseRange = 12.5f;

        if (_eyeQuery.Resolve(uid, ref eye, false) && eye.PvsScale > 0.1f)
            return Math.Max(baseRange * eye.PvsScale, 7.5f);

        return Math.Max(baseRange, 7.5f);
    }

    /// <summary>
    /// Checks whether this vision source is currently emitting vision (enabled and not dead).
    /// </summary>
    public bool IsVisionActive(EntityUid uid, RTSVisionSourceComponent? vision = null, MobStateComponent? mobState = null)
    {
        if (!Resolve(uid, ref vision, false))
            return false;

        if (!vision.Enabled)
            return false;

        if (Resolve(uid, ref mobState, false) && _mobState.IsDead(uid, mobState))
            return false;

        return true;
    }

    public virtual void SetEnabled(Entity<RTSVisionSourceComponent?> ent, bool enabled)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false))
            return;

        if (ent.Comp.Enabled == enabled)
            return;

        ent.Comp.Enabled = enabled;
        Dirty(ent);
    }

    /// <summary>
    /// Computes the exact tile-based line-of-sight visibility for all allied RTS units on a grid.
    /// Uses OpenDream / BYOND ViewAlgorithm executed in parallel across CPU cores.
    /// </summary>
    public void GetView(
        Entity<BroadphaseComponent, MapGridComponent> grid,
        string faction,
        Box2Rotated worldBounds,
        HashSet<Vector2i> visibleTiles,
        float expansionSize = 12f)
    {
        _viewportTiles.Clear();
        _opaque.Clear();
        _seeds.Clear();
        _controllableSeeds.Clear();

        _seedJob.Grid = (grid.Owner, grid.Comp2);
        var invMatrix = _xforms.GetInvWorldMatrix(grid);
        var localAabb = invMatrix.TransformBox(worldBounds);
        var enlargedLocalAabb = invMatrix.TransformBox(worldBounds.Enlarged(expansionSize));
        _seedJob.ExpandedBounds = enlargedLocalAabb;
        _parallel.ProcessNow(_seedJob);
        _job.Data.Clear();

        var addedUids = new HashSet<EntityUid>();

        foreach (var seed in _seeds)
        {
            if (!IsVisionActive(seed.Owner, seed.Comp))
                continue;

            var sourceFaction = ResolveFaction(seed.Owner, seed.Comp);
            if (!RTSFactionHelper.AreFactionsCompatible(faction, sourceFaction))
                continue;

            var radius = GetVisionRadius(seed.Owner, seed.Comp);
            if (radius <= 0.1f)
                continue;

            var rangeInTiles = radius / grid.Comp2.TileSize;
            _job.Data.Add(new RTSVisionSeed(seed.Owner, rangeInTiles));
            addedUids.Add(seed.Owner);
        }

        foreach (var controllable in _controllableSeeds)
        {
            if (addedUids.Contains(controllable.Owner))
                continue;

            if (TryComp<MobStateComponent>(controllable.Owner, out var mobState) && _mobState.IsDead(controllable.Owner, mobState))
                continue;

            var sourceFaction = controllable.Comp.Faction;
            if (string.IsNullOrWhiteSpace(sourceFaction))
                sourceFaction = ResolveFaction(controllable.Owner);

            if (!RTSFactionHelper.AreFactionsCompatible(faction, sourceFaction))
                continue;

            var radius = GetDefaultVisionRadius(controllable.Owner);
            if (radius <= 0.1f)
                continue;

            var rangeInTiles = radius / grid.Comp2.TileSize;
            _job.Data.Add(new RTSVisionSeed(controllable.Owner, rangeInTiles));
            addedUids.Add(controllable.Owner);
        }

        if (_job.Data.Count == 0)
            return;

        // Viewport tiles and occluders
        var tileEnumerator = _maps.GetLocalTilesEnumerator(grid, grid, localAabb, ignoreEmpty: false);
        while (tileEnumerator.MoveNext(out var tileRef))
        {
            if (IsOccluded(grid, tileRef.GridIndices))
            {
                _opaque.Add(tileRef.GridIndices);
            }

            _viewportTiles.Add(tileRef.GridIndices);
        }

        tileEnumerator = _maps.GetLocalTilesEnumerator(grid, grid, enlargedLocalAabb, ignoreEmpty: false);
        while (tileEnumerator.MoveNext(out var tileRef))
        {
            if (_viewportTiles.Contains(tileRef.GridIndices))
                continue;

            if (IsOccluded(grid, tileRef.GridIndices))
            {
                _opaque.Add(tileRef.GridIndices);
            }
        }

        for (var i = _job.Vis1.Count; i < _job.Data.Count; i++)
        {
            _job.Vis1.Add(new Dictionary<Vector2i, int>());
            _job.Vis2.Add(new Dictionary<Vector2i, int>());
            _job.SeedTiles.Add(new HashSet<Vector2i>());
            _job.BoundaryTiles.Add(new HashSet<Vector2i>());
        }

        _job.Grid = (grid.Owner, grid.Comp2);
        _job.VisibleTiles = visibleTiles;
        _parallel.ProcessNow(_job, _job.Data.Count);

        var gridSet = GetOrCreateVisibleTiles(grid.Owner);
        if (!ReferenceEquals(gridSet, visibleTiles))
        {
            gridSet.Clear();
            foreach (var tile in visibleTiles)
            {
                gridSet.Add(tile);
            }
        }
    }

    private bool IsOccluded(Entity<BroadphaseComponent, MapGridComponent> grid, Vector2i tile)
    {
        var tileBounds = _lookup.GetLocalBounds(tile, grid.Comp2.TileSize).Enlarged(-0.05f);
        _occluders.Clear();
        _lookup.GetLocalEntitiesIntersecting((grid.Owner, grid.Comp1), tileBounds, _occluders, query: _occluderQuery, flags: LookupFlags.Static | LookupFlags.Approximate);

        foreach (var occluder in _occluders)
        {
            if (!occluder.Comp.Enabled)
                continue;

            return true;
        }

        return false;
    }

    private int GetMaxDelta(Vector2i tile, Vector2i center)
    {
        var delta = tile - center;
        return Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
    }

    private int GetSumDelta(Vector2i tile, Vector2i center)
    {
        var delta = tile - center;
        return Math.Abs(delta.X) + Math.Abs(delta.Y);
    }

    private bool CheckNeighborsVis(Dictionary<Vector2i, int> vis, Vector2i index, int d)
    {
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                var neighbor = index + new Vector2i(x, y);
                var neighborD = vis.GetValueOrDefault(neighbor);

                if (neighborD == d)
                    return true;
            }
        }
        return false;
    }

    private bool IsCorner(
        HashSet<Vector2i> tiles,
        HashSet<Vector2i> blocked,
        Dictionary<Vector2i, int> vis1,
        Vector2i index,
        Vector2i delta)
    {
        var diagonalIndex = index + delta;

        if (!tiles.TryGetValue(diagonalIndex, out var diagonal))
            return false;

        var cardinal1 = new Vector2i(index.X, diagonal.Y);
        var cardinal2 = new Vector2i(diagonal.X, index.Y);

        return vis1.GetValueOrDefault(diagonal) != 0 &&
               vis1.GetValueOrDefault(cardinal1) != 0 &&
               vis1.GetValueOrDefault(cardinal2) != 0 &&
               blocked.Contains(cardinal1) &&
               blocked.Contains(cardinal2) &&
               !blocked.Contains(diagonal);
    }

    public readonly struct RTSVisionSeed
    {
        public readonly EntityUid Uid;
        public readonly float Range;

        public RTSVisionSeed(EntityUid uid, float range)
        {
            Uid = uid;
            Range = range;
        }
    }

    private record struct SeedJob : IRobustJob
    {
        public required SharedRTSVisionSystem System;
        public Entity<MapGridComponent> Grid;
        public Box2 ExpandedBounds;

        public void Execute()
        {
            System._lookup.GetLocalEntitiesIntersecting(Grid.Owner, ExpandedBounds, System._seeds, flags: LookupFlags.All | LookupFlags.Approximate);
            System._lookup.GetLocalEntitiesIntersecting(Grid.Owner, ExpandedBounds, System._controllableSeeds, flags: LookupFlags.All | LookupFlags.Approximate);
        }
    }

    private record struct ViewJob() : IParallelRobustJob
    {
        public int BatchSize => 1;

        public required IEntityManager EntManager;
        public required SharedMapSystem Maps;
        public required SharedRTSVisionSystem System;

        public Entity<MapGridComponent> Grid;
        public List<RTSVisionSeed> Data = new();

        public required HashSet<Vector2i> VisibleTiles;

        public readonly List<Dictionary<Vector2i, int>> Vis1 = new();
        public readonly List<Dictionary<Vector2i, int>> Vis2 = new();

        public readonly List<HashSet<Vector2i>> SeedTiles = new();
        public readonly List<HashSet<Vector2i>> BoundaryTiles = new();

        public void Execute(int index)
        {
            var seed = Data[index];
            if (!EntManager.TryGetComponent<TransformComponent>(seed.Uid, out var seedXform))
                return;

            var range = seed.Range;
            var vis1 = Vis1[index];
            var vis2 = Vis2[index];

            var seedTiles = SeedTiles[index];
            var boundary = BoundaryTiles[index];

            vis1.Clear();
            vis2.Clear();
            seedTiles.Clear();
            boundary.Clear();

            var maxDepthMax = 0;
            var sumDepthMax = 0;

            var eyePos = Maps.GetTileRef(Grid.Owner, Grid, seedXform.Coordinates).GridIndices;

            for (var x = Math.Floor(eyePos.X - range); x <= eyePos.X + range; x++)
            {
                for (var y = Math.Floor(eyePos.Y - range); y <= eyePos.Y + range; y++)
                {
                    var tile = new Vector2i((int)x, (int)y);
                    var delta = tile - eyePos;
                    var xDelta = Math.Abs(delta.X);
                    var yDelta = Math.Abs(delta.Y);

                    var deltaSum = xDelta + yDelta;

                    maxDepthMax = Math.Max(maxDepthMax, Math.Max(xDelta, yDelta));
                    sumDepthMax = Math.Max(sumDepthMax, deltaSum);
                    seedTiles.Add(tile);
                }
            }

            // Step 3, Diagonal shadow loop
            for (var d = 0; d < maxDepthMax; d++)
            {
                foreach (var tile in seedTiles)
                {
                    var maxDelta = System.GetMaxDelta(tile, eyePos);

                    if (maxDelta == d + 1 && System.CheckNeighborsVis(vis2, tile, d))
                    {
                        vis2[tile] = (System._opaque.Contains(tile) ? -1 : d + 1);
                    }
                }
            }

            // Step 4, Straight shadow loop
            for (var d = 0; d < sumDepthMax; d++)
            {
                foreach (var tile in seedTiles)
                {
                    var sumDelta = System.GetSumDelta(tile, eyePos);

                    if (sumDelta == d + 1 && System.CheckNeighborsVis(vis1, tile, d))
                    {
                        if (System._opaque.Contains(tile))
                        {
                            vis1[tile] = -1;
                        }
                        else if (vis2.GetValueOrDefault(tile) != 0)
                        {
                            vis1[tile] = d + 1;
                        }
                    }
                }
            }

            // Add the eye itself
            vis1[eyePos] = 1;

            // Step 8.
            foreach (var tile in seedTiles)
            {
                vis2[tile] = vis1.GetValueOrDefault(tile, 0);
            }

            // Step 9. Detect corners for boundary walls facing visible tiles
            foreach (var tile in seedTiles)
            {
                if (!System._opaque.Contains(tile))
                    continue;

                var tileVis1 = vis1.GetValueOrDefault(tile);
                if (tileVis1 != 0)
                    continue;

                if (System.IsCorner(seedTiles, System._opaque, vis1, tile, Vector2i.UpRight) ||
                    System.IsCorner(seedTiles, System._opaque, vis1, tile, Vector2i.UpLeft) ||
                    System.IsCorner(seedTiles, System._opaque, vis1, tile, Vector2i.DownLeft) ||
                    System.IsCorner(seedTiles, System._opaque, vis1, tile, Vector2i.DownRight))
                {
                    boundary.Add(tile);
                }
            }

            // Make all wall/corner boundary tiles visible
            foreach (var tile in boundary)
            {
                vis1[tile] = -1;
            }

            // Export to visible tiles
            foreach (var tile in seedTiles)
            {
                if (!System._viewportTiles.Contains(tile))
                    continue;

                var tileVis = vis1.GetValueOrDefault(tile, 0);
                if (tileVis != 0)
                {
                    lock (VisibleTiles)
                    {
                        VisibleTiles.Add(tile);
                    }
                }
            }
        }
    }
}
