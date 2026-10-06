using Content.Shared._CM14RTS.Observer;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._CM14RTS.Selection;

/// <summary>
/// Manages the local player's RTS selection state and overlay lifecycle.
/// Follows the CanDo -> TryDo -> Do architectural interaction flow.
/// </summary>
public sealed class RTSSelectionSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly RTSSelectionInputSystem _inputSystem = default!;

    private readonly HashSet<EntityUid> _selected = new();
    private RTSSelectionOverlay _overlay = default!;

    public IReadOnlySet<EntityUid> Selected => _selected;
    public int SelectionCount => _selected.Count;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new RTSSelectionOverlay(
            EntityManager,
            _playerManager,
            _prototypeManager,
            this,
            _inputSystem);
        _overlayManager.AddOverlay(_overlay);

        SubscribeLocalEvent<RTSControllableComponent, MobStateChangedEvent>(OnControllableMobStateChanged);
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay(_overlay);
        _selected.Clear();
    }

    public bool IsSelected(EntityUid uid)
    {
        return _selected.Contains(uid);
    }

    /// <summary>
    /// Checks whether an entity can be selected by the specified user (CanDo check).
    /// </summary>
    public bool CanSelect(EntityUid user, EntityUid target, bool quiet = true)
    {
        if (!Exists(target) || IsClientSide(target))
            return false;

        if (!TryComp<RTSControllableComponent>(target, out var controllable))
            return false;

        if (_mobState.IsDead(target))
            return false;

        if (_container.IsEntityInContainer(target))
            return false;

        if (!TryComp<RTSObserverComponent>(user, out var observer))
            return false;

        if (!RTSFactionHelper.AreFactionsCompatible(observer.Faction, controllable.Faction))
            return false;

        return true;
    }

    /// <summary>
    /// Attempts to select a single entity (TryDo API).
    /// </summary>
    public bool TrySelect(EntityUid user, EntityUid target, bool clearOthers = true)
    {
        if (!CanSelect(user, target))
            return false;

        DoSelect(user, target, clearOthers);
        return true;
    }

    /// <summary>
    /// Attempts to select a group of entities (TryDo API).
    /// </summary>
    public bool TrySelectGroup(EntityUid user, IEnumerable<EntityUid> targets, bool clearOthers = true)
    {
        var validTargets = new List<EntityUid>();
        foreach (var target in targets)
        {
            if (CanSelect(user, target, quiet: true))
                validTargets.Add(target);
        }

        DoSelectGroup(user, validTargets, clearOthers);
        return validTargets.Count > 0;
    }

    /// <summary>
    /// Toggles selection of a target (TryDo API).
    /// </summary>
    public bool TryToggleSelect(EntityUid user, EntityUid target)
    {
        if (IsSelected(target))
        {
            return DoDeselect(user, target);
        }

        return TrySelect(user, target, clearOthers: false);
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        if (_selected.Count == 0)
            return;

        _selected.Clear();
        NotifySelectionChanged();
    }

    /// <summary>
    /// Deselects a specific entity.
    /// </summary>
    public bool Deselect(EntityUid target)
    {
        if (_playerManager.LocalEntity is not { } local)
            return false;

        return DoDeselect(local, target);
    }

    private void DoSelect(EntityUid user, EntityUid target, bool clearOthers)
    {
        var changed = false;
        if (clearOthers)
        {
            if (_selected.Count > 1 || !_selected.Contains(target))
            {
                _selected.Clear();
                changed = true;
            }
        }

        if (_selected.Add(target))
            changed = true;

        if (changed)
            NotifySelectionChanged();
    }

    private void DoSelectGroup(EntityUid user, IReadOnlyList<EntityUid> targets, bool clearOthers)
    {
        var changed = false;
        if (clearOthers)
        {
            if (_selected.Count > 0)
            {
                _selected.Clear();
                changed = true;
            }
        }

        foreach (var target in targets)
        {
            if (_selected.Add(target))
                changed = true;
        }

        if (changed)
            NotifySelectionChanged();
    }

    private bool DoDeselect(EntityUid user, EntityUid target)
    {
        if (_selected.Remove(target))
        {
            NotifySelectionChanged();
            return true;
        }

        return false;
    }

    private void NotifySelectionChanged()
    {
        if (_playerManager.LocalEntity is not { } local)
            return;

        var ev = new RTSSelectionChangedEvent(_selected);
        RaiseLocalEvent(local, ref ev);
    }

    private void OnControllableMobStateChanged(Entity<RTSControllableComponent> ent, ref MobStateChangedEvent args)
    {
        if (_mobState.IsDead(ent.Owner) && _selected.Remove(ent.Owner))
        {
            NotifySelectionChanged();
        }
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        if (_selected.Remove(args.Entity.Owner))
        {
            NotifySelectionChanged();
        }
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        if (!HasComp<RTSObserverComponent>(args.Entity))
        {
            ClearSelection();
        }
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        ClearSelection();
    }
}
