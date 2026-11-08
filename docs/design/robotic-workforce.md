# Robotic workforce: repository audit and implementation plan

Status: **implemented**. The recommendations in section 7 were approved on 2026-11-08; section 9 lists what was built, in which PR, and what is still a placeholder or a later feature.
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

## 7. Recommendations for the decisions (for review)

These are recommendations, not decisions. Each one says what it rests on:
- **[repo]**: a sourced note already in `docs/physics/`.
- **[NASA]**: NASA Exoplanet Archive values in `planets.json` or the archive cache (retrieved 2026-11-05).
- **[source]**: a published source found for this plan; it still needs a check before it goes into `physics-verification.md`.
- **[design]**: a game choice with no physical source.

The high-impact decisions come first, in this order: D3, D5, D6, D7, D8.

### D3. Resources, and how they differ by planet

**Principle.** Keep **build parts (Wh)** as the energy every building costs; it already works and is balanced (#38). Add a small set of **materials (kg)** on top. A planet that lacks a material is still playable, because each scarce material has a **substitute** that costs more energy or performs worse. Poorer chemistry therefore shows up as "more energy per building" or "weaker robots", which fits an energy game. Then upgrade paths come from finding a better material. The ore-placement rules come from `docs/physics/notes/geology_resources.md`.

**What the planet data can tell us** **[NASA]**:

| Planet | Mass and radius | Bulk density | Host [Fe/H] | Game look today | What it means for resources |
| --- | --- | --- | --- | --- | --- |
| Viridia (hand-made Earth) | 1.00 M⊕, 1.00 R⊕ | 5,495 kg/m³ | solar (assumed) | liquid water, 62 % ocean | Baseline: every deposit type, under Earth-like assumptions |
| Proxima Cen b | **m sin i** 1.06 M⊕ (minimum mass); radius from a mass-radius relation, not measured | not measured | not in the archive; published values range from 0.08 ± 0.12 to sub-solar **[source]** | no water, −55 °C, tidally locked, M5.5 star | Composition unknown. Dry, so no water-made deposits. Flares |
| TRAPPIST-1 e | **measured** 0.69 M⊕ (±3 %), 0.92 R⊕ | 4,900 kg/m³ | +0.05 ± 0.07 | liquid water, 85 % ocean | All seven TRAPPIST-1 planets fit **~21 wt % iron vs Earth's 32 %** (Agol et al. 2021) **[source]**. Iron-poor, water-rich, little land |
| Kepler-452 b | 1.63 R⊕ (±0.23); mass 3.29 M⊕ from a mass-radius relation (**not measured**) | 4,170 (derived) | **+0.21 ± 0.09** | no water, −8 °C, high gravity 12.1 m/s² | Metal-rich star (10^0.21 ≈ 1.6× solar metals). Surface "uncertain" at the rocky limit |
| Kepler-16 b | 105.8 M⊕, 8.45 R⊕ | 964 | [M/H] −0.3 | not playable (gas giant, two stars) | No ground. **Future** only: atmospheric He-3/deuterium **[repo: energy_sources]** |

**Recommended materials.** Phase 3 first ships tiers A and B; tier C is **Future**.

| Tier | Material (unit kg) | Used for | Where it forms **[repo: geology_resources]** | Substitute or upgrade |
| --- | --- | --- | --- | --- |
| A | **Regolith / silicate rock** (Si, Al, O, Mg) | Glass, PV silicon, aluminium, sintered blocks | Every land tile | Base material, no substitute needed |
| A | **Iron (Fe)** | Chassis, structures, LFP cathodes, ferrite magnets | Everywhere. BIF in old ocean basins only with an ocean history; impact melt sheets on dry worlds | Scale by planet iron fraction × 10^[Fe/H] (proposed rule, Claim 10) |
| A | **Water / ice (H₂O)** | Processing, electrolysis, coolant; later D-D fuel | Liquid ocean tiles, Ice, FrozenSea, Tundra ground ice | None. Dry planets must find ice (cold traps), **[design]** gameplay hook |
| B | **Copper (Cu)** | Motors, electronics, compact wiring | Porphyry and epithermal deposits need water **and** plate tectonics; VMS needs an ocean and volcanism | **Aluminium** (from regolith): 1.6× the resistivity of Cu but 30 % of its density, so better per kg for long lines and worse for compact motors **[repo: Claim 7]** |
| B | **Lithium (Li)** | Robot and grid batteries (LFP) | Brines in **dry closed basins** (the existing `DryBasin` terrain) with a volcanic source and a water history; clays; pegmatites in evolved crust | **Na-ion**: Na from salt or seawater, ~160 Wh/kg vs ~250 Wh/kg, so robots get less range per kg **[repo: energy_sources]** |
| B | **Germanium (Ge)** | PV cells tuned to red-dwarf light | By-product of Zn (VMS) ore **[background, needs a source]** | Under a 2,600 K star, Ge reaches a 21.7 % limit vs Si 12.7 %; under a Sun-like star, Si wins **[repo: stellar_exoplanet table]**. This is the red-dwarf PV upgrade |
| C | Nickel/PGE, rare earths (Nd), silver (Ag), He-3 | Electrolysers, NdFeB magnets, PV contacts, fusion | Ni-Cu-PGE in impact melt sheets; He-3 needs an airless body | Ferrite or wound-rotor magnets; Cu-plated contacts; **Future** |

**How each playable planet plays out.** This follows the rules above; nothing is tuned yet.

- **Viridia:** has everything. It is the tutorial planet, with Cu from arcs, Li from dry basins, and plenty of iron and water.
- **TRAPPIST-1 e:** water everywhere, but little land: 85 % ocean in the game look, and Agol's data allow a water-rich interior.
  - **Iron:** scarce, at ~0.66× Earth from 21/32.
  - **Copper:** only from VMS on the sea floor, which needs equipment that is future work. Use aluminium for wiring.
  - **Dry basins:** few, so lithium is rare and Na-ion from seawater is the normal battery.
  - **Solar:** the Ge upgrade matters most here, under the coolest star.
- **Proxima Cen b:** dry and cold, around a flaring star.
  - **Missing deposits:** no water deposits of any kind, so no porphyry/VMS copper and no lithium brines.
  - **Substitutes:** aluminium wiring, and Na-ion made from rock, which costs a lot of energy.
  - **Ice:** water only from ice, if cold traps on the night side are allowed. That is a **[design]** choice: the game look says "no water".
  - **Iron:** the iron fraction is unknown because the radius is not measured. Default it to Earth-like and show the player "unknown, measure it". That fits the "measuring matters" loop.
  - **Solar:** the Ge upgrade applies here too.
- **Kepler-452 b:** rich in metals, at 1.6× from [Fe/H] +0.21, but dry and high-gravity.
  - **Metals:** iron, nickel and impact ores are plentiful.
  - **Copper and lithium:** scarce, because there is no water.
  - **Gravity:** high gravity makes movement cost more energy (D9).
  - **Solar:** under a Sun-like star, Si panels are already best, so Ge is not an upgrade here.

**Prospecting.** Deposits should start hidden and be found by measuring, as in the sky notebook (#48). This is a natural first job for Research robots. The notes give the realistic steps **[repo: Claim 8]**:
1. An orbital gamma/neutron scan gives a coarse map of H (ice), Fe, K and Th.
2. A drone reflectance scan gives mineral classes through absorption bands (olivine ~1.05 µm, pyroxene ~1/2 µm, hydrated minerals ~1.4/1.9/2.8–3.0 µm).
3. A rover LIBS shot gives element-level ppm, for example Li at 670.8 nm.

**Still yours to decide:**
- The quantities: kg per building and kg per robot. Possible sources for those are PV and battery LCA inventories.
- Whether build parts later turn into "build parts + materials" for all buildings, or for new buildings only. I recommend new buildings only, so existing saves and balance stay intact.

### D5. Battery ageing model

- **Recommendation.** Use NREL's reduced-order life model **[source]**:
  - Health Q = Q₀ · (b₀ − b₁·√t − b₂·N_eq), where t is time and N_eq is equivalent full cycles.
  - The calendar term b₁ grows with temperature (Arrhenius) and with high state of charge. The cycle term b₂ grows with depth of discharge.
  - NREL's material shows fade moving to a faster mechanism above ~80 % DoD. That covers the spec's "deep discharge" and "repeated 0 %" damage without an invented rule.
  - Calibrated LFP and NMC parameter sets are published in NREL BLAST-Lite (BSD-3, DOI-cited). Use the LFP set for robots.
- **Fallback if that is too heavy for the first version.** Use the lifetimes already in the repo: LFP/NMC lasts 10–20 years or 3,000–8,000 cycles **[repo: energy_sources table]**.
  - Health falls linearly to 80 % at the low end of the cycle range.
  - Calendar ageing is left out, and the doc says so.
- **Either way.** Add the equations and test values to `physics-verification.md` first, as `CLAUDE.md` requires, and add tests on both sides if the Python prototype models it.
- Health is separate from charge. Replacing the battery resets health, and other parts are kept (§8 of the spec).
- Robots on a cold planet age differently, through the Arrhenius term. That gives a per-planet difference for free.

### D6. Grid priority for robot charging

- **Today.** Power flows: generation → colony demand → grid battery → spare power, which becomes build parts → curtailed.
- **Recommended order:**
  1. Colony demand. This is unchanged, and brownouts already slow growth.
  2. **Robots below 20 %.** A robot that runs out strands itself, and §12 rescue is deferred, so it has no way back.
  3. Grid battery.
  4. **Robots topping up from 20 % to 80 %.**
  5. Build parts.
  6. Curtailed.
- **Rationale.** The workforce never charges by starving the colony. A stranded robot is worse than slower construction. Topping up uses spare power the way build parts do today.
- **Where the code changes.** The change stays inside `PowerGrid` as one extra load, with no second network.

### D7. Energy quotas

- **Recommendation.** **Quotas are the existing K milestones**: every +0.1 of planet K, which is ten times the power, judged on the day-average used power.
- They already exist, are saved, have an event (`MilestoneReached`), and gate panel tiers.
- Quotas unlock workforce technology in the same way as `PanelTiers`.
- **No deadlines or penalties.** The spec defines none, so adding them would be inventing mechanics.
- Show the next quota in the Workforce Overview: "Next quota: X W for a day (milestone n)".

### D8. Technology and research

- **Recommendation: both, with distinct roles.**
  - **Milestones (quotas) decide *what* can be built.** That covers robot types, Workshop tiers, group capacity steps and module upgrades.
  - **Research robots decide *where and how well*.** They do concrete measurements, as the sky notebook does (#48): measure the sun and latitude, prospect deposits (the D3 scan ladder), and measure the planet's iron fraction where the archive has none.
- **No abstract research points.** Each measurement unlocks one named thing. That keeps the "measuring matters" teaching loop and avoids inventing a points economy.
- **Group capacity** (D-CAP) steps up with milestones.

### Remaining decisions

| # | Recommendation | Basis |
| --- | --- | --- |
| D1 | **Phase 1 tasks:** Build a site, Salvage a building, Survey the sky (the notebook's measurements done by a robot). **Phase 2:** Charge, Repair a robot, Replace a battery. **Phase 3:** Prospect, Mine, Manufacture. Transport and rescue stay **Future**. | [design]; uses only systems that exist at each phase |
| D2 | Energy stays the only limit on how fast sites are built. A site only takes build parts while a robot capable of building is **working at it**: presence is a gate, not a rate, so no new rate number is needed. Saves with sites already in progress keep progressing, with the starting robot assigned automatically. | [design]; keeps the #38 balance |
| D4 | Use clearly marked `// TBD` placeholders, plus NASA rovers as **sourced anchors** where they exist: Perseverance (~1,025 kg, MMRTG ~110 W, two Li-ion batteries of ~43 Ah, top speed ~152 m/h). These need a citation check before use. No balance claims. | [source, to verify] |
| D9 | Robots live on **tiles** and move along `HexSphere.Neighbors`.<br>• **Ocean tiles:** not accessible; boats are Future.<br>• **Ice and FrozenSea:** need equipment.<br>• **Mountain:** slower.<br>• **Energy per distance:** scales with surface gravity (rolling resistance ∝ m·g). On Kepler-452 b (12.1 m/s²) that is ~1.24× Earth; on TRAPPIST-1 e (8.0 m/s²), ~0.82×.<br>The formula goes into `physics-verification.md` first. | [NASA] gravity; [source needed] for the rolling-resistance coefficient |
| D10 | **The lander is home and the first charger**: its RTGs already feed the grid, and it holds a small number of charging slots. | [design] |
| D11 | Plan for **tens early and hundreds late**: simulate each robot individually up to a few hundred, and show counts per group beyond that. | [design] |
| D12 | Robots charge at the **lander**, the **battery yard** (named in the spec), and a new **charging station** building. Panels don't charge robots directly. | [design] |
| D13 | Use only hazards the data supports:<br>• **Cold:** air temperature, which also feeds the battery Arrhenius term.<br>• **Stellar flares on M-dwarf planets:** Proxima b has ≥ 5 superflares a year; TRAPPIST-1 has about one flare every ~1.9 days at K2 sensitivity **[repo: stellar_exoplanet]**.<br>No dust, radiation or vacuum: there are no data fields for them, and every planet assumes 1 atm. | [repo], [NASA] |
| D14 | One **condition** value per robot plus a separate **battery health**. Modules (chassis, drive, battery, processor, sensors, tool) are **upgrade slots**, not separately worn parts. A repair restores condition and keeps the processor and upgrades. | [design]; simplest model that meets spec §7–8 |
| D15 | **Build, salvage and mining keep their progress**, because the progress lives on the site. A measurement in progress restarts. Charging just stops. | [design] |
| D16 | Demand program order:<br>1. Brownout risk (the grid battery running down at night).<br>2. Robots below 20 %.<br>3. Damaged robots.<br>4. Sites waiting. | [design] |
| D-AO | Automatic organisation **by robot kind**: one group per kind. A new robot joins its kind's group, or a new group if that one is full. Manual groups are never touched. | [design] |
| D-CAP | Starting capacity is a `// TBD` placeholder; it steps up at milestones (D8). | [design] |

Sources found for this plan (to check before they go into `physics-verification.md`):
- Agol et al. 2021, PSJ 2:1, TRAPPIST-1 masses, radii and densities ([arXiv:2010.01074](https://arxiv.org/abs/2010.01074v2); [NASA NTRS](https://ntrs.nasa.gov/citations/20210000129)).
- Proxima Cen metallicity estimates ([arXiv:1608.06919](https://arxiv.org/pdf/1608.06919); [Wikipedia, unsourced 0.21](https://en.wikipedia.org/wiki/Proxima_Centauri)).
- NREL reduced-order battery life model ([NREL 66708](https://www.nrel.gov/docs/fy16osti/66708.pdf); [NREL 68759](https://www.nrel.gov:443/docs/fy17osti/68759.pdf); [BLAST degradation engine notes](https://breos.readthedocs.io/en/stable/architecture/blast-degradation-engine.html)).
- Xu et al. 2018, "Modeling of Lithium-Ion Battery Degradation for Cell Life Assessment", IEEE Trans. Smart Grid 9(2):1131 ([dblp](https://dblp.org/rec/journals/tsg/XuOUAK18.html)), an alternative model with explicit DoD and SoC stress factors.
- NASA Exoplanet Archive `pscomppars`: [Fe/H], mass provenance (Msini, Mass, M-R relationship) and density.

## 8. Tests planned (EditMode)

- Workforce: starts with one General robot at the base; group create/rename/delete; membership; capacity limit.
- Overrides: individual program/priority overrides survive group program or priority changes; clearing an override falls back to the group.
- Automation: idle robots take the highest-priority task they can do; Disabled tasks are never taken; ties broken by ranked order.
- Interruption: only Critical tasks interrupt automatically; manual interrupt works on any; after a temporary task the robot resumes and re-evaluates.
- States: Automatic → Interrupted → Automatic; Paused robots take no tasks; settings unchanged by pausing.
- Saves: version 7 round-trip; a version 6 save loads with one General robot.
- Phase 2+: 20 %/80 % charging hysteresis, charging power and slot limits, grid load, battery health vs charge, condition shutdown, repair vs replace.

## 9. Implementation status (2026-11-08)

The PRs are stacked; merge them in order. Code lives in `Simulation/Robots/` (namespace `Robots`, not `Workforce/` as section 3 proposed, so the `Workforce` class doesn't clash with its namespace) and `Simulation/Resources/`.

| PR | Phase | What it adds | Save version |
| --- | --- | --- | --- |
| #50 | 1 | Robots, groups (automatic by kind, manual), capacity, overrides, priority automation, Critical-only automatic interruption, pause/resume/interrupt, robot-gated construction (D2), salvage orders, Robots panel (G) | 7 |
| #51 | 2 | Robot battery (2,408 Wh, Perseverance), cycle ageing, 20 %/80 % charging, charging on the grid (dock at the lander, D6/D10), battery yards as chargers, wear, maintenance and battery replacement at the lander, flare wear, "keep working" above 20 % | 8 |
| #52 | 3a + 3b | Planet chemistry ([Fe/H], iron fraction), deposits per tile, survey and mining tasks; Robot Workshop, manufacturing, substitutes (aluminium, Na-ion), modules, unlocks by milestone, group capacity steps | 9, 10 |
| #53 | 4 | Facility, Demand and Task queue programs, ranked ordering, survey restart rule (D15), full Robots panel controls | 11 |
| #54 | §10 | Hybrid movement: routes, terrain pace, drive power ∝ g, range checks, charging trips, stranded robots, manual send | 12 |

**Placeholders.** Every value marked `TBD` in `RobotSpecs`, `RobotTech`, `DepositMap`, `RouteFinder` and `ChargingCapability` has no balance claim (D4). That covers power use, wear, repair, slots, recipes, unlocks, deposit shares, hop times and drive power.

**Sourced values:**
- the battery size;
- the cycle-life fallback;
- the Na-ion capacity ratio;
- [Fe/H] and the TRAPPIST-1 iron fraction;
- the P ∝ g rule.

These are recorded in `docs/physics/physics-verification.md` or in the notes.

**Not built, as the spec says:**
- transportation (spec §11): Transport robots can't be made;
- rescue (spec §12): stranded robots wait.

**Changed from the plan:**
- **Sky surveys stay player actions.** The notebook's measurements depend on the moment you take them, so they are not robot tasks. D1 listed them; the ground survey task was added instead.
- **Cold and calendar ageing are not modelled.** The cycle-only battery model has no temperature term. NREL's model is the next step (D5).

