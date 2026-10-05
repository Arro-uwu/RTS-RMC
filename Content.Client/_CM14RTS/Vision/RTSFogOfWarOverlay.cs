using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._CM14RTS.CCVar;
using Content.Shared._CM14RTS.Observer;
using Content.Shared._CM14RTS.Vision;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._CM14RTS.Vision;

public sealed class RTSFogOfWarOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> StencilMaskShader = "StencilMask";
    private static readonly ProtoId<ShaderPrototype> StencilDrawShader = "StencilDraw";

    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _playerMan = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly HashSet<Vector2i> _visibleTiles = new();
    private List<Entity<MapGridComponent>> _grids = new();

    private IRenderTexture? _stencilTexture;
    private IRenderTexture? _veilTexture;

    private float _updateRate = 1f / 30f;
    private float _accumulator;

    private readonly SharedTransformSystem _transform;
    private readonly SharedRTSVisionSystem _vision;

    // Animated smoke textures from _RMC14/Effects/fog.rsi (4 directions x 4 frames)
    private readonly Texture[][] _smokeFrames = new Texture[4][];
    private bool _smokeLoaded;

    public RTSFogOfWarOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 0;

        _transform = _entMan.System<SharedTransformSystem>();
        _vision = _entMan.System<SharedRTSVisionSystem>();

        // Load dense fog smoke sprites for organic billowing mist effect
        var rsiPath = new ResPath("/Textures/_RMC14/Effects/fog.rsi");
        if (_resourceCache.TryGetResource<RSIResource>(rsiPath, out var rsiResource))
        {
            var rsi = rsiResource.RSI;
            if (rsi.TryGetState("smoke", out var state))
            {
                _smokeFrames[0] = state.GetFrames(RsiDirection.South);
                _smokeFrames[1] = state.GetFrames(RsiDirection.East);
                _smokeFrames[2] = state.GetFrames(RsiDirection.North);
                _smokeFrames[3] = state.GetFrames(RsiDirection.West);
                _smokeLoaded = true;
            }
        }
    }

    protected override void DisposeBehavior()
    {
        base.DisposeBehavior();
        _stencilTexture?.Dispose();
        _veilTexture?.Dispose();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return;

        if (!_cfg.GetCVar(CM14RTSCVars.RTSFogOfWarEnabled))
            return;

        var localEnt = _playerMan.LocalEntity;
        if (localEnt == null)
            return;

        // Active only for RTS observers or commanders directly controlling a unit
        if (!_vision.TryGetPlayerFaction(localEnt.Value, out var faction))
            return;

        // Ensure render targets match current viewport size
        if (_stencilTexture?.Texture.Size != args.Viewport.Size)
        {
            _veilTexture?.Dispose();
            _stencilTexture?.Dispose();

            _stencilTexture = _clyde.CreateRenderTarget(args.Viewport.Size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                name: "rts-fow-stencil");

            _veilTexture = _clyde.CreateRenderTarget(args.Viewport.Size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                name: "rts-fow-veil");
        }

        var worldHandle = args.WorldHandle;
        var worldBounds = args.WorldBounds;
        var worldAABB = args.WorldAABB;
        var invMatrix = args.Viewport.GetWorldToLocalMatrix();
        var mapId = args.MapId;

        _accumulator -= (float) _timing.FrameTime.TotalSeconds;
        var shouldUpdate = _accumulator <= 0f;
        if (shouldUpdate)
        {
            _accumulator = MathF.Max(0f, _accumulator + _updateRate);
            _visibleTiles.Clear();
        }

        var lookups = _entMan.System<EntityLookupSystem>();
        var broadphaseQuery = _entMan.GetEntityQuery<BroadphaseComponent>();

        _grids.Clear();
        _mapManager.FindGridsIntersecting(mapId, worldAABB, ref _grids);

        if (_entMan.TryGetComponent<TransformComponent>(localEnt.Value, out var playerXform) &&
            playerXform.GridUid is { Valid: true } playerGrid &&
            _entMan.TryGetComponent<MapGridComponent>(playerGrid, out var playerGridComp))
        {
            var found = false;
            foreach (var g in _grids)
            {
                if (g.Owner == playerGrid)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                _grids.Add((playerGrid, playerGridComp));
            }
        }

        // 1. Draw visible tiles to stencil texture
        worldHandle.RenderInRenderTarget(_stencilTexture!, () =>
        {
            foreach (var grid in _grids)
            {
                if (!broadphaseQuery.TryComp(grid.Owner, out var broadphase))
                    continue;

                var visibleTiles = _vision.GetOrCreateVisibleTiles(grid.Owner);
                if (shouldUpdate)
                {
                    visibleTiles.Clear();
                    _vision.GetView((grid.Owner, broadphase, grid.Comp), faction, worldBounds, visibleTiles);

                    foreach (var tile in visibleTiles)
                    {
                        _visibleTiles.Add(tile);
                    }
                }

                var gridMatrix = _transform.GetWorldMatrix(grid.Owner);
                var matty = Matrix3x2.Multiply(gridMatrix, invMatrix);
                worldHandle.SetTransform(matty);

                foreach (var tile in visibleTiles)
                {
                    var aabb = lookups.GetLocalBounds(tile, grid.Comp.TileSize);
                    worldHandle.DrawRect(aabb, Color.White);
                }
            }
        }, Color.Transparent);

        // 2. Fill _veilTexture with atmospheric dark space fog and billowing smoke clouds
        var alpha = Math.Clamp(_cfg.GetCVar(CM14RTSCVars.RTSFogOfWarAlpha), 0f, 1f);
        var fogColor = new Color(0.03f, 0.04f, 0.05f, alpha);

        worldHandle.RenderInRenderTarget(_veilTexture!, () =>
        {
            worldHandle.SetTransform(invMatrix);
            // Draw dark atmospheric base veil
            worldHandle.DrawRect(worldBounds, fogColor);

            // Draw organic animated billowing smoke clouds over fogged area
            DrawSmokeClouds(worldHandle, worldAABB);
        }, Color.Transparent);

        // 3. Mark stencil buffer = 1 for visible areas
        worldHandle.UseShader(_protoMan.Index(StencilMaskShader).Instance());
        worldHandle.DrawTextureRect(_stencilTexture!.Texture, worldBounds);

        // 4. Draw the smoky atmospheric fog veil where stencil buffer != 1
        worldHandle.UseShader(_protoMan.Index(StencilDrawShader).Instance());
        worldHandle.DrawTextureRect(_veilTexture!.Texture, worldBounds);

        // 5. Restore drawing state
        worldHandle.SetTransform(Matrix3x2.Identity);
        worldHandle.UseShader(null);
    }

    private void DrawSmokeClouds(DrawingHandleWorld worldHandle, Box2 worldAABB)
    {
        if (!_smokeLoaded)
            return;

        var minX = (int) MathF.Floor(worldAABB.Left) - 1;
        var maxX = (int) MathF.Ceiling(worldAABB.Right) + 1;
        var minY = (int) MathF.Floor(worldAABB.Bottom) - 1;
        var maxY = (int) MathF.Ceiling(worldAABB.Top) + 1;

        // Skip individual tile sprites at extreme zoom out to preserve max FPS
        var tileCount = (maxX - minX + 1) * (maxY - minY + 1);
        if (tileCount > 3500)
            return;

        var curTime = (float) _timing.RealTime.TotalSeconds;
        var baseFrame = (int) (curTime / 0.35f);

        // Murky volumetric greenish-slate smoke tint matching the reference RMC fog aesthetic
        var smokeColor = new Color(0.13f, 0.17f, 0.18f, 0.50f);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                // Skip tiles that are in visible area to avoid wasted draw calls
                if (_visibleTiles.Contains(new Vector2i(x, y)))
                    continue;

                // Deterministic pseudo-random variation per tile to avoid visible grid tiling
                var hash = (x * 73856093) ^ (y * 19349663);
                var dirIndex = Math.Abs(hash) % 4;
                var frameOffset = Math.Abs(hash >> 3) % 4;
                var frame = (baseFrame + frameOffset) % 4;

                var texture = _smokeFrames[dirIndex][frame];
                worldHandle.DrawTexture(texture, new Vector2(x, y), smokeColor);
            }
        }
    }
}
