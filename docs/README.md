# Documentation map

Where everything is written down, and which document wins when two disagree.

## 1. Start here

| If you want to know | Read |
| --- | --- |
| What the game is and what it must not become | [`game/game-vision.md`](game/game-vision.md) |
| The loop, the systems and how far each is decided | [`game/master-game-design.md`](game/master-game-design.md) |
| What is built, what is approved, what is open | [`development/feature-registry.md`](development/feature-registry.md) |
| How the code is organised and the rules it keeps | [`architecture/system-architecture.md`](architecture/system-architecture.md) |
| How to do a task | [`development/development-workflow.md`](development/development-workflow.md) |
| What to build next | [`development/roadmap.md`](development/roadmap.md) |

## 2. All documents

### Game

| Document | Contents | Language |
| --- | --- | --- |
| [`game/game-vision.md`](game/game-vision.md) | Identity, the ten pillars, what to avoid | English |
| [`game/master-game-design.md`](game/master-game-design.md) | Gameplay loop, every system with its design status, cross-system rules | English |
| [`design/README.md`](design/README.md) | Index of system specifications and the template for a new one | English |
| [`design/robotic-workforce.md`](design/robotic-workforce.md) | Workforce specification v1: audit, decisions D1–D16, phases 1–4 | English |
| [`design/robotic-workforce-v2-audit.md`](design/robotic-workforce-v2-audit.md) | Workforce specification v2: stages A–H, questions Q1–Q4 | English |
| [`design/tidal-barrage.md`](design/tidal-barrage.md) | Tidal barrage proposal: physics, scale problem, coast effects, open choices (PROPOSED) | English |
| [`design/tidal-wave-energy.md`](design/tidal-wave-energy.md) | Tidal and wave energy proposal: physics rules, buildings, research steps, open choices (PROPOSED) | English |
| [`design/research-station.md`](design/research-station.md) | Research Station: measuring and experiments at a station, knowledge gates and safety margins, the brief before landing | English |
| [`credits.md`](credits.md) | Who made the sounds (Kenney, CC0) and which file each one came from | English |

### Architecture

| Document | Contents | Language |
| --- | --- | --- |
| [`architecture/system-architecture.md`](architecture/system-architecture.md) | Layers, modules, rules, target structure, migration plan | English |
| [`architecture/system-interactions.md`](architecture/system-interactions.md) | Flows of energy, matter, information and work; step order; contracts | English |
| [`architecture/data-architecture.md`](architecture/data-architecture.md) | Where content lives, naming and units, provenance, moving content to data | English |
| [`architecture/information-architecture.md`](architecture/information-architecture.md) | Actual, observed and known state; the observation record; reports | English |
| [`simulation-interface.md`](simulation-interface.md) | The contract between Simulation and Presentation: coordinates, units, members | Thai |

### Science

| Document | Contents | Language |
| --- | --- | --- |
| [`physics/scientific-standards.md`](physics/scientific-standards.md) | Modelling rules, content classes, units, how to add a formula | English |
| [`physics/physics-verification.md`](physics/physics-verification.md) | Checked formulas, corrections, test values with tolerances, known gaps | Thai |
| [`physics/notes/`](physics/notes/) | Research notes: solar model, energy sources, geology, Kardashev and heat, stars and exoplanets | English |
| [`physics/explainer-th.md`](physics/explainer-th.md) | The physics explained for the team | Thai |

### Development

| Document | Contents | Language |
| --- | --- | --- |
| [`../AGENTS.md`](../AGENTS.md) | Project instructions and workflow | Thai |
| [`../CONTRIBUTING.md`](../CONTRIBUTING.md) | Team Git guide: setup, daily steps, conflicts | Thai |
| [`development/development-workflow.md`](development/development-workflow.md) | The ten steps, prohibitions, pull requests, which document to update, model roles | English |
| [`development/testing-standards.md`](development/testing-standards.md) | Kinds of test, how to run them, definition of done | English |
| [`development/feature-registry.md`](development/feature-registry.md) | Design and implementation status of every feature; decisions; open questions | English |
| [`development/roadmap.md`](development/roadmap.md) | Phases in dependency order | English |
| [`development/technical-decisions.md`](development/technical-decisions.md) | Engineering decisions and why | English |
| [`development/known-issues.md`](development/known-issues.md) | Known problems and limitations | English |
| [`development/repository-audit.md`](development/repository-audit.md) | Snapshot of the repository on 2026-11-12 | English |

### Beside the code

| Document | Contents |
| --- | --- |
| `prototypes/solar/README.md` | The Python solar prototype and the pvlib comparison |
| `tools/exoplanets/README.md` | The planet data pipeline |
| `tools/nasa_power/README.md` | The cloud climatology pipeline |

## 3. When documents disagree

1. **The code on `main` is the truth about what is built.** A document that says otherwise is out of date: fix the document.
2. **The game director is the truth about what the game should be.** The vision, then the feature registry's CONFIRMED rows, then the specifications.
3. **For science, the source wins.** `physics-verification.md` and the sources it cites outrank a comment or a constant.
4. Between two documents of the same kind, the more specific and more recent one wins, and the other is corrected in the same change.

The audit is a snapshot and is never updated; the registry, the known-issues list and the roadmap are living documents.

## 4. Conventions

- New documents are in English, in the folder for their area, with lowercase hyphenated file names.
- A document says at the top what it is for and, where it mixes them, which parts describe the code and which are proposals.
- Statuses use the words defined in the feature registry.
- Paths are written from the repository root or as relative links; code names are in backticks.
- Diagrams are Mermaid blocks, which GitHub renders.
- A date is written as `2026-11-12`.

- [nasa-data.md](nasa-data.md): every NASA dataset the game uses, what it feeds and how to rebuild it.
