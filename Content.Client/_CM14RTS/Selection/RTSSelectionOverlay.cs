using System.Numerics;
using Content.Shared._CM14RTS.Observer;
using Content.Shared._RMC14.Stun;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client._CM14RTS.Selection;

/// <summary>
/// Renders the tactical Marquee selection rectangle in screen space
/// and glowing selection rings under selected units' feet in world space.
/// </summary>
public sealed class RTSSelectionOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace | OverlaySpace.WorldSpaceBelowEntities;

    private readonly IEntityManager _entityManager;
    private readonly IPlayerManager _playerManager;
    private readonly RTSSelectionSystem _selectionSystem;
    private readonly RTSSelectionInputSystem _inputSystem;
    private readonly SharedTransformSystem _transform;
    private readonly SpriteSystem _sprite;
    private readonly ShaderInstance _shader;

    // USCM / Marine tactical palette (Radar Green)
    private static readonly Color MarineBoxFill = new(0.12f, 0.78f, 0.35f, 0.16f);
    private static readonly Color MarineBoxBorder = new(0.25f, 1.00f, 0.45f, 0.90f);
    private static readonly Color MarineDiscFill = new(0.12f, 0.82f, 0.36f, 0.14f);
    private static readonly Color MarineRingPrimary = new(0.25f, 1.00f, 0.45f, 0.90f);
    private static readonly Color MarineRingHalo = new(0.25f, 1.00f, 0.45f, 0.35f);

    // Hive / Xeno tactical palette (Bio Purple)
    private static readonly Color HiveBoxFill = new(0.65f, 0.15f, 0.85f, 0.18f);
    private static readonly Color HiveBoxBorder = new(0.85f, 0.30f, 1.00f, 0.90f);
    private static readonly Color HiveDiscFill = new(0.65f, 0.15f, 0.85f, 0.15f);
    private static readonly Color HiveRingPrimary = new(0.85f, 0.30f, 1.00f, 0.90f);
    private static readonly Color HiveRingHalo = new(0.85f, 0.30f, 1.00f, 0.35f);

    public RTSSelectionOverlay(
        IEntityManager entityManager,
        IPlayerManager playerManager,
        IPrototypeManager prototypeManager,
        RTSSelectionSystem selectionSystem,
        RTSSelectionInputSystem inputSystem)
    {
        _entityManager = entityManager;
        _playerManager = playerManager;
        _selectionSystem = selectionSystem;
        _inputSystem = inputSystem;
        _transform = _entityManager.System<SharedTransformSystem>();
        _sprite = _entityManager.System<SpriteSystem>();

        _shader = prototypeManager.Index<ShaderPrototype>("unshaded").Instance();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var localPlayer = _playerManager.LocalEntity;
        if (localPlayer == null || !_entityManager.TryGetComponent<RTSObserverComponent>(localPlayer.Value, out var observer))
            return;

        var isHive = RTSFactionHelper.Normalize(observer.Faction) == "hive";

        // 1. Screen-space Marquee selection box
        if ((args.Space & OverlaySpace.ScreenSpace) != 0)
        {
            if (_inputSystem.IsDragging && _inputSystem.DragBox is { } box)
            {
                var handle = args.ScreenHandle;
                var fillColor = isHive ? HiveBoxFill : MarineBoxFill;
                var borderColor = isHive ? HiveBoxBorder : MarineBoxBorder;

                // Translucent box interior
                handle.DrawRect(box, fillColor, filled: true);

                // Crisp border
                handle.DrawRect(box, borderColor, filled: false);

                // Inner outline for enhanced clarity on high-DPI
                if (box.Width > 2f && box.Height > 2f)
                {
                    var innerBox = new UIBox2(box.Left + 1f, box.Top + 1f, box.Right - 1f, box.Bottom - 1f);
                    handle.DrawRect(innerBox, borderColor.WithAlpha(borderColor.A * 0.65f), filled: false);
                }
            }
        }

        // 2. World-space tactical unit circles beneath feet
        if ((args.Space & OverlaySpace.WorldSpaceBelowEntities) != 0)
        {
            var selected = _selectionSystem.Selected;
            if (selected.Count == 0)
                return;

            var worldHandle = args.WorldHandle;
            worldHandle.UseShader(_shader);

            var discColor = isHive ? HiveDiscFill : MarineDiscFill;
            var ringColor = isHive ? HiveRingPrimary : MarineRingPrimary;
            var haloColor = isHive ? HiveRingHalo : MarineRingHalo;

            foreach (var uid in selected)
            {
                if (!_entityManager.EntityExists(uid) || !_entityManager.TryGetComponent<TransformComponent>(uid, out var xform))
                    continue;

                if (xform.MapID != args.MapId)
                    continue;

                var worldPos = _transform.GetWorldPosition(xform);
                var radius = GetUnitSelectionRadius(uid);
                var haloRadius = radius + 0.04f;

                // Soft circular ground disc under unit
                worldHandle.DrawCircle(worldPos, radius, discColor, filled: true);

                // Primary crisp tactical double ring for visibility
                worldHandle.DrawCircle(worldPos, radius, ringColor, filled: false);
                worldHandle.DrawCircle(worldPos, radius - 0.02f, ringColor.WithAlpha(ringColor.A * 0.7f), filled: false);

                // Subtle outer halo ring
                worldHandle.DrawCircle(worldPos, haloRadius, haloColor, filled: false);
            }
        }
    }

    /// <summary>
    /// Calculates the tactical ring radius based on the entity's RMCSize tier and sprite bounds.
    /// Ensures circles are properly visible under massive xenos (Queen, Crusher) and standard marines.
    /// </summary>
    private float GetUnitSelectionRadius(EntityUid uid)
    {
        var radius = 0.45f;

        // 1. Check RMCSizeComponent for known RMC size tiers
        if (_entityManager.TryGetComponent<RMCSizeComponent>(uid, out var rmcSize))
        {
            switch (rmcSize.Size)
            {
                case RMCSizes.Small:
                case RMCSizes.VerySmallXeno: // Larva
                    radius = 0.32f;
                    break;
                case RMCSizes.Humanoid: // Marines
                    radius = 0.45f;
                    break;
                case RMCSizes.SmallXeno: // Runner
                    radius = 0.52f;
                    break;
                case RMCSizes.Xeno: // Drone, Warrior, Defender, Spitter
                    radius = 0.65f;
                    break;
                case RMCSizes.Big: // Praetorian, Ravager, Queen
                    radius = 0.90f;
                    break;
                case RMCSizes.Immobile: // Crusher
                    radius = 1.15f;
                    break;
            }
        }

        // 2. Also check SpriteComponent local bounds to dynamically accommodate custom or scaled sprites
        if (_entityManager.TryGetComponent<SpriteComponent>(uid, out var sprite))
        {
            var bounds = _sprite.GetLocalBounds((uid, sprite));
            var spriteRadius = MathF.Max(bounds.Width, bounds.Height) * 0.45f;
            if (spriteRadius > radius)
            {
                radius = spriteRadius;
            }
        }

        return MathHelper.Clamp(radius, 0.30f, 2.5f);
    }
}
