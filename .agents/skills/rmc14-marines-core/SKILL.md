---
name: rmc14-marines-core
description: Basic architecture and mechanics of Marines in CM14-RTS / RMC-14. Covers Marine components, Squads, and specific Marine systems.
---

# CM14-RTS / RMC-14 Marines Core

This skill covers the basic architecture and concepts of the United States Colonial Marines (USCM) in the RMC-14 / CM14-RTS codebase. Use this as a reference when working on Marine mechanics, squads, roles, or general marine gameplay.

## Core Concepts

Marines in RMC-14 are structured around a few foundational systems:

1. **`MarineComponent` & `MarineSystem`**: Identifies an entity as a Marine. Handles basic marine interactions, status effects, and specific rules that apply to USCM personnel.
2. **`SquadComponent` & `SquadSystem`**: Marines are organized into squads (Alpha, Bravo, Charlie, Delta, etc.). This system manages squad assignments, squad leaders, and UI/HUD elements for tracking squadmates.
3. **Roles and Jobs**: Marine roles (Rifleman, Medic, Engineer, Squad Leader, Commander) heavily rely on the `ss14-loadout-authoring` system to provide specific gear, weapons, and access rights.

## Architecture Guidelines

- All Marine specific logic should reside in the `Content.Server/_CM14RTS/Marines` and `Content.Shared/_CM14RTS/Marines` directories (or their upstream equivalents `_RMC14`).
- **Do not mix Marine logic with Xenonid logic.** 
- Interactions with Marine gear (Weapons, Armor, Scopes) are usually handled in separate systems (e.g. `_CM14RTS/Weapons`), but use `MarineComponent` to ensure only qualified personnel can use certain items if needed.

## Common Integrations

- **Squad HUDs**: Squads have specific colored HUDs showing the health and position of squad members. This integrates with the robust UI system and `SquadSystem`.
- **Friendly Fire & Targeting**: Marines have specific rules for IFF (Identify Friend/Foe) to prevent automated defenses (Sentries) from firing on them.

## Important Namespaces

- `Content.Shared._CM14RTS.Marines`
- `Content.Shared._CM14RTS.Marines.Squads`
- `Content.Shared._CM14RTS.Roles`

## Naming Conventions

- Follow the standard `CM14RTS` rules (`ss14-naming-conventions`).
- Marine prototype IDs for loadouts and groups should typically be prefixed with `CM14RTSLoadout*` or `CM14RTSMarine*` for new custom RTS content.
