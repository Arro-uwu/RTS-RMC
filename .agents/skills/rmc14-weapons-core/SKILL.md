---
name: rmc14-weapons-core
description: Basic architecture and mechanics of Weapons in CM14-RTS / RMC-14. Covers gun mechanics, attachments, ammunition, and custom RMC weapon logic.
---

# CM14-RTS / RMC-14 Weapons Core

This skill covers the basic architecture and concepts of Weapons in the RMC-14 / CM14-RTS codebase. RMC-14 extends the vanilla SS14 gun system with specific features like attachments, pump-action shotguns, specialized fire modes, and specific armor-piercing mechanics.

## Core Concepts

1. **Vanilla SS14 Gun System**: RMC-14 heavily uses the base `Content.Shared.Weapons.Ranged` systems (like `GunSystem`, `AmmoSystem`). Familiarize yourself with the vanilla SS14 ECS Components for ranged weapons before modifying RMC weapons.
2. **`RMCWeaponComponent`**: Adds CM14-specific weapon behaviors.
3. **Attachments & Upgrades**: Marine weapons often have rails for attachments (scopes, grips, underbarrel launchers, flashlights). This is managed via custom attachment components and systems.
4. **Ammunition & Magazines**: CM14 has a very detailed ammunition system (AP, HE, Incendiary, Slugs, Buckshot). Ammunition types are defined in Prototypes (`.yml`) and use custom damage profiles.
5. **Armor Piercing (AP)**: Armor piercing is a critical mechanic against Xenomorph armor. AP values on projectiles reduce the effective armor of the target before calculating damage.

## Architecture Guidelines

- Extensions to the gun system should reside in `Content.Server/_CM14RTS/Weapons` and `Content.Shared/_CM14RTS/Weapons` (or upstream `_RMC14`).
- **Never modify vanilla `GunSystem` directly unless absolutely necessary.** Use event subscriptions (e.g., `BeforeFireEvent`, `AmmoShotEvent`, `ProjectileHitEvent`) to hook into the firing process.
- Create partial classes in `_CM14RTS` for any needed extensions to vanilla systems.

## Common Integrations

- **Scopes and Zoom**: Managed by systems that adjust the player's camera zoom when aiming down sights.
- **Smart Guns**: Smart weapons auto-target or provide aiming assists, integrating with `XenoComponent` to identify hostile targets.
- **Sentries**: Automated defense turrets that use the gun system but are fired by an AI component rather than a player.

## Important Namespaces

- `Content.Shared._CM14RTS.Weapons`
- `Content.Shared.Weapons.Ranged.Systems` (Vanilla)

## Naming Conventions

- Follow the standard `CM14RTS` rules (`ss14-naming-conventions`).
- Weapon prototype IDs should typically be prefixed with `CM14RTSWeapon*` or `CM14RTSMagazine*` for new custom RTS content.
