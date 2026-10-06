using System.Numerics;
using Content.Client.Construction;
using Content.Client.Interaction;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._CM14RTS.Observer;
using Content.Shared.Input;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._CM14RTS.Selection;

/// <summary>
/// Handles user input for RTS selection:
/// - Dragging LMB draws a Marquee box and selects all friendly controllable units inside.
/// - Single click LMB selects an individual friendly unit (or clears selection if clicking ground).
/// - Shift + Click / Shift + Box unions with current selection.
/// - Double click LMB selects all friendly units of the same prototype on screen.
/// - Safely ignores clicks when hovering over UI controls or when targeting an action (e.g. Take Control).
/// </summary>
public sealed class RTSSelectionInputSystem : EntitySystem
{
    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly RTSSelectionSystem _selectionSystem = default!;
    [Dependency] private readonly IPlacementManager _placementManager = default!;

    private const float DragThreshold = 6f; // pixels
    private static readonly TimeSpan DoubleClickThreshold = TimeSpan.FromMilliseconds(350);

    private bool _dragArmed;
    private bool _isDragging;
    private Vector2 _dragStartScreenPos;
    private Vector2 _dragCurrentScreenPos;
    private EntityUid? _potentialClickEntity;

    private TimeSpan _lastClickTime;
    private EntityUid? _lastClickedEntity;

    public bool IsDragging => _isDragging;
    public Vector2 DragStartScreen => _dragStartScreenPos;
    public Vector2 DragCurrentScreen => _dragCurrentScreenPos;

    public UIBox2? DragBox
    {
        get
        {
            if (!_isDragging)
                return null;

            var left = MathF.Min(_dragStartScreenPos.X, _dragCurrentScreenPos.X);
            var right = MathF.Max(_dragStartScreenPos.X, _dragCurrentScreenPos.X);
            var top = MathF.Min(_dragStartScreenPos.Y, _dragCurrentScreenPos.Y);
            var bottom = MathF.Max(_dragStartScreenPos.Y, _dragCurrentScreenPos.Y);
            return new UIBox2(left, top, right, bottom);
        }
    }

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(new CommandBind(
                EngineKeyFunctions.Use,
                new PointerInputCmdHandler(OnUse, ignoreUp: false, outsidePrediction: true),
                before: new[] { typeof(ConstructionSystem), typeof(DragDropSystem) },
                after: new[] { typeof(ActionUIController) }))
            .Register<RTSSelectionInputSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<RTSSelectionInputSystem>();
        _dragArmed = false;
        _isDragging = false;

