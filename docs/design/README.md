# System specifications

One specification per game system: what it should do, what has been decided, and how it is built. The top-level design is [`../game/master-game-design.md`](../game/master-game-design.md); status lives in [`../development/feature-registry.md`](../development/feature-registry.md).

## Index

| System | Specification | State |
| --- | --- | --- |
| Robotic workforce, v1 (phases 1–4, movement) | [`robotic-workforce.md`](robotic-workforce.md) | Built |
| Robotic workforce, v2 (stages A–H) | [`robotic-workforce-v2-audit.md`](robotic-workforce-v2-audit.md) | Stages A–D built; E in progress on a branch; F–H not started |
| Solar measurements and research puzzles | `solar-measurements.md` | On branch `feature/building-footprints`, not on `main` |
| Simulation and Presentation contract | [`../simulation-interface.md`](../simulation-interface.md) (Thai) | Needs updating |
| Solar model | `docs/physics/physics-verification.md` and `docs/physics/notes/solar_model.md` | Built and verified |

Systems with no specification yet: power grid and storage beyond the interface document, construction, resources and deposits, manufacturing, progression, and every system the vision lists as future. Write the specification before building or extending one of these.

## Writing a specification

Use this outline. Leave a heading in and write "none" when it does not apply, so that a reader can see it was considered.

```markdown
# <System name>

Status: <draft | approved on DATE by the director | built>
Registry entries: <ids>

## 1. Purpose
What the player gets from this system, and which design pillars it serves.

## 2. What exists today
Code, tests and data that this system builds on or replaces. Checked against `main` at <commit>.

## 3. Design
The rules of the system. Mark each rule: CONFIRMED (who, when), PROPOSED, or OPEN.

## 4. Interactions
What it takes from other systems and gives to them: energy (W, Wh), matter (kg), information, work.
Where it runs in the step order.

## 5. Science
Each model: formula, source, range of validity, content class (established, theoretical, speculative, fictional).
Each number: measured, derived, assumed, sourced, game rule or placeholder.

## 6. Data
Definitions it needs, their fields and units. State it saves, and the save version.

## 7. Information
What the colony must observe before the player can know it. Units, source, uncertainty, freshness.

## 8. Interface
What the player sees and does. Which permanent elements, which reports, which inspections.

## 9. Tests
The tests that prove it, by kind.

## 10. Stages
How it is delivered: one pull request per stage, each playable and tested.

## 11. Open questions
What the director must decide, and what is assumed until then.

## 12. Out of scope
What this system deliberately does not do.
```

Rules for specifications:

- Start with an audit of what exists (section 2). Both workforce documents are the model for this.
- Recommendations are labelled as recommendations until the director approves them, with the date of approval recorded.
- When the system is built, update the status line and the registry; do not leave "planned" wording in a built specification.
