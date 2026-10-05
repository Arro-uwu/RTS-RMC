---
name: rmc14-xenonids-core
description: Basic architecture and mechanics of Xenonids (Aliens) in CM14-RTS / RMC-14. Covers XenoComponent, Plasma, Hives, Weeds, Pheromones and general Xeno logic.
---

# CM14-RTS / RMC-14 Xenonids Core

This skill covers the basic architecture and concepts of the Xenomorphs (Xenonids) in the RMC-14 / CM14-RTS codebase. Use this as a reference when working on Alien mechanics, abilities, mutations, or general alien gameplay.

## Core Concepts

Xenonids in RMC-14 are structured around a few foundational systems:

1. **`XenoComponent` & `XenoSystem`**: The core component that identifies an entity as a Xenonid. It handles basic alien interactions, combat modifications (e.g., preventing friendly fire within the hive, scaling melee damage), visual states, and basic entity lifecycle events.
2. **`XenoPlasmaComponent` & `XenoPlasmaSystem`**: Xenonids do not use traditional stamina or mana for abilities; they use Plasma. The plasma system handles passive regeneration (especially on weeds), max plasma caps based on evolution, and spending plasma for abilities.
3. **`XenoWeedsComponent` & `XenoWeedsSystem`**: Weeds are a central mechanic. They provide plasma regeneration, movement speed bonuses (or lack of penalties), and health regeneration. Aliens place weed nodes, which spread weeds on the floor.
4. **`XenoHiveComponent` & `SharedXenoHiveSystem`**: Xenonids belong to a hive. Hives determine friendly/hostile targeting, hivemind chat, and pheromone sharing.
5. **`XenoPheromonesComponent`**: Aliens emit pheromones (Recovery, Frenzy, Warding) that buff nearby hive members.

## Architecture Guidelines

- All Xenonid specific logic should reside in the `Content.Server/_CM14RTS/Xenonids` and `Content.Shared/_CM14RTS/Xenonids` directories (or their RMC-14 upstream equivalents `_RMC14`).
- **Do not mix Marine logic with Xenonid logic.** Always separate components. If an entity can be both or interact with both, use distinct marker components or interface systems.
- Use `TryComp<XenoComponent>(uid, out var xeno)` to verify if an entity is a xenomorph.
- Xenonid attacks and friendly fire prevention are handled by `OnXenoAttackAttempt` and `OnXenoMeleeAttackAttempt`. When creating new AoE or projectile attacks, ensure they respect `XenoFriendlyComponent` and hive checks.

## Common Integrations

- **Damage and Regeneration**: Aliens naturally regenerate health and plasma faster when resting (`XenoRestSystem`) or on weeds.
- **Actions**: Alien abilities are primarily implemented via `SharedActionsSystem`. Abilities usually require checking plasma via `XenoPlasmaSystem.TryRemovePlasma`.
- **Movement**: Xenonids ignore certain slowdowns (e.g., weeds). Handled by `RefreshMovementSpeedModifiersEvent` subscriptions in `XenoSystem`.

## Important Namespaces

- `Content.Shared._CM14RTS.Xenonids`
- `Content.Shared._CM14RTS.Xenonids.Plasma`
- `Content.Shared._CM14RTS.Xenonids.Weeds`
- `Content.Shared._CM14RTS.Xenonids.Hive`

## Naming Conventions

- Follow the standard `CM14RTS` rules (`ss14-naming-conventions`).
- Xenonid prototype IDs should typically be prefixed with `CM14RTSXeno*` or `CM14RTSActionXeno*` for new custom RTS content.
