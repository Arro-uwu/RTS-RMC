---
name: rmc14-medical-core
description: Basic architecture and mechanics of the Medical system in CM14-RTS / RMC-14. Covers damage types, blood, surgery, revival, and medical items.
---

# CM14-RTS / RMC-14 Medical Core

This skill covers the basic architecture and concepts of the Medical system in the RMC-14 / CM14-RTS codebase. The medical system in CM14 is significantly more complex than vanilla SS14, involving intricate organ damage, blood loss, CPR/defibrillation, and advanced surgical procedures.

## Core Concepts

1. **Damage System Extensions (`SharedRMCDamageableSystem`)**: CM14 expands the vanilla damage system with concepts like internal organ damage, bone fractures, and bleeding. 
2. **Blood and IV Drips**: Entities have a blood volume. Blood loss leads to low blood pressure, hypoxia, and eventually death. IV Drips and Blood Packs are used to restore volume.
3. **Surgery (`SurgerySystem`)**: A step-by-step process requiring specific tools (scalpel, hemostat, retractor, bone gel, etc.) to fix internal issues like shrapnel, fractures, or embedded alien embryos.
4. **Revival (CPR & Defibrillation)**: Dead marines can often be revived within a certain time window if their bodies aren't husked or missing vital organs. This involves CPR to keep the revival window open, and a Defibrillator to restart the heart.
5. **Medical Scanners (`HealthScannerSystem`)**: Scanners provide detailed readouts of damage, internal bleeding, and fractures, instead of just a simple health bar.

## Architecture Guidelines

- Medical extensions reside in `Content.Server/_CM14RTS/Medical` and `Content.Shared/_CM14RTS/Medical` (or upstream `_RMC14`).
- **Use CM14-specific events and components** when dealing with healing. Vanilla SS14 healing items might not interact correctly with CM14's advanced wound system.
- Surgery steps are defined as ECS Systems implementing specific logic for each tool use.

## Common Integrations

- **Xenonid Parasites (Facehuggers)**: Implant embryos that must be surgically removed before they burst. This integrates closely with both `Xenonids` and `Medical` systems.
- **Armor**: Armor reduces incoming damage, preventing fractures and deep wounds. Handled during the damage modification pipeline.

## Important Namespaces

- `Content.Shared._CM14RTS.Medical`
- `Content.Shared._CM14RTS.Medical.Scanner`
- `Content.Shared._CM14RTS.Medical.Surgery`
- `Content.Shared._CM14RTS.Damage`

## Naming Conventions

- Follow the standard `CM14RTS` rules (`ss14-naming-conventions`).
- Medical prototype IDs should typically be prefixed with `CM14RTSMedical*` or `CM14RTSSurgery*` for new custom RTS content.
