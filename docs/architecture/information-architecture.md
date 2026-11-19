# Information architecture

How information about the world reaches the player and the robots. The principle is set by the game director (pillar 8 of [`../game/game-vision.md`](../game/game-vision.md)). The record shapes and the migration in sections 4–6 are **proposals**; the mechanics of reports, consultations and the AI assistant are not designed.

## 1. Principle

The game has little permanent interface. The player learns about the colony and the planet through:

- robot observations;
- operational, environmental and scientific reports;
- inspections the player asks for;
- the status of facilities and robots;
- colony records and history;
- consultations with scientists;
- optional explanations from an AI assistant.

Nothing in the game knows a fact that has not been observed or communicated.

## 2. Three states

| State | What it is | Who may read it |
| --- | --- | --- |
| **Actual** | The simulation's truth: what is in the ground, how charged a robot really is | Simulation systems that model physics. Never the player directly |
| **Observed** | What some sensor, robot or instrument has measured, when, and how well | The observer; anyone it has reported to |
| **Known** | What has reached the colony's shared knowledge and is synchronised | The player; robots that are connected |

Observed becomes known by communication. A robot that is cut off holds observations the colony does not have, and the colony may hold instructions the robot does not have.

## 3. What exists today

| Mechanism | State it models | Where |
| --- | --- | --- |
| Deposits hidden until surveyed | Actual versus known | `DepositMap.IsSurveyed`, survey orders, `TileSurveyed` |
| Sky notebook: measure before the game gives best-angle advice | Observed and known | `Solar/SkyNotebook.cs` |
| Planet values labelled NASA, derived or assumed | Source identification | `PlanetRecord.OriginOf`, shown in the planet chooser |
| Solar parameters as estimates with a spread, narrowed by measurement | Uncertainty | Unmerged: `Solar/SolarParameters.cs` on `feature/building-footprints` |
| Robots cut off from the network work from the rules they last synchronised | Known versus synchronised | Unmerged: colony network, same branch |
| Seven-day power history | Historical record | `Game/PowerHistory.cs` |
| God mode (F10) | Shows actual state on purpose, as a debugging and teaching tool | `Presentation/Planet/GodMode.cs` |

And where the game does not yet follow the principle:

- The HUD, the colony panel, the robots panel and the tile panel read actual simulation state every frame.
- Robot charge, condition and position are always known exactly and instantly.
- There is no report, no inbox, no record of when something was learned.

The director decided OQ-1 on 2026-11-20: keep the existing panels, and new features follow section 5, until the director wants to add or change something. Existing panels are left alone.

## 4. Observation record (proposed)

A common shape for anything the colony learns. It is not implemented; a system that starts producing observations should use these fields so that reports and the knowledge database can be built on them later.

| Field | Meaning | Convention |
| --- | --- | --- |
| Subject | What it is about: a tile, a building, a robot, a planet parameter | A typed id |
| Quantity | Which property | An id from a fixed list |
| Value | The measured value | SI as in [`data-architecture.md`](data-architecture.md) |
| Unit | Stated, never implied | Shown with every number on screen |
| Uncertainty | Half-width of the range the true value lies in | Same unit as the value; zero only for counts and definitions |
| Confidence | How far to trust it | A small ordered scale (for example low, medium, high), not a fake percentage |
| Source | Who or what observed it | Robot id, instrument, dataset, or "assumed" |
| Observed at | Game time of the measurement | Hours since landing |
| Known at | Game time it reached the colony | Empty while unsynchronised |

Derived conventions:

- **Freshness** is the current time minus "observed at". Each quantity has a time after which the value is shown as stale. A stale value is still shown, with its age.
- **Missing information** is shown as unknown, with what would be needed to find out. It is never shown as zero.
- **Conflicting observations** keep both records; the display uses the most recent of the highest confidence.
- **Source labels** extend the existing three (NASA, derived, assumed) with "measured by the colony".

## 5. Rules for new features

1. Decide which of the three states the feature shows. If it shows actual state, say why in the specification.
2. A new fact about the world is unknown at landing unless the planet record lists it with a provenance label.
3. A number on screen has a unit and, for world properties, a source.
4. A value with meaningful uncertainty shows the uncertainty.
5. Anything the player is told about a place the colony cannot currently reach carries its age.
6. Automation acts on known state. A robot acts on what it holds itself.
7. Debug views that show actual state are clearly separate from play.

## 6. Path from today (proposed)

| Step | Change | Depends on |
| --- | --- | --- |
| I1 | Merge the solar measurements and colony network work; both already model uncertainty and synchronisation | Their branch |
| I2 | Introduce the observation record in Simulation and convert survey results and sky measurements to it | Architecture step M5 |
| I3 | A knowledge store per colony: latest known value per subject and quantity, with history | I2 |
| I4 | Reports: a generated, timestamped message when known state changes in a way the player cares about | I3; design needed |
| I5 | Move panels from actual state to known state, one panel at a time, as the director decides OQ-1 | I3 |
| I6 | Scientist consultation and the AI assistant read the knowledge store only | I3; design needed |

## 7. The AI assistant and external references

Recorded as constraints, since the feature is FUTURE:

- The assistant sees what the colony knows, never actual simulation state.
- It labels each statement by scientific class (see [`../physics/scientific-standards.md`](../physics/scientific-standards.md)).
- Online references are optional; the game is complete without a network connection.
- What it may read is defined by an explicit interface, so that its access can be reviewed.
