# Electrical system redesign: Stage 0 assessment and plan

Status: **PROPOSED, nothing built.** Stage 0 of the brief "Electrical Wiring System Redesign" (pasted by the director, 2026-11-20). This document inspects the repository, says what can stay, what must change and what is new, lists the decisions only the director can make, and proposes the stages. No code, no save change and no gameplay change comes with it.
Registry entries: EL-01 to EL-12 (new, all PROPOSED; see section 9).

## 1. Purpose

Build a coherent electrical engineering foundation: a mesh-capable electrical graph with physical cables, explicit building terminals, fixed voltage and frequency classes (DC first, AC through progression), resistance-aware transmission, and player-designed physical circuits (blueprints installed into host devices) solved by a real DC circuit solver. Pillars served: 1 (real science), 2 (few, clear commands), 6, 8, 9. Scope limits (no waveforms, no electrons, no dispatch policy) are the brief's section 21 and are kept.

## 2. What exists today

Checked against `main` at `2401f2a` (save version 42). Sizes are lines of code.

| Piece | Where | What it does |
| --- | --- | --- |
| Tile wires | `Game/PlanetSimulation.Wiring.cs` (554), `Game/WireBook.cs` (49) | A wire is a *building* (`PowerLine`, `HighVoltageLine`) on a slot of one tile, with a link target tile (`LinkTo`). Links join neighbouring tiles. Powered tiles are found by a breadth-first walk from the lander tile; `lineParent` makes a **rooted tree** (one parent per tile) |
| Flow | same file, `ComputeFlow` | Per-tile raw power from panels, turbines, wave lines and barrages (each source's generation is summed per tile), carried bottom-up to the lander through the tree, clipped to a **fixed link capacity** (plain 20 MW, high-voltage 1 GW, game rules), minus **2 % per hop** (`LineLossPerHop`). Top-down `tileFactor` gives each tile the share that gets through |
| Building wiring | same file, `wiredBuildings` | A building counts only once the player has dropped a power line on it (`PlaceBuildingWire`, instant, free). Generators unwired make nothing; stations, batteries, Workshop, Relay, research and refinery modules unwired do not work. Roles in `Game/WireRoles.cs` (12): output = generators, input = consumers |
| The grid | `Energy/PowerGrid.cs`, `GridStep` | **One** grid: one battery, one demand, one brownout flag, RTG, priority loads (network, robots, computing, Workshop, refineries). It receives one number, `ComputeGeneration()` |
| Tool and view | `Presentation/Planet/WireTool.cs` (167), `WireNotes.cs` (187), `GridView.cs` (239) | Click tile then next tile; markers and pop-ups; wire drawing. Presentation only reads `WireLinks()`, `UnwiredBuildings()` and similar through `IPlanetSimulation` |
| Save | `SimulationState` (`WiredGrid`, `WiredBuildings`; the lines are saved as ordinary `BuildingRecord`s with `LinkTo`) | Version 42. Old saves: before 37 everything counts as wired (`WireAllBuildings` on load) |
| Tests | `Tests/EditMode/Game/WiredGridTests.cs` (about 30 tests) and the mission-operations scenario tests | Links, overload, high voltage, flow, building wires, save/load |

### What depends on it

`IsPowered`, `PlaceWire`, `PlaceBuildingWire`, `UnwiredBuildings`, `LineFlowW`, `LineCapacityW`, `GridEfficiency`, `HasLineOn`, `WiredGrid` are used by: `PlanetSimulation.Footprints.cs` (placement gate: stations and storage only where the grid reaches), `.Save.cs`, `IPlanetSimulation(.Roles).cs`, `GameFlow.cs` (tutorial step, Skip wires everything), `SimulationRunner.cs` (new missions wired), `PanelPlacer.cs` (power-line card drops on a building), `TileInfoView.cs`, `GridView.cs`, `WireTool.cs`, `WireNotes.cs`, `DevDriver.cs`, and about 40 existing tests. The refineries, Workshop, Relay, Research Station, chargers and batteries read it through `WiredOrOff`.

## 3. Assessment against the brief

| Brief requirement | Today | Verdict |
| --- | --- | --- |
| Mesh, loops, several paths | Rooted tree by construction (`lineParent`) | **Replace** the topology and flow code; keep the tile-to-tile link idea |
| Independent networks, splitting | One network: whatever the lander reaches | **New**: connected components of the electrical graph |
| Separate physical and electrical layers | Wires are slot buildings; connectivity is "tile has a link" | **Refactor**: the electrical graph becomes its own pure model; slot buildings stay as the *physical* posts |
| Explicit terminals on buildings | A per-building boolean (`wiredBuildings`) | **Refactor** into terminals connected to a node; the boolean maps to "terminal connected" |
| Cable physics (R = rho L / A, rating, losses) | Fixed MW capacity and 2 % per hop | **Replace** the two constants with a cable specification; keep them only as migration defaults |
| Fixed voltage classes, AC and DC | None | **New** |
| Circuit blueprints, instances, solver, editor | None | **New** (largest part) |
| Single `PowerGrid` | One battery, demand, brownout | **Decision needed** (section 6, D1): islands vs one grid |
| Simulation / Presentation split, interface only | Kept | **Keep**; add role interfaces and read models |
| Determinism, stable ids, saves | Sorted iteration used; ids are tile and slot | **Keep**; new ids needed for nodes, connections, terminals, blueprints, instances |

**What can be retained:** the tile-to-tile link concept and neighbour rule; the placement gate idea; the roles (output/input); the wire tool's click flow and markers (re-pointed at the new commands); the building definitions and bills of the existing `PowerLine` and `HighVoltageLine` (as the first two cable products); all generator code (`RawPanelsByTile` and the others) unchanged; `PowerGrid` unchanged until D1 is answered; the tests as a regression suite (they must still pass through a compatibility layer).
**What must be replaced:** the tree flow (`ComputeFlow`, `lineParent`, `lineHops`, `edgeCapacity`), the per-hop constant loss, the fixed capacities.
**New systems:** `Electrical` namespace (graph, cable specs, materials, voltage and frequency classes, terminals, devices, connectivity), `Circuits` namespace (components, circuits, blueprints, instances, solver, diagnostics), the circuit editor (Presentation), an engineering-facility building.

## 4. Save compatibility

Current version 42. The change adds electrical state (nodes, connections, terminals, cable specs, blueprints, instances); each stage that adds state bumps the version once and carries a migration test.
- **Migration of today's world:** every existing `PowerLine` or `HighVoltageLine` becomes an `ElectricalConnection` between the two tile nodes (cable spec derived from the tier: plain = aluminium rated to carry 20 MW at its voltage class, high-voltage likewise 1 GW; the legacy `LinkTo` stays readable); every wired building becomes a terminal connected to its tile node; an unwired building gets an unconnected terminal. Versions before 37 keep the "everything wired" rule.
- Derived data (components, flows, losses, powered flags) is rebuilt, not saved.
- The legacy fields (`WiredGrid`, `WiredBuildings`, the line `BuildingRecord`s) stay readable for at least one version after the switch; removal is a separate, announced step.
- Enums stay append-only (`BuildingType` for new equipment, `TaskKind` for any work).

## 5. Performance and determinism risks

- **Connectivity** must be incremental: components recomputed only when a connection or terminal changes (a topology stamp), not each step (today `EnsureWiring` already caches by stamp).
- **Power flow on a mesh** is a linear-algebra problem, not a walk; sizes matter (planet tiles are in the thousands, but wires follow the player's routes, so graphs are small). A sparse nodal solve with a fixed node ordering (by stable id) is deterministic and cheap at these sizes. Floating-point order must be fixed (sorted ids, no hash-order sums).
- **Circuit solving** per installed circuit: modified nodal analysis on tens of nodes; re-solved only when its topology or a switch state changes, not each step.
- No allocation on the step path: reuse arrays; derive per-step values from cached components.
- The existing step budget (2.2 ms per step with 300 robots and 26 plants) must not regress noticeably; a test guards it.

## 6. Decisions the director must make

These are missing rules. They are asked, not decided.

- **D1. Islands and the single `PowerGrid`.** The game has one battery, one demand and one brownout flag. Proposal for stage 3: the island that contains the lander is the *main grid* and keeps feeding that one `PowerGrid` (today's behaviour); a device in another island is "connected but not on the main grid" and does not contribute or consume until the future per-island solver (out of scope) exists. This keeps every current number. Alternative: give each island its own grid now (much larger change, needs dispatch rules the brief defers). **Recommend the first.**
- **D2. Voltage classes (candidate table, not decided).** Physics: for a cable of resistance R carrying power P at voltage V, the loss is P R / V squared times P, so a hundred times the voltage cuts the loss ten-thousand-fold. Example with aluminium (resistivity 2.83e-8 ohm m, the repo's 61 % IACS), 1 000 mm2, one tile of 300 km (R = 8.5 ohm), 20 MW: at 33 kV the current is 606 A and the loss 3.1 MW (16 %, far above the game's 2 % per hop); at 500 kV the current is 40 A and the loss 14 kW (0.07 %), for one conductor; a two-conductor DC line doubles the resistance. So the class ladder decides how far a tier can transmit. Candidate DC ladder: 24 V (robots and tools), 400 V (a colony site), 33 kV (regional), 500 kV (planetary, high-voltage DC). Candidate AC (tier 3): 400 V, 11 kV, 132 kV, 400 kV with one fixed frequency each tier (candidates 50 Hz or 60 Hz; the choice is cultural, not physical). Questions: how many classes, which numbers, and which milestone unlocks each.
- **D3. Cable length between tiles.** Circuit wires have a manually configured length (confirmed). For tile-to-tile cables: should length be the real centre-to-centre distance (a tile is about 300 km on an Earth-sized planet, so cables are very long and voltage class dominates) or a configurable value? **Recommend the real distance**, since the planet's size is already a game input; and cable cross-section chosen from a small catalogue (the cable product), not free.
- **D4. Which cable products and materials at the start.** Copper (1.72e-8 ohm m) and aluminium (2.83e-8) exist (the repo's stand-ins and IACS). Propose two products at launch: the existing plain and high-voltage lines, becoming "aluminium cable, 33 kV class" and "aluminium cable, 500 kV class"; copper and silver later. OK?
- **D5. Circuit hosts and the facility.** The brief says circuits are designed in "engineering facilities" and installed in "supported buildings, robots and machinery". Which building is the facility (a new `BuildingType`, e.g. Engineering Bay), and which hosts take circuits first (proposal: Charging station, Relay, Workshop)? What is the circuit capacity of a host (components and terminals)?
- **D6. Technology tiers.** The four proposed tiers need unlock rules: milestones, research experiments or both? (No lore or research cost is invented here.)
- **D7. Phase order of the work.** This is many pull requests (section 7). Confirm that stages 1 to 3 (graph, cables, building terminals) come before any circuit editor work, and that the legacy wire tool keeps working throughout.
- **D8. Legacy behaviour.** Keep "nothing is wired automatically" (decided 2026-11-20) as the rule for terminals; the Skip-setup shortcut wires what it places. Confirm.

## 7. Staged plan (one pull request per stage, each buildable and tested)

| Stage | Content | Save | Tests |
| --- | --- | --- | --- |
| 0 | This document, registry rows, open questions | none | none |
| 1 | `Electrical` namespace: node, connection, terminal, device, network; connected components (union-find, deterministic order), incremental by topology stamp; mesh, splitting, independent networks. A compatibility adapter builds the graph from today's links; nothing uses it for power yet | +1 (graph state) | topology, loops, splits, determinism, adapter equals legacy reachability |
| 2 | Materials (data table), `CableSpec` (rho L / A, rating, voltage class), voltage and frequency classes (values from D2), voltage drop, I squared R loss, current limit; a pure two-terminal transmission calculator | none or +1 | resistance, drop, loss, rating, class compatibility |
| 3 | Terminals on the existing buildings; a nodal power-flow solver for the main island (D1) behind an interface that the future dispatch can replace; the legacy tree flow removed after the new solver reproduces it within tolerance on the regression suite; migration of lines and wired buildings | +1 and migration | all of `WiredGridTests` through the new solver, migration tests, scenario tests |
| 4 | Circuit data model: components (data), circuit graph, blueprint, instance, power and signal ports | +1 | serialization, validation |
| 5 | DC solver: modified nodal analysis, sources, resistors, switches, lamps, series and parallel, junction conservation, open and short detection, diagnostics | none | the brief's circuit test list |
| 6 | Circuit editor (Presentation): grid workspace, placement, rotation, wire drawing, explicit junctions, wire length, probes, test, save blueprint | none | Play-mode checklist (no Presentation tests exist, KI-26) |
| 7 | Engineering facility and installation: host compatibility, instance runtime, control interfaces to machines | +1 | install, compatibility, interface tests |
| 8 | AC: RMS steady state, fixed frequency classes, transformer, rectifier, inverter | +1 | AC list in the brief |
| 9 | Hardening: migration docs, performance guard, documentation, registry verification | none | regression, performance |

Each stage report follows the brief's section 23 (what and why, files, tests added and run, documents, limits, decisions needed). Time dependencies: stage 3 needs D1 and D2; stage 5 needs nothing beyond stage 4; stage 7 needs D5; stage 8 needs D6.

## 8. Interfaces (proposed, per the architecture rules)

- New role interfaces on `IPlanetSimulation`: `IElectricalNetwork` (read models: nodes, connections, networks, terminal status, per-device state "connected, compatible, operational, receiving power, receiving enough") and `IElectricalCommands` (place node or cable, connect terminal, remove); `ICircuitCommands` and `ICircuitReadout` for blueprints and instances. Presentation submits commands and reads values; it never touches a model object.
- The five states of the brief (connected, compatible, operational, receiving power, receiving enough) are separate fields of the device read model from stage 3.
- Power-flow and consumer allocation sit behind an interface (`INetworkSolver`) so that dispatch policy can be designed later without touching the graph.
- Circuits never write into other systems; a circuit's outputs (a relay contact state, a load's draw, a sensor input) are values the orchestrator passes (rule 5).

## 9. Registry rows to add (all PROPOSED, "Designed")

EL-01 electrical graph with mesh and independent networks; EL-02 explicit building terminals; EL-03 cable specification and physics; EL-04 fixed voltage classes; EL-05 fixed frequency classes (AC tier); EL-06 main-island power-flow behind an interface; EL-07 circuit components, blueprints and instances; EL-08 DC circuit solver and diagnostics; EL-09 circuit editor; EL-10 engineering facility and hosts; EL-11 AC components (transformer, rectifier, inverter); EL-12 migration of the legacy wiring. The registry gets these rows when the director approves the plan.

## 10. Risks

- **Scale.** This is the largest single design so far; the staged plan keeps each pull request reviewable.
- **Behaviour change.** The 2 % per hop and fixed capacities become physics; some colonies will see different losses. The migration derives cables that reproduce today's capacities; the loss figure will differ and is reported, not hidden.
- **Two sources of truth** during stage 1 to 3 (legacy links and the graph): the adapter is one-way and tested for equality.
- **Editor effort.** The circuit editor is a large Presentation job with no automated tests; it is last among the visible stages on purpose.

## Gate G1 calibration (stage 2 result, candidates only)

Today's model against the physical solver, one generator at the end of a chain of 300 km tiles, one 500 mm2 copper conductor. The voltages (33 kV, 500 kV) and the cable product are **candidates**, not approved (D2, D4). Run `CalibrationTests.PrintTodayVersusPhysicalTable` for the full table.

| Line | Generated | Hops | Today delivered | Physical delivered | Physical loss |
|---|---|---|---|---|---|
| plain 33 kV | 1 MW | 1 | 0.98 MW | 0.99 MW | 0.9 % |
| plain 33 kV | 5 MW | 3 | 4.71 MW | 4.44 MW | 11 % |
| plain 33 kV | 20 MW | 1 | 19.60 MW | 17.19 MW | 14 % |
| plain 33 kV | 20 MW | 3 | 18.82 MW | 14.23 MW | 29 % |
| plain 33 kV | 100 MW | 1 | 19.60 MW (clipped) | 62.68 MW, 1899 A | 37 % |
| high 500 kV | 20 MW | 3 | 18.82 MW | 19.95 MW | 0.2 % |
| high 500 kV | 1 GW | 1 | 980 MW | 962 MW, 1923 A | 3.8 % |

What it shows: at about 1 MW the two models agree. At 20 MW a plain line loses 14 to 29 %, not 2 % a hop, and high voltage loses far less than today's flat 2 %. Today's 20 MW clip corresponds to a current rating of about 600 A at 33 kV; a 1000 A rating would let a plain line carry about 35 MW. Nothing in gameplay uses these numbers yet.

## Gate G0(d): the cable build bill (director decision needed)

Today a power line costs 15 Cu, 30 Fe, 20 rock per tile. One 500 mm2 copper conductor over a 300 km tile weighs 1.33 million kg; CO-03a (real mass x 1 % on site) would give **13,335 kg of copper** per tile, about 890 times today's bill. Aluminium for the same resistance weighs about half (4,054 kg Al x 1 % at equal resistance would be 6,700 kg).

| Option | One plain tile | Effect |
|---|---|---|
| A. Real mass x 1 % (CO-03a as written) | about 13.3 t Cu | Honest, but a line to the next tile needs more copper than the early colony will mine; makes aluminium and the refining chain matter at once |
| B. Real mass x a smaller share (for example 0.01 %) | about 133 kg Cu | Metal still scales with area, length and material, but a line stays affordable; the share is a new game rule to tune |
| C. Keep the flat bill; use the physics only for advice and salvage | 15 Cu | Nothing changes in play; cable choice shows in loss only |

Recommendation: C now, then B once mining output is known, so the share can be tuned to a number of days of mining. The advice function and the mass formula are built and tested either way.

## Gate G3: how much the interface hides (director decision needed)

The simulation reports honestly: an unmetered line's flow is NaN, so a window can choose. Options for the Power window and the grid view:

| Option | Unmetered line shows | Cost to the player |
|---|---|---|
| A. Hide fully | only its design capacity and a "not measured" note; no colour by load | The player must build meters to see where a grid is overloaded; most "information as observation" |
| B. Show the estimate | capacity plus a rough range (0 to capacity); the overload colour only when measured | Middle: the player sees the plan, not the live state |
| C. Show everything as today | true flow, with a small marker for measured lines | Nothing hidden; meters become a cosmetic |

Recommendation: B. Also open: price of a meter, and the robot Inspect task (a robot walks the line and reports condition and faults; needs the cable condition from stage 8 to be worth doing).

## Decisions of 2026-11-23 (director delegated, confirmed in chat)

The AC and airless-world questions, the meter price and the build order are decided and recorded in the registry decision log (EN-35, EN-38). Summary: one 50 Hz AC frequency; AC classes 400 V, 33 kV, 500 kV; DC classes 24 V (DC-only), 400 V, 33 kV, 500 kV; converter efficiencies in `VoltageLadder` (all TBD, recalled from published ranges: they must be checked before tuning); corona in three regimes (Peek, Paschen fallback, none in vacuum); the derate never lowers a voltage class; a meter costs 2 Cu and 1 Fe; a circuit's supply is a grid load. Follow-ups: the AC line type and the Transformer, Inverter and Rectifier buildings; fuses and breakers on lines; drag-to-wire in the grid schematic; a convection term in the cable thermal model (so an airless world cools only by radiation); the real-mass cable bill in the balance pass.

## AC in play (built 2026-11-23, EN-43)

The planet grid knows three media: DC, AC 33 kV and AC 500 kV. A line joins two tiles in one medium. A converter on a tile crosses between media, and only in the direction the power flows: from beyond the lander, a Rectifier turns AC into DC (placed where the AC line meets the DC grid), an Inverter sends DC onto the AC line (placed at the generating end), a Transformer joins the two AC classes. Each crossing multiplies the generators' power by the converter's efficiency; the loss is heat. In the nodal flow a three-phase line of class V is one conductor at V (loss 3 I2R = P2 R / V2), with the current rating counted per phase, so the AC and DC lines of the same voltage lose the same and AC differs only by the converters and the larger rating. The trade the decision asked for follows: a transformer changes voltage cheaply, conversion between DC and AC costs efficiency and material, DC stays better under the sea (AC is refused over 100 km of sea). Open: costs, the milestone and all efficiencies are TBD.
