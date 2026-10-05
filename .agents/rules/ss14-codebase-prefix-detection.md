---
trigger: always_on
---

# Rule: Defining the codebase prefix, project folder and edit markers

This rule is mandatory for any task in the RTS-RMC repository (`RTS-RMC`).

## 1. What you need to determine before starting work

Before analyzing, planning paths and making changes, always fix three values:

1. Active codebase prefix (`CM14RTS`, `CM14-RTS`).
2. Forked project folder (`_CM14RTS`).
3. The text of the edit marker that should be used in comments (`cm14-rts-edit`, `CM14-RTS-edit`).

Don't start editing vanilla or upstream files until these three values are defined.

## 2. How to determine the project and fork context

Define the project identity by collecting concrete signals:

1. Git remote repository slug: `RTS-RMC` in `Arro-uwu/RTS-RMC`.
2. The name of the repository root folder and the path of the working directory: `RTS-RMC`.
3. Which forked project folder is used for custom changes: `_CM14RTS`.
4. The nearest existing edit markers in the adjacent code: `cm14-rts-edit`, `CM14-RTS-edit`, `// cm14-rts-edit start` / `// cm14-rts-edit end`.

### Upstream and inherited code:
- Upstream repository is RMC-14 (`RMC-14/RMC-14`), using an MIT codebase based on vanilla Space Station 14.
- The repository structure consists of:
  1. Base SS14 projects (`Content.Server`, `Content.Shared`, `Content.Client`, `Resources/Prototypes/Entities`, etc.).
  2. RMC-14 fork code in `_CM14RTS` project folders.
- **Critical rule for new code:** All new custom features, systems, components, prototypes, and assets belonging to our project must strictly go into `_CM14RTS`. Never place new custom files into vanilla SS14 directories.
- **Rule for modifying upstream files:** Modifying upstream files (vanilla SS14 or RMC-14) is **allowed when unavoidable** (e.g. hooking an event deep inside an upstream method or modifying an upstream prototype where inheritance/migration is insufficient), but edits must remain **minimal** (ideally 1–2 lines raising an event or calling a hook) and must strictly be marked with `cm14-rts-edit` markers.

## 3. Correspondence map

Select the line matching our project configuration:

| Match | Prefix | Project folder | Single-line marker | Block markers | Note |
| --- | --- | --- | --- | --- | --- |
| `Arro-uwu/RTS-RMC`, `RTS-RMC` | `CM14RTS` / `CM14-RTS` | `_CM14RTS` | `cm14-rts-edit` / `CM14-RTS-edit` | `cm14-rts-edit start` / `cm14-rts-edit end`, `cm14-rts added start` / `cm14-rts added end` | Primary project configuration. Default single-line marker is `cm14-rts-edit`. |

## 4. How to apply a marker in a specific file

The general rules for marking edits:

1. Use `Prefix` (`CM14RTS` / `CM14-RTS`), `Project folder` (`_CM14RTS`) and `marker` (`cm14-rts-edit` / `CM14-RTS-edit`).
2. Do not change the marker text, just adapt the comment syntax to the file language.
3. If the file already uses an existing local case/variant (e.g. `#CM14-RTS-edit` or `// cm14-rts-edit start`), follow the local file style.

Select the comment syntax to match the file language:

- C#, C++, Java: `// cm14-rts-edit`, `// cm14-rts-edit start - reason` ... `// cm14-rts-edit end`
- YAML, FTL, Python, Shell: `# cm14-rts-edit`, `# cm14-rts-edit start - reason` ... `# cm14-rts-edit end`
- XML, HTML: `<!-- cm14-rts-edit -->`, only if comments in this format are allowed and really needed

## 5. How does this affect the structure of edits?

1. Place all new project files, systems, components, prototypes, and assets in `_CM14RTS` (e.g. `Content.Shared/_CM14RTS`, `Content.Server/_CM14RTS`, `Content.Client/_CM14RTS`, `Resources/Prototypes/_CM14RTS`, `Resources/Locale/*/_CM14RTS`).
2. Mark minimal hooks in upstream/vanilla files with the edit marker `cm14-rts-edit`.
3. Never put custom RTS-RMC code into vanilla SS14 folders.
4. Keep modifications to upstream and vanilla files minimal to prevent merge conflicts when rebasing or merging upstream updates.
