# Feature registry

The single record of what has been decided and what has been built. Design status and implementation status are separate columns because they are separate facts.

**Last full check against the code:** 2026-11-12, `main` at `abe383c`.

## 1. How to use this registry

- Before starting work, find the feature here. If it is missing, add it as PROPOSED and ask the director.
- When a pull request changes a feature's implementation status, it updates the row.
- Only the game director moves a feature to CONFIRMED or REJECTED. Record the date and where the decision was made.
- An AI developer may add PROPOSED rows and open questions at any time.

### Design status

| Status | Meaning |
| --- | --- |
| **CONFIRMED** | The director approved this design explicitly: in a specification marked approved, in a recorded decision, or by merging the pull request that built it |
| **PROPOSED** | Recommended by a developer or an AI; not approved |
| **REJECTED** | Deliberately excluded by the director |
| **FUTURE** | Accepted as a direction; not in scope now and not designed |

### Implementation status

| Status | Meaning |
| --- | --- |
| **Not started** | No code and no specification |
| **Designed** | A specification exists in `docs/design/` |
| **In progress** | Code exists on a branch or in a working copy, not on `main` |
| **Implemented** | On `main` and wired into the game |
| **Tested** | Implemented, with automated tests on `main` that pass |
| **Verified** | Tested, and checked against something outside the code: an external scientific reference for a model, or the director's play test for gameplay, with the check recorded |

"Verified" below is used only where the repository records the check. Play tests by the director are not recorded in the repository, so gameplay features stop at "Tested".

### Kinds of decision

| Kind | Recorded in |
| --- | --- |
| Human-approved design decision | This registry (CONFIRMED rows, section 3) and the specification |
| AI recommendation | This registry (PROPOSED rows) or a specification section marked as recommendation |
| Technical implementation decision | [`technical-decisions.md`](technical-decisions.md) |
| Scientific assumption | `docs/physics/physics-verification.md`; placeholders marked `TBD` in code |
| Open question | Section 5 |

## 2. Features

Basis abbreviations: **V** = the director's vision statement of 2026-11-12; **PR #n** = merged by the director; **WF** = workforce decisions approved 2026-11-08 (`docs/design/robotic-workforce.md`).

### 2.1 Planet and environment

| ID | Feature | Design | Basis | Implementation | Where |
| --- | --- | --- | --- | --- | --- |
| PL-01 | Hex-tiled planet, tile picking | CONFIRMED | PR #3, #19 | Tested | `Planet/HexSphere.cs` |
| PL-02 | Terrain per planet from seed and flags | CONFIRMED | PR #45 | Tested | `Planet/PlanetSurface.cs` |
| PL-03 | Real planets from the NASA Exoplanet Archive with provenance labels | CONFIRMED | PR #16, #43 | Tested | `Planet/PlanetRecord.cs`, `tools/exoplanets` |
| PL-04 | Choose the planet and the landing tile | CONFIRMED | PR #43, #55 | Tested (simulation side) | `SimulationRunner`, `PlanetChooserView` |
| PL-05 | Building slots per tile; multi-slot footprints | CONFIRMED | PR #28, #33, #61 | Tested | `Planet/TileSite.cs`, `Game/BuildingFootprints.cs` |
| PL-06 | Earth cloud climatology on Earth-like planets | CONFIRMED | PR #23 | Tested | `Climate/CloudClimate.cs` |
| PL-07 | Atmosphere, climate, temperature and pressure simulation | FUTURE | V | Not started | — |
| PL-08 | Subsurface exploration | FUTURE | V | Not started | — |
| PL-09 | Greenhouse offset for air temperature of real planets | PROPOSED | physics report P1 item 14 | Not started | — |

### 2.2 Energy