        base.Shutdown();
    }

    private bool IsActionTargeting()
    {
        if (_placementManager.IsActive)
            return true;

        var actionController = _uiManager.GetUIController<ActionUIController>();
        return actionController.SelectingTargetFor != null;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_dragArmed)
            return;

        var localEnt = _playerManager.LocalEntity;
        if (localEnt == null || !HasComp<RTSObserverComponent>(localEnt.Value) || IsActionTargeting())
        {
            _dragArmed = false;
            _isDragging = false;
            return;
        }

        // If mouse button was released outside the window, cancel drag
        if (!_inputManager.IsKeyDown(Keyboard.Key.MouseLeft))
        {
            _dragArmed = false;
            _isDragging = false;
            return;
        }

        var mousePos = _inputManager.MouseScreenPosition;
        if (!mousePos.IsValid)
            return;

        _dragCurrentScreenPos = mousePos.Position;

        if (!_isDragging)
        {
            var delta = _dragCurrentScreenPos - _dragStartScreenPos;
            if (delta.LengthSquared() >= DragThreshold * DragThreshold)
            {
                _isDragging = true;
            }
        }
    }

    private bool OnUse(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        var localEnt = _playerManager.LocalEntity;
        if (localEnt == null || !HasComp<RTSObserverComponent>(localEnt.Value))
            return false;

        // If an action is currently targeting (e.g. "Take Control" from the hotbar),
        // or a placement ghost is active, let them handle the click.
        if (IsActionTargeting())
        {
            _dragArmed = false;
            _isDragging = false;
            return false;
        }

        if (args.State == BoundKeyState.Down)
        {
            return OnUseDown(args, localEnt.Value);
        }
        else if (args.State == BoundKeyState.Up)
        {
            return OnUseUp(args, localEnt.Value);
        }

        return false;
    }

    private bool OnUseDown(in PointerInputCmdHandler.PointerInputCmdArgs args, EntityUid observerEnt)
    {
        if (IsActionTargeting())
            return false;

        // Don't intercept clicks on UI controls (hotbar, menus, chat, etc.)
        if (_uiManager.CurrentlyHovered is not IViewportControl)
            return false;

        _dragArmed = true;
        _isDragging = false;
        _dragStartScreenPos = args.ScreenCoordinates.Position;
        _dragCurrentScreenPos = args.ScreenCoordinates.Position;
        _potentialClickEntity = args.EntityUid;

        return true;
    }

    private bool OnUseUp(in PointerInputCmdHandler.PointerInputCmdArgs args, EntityUid observerEnt)
    {
        if (!_dragArmed)
            return false;

        _dragArmed = false;

        if (IsActionTargeting())
        {
            _isDragging = false;
            return false;
        }
        var shiftHeld = _inputManager.IsKeyDown(Keyboard.Key.Shift);

        if (_isDragging)
        {
            _isDragging = false;
            HandleBoxSelect(observerEnt, _dragStartScreenPos, args.ScreenCoordinates.Position, shiftHeld);
            return true;
        }

        HandleClickSelect(observerEnt, args, shiftHeld);
        return true;
    }

    private void HandleBoxSelect(EntityUid observerEnt, Vector2 start, Vector2 end, bool shiftHeld)
    {
        var left = MathF.Min(start.X, end.X);
        var right = MathF.Max(start.X, end.X);
        var top = MathF.Min(start.Y, end.Y);
        var bottom = MathF.Max(start.Y, end.Y);
        var box = new UIBox2(left, top, right, bottom);

        var eye = _eyeManager.CurrentEye;
        if (eye == null)
            return;

        var mapId = eye.Position.MapId;
        var worldViewport = _eyeManager.GetWorldViewport();

        var unitsInBox = new List<EntityUid>();
        var query = EntityQueryEnumerator<RTSControllableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            var worldPos = _transform.GetWorldPosition(xform);
            if (!worldViewport.Contains(worldPos))
                continue;

            if (!_selectionSystem.CanSelect(observerEnt, uid, quiet: true))
                continue;

            var screenCoords = _eyeManager.CoordinatesToScreen(xform.Coordinates);
            if (screenCoords.IsValid && box.Contains(screenCoords.Position))
            {
                unitsInBox.Add(uid);
            }
        }

        if (shiftHeld)
        {
            if (unitsInBox.Count > 0)
                _selectionSystem.TrySelectGroup(observerEnt, unitsInBox, clearOthers: false);
        }
        else
        {
            _selectionSystem.TrySelectGroup(observerEnt, unitsInBox, clearOthers: true);
        }
    }

    private void HandleClickSelect(EntityUid observerEnt, in PointerInputCmdHandler.PointerInputCmdArgs args, bool shiftHeld)
    {
        // Require clicking directly on the entity's sprite/body (or equipment), no proximity snapping
        var targetEntity = args.EntityUid.IsValid() ? args.EntityUid : _potentialClickEntity;
        var resolvedTarget = ResolveDirectTarget(observerEnt, targetEntity);

        if (resolvedTarget != null)
        {
            var target = resolvedTarget.Value;
            var now = _timing.CurTime;

            // Double click check
            if (_lastClickedEntity == target && now - _lastClickTime <= DoubleClickThreshold)
            {
                _lastClickTime = TimeSpan.Zero;
                _lastClickedEntity = null;
                HandleDoubleClickSelect(observerEnt, target, shiftHeld);
                return;
            }

            _lastClickTime = now;
            _lastClickedEntity = target;

            if (shiftHeld)
            {
                _selectionSystem.TryToggleSelect(observerEnt, target);
            }
            else
            {
                _selectionSystem.TrySelect(observerEnt, target, clearOthers: true);
            }
        }
        else
        {
            _lastClickedEntity = null;
            if (!shiftHeld)
            {
                _selectionSystem.ClearSelection();
            }
        }
    }

    private EntityUid? ResolveDirectTarget(EntityUid observerEnt, EntityUid? rawTarget)
    {
        if (rawTarget is not { } direct || !Exists(direct) || TerminatingOrDeleted(direct))
            return null;

        // 1. Direct hit on controllable entity
        if (_selectionSystem.CanSelect(observerEnt, direct, quiet: true))
            return direct;

        // 2. Direct hit on worn item, held weapon, or child entity of a controllable unit
        var current = direct;
        while (TryComp(current, out TransformComponent? xform) && xform.ParentUid.IsValid())
        {
            current = xform.ParentUid;
            if (_selectionSystem.CanSelect(observerEnt, current, quiet: true))
                return current;
        }

        return null;
    }

    private void HandleDoubleClickSelect(EntityUid observerEnt, EntityUid target, bool shiftHeld)
    {
        var protoId = MetaData(target).EntityPrototype?.ID;
        if (string.IsNullOrEmpty(protoId))
            return;
        var eye = _eyeManager.CurrentEye;
        if (eye == null)
            return;

        var mapId = eye.Position.MapId;
        var worldViewport = _eyeManager.GetWorldViewport();

        var sameTypeUnits = new List<EntityUid>();
        var query = EntityQueryEnumerator<RTSControllableComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var xform, out var targetMeta))
        {
            if (xform.MapID != mapId)
                continue;

            if (targetMeta.EntityPrototype?.ID != protoId)
                continue;

            var worldPos = _transform.GetWorldPosition(xform);
            if (!worldViewport.Contains(worldPos))
                continue;

            if (!_selectionSystem.CanSelect(observerEnt, uid, quiet: true))
                continue;

            sameTypeUnits.Add(uid);
        }

        if (shiftHeld)
        {
            _selectionSystem.TrySelectGroup(observerEnt, sameTypeUnits, clearOthers: false);
        }
        else
        {
            _selectionSystem.TrySelectGroup(observerEnt, sameTypeUnits, clearOthers: true);
        }
    }
}
