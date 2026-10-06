namespace Content.Client._CM14RTS.Selection;

/// <summary>
/// Raised on the local observer entity when the RTS unit selection changes.
/// </summary>
[ByRefEvent]
public readonly record struct RTSSelectionChangedEvent(IReadOnlySet<EntityUid> Selected);