| ID | Feature | Design | Basis | Implementation | Where |
| --- | --- | --- | --- | --- | --- |
| EN-01 | Solar panel model: orbit to yearly energy | CONFIRMED | PR #2, #21, #23 | Verified (pvlib, NASA POWER) | `Solar/*`, `docs/physics/physics-verification.md` |
| EN-02 | Spectral match by star temperature; binary stars; tidally locked planets | CONFIRMED | PR #23 | Tested | `Solar/SpectralMatch.cs`, `BinaryStar.cs` |
| EN-03 | Panel placement with angles, best-angle search and bonus | CONFIRMED | PR #27, #32 | Tested | `Game/PanelAngleSearch.cs`, `PanelPlacer` |
| EN-04 | Light-potential map | CONFIRMED | PR #26 | Tested | `Solar/LightPotential.cs` |
| EN-05 | Power grid: one bus, battery, RTGs, brownouts | CONFIRMED | PR #29 | Tested | `Energy/PowerGrid.cs` |
| EN-06 | Battery yards as buildings | CONFIRMED | PR #36 | Tested | orchestrator |
| EN-07 | Solar tiers unlocked by milestones | CONFIRMED | PR #46 | Tested | `Game/PanelTiers.cs` |
| EN-08 | Re-angling a built panel is done by a robot | CONFIRMED | chat 2026-11-09, per `solar-measurements.md` on its branch | In progress | `feature/building-footprints` |
| EN-09 | Waste heat warming the planet | FUTURE | V; model from PR #23 | Tested as a library; not in play | `Climate/WasteHeat.cs` |
| EN-10 | Power distribution and transmission losses | FUTURE | V | Not started | — |
| EN-11 | Nuclear, geothermal, tidal, wave, ocean thermal, floating solar, advanced sources | FUTURE | V | Not started; several have checked numbers in the physics report | — |
| EN-12 | Thermal management and waste-heat recovery | FUTURE | V | Not started | — |
| EN-13 | Planetary energy engineering, stellar collection, megastructures | FUTURE | V | Not started | — |

### 2.3 Colony, construction and progression

| ID | Feature | Design | Basis | Implementation | Where |
| --- | --- | --- | --- | --- | --- |
| CO-01 | Game clock, speeds, day and night, seasons | CONFIRMED | PR #25 | Tested | `Game/SimClock.cs` |
| CO-02 | Colony demand grows with reliable power | CONFIRMED | PR #36 | Tested | `Energy/ColonyDemand.cs` |
| CO-03 | Build parts in Wh from spare power; sites; salvage | CONFIRMED | PR #38 | Tested | `Game/Construction.cs` |
| CO-04 | Sites progress only while a robot works on them | CONFIRMED | WF D2 | Tested | orchestrator, `Workforce` |
| CO-05 | Absolute K beside planet K | CONFIRMED | PR #42 | Tested | `Civilization/Kardashev.cs` |
| CO-06 | Milestones every 0.1 of planet K on a day's average | CONFIRMED | PR #44 | Tested | `Kardashev.cs`, orchestrator |
| CO-07 | Milestones as the unlock mechanism ("energy quotas") | CONFIRMED | WF D7, D8 | Tested | `PanelTiers`, `RobotTech` |
| CO-08 | Save and load, versioned | CONFIRMED | PR #37 | Tested | `Game/SimulationState.cs` |
| CO-09 | Technology development beyond milestones | FUTURE | V | Not started | — |
| CO-10 | Several colonies, several planets, interplanetary transport | FUTURE | V | Not started | — |
| CO-11 | Orbital engineering and megastructures | FUTURE | V | Not started | — |

### 2.4 Robotic workforce

| ID | Feature | Design | Basis | Implementation | Where |
| --- | --- | --- | --- | --- | --- |
| RB-01 | Robots, groups, capacity, overrides, priority automation | CONFIRMED | WF; PR #50 | Tested | `Robots/Workforce.cs` |
| RB-02 | Robot battery, charging, wear, repair | CONFIRMED | WF; PR #51 | Tested; placeholder numbers | `Robots/RobotBattery.cs`, `RobotUpkeep.cs` |
| RB-03 | Four operating programs, ranked ordering | CONFIRMED | WF; PR #53 | Tested | `Robots/Workforce.cs` |
| RB-04 | Movement over tiles, range, stranded robots | CONFIRMED | WF D9; PR #54 | Tested | `Robots/RouteFinder.cs` |
| RB-05 | Robot catalogue: attributes, compatibility, shutdown kinds | CONFIRMED | spec v2; PR #56 | Tested | `Robots/RobotCatalog.cs` |
| RB-06 | Task records, scheduler scoring, reservations | CONFIRMED | spec v2; PR #57 | Tested | `Robots/TaskBook.cs` |
| RB-07 | Facility staffing with diminishing returns | CONFIRMED for the Workshop; other buildings open (OQ-7) | PR #58 | Tested | `Robots/FacilityStaffing.cs` |
| RB-08 | Battery ageing by depth, cold capacity, task wear, charging station, maintenance rules | CONFIRMED | PR #59 | Tested; two unsourced placeholders | `Robots/RobotBattery.cs` |
| RB-09 | Colony network: hub, relays, range, brownout, cut-off robots | CONFIRMED | Q1 decided 2026-11-08 | In progress | `feature/building-footprints` |
| RB-10 | AI levels, memory, synchronisation, backups, colony computing | CONFIRMED as scope; details open | spec v2 stage E | Designed | `robotic-workforce-v2-audit.md` |
| RB-11 | Component tiers and assembly (manufacturing v2) | CONFIRMED as scope | spec v2 stage F | Designed | same |
| RB-12 | Robot inspection view, task monitor, group presets | CONFIRMED as scope | spec v2 stage G | Designed | same |
| RB-13 | Aggregate simulation and performance budget | CONFIRMED as scope | spec v2 stage H | Designed | same |
| RB-14 | Transport robots and logistics | FUTURE | workforce spec §11; OQ-8 | Not started | — |
| RB-15 | Robot rescue | FUTURE | workforce spec §12 | Not started | — |
| RB-16 | Robot destruction | Open (OQ-9) | v2 audit Q4 | Not started | — |

