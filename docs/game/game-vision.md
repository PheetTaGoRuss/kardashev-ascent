# Game vision

**Authority:** this document records the game director's statement of the vision, given on 2026-11-12. Only the director changes it. Everything in it is direction, not a list of built features; what is built is in [`../development/feature-registry.md`](../development/feature-registry.md).

## What the game is

Kardashev Ascent is a scientifically grounded simulation of planetary engineering, automation, exploration and civilization building.

Humanity sends robotic expeditions to other planets. The expeditions discover resources, build infrastructure and industry, study what they do not understand, and grow into a civilization that spans planets. The long direction is ever larger energy civilizations: up the Kardashev scale and, possibly, to engineering at the scale of a star.

## What it draws on

- Planetary colony management
- Factory automation and industrial logistics
- Space exploration
- Scientific research and experiment
- Managing a robotic workforce
- Energy infrastructure engineering
- Simulation of planetary environments
- Development of an interplanetary civilization

The game takes ideas from these genres and must have its own identity. It does not copy an existing game.

## Design pillars

| # | Pillar | What it means for a feature |
| --- | --- | --- |
| 1 | Scientific authenticity with accessible gameplay | The numbers come from real science; the player does not need a degree to play |
| 2 | Realistic simulation underneath, simple interaction on top | Complexity lives in the simulation. The player gives few, clear commands |
| 3 | The planet shapes engineering | Gravity, star, air, terrain and chemistry change what is worth building and where |
| 4 | Robots are autonomous and controllable | Robots work without orders; the player can always step in |
| 5 | Matter obeys physics and chemistry | Resources and materials cannot be created, destroyed or transformed for free |
| 6 | Energy is central | Generating, storing, moving and using energy is the main thread of progress |
| 7 | Discovery creates opportunity | Exploration and science open options the player did not have before |
| 8 | Information arrives as observation | The player learns through observations, reports, inspections and consultations, not through an all-knowing display |
| 9 | Systems are connected | A mechanic that touches nothing else does not belong |
| 10 | Progress ends in civilization-scale engineering | The late game is about planets, orbits and stars |

## What the game must not become

- **A spreadsheet.** If playing well means reading tables of numbers, the interface has failed. The simulation may be detailed; the screen stays calm.
- **A copy.** A feature justified only by "that other game has it" is not justified.
- **Science fiction presented as science.** Speculative and invented content is allowed and is always labelled as such (see [`../physics/scientific-standards.md`](../physics/scientific-standards.md)).

## Using this document

Before proposing a feature, check it against the pillars. A proposal should say which pillars it serves. A proposal that works against a pillar needs the director's explicit agreement and an entry in the feature registry.
