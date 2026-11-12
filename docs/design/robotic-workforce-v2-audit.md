# Robotic workforce, spec v2: audit and staged plan

Status: **in progress**. Stages A (#56), B (#57), C (#58) and D are done; stages E–H are not started.

- **Source:** "Kardashev Ascent — Robotic Workforce System: Claude Implementation Handoff" (spec v2, 2026-11-08).
- **Compared against:** the top of the unmerged stack #50 → #51 → #52 → #53 → #54 → #55 (branch `feature/landing-robots-camera`).
- **`main`** is still 274f999. Nothing in the workforce stack is merged.
- **Open issues:** only #14 and #15, both about submission, so they are out of scope.

Legend: **Done** (built and tested in the stack), **Partial** (built, but short of spec v2), **Absent**, **Conflict** (built differently from what spec v2 says), **Future** (spec v2 says do not build yet).

---

## 1. What exists today

| Spec v2 area | Status | Where it is | Gap |
| --- | --- | --- | --- |
| §1 One General robot, quotas unlock things | Done | `Workforce.Reset`; K milestones → `RobotTech` unlocks | Unlocks cover robots, modules, refining and group capacity. Buildings unlock only through panel tiers. |
| §2 Hybrid workforce, 5 specialists made as new units | Partial | `RobotKind`, `RobotTech.Robot`, Workshop jobs | **Conflict:** Transport robots can't be made, because spec v1 said not to build transport. Spec v2 confirms Transport as a category but keeps logistics future (see Q3). |
| §2 Compatibility data-driven; secondary capabilities via research | Partial | `RobotAbilities.CanDo`/`Speed` (a `switch`) | Not a data table. No secondary capabilities. No advanced tasks that only specialists can do. |
| §3 Groups, auto and manual, capacity by technology, overrides | Done | `Workforce`, `RobotGroup`, `RobotTech.GroupCapacity` | Group presets (General Operations, …) are absent. They are a proposal, not a requirement. |
| §3 Four programs, states, five priority levels, ranked order | Done | `OperatingProgram`, `GroupState`, `TaskPriority`, `RankOf` | — |
| §3 Critical-only automatic interruption, per task type | Partial | `Workforce.Assign`, D15 (a survey restarts) | Any Critical task interrupts any non-Critical one. There is no per-task rule for **whether a task may be interrupted** or for how much progress it keeps. |
| §4 Common task lifecycle and states | Partial | `WorkTask` is rebuilt from game state on every assignment | No explicit states (Reserved, Travelling, Blocked, Assistance needed, Cancelled …) and no blocked reasons. |
| §4 Scheduler factors | Partial | Done: priority, compatibility, availability (battery/condition), energy range, program/work area, terrain access | Absent: **specialist preference, nearest robot, balanced workload, facility demand/capacity, manual reservations, equipment/technology requirements.** |
| §4 Allocation: several robots per facility, capacity, balance | Absent | One robot per task | — |
| §4 Temporary replacement while charging; return to the original assignment | Absent | A robot that goes to charge drops its task, and anyone may take it | The original assignment is not kept. |
| §5 Facility workforce requirements and diminishing-returns productivity | Absent | Panels and battery yards run with no workers. The Workshop needs exactly one robot. | The biggest new system (Q2). |
| §6 Core and specialized attributes, combined productivity | Partial | `RobotAbilities.Speed`, `SpecialistSpeed`, `FastDrive` | No attribute model. Condition doesn't reduce performance. |
| §7 Idle + task energy, 20/80 thresholds, range to a charger | Done | `RobotSpecs`, `RobotUpkeep`, `Workforce.InRange` | AI processing energy is absent. |
| §7 Charging facilities declare capability, power, slots; PowerGrid | Partial | `ChargingCapability` (lander, battery yard), dock and priority on `PowerGrid` | No dedicated **Charging Station** building and no access restrictions. |
| §7 Charge vs health; ageing | Partial | `RobotBattery` (linear in equivalent full cycles, `physics-verification.md`) | **Deep discharge and repeated 0 % don't speed up ageing**, though spec v2 confirms they should. This needs a sourced model (D5) and a minor battery repair. |
| §7 Energy, mechanical and critical-damage shutdown as separate states | Partial | `RobotState.Shutdown` covers condition 0 and stranded | Not separated. No critical damage. |
| §8 Wear from operation, heavy workloads, environment | Partial | Wear per working hour, flare wear | Wear doesn't depend on the task. Performance doesn't drop with condition. |
| §8 Maintenance automatic and configurable, progressive, keeps parts | Partial | Automatic repair at the lander with build parts; a Maintenance robot speeds it up | No per-group rules. Repairs use no materials or components. There is no advanced maintenance. |
| §9 Manufacturing early, mid and late; Workshop upgrades | Partial | Workshop, recipes, substitutes; battery, drive and shielding modules | No component tiers or assembly. No chassis, processor or sensor upgrades. |
| §10 Movement: terrain speed, inaccessible terrain, energy range | Done | `RouteFinder`, drive power ∝ g | No physical working limit (a maximum distance). No infrastructure speed-ups (future). Ice needs no equipment. |
| §10 Roads, rail, tunnels, drones | Future | Not built | Correctly not built. |
| §11 Environment and resistance | Partial | Gravity (driving), terrain, flares and shielding | Temperature (data exists: `airTemperatureC`) is absent. Pressure, dust and radiation have no or only partial planet data. |
| §12 AI levels, network, autonomy, memory, sync, backups, AI energy, colony computing | Absent | — | A whole new area (Q1, Q4). |
| §13 Hybrid visible and background simulation | Partial | Rovers are visual only (`RobotView`), and the simulation never reads them | Every robot is simulated each step. No aggregate path for large colonies, and no performance test. |
| §14 UI: overview, groups, robot inspection, task monitor | Partial | Robots panel (G), TASKS list | Missing: counts by state, a detailed inspection view (attributes, modules, AI, resistance), and task states, priorities and reasons. |
| §15 Module boundaries | Partial | See the mapping below | `StubPlanetSimulation` is now 1,211 lines and does the colony's orchestration too (risk R2). |

**Mapping to the spec's proposed modules:**
- `RobotSystem` → `Robot`.
- `WorkforceManager` → `Workforce`.
- `TaskScheduler` → `Workforce.Assign` + `StubPlanetSimulation.OpenTasks`.
- `RobotMovement` → `RouteFinder`.
- `RobotEnergy` and `RobotMaintenance` → `RobotBattery` + `RobotUpkeep`.
- `RobotManufacturing` → `Manufacturing` / `RobotTech`.
- `RobotAI` and `RobotCommunication` → absent.

## 2. Reuse, and what is new

| Reuse as it is | Extend | New |
| --- | --- | --- |
| `PowerGrid` (dock, priority load), `Construction`, `BuildCosts`, `SimulationState` versioning, `RouteFinder`, `DepositMap`, the Workshop, milestones | `Workforce.Assign` → a scheduler with scoring and reservations. `WorkTask` → a task record with a lifecycle. `RobotSpecs`/`RobotTech` → one configuration catalogue. `RobotBattery` → depth-of-discharge ageing. `RobotState` → separate shutdown kinds. | `RobotCatalog` (attributes, compatibility). `FacilityWorkforce` (staffing, productivity curve). `TaskBook` (task records, states, reasons). `ColonyNetwork` + `RobotAI` (levels, autonomy, memory, sync). `ColonyComputing` (a PowerGrid load with priorities). |

## 3. Staged plan (one PR per stage, each small and tested)

The spec's suggested order (§19) has been changed in two places, for these reasons:
- **Configuration and attributes come first.** Every later stage needs them.
- **Facility workforce comes before energy v2.** Replacement staffing (§4) depends on facilities having staff.

| Stage | Contents | Main files | New tests | Save |
| --- | --- | --- | --- | --- |
| **A. Configuration and attributes** (done, #56) | `RobotCatalog`: per kind, core attributes (speed, strength, energy efficiency, durability, processing) and specialized ones (mining, construction, repair, transport capacity, research, sensors). A compatibility table (task kind → allowed kinds, efficiency, required technology), replacing the `switch`. One productivity function (kind × attributes × condition × modules × environment), with condition giving a gradual drop plus threshold penalties. Separate shutdown states: energy, mechanical, critical damage. All values are TBD placeholders in one place. | `Robots/RobotCatalog.cs` (new), `WorkTask.cs`, `Robot.cs`, `RobotUpkeep.cs` | Compatibility from data; secondary capability after an unlock; condition penalty curve; three shutdown kinds | 13 (attributes, shutdown kind) |
| **B. Task lifecycle and scheduler** (done) | `TaskBook` keeps task records across assignments. States: Available, Reserved, Travelling, Working, Paused, Blocked(reason), AssistanceNeeded, Completed, Cancelled. An interruption policy per task kind (may be interrupted? keeps progress?). Scoring: priority, then suitability (specialist efficiency), then travel time, then workload balance; stable tie-breaks for determinism. **Manual reservations** (robot ↔ task or facility) win over automatic allocation, and a reassignment must have a recorded reason. | `Robots/TaskBook.cs` (new), `Workforce.cs`, the stub's task generation | Ordering, specialist preference, nearest robot, reservation precedence, no silent reassignment, blocked reasons, deterministic results | 14 (tasks, reservations) |
| **C. Facility workforce** (done; panels and battery yards stay unstaffed until Q2 is answered) | `FacilityWorkforce` per building type: capacity, the robot kinds needed for full efficiency, output with no workers, and a diminishing-returns curve (configurable, e.g. `1 − e^(−k·n)`, with suitability weights). Facilities request workers. A shortage is reported to the player. Balanced allocation, never above capacity. While a worker charges, a temporary replacement is found if possible; otherwise the facility runs at reduced output, and the original worker comes back. | `Robots/FacilityWorkforce.cs` (new), stub generation/output hooks | Capacity limit, diminishing returns, partial staffing, balance across facilities, replacement and return, shortage notice | 15 |
| **D. Energy and condition v2** (done; depth exponent and 0 % damage are unsourced TBD values) | Battery ageing that depends on depth of discharge, using a sourced model added to `physics-verification.md` first (the NREL DoD term or Xu et al. 2018 stress factors), plus extra wear for repeated 0 %. Minor battery repair. Wear that depends on the task (heavy workloads). A **Charging Station** building with access rules. Per-group maintenance rules (thresholds). Basic maintenance uses materials as well as energy. Temperature effects where planet data exists, and resistance modules. | `RobotBattery.cs`, `RobotUpkeep.cs`, `RobotSpecs.cs`, `BuildingType` | Deep vs shallow cycling ageing, 0 % events, minor repair, task wear, station capacity and access, maintenance rules | 16 |
| **E. AI, network and knowledge** | AI level from processor module + software research. Onboard AI draw that depends on the activity. A `ColonyNetwork` (rules in Q1). While disconnected, robots finish their task and then choose from **cached** program and priorities, and nearby robots exchange information directly. On reconnection, data, task progress and instructions are synced. Individual memory (discoveries such as survey results, deposits, routes) and a shared colony database, with periodic backups; unsynced data is lost when a robot is destroyed (Q4). A failure ladder: try alternatives, then ask a suitable robot for help, then notify the player, keeping progress. Colony computing as a `PowerGrid` load with priorities when power is short; basic autonomy never needs it. | `Robots/RobotAI.cs`, `Robots/ColonyNetwork.cs`, `Robots/Knowledge.cs` (new) | Disconnected operation with a cached program, nearby exchange, sync on reconnect, backup and loss, AI energy, computing shortage priorities, basic autonomy without computing | 17 |
| **F. Manufacturing v2** | Component tiers (basic → manufactured components → advanced). An assembly step. Chassis, processor, sensor and equipment upgrades. Choosing components in recipes through technology. | `Manufacturing.cs`, `RobotTech.cs` | Tiers gate recipes, components are used, upgrade effects go through the catalogue | 18 |
| **G. UI** | Overview counts (active, charging, maintenance, disabled). Group presets as templates. A robot inspection view (attributes, modules, battery, AI, resistance, maintenance). A task monitor (states, priority, assignee, blocked reason, assistance requests). Notifications. | `WorkforceView.cs`, new `RobotInspectView.cs`, UXML | Presentation only, no simulation tests | — |
| **H. Performance and determinism** | Split the colony orchestration out of `StubPlanetSimulation` (R2). Step background facility work in aggregate. Check a large colony (e.g. 500 robots) against a step-time budget. Check that the same inputs give the same outputs, and that the visuals don't change results. | Stub refactor, tests | Determinism; performance budget (EditMode) | — |

## 4. Save migration

Each stage raises `SimulationState.Version` by one and keeps reading older saves:
- Missing fields get defaults: attributes from the catalogue, shutdown kind from the old state, task records rebuilt from orders, no reservations, network connected, empty memories.
- Each stage adds a migration test from the version before it, as stages 1–4 and movement already did (versions 7–12).

## 5. Risks

- **R1. The stack is deep and unmerged.** Six PRs (#50 → #55) wait on one another. Stage A should start after they are merged, or conflicts will grow with every stage.
- **R2. The stub is growing too large.** `StubPlanetSimulation` (1,211 lines) holds the grid, construction, robots, resources and manufacturing. New stages should go into separate services; stage H finishes the split.
- **R3. Placeholder balance.** Every new number is TBD (spec §0). Playtests will show odd balance until values are designed.
- **R4. Per-robot simulation cost.** Fine for hundreds of robots; thousands need stage H's aggregate path.
- **R5. Physics sources.** Depth-of-discharge ageing and any temperature effects need published sources in `physics-verification.md` before code (`CLAUDE.md`).

## 6. Questions that change what gets built

- **Stage E, first part — BUILT 2026-11-12** (`Robots/ColonyNetwork.cs`, save version 22): the hub, relays, range, brownout, and
  cut-off robots on cached rules (below). Still to come from stage E: AI levels and the processor module, onboard AI power
  draw, nearby robot-to-robot exchange, individual memory and the colony database with backups, the failure ladder, and
  colony computing as a grid load. Design notes from building it:
  - The network's draw (hub + relays) is served as a **priority load** next to critical robot charging, not as part of the
    growable colony demand: the hub is the last thing to lose power, and it goes down only when the colony itself browns out.
  - A cut-off robot keeps the program and priorities it last synced (`Robot.CachedProgram`/`CachedPriorities`). Under a
    cached Demand program it works at Normal priority, since it can't hear the colony's needs. It syncs on reconnect.
  - **A colony that can't power itself loses its network**: a fresh colony with no panels browns out within a day on the
    lander's RTGs, and the hub goes with it. This is by design (Q1), and the Network window says so. Tests that need the
    network up give the colony panels first.
  - Survey and measurement results still land at once while cut off; "unsynced findings" is counted but not yet delayed.
- **Q1 (network): DECIDED 2026-11-08: relay range + hub power.** Stage E builds it like this; the details below are design choices, and the numbers are configurable TBD placeholders.
  - **Hub:** the lander is the network hub. It draws a small load from `PowerGrid` as part of colony demand. When the colony is in brownout, the hub is unpowered and the whole network is down.
  - **Range:** a robot is connected while it is within the relay range of the hub or of a powered **Relay** (a new building). Range is counted in tiles along `HexSphere.Neighbors` (TBD). Relays extend coverage and need power too; a relay on a browned-out grid is down.
  - **Disconnected robots** keep working on their own: they finish the current task, then choose from their cached program and priorities. Nearby robots exchange information directly. Everything syncs when the robot reconnects (spec v2 §12).
  - **No other causes** (events, damage) for now; events are future scope.
- **Q2 (facility staffing, blocks stage C):** which buildings use workers?
  - Recommendation:
    - **Panels:** optional; workers raise output by cleaning and tracking, at up to 1 robot per N slots.
    - **Battery yards:** no workers.
    - **Workshop:** 1–3 workers, with Construction robots most effective.
    - **Mining:** a mining order becomes a "mine site" facility with capacity.
- **Q3 (Transport robots):** spec v2 confirms Transport as a category, but logistics is future work and materials are one colony-wide stock, so Transport robots would have no task. Options:
  - keep them locked until logistics is designed (recommended);
  - allow them now, as General-like robots with a transport-capacity attribute and no tasks of their own.
- **Q4 (destruction):** robots can't be destroyed today. Spec v2 needs destruction for "unsynced data is lost". What destroys a robot: critical damage, events, the player scrapping it?

Routine choices are made in the code, documented and configurable, as spec v2 §20 asks.