### 2.5 Resources and industry

| ID | Feature | Design | Basis | Implementation | Where |
| --- | --- | --- | --- | --- | --- |
| RS-01 | Materials in kg; planet chemistry from star metallicity | CONFIRMED | WF D3; PR #52 | Tested | `Resources/Material.cs` |
| RS-02 | Deposits by terrain; survey reveals them | CONFIRMED | PR #52 | Tested | `Resources/DepositMap.cs` |
| RS-03 | Copper deposits near seas and in high ground | CONFIRMED | PR #60 | Tested | `DepositMap.cs` |
| RS-04 | Mining into one colony stock | CONFIRMED | PR #52 | Tested | orchestrator |
| RS-05 | Robot Workshop: robots, upgrades, substitute materials. Refining conserves mass: leftover rock is slag (PROPOSED addition, not yet approved) | CONFIRMED | PR #52 | Tested; placeholder recipes | `Robots/Manufacturing.cs` |
| RS-06 | Industrial production foundation: phases, storage classes, production blockers | Unknown: no decision recorded in the repository | — | In progress (uncommitted, seen locally 2026-11-12) | `Resources/IndustrialProduction.cs` in the main checkout |
| RS-07 | Deposit depletion; purity | FUTURE | V | Not started | — |
| RS-08 | Storage and logistics | FUTURE | V | Not started | — |
| RS-09 | Chemical processing, production chains, byproducts, waste, recycling | FUTURE | V | Not started | — |
| RS-10 | Environmental effects of industry | FUTURE | V | Not started | — |
| RS-11 | Water infrastructure (all topics) | FUTURE | V | Not started | — |

### 2.6 Research, information and AI

| ID | Feature | Design | Basis | Implementation | Where |
| --- | --- | --- | --- | --- | --- |
| IN-01 | Sky notebook: measure to unlock advice | CONFIRMED | PR #48 | Tested | `Solar/SkyNotebook.cs` |
| IN-02 | Solar parameters as estimates that research robots measure through reasoning puzzles | CONFIRMED | chat 2026-11-09, per `solar-measurements.md` on its branch | In progress | `feature/building-footprints` |
| IN-03 | Information arrives as observations; nothing is known unobserved | CONFIRMED as a principle | V | Partly (surveys, notebook); panels read actual state | [`../architecture/information-architecture.md`](../architecture/information-architecture.md) |
| IN-04 | Observation record with source, time, uncertainty | PROPOSED | information architecture §4 | Not started | — |
| IN-05 | Reports, colony knowledge database, historical records | FUTURE | V | Power history only | `Game/PowerHistory.cs` |
| IN-06 | Unknown substances, samples, experiments, laboratories, experimental failure | FUTURE | V | Not started | — |
| IN-07 | Scientist consultation | FUTURE | V | Not started | — |
| IN-08 | Local AI scientific assistant; optional online references | FUTURE | V | Not started | — |
| IN-09 | Events: anomalies, disasters, rare discoveries | FUTURE | V | Flare wear on robots only | `RobotUpkeep.cs` |

### 2.7 Interface

