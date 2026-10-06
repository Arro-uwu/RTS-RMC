using Content.Shared._CM14RTS.CCVar;
using Content.Shared._CM14RTS.Vision;
using Robust.Client.Player;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.Client._CM14RTS.Vision;

/// <summary>
/// Filters and mutes positional world audio occurring inside the Fog of War for RTS observers/commanders.
/// The observer only hears positional audio (footsteps, screams, attacks, construction)
/// originating from tiles currently illuminated by allied vision.
/// </summary>
public sealed class RTSAudioFilterSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _playerMan = default!;
    [Dependency] private readonly SharedRTSVisionSystem _vision = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private readonly Dictionary<EntityUid, float> _mutedAudios = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AudioComponent, EntityTerminatingEvent>(OnAudioTerminating);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        RestoreAll();
    }

    private void OnAudioTerminating(Entity<AudioComponent> ent, ref EntityTerminatingEvent args)
    {
        _mutedAudios.Remove(ent.Owner);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_cfg.GetCVar(CM14RTSCVars.RTSFogOfWarEnabled))
        {
            RestoreAll();
            return;
        }

        var localEnt = _playerMan.LocalEntity;
        if (localEnt == null || !_vision.TryGetPlayerFaction(localEnt.Value, out _))
        {
            RestoreAll();
            return;
        }

        var query = EntityQueryEnumerator<AudioComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var audio, out var xform))
        {
            if (audio.Global)
                continue;

            if (_vision.TryGetTile(uid, xform, out var gridUid, out var tile))
            {
                if (!_vision.IsTileVisible(gridUid, tile))
                {
                    // Inside fog of war -> mute
                    if (!_mutedAudios.ContainsKey(uid))
                    {
                        _mutedAudios[uid] = audio.Gain;
                    }
                    _audio.SetGain(uid, 0f, audio);
                }
                else if (_mutedAudios.Remove(uid, out var originalGain))
                {
                    // Restored to allied visibility -> restore original gain
                    _audio.SetGain(uid, originalGain, audio);
                }
            }
        }
    }

    private void RestoreAll()
    {
        if (_mutedAudios.Count == 0)
            return;

        foreach (var (uid, originalGain) in _mutedAudios)
        {
            if (TryComp<AudioComponent>(uid, out var audio))
            {
                _audio.SetGain(uid, originalGain, audio);
            }
        }
        _mutedAudios.Clear();
    }
}
