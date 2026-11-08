# Robotic workforce: repository audit and implementation plan

Status: **plan for review**. Nothing in this document is implemented yet.
Source: the Robotic Workforce Specification (sections 1–14) given on 2026-11-08.
Sections 11 (transportation) and 12 (robot rescue) are out of scope and stay unimplemented.

Legend used below: **Implemented** (exists in `main` today), **Reuse** (existing code the workforce builds on),
**New** (to write), **Placeholder** (structure only, values or rules pending a decision), **Future** (deferred by the spec).

---

## 1. What exists today (audit of `main` at 274f999)

| Area | Code | What it does | Relevance to the workforce |
| --- | --- | --- | --- |
| Simulation boundary | `Simulation/Game/IPlanetSimulation.cs` (≈40 members), `StubPlanetSimulation.cs` | All game state and commands; Presentation only talks through this | Add a `Workforce` read model and commands here; the stub owns the workforce instance |
| Time | `StubPlanetSimulation.Tick` → `Step(dt ≤ 0.25 h)` | Fixed sub-steps; grid, construction, colony, history, milestones each step | Workforce steps inside the same sub-step, after the grid decides power |
| Power | `Energy/PowerGrid.cs`, `Energy/Battery.cs`, `Energy/Rtg.cs` | One bus: generation (solar + RTG) → colony demand → battery charge/discharge → curtailed surplus | Robot charging is a load on this bus; robot batteries can reuse `Battery` (capacity, round-trip efficiency, C-rate) |
| Colony demand | `Energy/ColonyDemand.cs` | Demand grows with reliable supply; brownouts hold it back | Unchanged; robot charging is a separate load (priority to decide, D6) |
| Construction | `Game/Construction.cs` (`BuildCosts`, `Construction`) | **Build parts** in Wh made from spare power; sites finish FIFO automatically; cancel refunds, salvage 50 % | Construction robots would change how sites progress (D2). Build parts are the only "resource" |
| Buildings | `BuildingType { None, SolarPanel, Battery }`; slots per tile (`Planet/TileSite.cs`) | Panels (tiered by milestones, `Game/PanelTiers.cs`) and battery yards on slots | New building types (charging station, Robot Workshop, maintenance facility) go here; the battery yard is the spec's example charging facility |
| Progression | `Civilization/Kardashev.cs` milestones (planet K every 0.1), `PanelTiers` unlocks | Milestones unlock bigger panels | Candidate for "energy quotas" (D7) and for technology unlocks (D8) |
| Knowledge | `Solar/SkyNotebook.cs` (#48) | Measured declination, sun, latitude survey; unlocks best-angle advice; saved | A natural first "research/exploration" activity for Research robots |
| Terrain | `Planet/PlanetSurface.cs` (`TerrainType`, `Terrains` helper), `HexSphere.Neighbors` | Ten terrain types, water check, albedo, base-site rank; tile adjacency | Movement cost and accessibility per terrain (D9); paths along `Neighbors` |
| Saves | `Game/SimulationState.cs` (version 6), `RestoreState`, `Presentation/Game/SaveGameController.cs` (JsonUtility, F5/F9) | Versioned state; older saves load with defaults | Workforce adds version 7 with a migration (old saves → one general robot at the base) |
| UI | UI Toolkit panels (`UI/*`), `ColonyView` (C), `PlanetChooserView` (L), `SkyPanelView` (V); `Editor/MainSceneSetup.cs` wires the scene | Panel pattern, sorting orders, Set Up Scene | Workforce Overview panel follows the same pattern |
| Research docs | `docs/physics/physics-verification.md`, `docs/physics/notes/*` | Verified physics; geology notes recommend ore-placement and scanning rules (rover/drone LIBS) | No robot, robot-battery ageing or manufacturing formulas exist yet (D5) |

**Not present at all:** robots, groups, tasks, operating programs, materials or components (only Wh build parts), a technology/research system, manufacturing, movement, charging facilities other than the grid battery, condition/maintenance, battery health.

## 2. Reuse instead of duplicate

| Spec need | Reuse | Do not |
| --- | --- | --- |
| Robot charging | Draw from `PowerGrid` as an extra load each step | Build a second power network |
| Robot internal battery | Wrap `Energy/Battery` (capacity, RTE, C-rate) and add health on top | Write a new battery class for charge/discharge |
| Building charging stations, workshops | `BuildingType` + `Construction` sites + `BuildCosts` | A separate building system |
| Unlocks by progress | The milestone/`PanelTiers` pattern | A parallel progression counter |
| Robot movement between tiles | `HexSphere.Neighbors`, `Terrains` | A separate map |
| Saving | `SimulationState` versioning + `RestoreState` | A second save file |
| UI | UI Toolkit panel pattern + Set Up Scene | Hand-edited scene objects |

## 3. Proposed architecture

### Simulation (`Assets/_Project/Scripts/Simulation/Workforce/`, engine-independent, `double`)

| Type | Role | Status in phase 1 |
| --- | --- | --- |
| `RobotKind` enum | General, Mining, Construction, Maintenance, Transport, Research | New (only General is creatable until manufacturing) |
| `Robot` | Id, kind, group id, tile, state (Working / Idle / Charging / Maintenance / Shutdown), current task, optional overrides (program, priority) | New |
| `RobotGroup` | Id, name, program, priority settings, state (Automatic / Interrupted / Paused), members | New |
| `OperatingProgram` enum | Priority (default), Facility, Demand, TaskQueue | New; **Priority** implemented first, others Placeholder until phase 4 |
| `TaskPriority` enum | Critical, High, Normal, Low, Disabled (+ optional ranked list) | New |
| `WorkTask` + `TaskKind` | What can be worked on: kind, target (tile/slot/robot), priority, interruptible, progress | New; task kinds depend on D1 |
| `TaskBoard` | Open tasks generated from game state (e.g. construction sites); priority automation picks the highest available | New |
| `Workforce` | Owns robots, groups, board; `Step(hours, …)`; group capacity; auto-organise; manual interrupt/pause/resume; overrides preserved when group settings change | New |
| `RobotBattery` | `Battery` + health % (long-term capacity) + ageing hooks | Phase 2; **Placeholder** until D5 |
| `RobotCondition` | Condition % and thresholds; shutdown at 0 % | Phase 2; Placeholder until D4/D13 |
| `ChargingCapability` | Per facility: can charge, power W, simultaneous robots | Phase 2; values pending D4/D12 |
| `IWorkforce` | Read model + commands exposed through `IPlanetSimulation.Workforce` | New |
| `WorkforceState` | Save data inside `SimulationState` (version 7) | New |

### Presentation

- `WorkforceView` (UI Toolkit, `UI/Workforce/`): groups, members, current task, state (working/idle/charging/maintenance), battery and condition; Interrupt, Pause, Resume, Reconfigure. Proposed key **G** ("groups"; W and R are taken by the camera).
- Robot markers on sites (later); Set Up Scene adds the panel.

## 4. Phased plan (one PR per phase)

### Phase 1: workforce entities, groups, overrides, capacity, priority automation
- `Robot`, `RobotGroup`, `Workforce`, `TaskBoard`, `WorkTask`, `TaskPriority`, `OperatingProgram` (Priority implemented).
- Start with **one General robot** at the base (spec: `starting_units: 1`).
- Manual group creation; automatic organisation rule (D-AO); capacity check; individual overrides that survive group setting changes.
- Priority automation: each step, idle robots in Automatic groups take the highest-priority available task they can do.
- Critical tasks interrupt automatically; manual interrupt/pause/resume; after a temporary task, resume and re-evaluate.
- `IPlanetSimulation.Workforce`; save version 7 with migration.
- **Tests:** creation, group membership and capacity, overrides preserved, priority selection order, critical-only automatic interruption, pause/resume state machine, save round-trip and migration from version 6.
- **Blocked by:** D1 (which tasks exist), D2 (does construction need robots), D-AO, D-CAP.

### Phase 2: battery, energy use, charging, condition, maintenance
- `RobotBattery` (reusing `Battery`), consumption idle vs working, return at 20 %, resume at 80 % (both given by the spec).
- Charging at facilities that declare `ChargingCapability`; charging load on `PowerGrid`.
- Condition, degradation sources, threshold penalties, shutdown at 0 %; maintenance tasks; battery health and replacement.
- **Tests:** charge thresholds (20 %/80 % hysteresis), charging power and slot limits, grid load accounting, condition shutdown, repair vs replace, battery health separate from charge.
- **Blocked by:** D4 (all numbers), D5 (battery ageing model), D6 (grid priority), D12 (which facilities charge), D13 (hazards), D3 (materials for repairs).

### Phase 3: manufacturing, specialised robots, Robot Workshop, upgrades
- Robot recipes, manufacture as new units, Workshop building, technology-based upgrades for the General robot (chassis, motors, battery, processor, sensors, environmental resistance, equipment).
- **Blocked by:** D3 (resources/components), D8 (technology system), D14 (module model), D4 (costs and times).

### Phase 4: operating programs, interruption details, Workforce UI
- Facility, Demand, TaskQueue programs; advanced ranked ordering; per-task interruption rules (progress kept or lost).
- Full Workforce Overview UI with actions.
- **Blocked by:** D15 (per-task interruption rules), D16 (what "colony needs" means for the Demand program).
- A minimal read-only overview can ship with phase 1 so the robot is visible.

## 5. Unresolved decisions (need answers before the phase that uses them)

| # | Decision | Needed for | Notes from the audit |
| --- | --- | --- | --- |
| D1 | **Task catalogue for the first version.** Which tasks exist? Candidates from existing systems: build a construction site, remove/salvage a building, measure the sky (notebook), maintain/repair a robot or facility, charge. Mining and transport need resources/logistics that don't exist yet. | Phase 1 | Without real tasks, priority automation has nothing to choose |
| D2 | **Construction and labour.** Today sites finish automatically from spare power. Should sites need a robot present to progress? Does work rate depend on robots (how)? Should the old automatic behaviour remain for existing saves? | Phase 1 | Changes existing gameplay and the #38 balance |
| D3 | **Resources.** The spec mentions basic materials, components, mining and manufactured parts; the repository only has build parts in Wh. Which resource types (names, units), where they come from (the geology notes propose ore-placement rules), and whether build parts become one of them? | Phases 2–3 | Biggest missing system |
| D4 | **All numbers:** robot battery capacity (Wh), idle and working consumption (W, per task), charging power and slots per facility, group capacity start and upgrades, degradation rates, repair and manufacture costs and times, movement speeds. | Phases 1–3 | Spec forbids inventing them; options: you supply values, or approve clearly marked placeholder constants (`// TBD`) with no balance claims |
| D5 | **Battery ageing model** (normal use, deep discharge, repeated 0 %). Needs a published model added to `docs/physics/physics-verification.md` before code, per `CLAUDE.md`. | Phase 2 | No source in the repo today |
| D6 | **Grid priority** for robot charging versus colony demand, the battery yard and build-part production. | Phase 2 | `PowerGrid` serves one demand number today |
| D7 | **Energy quotas.** Are they the existing K milestones (every 0.1 of planet K), or a separate quota system (targets, deadlines, rewards)? | Progression | Milestones already gate panel tiers |
| D8 | **Technology / research.** No tech system exists. Unlock by milestones (like `PanelTiers`), or research points produced by Research robots, or both? | Phases 1 (capacity), 3 | Group capacity is "upgradeable through technology" |
| D9 | **Movement scale.** Do robots live on tiles (move along `HexSphere.Neighbors`) or on slots inside a site? Which terrains are inaccessible without equipment (water? glacier?), speed per terrain, energy per distance. | Phase 2 (range), later | Spec: hybrid movement, energy-based range |
| D10 | **Lander.** Is the lander a charging facility and the robots' home at the start? | Phase 1–2 | The lander's RTGs already feed the grid |
| D11 | **Scale.** Roughly how many robots late game (tens, thousands)? | Phase 1 data structures, UI | Affects per-robot simulation cost and the overview layout |
| D12 | **Charging facilities.** Which buildings charge robots (battery yard yes per the spec; panels? a new charging station?), and their power/slots. | Phase 2 | Spec requires explicit definitions |
| D13 | **Environmental hazards** that wear robots: which ones (cold, heat, dust, radiation, vacuum) and from which planet data? | Phase 2 | Planet data has air temperature and pressure; no dust or radiation fields |
| D14 | **Robot module model.** Do robots have explicit modules (chassis, motors, battery, processor, sensors, equipment) with their own condition, so repairs can keep processors? | Phases 2–3 | Needed for "repair preserves valuable components" |
| D15 | **Interruption per task.** Which tasks keep progress when interrupted? | Phase 4 | Spec: task-dependent, to be designed |
| D16 | **"Colony needs"** for the Demand program: brownout risk, low battery, sites waiting, damaged robots? | Phase 4 | |
| D-AO | **Automatic organisation rule** (e.g. by robot kind, by site). | Phase 1 | |
| D-CAP | **Group capacity** starting value. | Phase 1 | Part of D4 |

## 6. What can start now without new decisions

Only structure with no balance values:
1. `Workforce`, `Robot` (General), `RobotGroup` with manual groups, overrides preserved across group changes, the group state machine (Automatic / Interrupted / Paused) and manual interrupt/pause/resume.
2. `TaskPriority` and highest-priority selection over an abstract task list (tested with test-only tasks).
3. Save version 7 and migration (one General robot at the base tile).
4. A read-only Workforce Overview listing groups and the robot.

Everything that moves energy, wears robots or costs anything waits for D1–D6.

## 7. Tests planned (EditMode)

- Workforce: starts with one General robot at the base; group create/rename/delete; membership; capacity limit.
- Overrides: individual program/priority overrides survive group program or priority changes; clearing an override falls back to the group.
- Automation: idle robots take the highest-priority task they can do; Disabled tasks are never taken; ties broken by ranked order.
- Interruption: only Critical tasks interrupt automatically; manual interrupt works on any; after a temporary task the robot resumes and re-evaluates.
- States: Automatic → Interrupted → Automatic; Paused robots take no tasks; settings unchanged by pausing.
- Saves: version 7 round-trip; a version 6 save loads with one General robot.
- Phase 2+: 20 %/80 % charging hysteresis, charging power and slot limits, grid load, battery health vs charge, condition shutdown, repair vs replace.