| ID | Feature | Design | Basis | Implementation | Where |
| --- | --- | --- | --- | --- | --- |
| UI-01 | HUD: power, battery, K, time, build parts | CONFIRMED when merged; now in question (OQ-1) | PR #31, #38, #42 | Implemented | `UI/HudView.cs` |
| UI-02 | Tile panel, side panels, colony overview, robots panel, sky view, planet chooser, hints | CONFIRMED when merged; now in question (OQ-1) | PR #32, #34, #39, #46, #48, #50–#53 | Implemented | `UI/*` |
| UI-03 | God mode (F10) | CONFIRMED | PR #61 | Implemented | `Planet/GodMode.cs` |
| UI-04 | One window per game system with a tab bar | No decision recorded in the repository | — | In progress | `feature/building-footprints` |
| UI-05 | Minimal permanent interface | CONFIRMED as a principle | V | Not started | — |
| UI-06 | Windows build from a menu | CONFIRMED | PR #44 | Implemented | `Editor/BuildWindows.cs` |

## 3. Decisions approved by the director

| Date | Decision | Recorded in |
| --- | --- | --- |
| 2026-11-08 | Workforce decisions D1–D16, D-AO, D-CAP as recommended, with two changes made during building (sky surveys stay player actions; cold and calendar ageing deferred) | `docs/design/robotic-workforce.md` sections 7 and 9 |
| 2026-11-08 | Colony network Q1: relay range plus hub power; the lander is the hub; a brownout takes the network down | `docs/design/robotic-workforce-v2-audit.md` section 6 |
| 2026-11-09 | Solar measurements: robots turn built panels; research robots measure every solar parameter; measurement is a reasoning puzzle | `docs/design/solar-measurements.md` (on its branch) |
| 2026-11-12 | Game vision and the ten design pillars | [`../game/game-vision.md`](../game/game-vision.md) |
| 2026-11-12 | The player learns through observations and reports; minimal permanent interface | vision; [`../architecture/information-architecture.md`](../architecture/information-architecture.md) |
| 2026-11-12 | Four design statuses and six implementation statuses; the director has final authority | this registry |

## 4. Rejected

No design has been recorded as rejected by the director.

Claims that the physics verification found wrong and that the game must not make are recorded in `docs/physics/physics-verification.md`. Examples: "Type I means all of a planet's sunlight", the linear waste-heat shortcut, antimatter as an energy source instead of a store.

## 5. Open questions

Each needs the director. Until answered, the listed default applies and work that depends on the answer says so.

| ID | Question | Why it matters | Default until answered |
| --- | --- | --- | --- |
| OQ-1 | How much permanent interface stays? Which of the HUD and panels become reports and inspections? | Pillar 8 against the existing HUD and panels | Keep existing panels; new information follows the information architecture |
| OQ-2 | One colony per planet? When do several planets run at once, and does time pass on planets the player is not watching? | Decides whether a layer above the planet simulation is needed (architecture step M9) | One planet, one colony |
| OQ-3 | Do build parts in Wh stay the construction currency once materials and industry exist? | Two currencies for building; affects every recipe | Both: parts for energy cost, materials on top, as today |
| OQ-4 | What may the player see that the colony has not observed? Is god mode a debug tool or a feature? | Pillar 8 | God mode is a debug and teaching tool |
| OQ-5 | Do milestones remain the unlock mechanism once research exists? | One unlock table or two systems | Milestones only |
| OQ-6 | Which language are the documents in? Existing ones mix Thai and English | Consistency; AI and team readability | New architecture documents in English; existing Thai documents stay (TD-14) |
| OQ-7 | Which buildings need staff (workforce Q2)? | Blocks staffing for panels, battery yards and mine sites | Only the Workshop |
| OQ-8 | Transport robots before logistics exists (workforce Q3)? | A robot kind with no task | Locked |
| OQ-9 | What destroys a robot (workforce Q4)? | Needed for "unsynchronised data is lost" | Robots cannot be destroyed |
| OQ-10 | Is the uncommitted industrial-production work approved, and what is its specification? | It starts the industry system with no recorded design | Not on `main`; treat as unapproved |
| OQ-11 | Which second energy source comes first? | Order of the energy roadmap | None scheduled |
| OQ-12 | Balance values for every `TBD` placeholder: the director supplies them, or approves a balancing pass? | Placeholders shape play today | Placeholders stay, marked |
| OQ-13 | Is the original design document to be added to the repository? | The physics report reviews a document that is not here | Not available |
| OQ-14 | Does the game still target the NASA Space Apps submission, a longer commercial path, or both? | Scope and priority of the roadmap | Long-term development, per the 2026-11-12 brief |
