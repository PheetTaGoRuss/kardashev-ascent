# Feature registry

The single record of what has been decided and what has been built. Design status and implementation status are separate columns because they are separate facts.

**Last full check against the code:** 2026-11-12, `main` at `abe383c`.

## 1. How to use this registry

- Before starting work, find the feature here. If it is missing, add it as PROPOSED and ask the director.
- When a pull request changes a feature's implementation status, it updates the row.
- Only the game director moves a feature to CONFIRMED or REJECTED. Record the date and where the decision was made.
- A contributor may add PROPOSED rows and open questions at any time.

### Design status

| Status | Meaning |
| --- | --- |
| **CONFIRMED** | The director approved this design explicitly: in a specification marked approved, in a recorded decision, or by merging the pull request that built it |
| **PROPOSED** | Recommended by a contributor; not approved |
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
| Contributor recommendation | This registry (PROPOSED rows) or a specification section marked as recommendation |
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
| PL-03a | Six more planets: TRAPPIST-1 f, TOI-700 d, LHS 1140 b, Kepler-186 f, Kepler-442 b from the NASA Exoplanet Archive, and Mars from the NASA NSSDC fact sheet | CONFIRMED (director 2026-11-14: add more NASA data) | chat 2026-11-14 | Tested; seen in Play mode | `tools/exoplanets/planets.json`, `Data/Planets/*.asset` |
| PL-15 | Every new colony lands on a new random map of its planet (continents and deposits); the seed is saved (version 28); older saves keep the planet's own map | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Tested; seen in Play mode | `Presentation/Game/SimulationRunner.cs` (`NewRandomMap`, `UseMap`), `GameFlow.cs` |
| PL-04 | Choose the planet and the landing tile | CONFIRMED | PR #43, #55 | Tested (simulation side) | `SimulationRunner`, `PlanetChooserView` |
| PL-04a | Landing is a required first step: after choosing the planet, time stays stopped and no building site opens until the player picks a land tile and presses Land here; the colony can't move afterwards (replaces "move before the first building") | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Implemented | `Presentation/Game/GameFlow.cs`, `UI/TileInfoView.cs` |
| PL-04b | Landing needs flat, open ground: lowland, desert, dry basin, beach, forest or tundra; not sea, wetland, mountain, highland, volcanic rock or ice (saved colonies elsewhere still load) | CONFIRMED (director 2026-11-14); which ground counts PROPOSED | chat 2026-11-14 | Tested; seen in Play mode | `Planet/PlanetSurface.cs` (`Terrains.LandingProblem`) |
| PL-05 | Building slots per tile; multi-slot footprints | CONFIRMED | PR #28, #33, #61 | Tested | `Planet/TileSite.cs`, `Game/BuildingFootprints.cs` |
| PL-06 | Earth cloud climatology on Earth-like planets | CONFIRMED | PR #23 | Tested | `Climate/CloudClimate.cs` |
| PL-06a | Earth air temperature (NASA POWER T2M, 2001–2025) by latitude and month on Earth-like planets: panel cell temperature, robot battery cold, the Air heat value | CONFIRMED (director 2026-11-14: add more NASA data) | chat 2026-11-14 | Tested | `Climate/CloudClimate.cs` (`EarthAirTemperature`), `tools/nasa_power/fetch_air_temperature.py`, `docs/nasa-data.md` |
| PL-06b | Earth ground reflectance (NASA POWER ALLSKY_SRF_ALB) as a seasonal change on each terrain, and aerosol haze (AOD_55 at 550 nm → whole spectrum by Molineaux et al. 1998 and Ångström α 1.3) as a change around the planet's τ, by latitude and month on Earth-like planets | CONFIRMED (director 2026-11-14: add both) | chat 2026-11-14 | Tested | `Climate/CloudClimate.cs` (`EarthGroundAndHaze`), `tools/nasa_power/fetch_ground_and_haze.py` |
| PL-10 | New terrain: forest, wetland, beach, volcanic rock (second terrain pass on warm wet planets; volcanic rock anywhere). Wetland is too soft for land buildings; forest and beach keep the landing rank of lowland; forest, wetland and volcanic rock slow robots; albedo and names per terrain | CONFIRMED (director's choice of all four, 2026-11-13) | chat 2026-11-13 | Tested; seen in Play mode | `Planet/PlanetSurface.cs` |
| PL-11 | Relief from orbit: land tiles raised with elevation (1.5 % of the radius at the top, ~10x true scale) with cliff walls; sea at sea level | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Seen in Play mode | `Presentation/Planet/PlanetMeshBuilder.cs`, `PlanetView.cs` |
| PL-12 | Rivers (springs on high land running to the sea; more water, slower crossing) | REJECTED for now (director 2026-11-14: looked bugged and weird; removed). Could return as FUTURE | chat 2026-11-14 | Removed | — |
| PL-13 | Level of detail: peaks and forest canopy from orbit; trees, reeds and rocks close up on the open site | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Seen in Play mode | `Presentation/Planet/TerrainFeatures.cs`, `SiteDecor.cs` |
| PL-14 | Buildings clear nature on their slots (visual only, no regrowth); from orbit a tile's canopy thins with the share built on | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Seen in Play mode | `SiteDecor.cs`, `TerrainFeatures.cs` |
| EN-14 | Floating solar on shallow sea and wetland: 1.5 times the cost of a land panel (game rule), 4 % more output from cooler cells (game rule, TBD), built and taken down by a robot with a boat hull (director 2026-11-14; before, raft crews); turned without a robot | CONFIRMED (director's choice, 2026-11-13; boats 2026-11-14) | chat 2026-11-13, 2026-11-14 | Tested | `Game/PlanetSimulation.Buildings.cs`, `RobotsAtWork.cs` |
| PL-07 | Atmosphere, climate, temperature and pressure simulation | FUTURE | V | Not started | — |
| PL-08 | Subsurface exploration | FUTURE | V | Not started | — |
| PL-09 | Greenhouse offset for air temperature of real planets | PROPOSED | physics report P1 item 14 | Not started | — |

### 2.2 Energy

| ID | Feature | Design | Basis | Implementation | Where |
| --- | --- | --- | --- | --- | --- |
| EN-01 | Solar panel model: orbit to yearly energy | CONFIRMED | PR #2, #21, #23 | Verified (pvlib, NASA POWER) | `Solar/*`, `docs/physics/physics-verification.md` |
| EN-02 | Spectral match by star temperature; binary stars; tidally locked planets | CONFIRMED | PR #23 | Tested | `Solar/SpectralMatch.cs`, `BinaryStar.cs` |
| EN-03 | Panel placement with angles, best-angle search and bonus | CONFIRMED | PR #27, #32 | Tested | `Game/PanelAngleSearch.cs`, `PanelPlacer` |
| EN-03a | A new panel starts flat (β = 0) or copies the angles of a panel already on its tile; the best angle is never set for you (and nothing gives the latitude away before it is measured) | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Seen in Play mode | `Presentation/Planet/PanelPlacer.cs` |
| EN-04 | Light-potential map | CONFIRMED | PR #26 | Tested | `Solar/LightPotential.cs` |
| EN-05 | Power grid: one bus, battery, RTGs, brownouts | CONFIRMED | PR #29 | Tested | `Energy/PowerGrid.cs` |
| EN-06 | Battery yards as buildings | CONFIRMED | PR #36 | Tested | orchestrator |
| EN-07 | Solar tiers unlocked by milestones | CONFIRMED | PR #46 | Tested | `Game/PanelTiers.cs` |
| EN-08 | Re-angling a built panel is done by a robot | CONFIRMED | chat 2026-11-09, per `solar-measurements.md` on its branch | In progress | `feature/building-footprints` |
| EN-08b | Edit angle: drag rings around a panel to turn its tilt and facing; saved angle layouts with the player's name and note, the slot and the tile's latitude and longitude | CONFIRMED (director's request, 2026-11-13) | chat 2026-11-13 | Tested; seen working in Play mode | `Presentation/UI/AngleEditor.cs`, `Game/AnglePreset.cs` |
| EN-09 | Waste heat warming the planet | FUTURE | V; model from PR #23 | Tested as a library; not in play | `Climate/WasteHeat.cs` |
| EN-10 | Power distribution and transmission losses | FUTURE | V | Not started | — |
| EN-17 | Tidal barrage: three-slot dam on a bay with 4 m or more of tide, ebb generation, basin survey experiment, milestone 1 gate, coast effects (tide drops 25 %, boats cannot cross) (director chose to build it with effects, 2026-11-19) | PROPOSED | - | Implemented, seen in Play mode | `Game/PlanetSimulation.Barrage.cs`, `Planet/Tides.cs`, `Robots/RouteFinder.cs`, `TidalBarrageTests`. One-way generation only; every number TBD |
| EN-18 | Wired grid: power lines carry power from the lander tile along chains of tiles; generators off the grid make nothing, stations and storage can only be built on the grid; new missions are wired, old saves and tests are not (save version 35) (director 2026-11-20) | PROPOSED | - | Implemented, seen in Play mode | `Game/PlanetSimulation.Wiring.cs`, `WiredGridTests`, `SimulationRunner.cs`. Numbers TBD (line cost 15 Cu, 30 Fe, 20 rock) |
| EN-16 | Wave-energy converter line on shallow sea: Pierson-Moskowitz sea state from latitude wind, deep-water wave energy flux, weather swing, rating and storm shutdown, wave buoy experiment (director 2026-11-19) | PROPOSED | - | Implemented, seen in Play mode | `Planet/Waves.cs`, `Game/PlanetSimulation.Waves.cs`, `WavesTests`, `WaveEnergyTests`. Wind is a latitude rule, TBD; barrage not started |
| EN-15 | Free-flow tidal turbine on shallow sea: generated moon, tide range per coast tile, tide cycle, turbine power into the grid (director chose free-flow first and a generated moon, 2026-11-19) | PROPOSED | - | Implemented (UI not yet seen on a world with sea) | `Planet/Tides.cs`, `Game/PlanetSimulation.Tidal.cs`, `TidesTests`, `TidalEnergyTests`. Tide gauge experiment (needs Gravity; gates tidal turbines; tile panel shows the tide only after it). Wave energy and barrage: not started |
| EN-11 | Nuclear, geothermal, tidal, wave, ocean thermal, floating solar, advanced sources | FUTURE | V | Not started; several have checked numbers in the physics report | — |
| EN-12 | Thermal management and waste-heat recovery | FUTURE | V | Not started | — |
| EN-13 | Planetary energy engineering, stellar collection, megastructures | FUTURE | V | Not started | — |

### 2.3 Colony, construction and progression

| ID | Feature | Design | Basis | Implementation | Where |
| --- | --- | --- | --- | --- | --- |
| CO-01 | Game clock, speeds, day and night, seasons | CONFIRMED | PR #25 | Tested | `Game/SimClock.cs` |
| CO-01a | Fastest speed is 1 day per second: no year per minute (button and key 5 removed) | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Implemented | `UI/HudView.cs`, `SimulationRunner.cs` |
| CO-02 | Colony demand grows with reliable power | CONFIRMED | PR #36 | Tested | `Energy/ColonyDemand.cs` |
| CO-03 | Build parts in Wh from spare power; sites; salvage | REJECTED (director 2026-11-14: buildings use real materials, not power; replaced by CO-03a) | PR #38 | Removed (old saves convert) | — |
| CO-03a | Buildings take a bill of real materials: the real mass of the installation (c-Si module composition; LFP cathode) times 1 % on site (game rule); stations have placeholder bills (TBD). A robot takes them from stock while it works on the site; spare power makes nothing; Workshop jobs draw their energy from the grid; repairs take iron and battery-cell materials. The lander brings a cargo for the first buildings (game rule, TBD). Save version 26 | CONFIRMED (director 2026-11-14); amounts PROPOSED | chat 2026-11-14 | Tested; seen in Play mode | `Resources/BuildCosts.cs`, `Game/Construction.cs` |
| CO-04 | Sites progress only while a robot works on them | CONFIRMED | WF D2 | Tested | orchestrator, `Workforce` |
| CO-04b | Construction takes robot work: 2 h plus 4 h per square root of a tonne of materials (TBD), at the crew's rate, never ahead of the materials delivered; a site is finished when both are in. Sites rise from the ground as they are built, pulse while a robot works, and pop when finished; robots hop while building. Save version 27 | CONFIRMED (director 2026-11-14); hours PROPOSED | chat 2026-11-14 | Tested; seen in Play mode | `Game/Construction.cs`, `Presentation/Planet/PanelPlacer.cs` |
| CO-04c | A site the stock can't supply yet shows red; once the materials are in stock it turns yellow (waiting to be built) | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Seen in Play mode | `Presentation/Planet/PanelPlacer.cs` |
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
| RB-09 | Colony network: hub, relays, range, brownout, cut-off robots | CONFIRMED | Q1 decided 2026-11-08 | Tested | `Robots/ColonyNetwork.cs`, `Game/PlanetSimulation.ColonyNetwork.cs` |
| RB-10 | AI levels (processor upgrades), nearby exchange, findings wait for sync, onboard AI power, colony computing | CONFIRMED (details 2026-11-14) | spec v2 stage E | Tested | `Robots/Workforce.cs`, `Game/PlanetSimulation.Findings.cs` |
| RB-10a | Individual memory beyond findings, colony database, backups and loss of unsynced data | CONFIRMED as scope | spec v2 stage E | Not started (needs OQ-9) | — |
| RB-10b | Failure ladder (alternatives, ask a robot for help, notify the player) and player-set computing priorities | CONFIRMED as scope | spec v2 §12 | Not started | — |
| RB-11 | Component tiers and assembly (manufacturing v2) | CONFIRMED as scope | spec v2 stage F | Designed | same |
| RB-12 | Robot inspection view, task monitor, group presets | CONFIRMED as scope | spec v2 stage G | Designed | same |
| RB-13 | Aggregate simulation and performance budget | CONFIRMED as scope | spec v2 stage H | Designed | same |
| RB-14 | Transport robots and logistics | FUTURE | workforce spec §11; OQ-8 | Not started | — |
| RB-15 | Robot rescue | FUTURE | workforce spec §12 | Not started | — |
| RB-17 | The lander brings three General robots (was one) | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Tested; seen in Play mode | `Robots/Workforce.cs` |
| RB-18 | Boat hull module: the robot crosses open water (shallow sea 1.5, deep ocean 2 flat tiles per hop, TBD) and builds floating solar; available from the start | CONFIRMED (director 2026-11-14); numbers PROPOSED | chat 2026-11-14 | Tested; ordered and fitted in Play mode | `Robots/RouteFinder.cs`, `Manufacturing.cs` |
| RB-19 | Long trips: an idle robot whose only reachable work needs more than the usual 80 % charge (round trip plus 20 % kept) charges up to what the trip needs, up to 100 %, before setting off; it stays at the charger meanwhile, and that charging is served right after the colony like charging below 20 % (changes D6 for this case) | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Tested; seen in Play mode | `Robots/Workforce.cs` (`PlanTripCharges`), `RobotUpkeep.cs` |
| CO-04a | A construction site is no work while the stock has none of a material it still needs: no robot drives out and back empty-handed; it becomes work again when the material arrives | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Tested; seen in Play mode | `Game/PlanetSimulation.RobotsAtWork.cs` |
| RB-20 | A robot that plugs in charges at priority (right after the colony, before the grid battery) up to 80 % or what its trip needs; it leaves early only in a brownout. A charging robot you send somewhere goes if it has the charge to get there and back keeping 20 %. Changes D6 (top-up after the grid battery only above 80 %) | CONFIRMED (director 2026-11-14: robots kept charging and did nothing) | chat 2026-11-14 | Tested; seen in Play mode | `Robots/RobotUpkeep.cs`, `Workforce.SendTo` |
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
| RS-12 | Calculation puzzles are occasional: a survey or measurement comes back as a puzzle 25 % of the time (TBD; a stable roll from tile, kind and hour); otherwise the robot works the result out exactly | CONFIRMED (director 2026-11-14); 25 % PROPOSED | chat 2026-11-14 | Tested | `Game/PlanetSimulation.Findings.cs` (`RollPuzzle`) |
| RS-13 | Research always ends in its own unique puzzle | FUTURE (director 2026-11-14) | chat 2026-11-14 | Not started | — |
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
| IN-10 | Research Station: every measurement and experiment is done there by a robot, and takes time; a latitude is measured where a station stands (the sky view still works anywhere) | CONFIRMED (director 2026-11-14) | `docs/design/research-station.md` | Implemented; tested outside Unity | `Game/PlanetSimulation.Research.cs` |
| IN-11 | Experiments: air pressure, water boiling point (Antoine), surface temperature (coldest/warmest month), gravity (pendulum); hours TBD | CONFIRMED (director 2026-11-14); hours PROPOSED | `docs/design/research-station.md` | Implemented; tested outside Unity | `Planet/Experiments.cs` |
| IN-12 | Knowledge gates buildings (floating solar needs to know water stays liquid) and sets safety margins until known (solar: 1 − spread of unknowns, floor 50 %; batteries charge to 80 % until the surface temperature is known) | Direction CONFIRMED (director 2026-11-14); margin numbers PROPOSED | `docs/design/research-station.md` | Implemented; tested outside Unity | `Game/PlanetSimulation.Research.cs` |
| IN-13 | Before landing: a rough brief from orbit (mean temperature, robot survival verdict, gravity, air pressure, water) with error bars (TBD) | CONFIRMED (director 2026-11-14); error bars PROPOSED | `docs/design/research-station.md` | Implemented | `UI/PlanetChooserView.cs`, `Planet/Experiments.cs` |
| IN-07 | Scientist consultation | FUTURE | V | Not started | — |
| IN-08 | Local AI scientific assistant; optional online references | FUTURE | V | Not started | — |
| IN-09 | Events: anomalies, disasters, rare discoveries | FUTURE | V | Flare wear on robots only | `RobotUpkeep.cs` |

### 2.7 Interface

| ID | Feature | Design | Basis | Implementation | Where |
| --- | --- | --- | --- | --- | --- |
| UI-01 | HUD: power, battery, K, time, build parts | CONFIRMED when merged; now in question (OQ-1) | PR #31, #38, #42 | Implemented | `UI/HudView.cs` |
| UI-02 | Tile panel, side panels, colony overview, robots panel, sky view, planet chooser, hints | CONFIRMED when merged; now in question (OQ-1) | PR #32, #34, #39, #46, #48, #50–#53 | Implemented | `UI/*` |
| UI-05 | Robots window cards (kind icon, state dot, battery/health/condition gauges, COMMAND zone with icon buttons, UPGRADE zone with module squares, folded history); Research window shows the experiment tree first with each experiment on its own row; materials HUD shows every material held; Settings and menu open before landing (director 2026-11-20) | PROPOSED | - | Implemented, seen in Play mode | `UI/WorkforceView.cs`, `UI/ResearchTreeView.cs`, `UI/HudView.cs`, `Game/GameFlow.cs` |
| UI-03 | God mode (inside the debug panel, F10) | CONFIRMED | PR #61 | Implemented | `Planet/GodMode.cs` |
| UI-04 | Debug mode (F10 panel: free/instant building, unlimited materials, unlock all, full battery, shortcuts; not saved, "DEBUG" badge) | PROPOSED (director asked for it 2026-11-18) | - | Implemented | `Game/DebugOptions.cs`, `Planet/DebugPanel.cs`, `DebugModeTests` |
| UI-04 | One window per game system with a tab bar | No decision recorded in the repository | — | In progress | `feature/building-footprints` |
| UI-05 | Minimal permanent interface | CONFIRMED as a principle | V | Not started | — |
| UI-06 | Windows build from a menu | CONFIRMED | PR #44 | Implemented | `Editor/BuildWindows.cs` |
| UI-08 | Game menu: main menu at launch (New game, Continue saved game, Quit) and a pause menu on Esc (Resume, Save, Load, Main menu, Quit); time is stopped on both | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Implemented | `Presentation/Game/GameFlow.cs` |
| UI-09 | Setup tutorial right after landing: time stays stopped while it asks for a solar panel, battery yard, charging station and Robot Workshop on the landing tile; Skip places the rest automatically; time starts when all four are placed | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Seen in Play mode | `Presentation/Game/GameFlow.cs`, `PanelPlacer.AutoPlace` |
| UI-10 | Research window: symbol badges per value, a bar for how unsure each is, one-word status and one button per row; a four-step picture tutorial the first time (and with ?); puzzle cards with a badge and one-line intros | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Seen in Play mode | `Presentation/UI/SystemWindows.cs` (`ResearchWindow`, `ParameterLook`), `LightPuzzleView.cs` |
| UI-11 | Sky view measuring as puzzles from real instruments: δ from tilt and orbit position (sin δ = sin ε · sin λ); the sun from a 1 m stick's shadow (h = arctan(stick / shadow), A_s = shadow bearing ± 180°); latitude from the noon sun (φ = 90° − h + δ) or the celestial pole with a quadrant (φ = 90° − reading); answers within 1° (sun direction 2°); a five-step first-time tutorial (? reopens) | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Tested; seen in Play mode | `Simulation/Solar/SkyNotebook.cs`, `Presentation/UI/SkyPanelView.cs` |
| UI-11a | A tile's latitude and longitude stay hidden (tile panel, sun path, map hover, saved layouts) until the colony measures a latitude; then the map gives every tile's | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Implemented; checked in Play mode | `UI/TileInfoView.cs`, `Planet/TilePicker.cs`, `UI/AngleEditor.cs` |
| UI-07 | Build bar in categories (Power, Stations) with an (i) description per building | CONFIRMED (director 2026-11-14) | chat 2026-11-14 | Seen in Play mode | `Presentation/UI/BuildInfo.cs`, `PanelsView.cs` |

## 3. Decisions approved by the director

| Date | Decision | Recorded in |
| --- | --- | --- |
| 2026-11-08 | Workforce decisions D1–D16, D-AO, D-CAP as recommended, with two changes made during building (sky surveys stay player actions; cold and calendar ageing deferred) | `docs/design/robotic-workforce.md` sections 7 and 9 |
| 2026-11-08 | Colony network Q1: relay range plus hub power; the lander is the hub; a brownout takes the network down | `docs/design/robotic-workforce-v2-audit.md` section 6 |
| 2026-11-14 | Stage E details: AI level from Processor (2) and Advanced processor (3) Workshop upgrades, unlocked by milestones until research exists; a cut-off robot's findings wait until it syncs (directly or through a robot nearby), backups and loss wait for OQ-9; colony computing is a grid load per AI level above 1, cut first in a shortage, robots then work at level 1 | `docs/design/robotic-workforce-v2-audit.md` section 6 |
| 2026-11-14 | Terrain and build bar: buildings clear trees and rocks on their slots, visual only; exaggerated relief from orbit; build bar in Power and Stations with (i) descriptions | PL-11 to PL-14, UI-07 |
| 2026-11-14 | Game flow: main menu plus pause menu; the player must pick the first landing tile before time starts, with the game paused while picking | PL-04a, UI-08 |
| 2026-11-14 | After landing a tutorial asks for the essential buildings or places them on Skip; 3 starter robots; boat upgrade for work on water; fastest speed 1 day/s; buildings use real materials, not power (answers OQ-3) | UI-09, RB-17, RB-18, CO-01a, CO-03a |
| 2026-11-14 | Long-trip charging and no work for sites without materials approved; calculation puzzles occasional (research will always have its own puzzle); construction takes time, with a building animation | RB-19, CO-04a, RS-12, RS-13, CO-04b |
| 2026-11-14 | No landing on sea, wetland or unbuildable ground; new panels flat or copying the tile; friendlier measuring with symbols, bars and a tutorial | PL-04b, EN-03a, UI-10 |
| 2026-11-14 | More NASA data: Earth air temperature from NASA POWER; every NASA source listed in docs/nasa-data.md | PL-06a |
| 2026-11-14 | More NASA data: ground reflectance and aerosol haze from NASA POWER | PL-06b |
| 2026-11-14 | More NASA data: five more archive planets and Mars from the NASA fact sheet; a random map for every new game | PL-03a, PL-15 |
| 2026-11-14 | Sky view measuring is a set of real-instrument puzzles (stick shadow, quadrant), with a tutorial, not click-to-know | UI-11 |
| 2026-11-14 | Rivers removed after a look in Play mode ("bugged and weird"); RiverMap, its rules and its drawing deleted | PL-12 |
| 2026-11-14 | Puzzle cards for beginners: bigger text and windows (Research 640 px, sky view 480 px), numbered "how to solve it" steps and a worked example with other numbers on every puzzle | UI-11 |
| 2026-11-14 | In-game science calculator (degrees; sin/cos/tan and inverses, ln, log, sqrt, ^): click a puzzle's numbers to put them in (also copied to the clipboard), "Put in answer" fills the answer box | UI-11 |
| 2026-11-14 | New UI look for every screen, inspired by factory games such as Mindustry (not copied): square near-opaque grey panels with an amber strip, grey block buttons lit amber on hover, amber selected states; calculator opens any time (K / HUD "Calc"), clicking a number copies it | UI (all) |
| 2026-11-14 | Inspect and info panels (tile info, sky, site/planner/placed boxes, building info card) drag by a grip like the calculator (double-click: put back); bigger text in them | UI (all) |
| 2026-11-14 | Research Station: measuring and experiments happen there; knowledge gates buildings and sets safety margins; a rough brief from orbit before landing | IN-10 to IN-13 |
| 2026-11-14 | Sound: effects and a looping space ambience synthesised in code (no files to license); button clicks, building, puzzles right/wrong, discoveries, brownout alarm, milestones; volume sliders and mute in the pause menu | UI (all) |
| 2026-11-15 | Sun path as a round sky chart (no broken lines where the sun passes overhead); buildings as one module per slot (fit, no overlap); volcanic rock as low lava mounds (sites no longer walled in); survey puzzles answered one material at a time (save v30); puzzle cards fold for later; tile alert when cores wait; calculator history and ans | UI-11, CO-03 |
| 2026-11-15 | Guide book (J): where every material is found, what each ground is good for, building costs, robots, research and controls, built from the game's own rules (tested against the deposit generator) | IN-05 |
| 2026-11-15 | Guide pictures: every material and ground drawn in code (ore chunk, water drop, hex tile with its features) in the list and on the page | IN-05 |
| 2026-11-15 | Epic menu (director): the real 3D planet rendered by a menu camera with its own sun behind the title, moon passing in front and behind; borderless text buttons that glow, slide, grow an amber bar and tick on hover; a cinematic intro (fade from black with a whoosh, the title slams in with a synthesised boom, flash, shock ring and shake, then the buttons slide in; a click skips it); whoosh and fade into New mission; team GIE in the credits and footer | UI-08 |
| 2026-11-15 | Space main menu (director): drawn space backdrop (nebula, twinkling stars, sun, planet with night-side lights, moon, shooting stars), big title, New mission / Continue / Guide book / Settings / Credits / Quit; Settings (sound, fullscreen, V-Sync, graphics quality, controls); Credits (NASA data, science, sound, tools; not affiliated with NASA); the guide book opens any time (J on the menus, planet chooser and before landing) | UI-08 |
| 2026-11-15 | Mission-control UI theme (director: epic, not default UI): navy glass panels with a cool frame and amber top strip, glowing tab underlines, themed toggles, sliders, scroll bars and drop-down lists (the pop-up list was white on blue); Settings and Credits slide in from the right over the live menu instead of replacing it | UI-08 |
| 2026-11-15 | Planet chooser remade as a star map (director): full-screen map centred on the Sun, each star at its real galactic direction (archive ra/dec to galactic longitude) and distance (sy_dist, log scale, rings at 1-1 000 pc), Kepler's field, a schematic Milky Way inset; star list nearest first; target panel with the system, worlds as tabs, a turning 3D preview made by the same surface generator and lit in the star's colour (locked worlds face their star), six key numbers and the full mission data; galaxy sky (Milky Way band, nebulae, stars) behind the planet in play; planets.json gains star raDeg, decDeg, distancePc | UI-02 |
| 2026-11-15 | Build times back to before the real-world change (director): 2 h + 4 h per root tonne of materials (a 1 ha field about 6 robot hours); crews of up to 6 kept | Director request; supersedes the day-to-week times |
| 2026-11-15 | Robots recharge only at a charger (charging station or battery yard); the lander repairs only; a charger on the landing tile is fed by the lander RTG first; with no charger anywhere robots work until empty; setup asks for the charger first. Building about 4x faster (1 ha field 240 robot hours, workshop 300, charger 12) | Director request; changes D10 (lander no longer charges) |
| 2026-11-15 | Real refining (director request): refinery modules (Alumina plant, Electrolysis cell) in slots touching a Workshop, one batch each on grid power, paused in a brownout; alumina from anorthosite (~30 % Al2O3) or rock (~14 %), Hall-Heroult aluminium (1.93 kg alumina, 13.5 kWh per kg, 7 days a batch), 99.99 % aluminium, sodium from salt (Downs cell) or slowly from rock; queue and auto (keep stock); new materials anorthosite, salt, alumina, 99.99 % aluminium; save v34 | Design status PROPOSED; batch sizes and hours game rules |
| 2026-11-15 | Build times as in real life (director): a 1 ha solar field about 1 000 robot hours, a workshop 1 200, a mast 48; crews of up to 6 robots per site; task hand-out scores computed once per round (faster) | Hours TBD |
| 2026-11-15 | Manufacturing window remade (tabs, recipe cards with material icons, i, auto refine); hover cards for anything with a description and for buildings on a site; canyons drawn deep with walls down to their floor | UI |
| 2026-11-15 | Barriers (director request): canyons carved as rifts; mountains and canyons block driving, building and mining until opened (tunnel: 300 kg iron, 150 h; blast: 1.2 t iron + 30 kg copper, 12 h; bridge: 1.5 t iron + 0.5 t rock, 60 h; a known cave halves a tunnel); the cut rock comes out; markers on the planet; save v33 (older saves keep used mountains open) | Design status PROPOSED; amounts game rules TBD |
| 2026-11-15 | Rare earths (director request): deep ore in canyons (rifts), mountains and volcanic rock, more with a cave; deep ground holds 3x the ore; hidden until a new Seismic survey experiment (needs gravity); used for the Faster drive magnets (NdFeB) | Design status PROPOSED; shares TBD |
| 2026-11-15 | Experiments need the Research Station tile surveyed; Research shows them as a tree (survey, then air pressure > boiling point, surface temperature, gravity > seismic survey) | Director request |
| 2026-11-15 | Place panel no longer blinks on ground a building cannot use (the smaller lander cargo tried that day was reverted: director) | Bug fix |
| 2026-11-15 | Deposits run out (director request, KI-12): each deposit holds its grade × a size per material (rock 1 000 t, iron and ice 50 t, copper 3 t, lithium 0.5 t at grade 1; game rule TBD); mining takes from it and stops when it is empty, with a report; sea water from a shore never runs out; the tile panel shows what is left; save v32 | RS-07 (sizes TBD) |
| 2026-11-15 | Start on any planet (director request): aluminium refined from rock stands in for copper in building bills (0.494 kg per kg, same resistance), sodium-ion cells for lithium (3.31 kg per kg, same charge); refining both is open from the start; a waiting site takes whichever metal is in stock; the choice is saved per building (save v31) and salvage gives back what it was made of; guide page "No copper or lithium?" | Design status PROPOSED (director asked; balance numbers TBD) |
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
| OQ-1 | How much permanent interface stays? Which of the HUD and panels become reports and inspections? | Pillar 8 against the existing HUD and panels | **Decided 2026-11-20 (director: "as you want / recommend until I want to add or change"):** keep the existing panels; new information follows the information architecture. The director can change it any time |
| OQ-2 | One colony per planet? When do several planets run at once, and does time pass on planets the player is not watching? | Decides whether a layer above the planet simulation is needed (architecture step M9) | One planet, one colony |
| OQ-3 | ~~Do build parts in Wh stay the construction currency once materials and industry exist?~~ **Answered by the director, 2026-11-18: no.** Construction already consumes materials, taken by the robots that build; build parts are not to be the lasting currency | Two currencies for building; affects every recipe | Build parts stay in the code until a design replaces them; no new feature may depend on them |
| OQ-4 | What may the player see that the colony has not observed? Is god mode a debug tool or a feature? | Pillar 8 | God mode is a debug and teaching tool |
| OQ-5 | Do milestones remain the unlock mechanism once research exists? | One unlock table or two systems | Milestones only |
| OQ-6 | Which language are the documents in? Existing ones mix Thai and English | Consistency; AI and team readability | New architecture documents in English; existing Thai documents stay (TD-14) |
| OQ-7 | Which buildings need staff (workforce Q2)? | Blocks staffing for panels, battery yards and mine sites | Only the Workshop |
| OQ-8 | Transport robots before logistics exists (workforce Q3)? | A robot kind with no task | Locked |
| OQ-9 | What destroys a robot (workforce Q4)? | Needed for "unsynchronised data is lost" | Robots cannot be destroyed |
| OQ-10 | Is the uncommitted industrial-production work approved, and what is its specification? | It starts the industry system with no recorded design | Not on `main`; treat as unapproved |
| OQ-11 | ~~Which second energy source comes first?~~ **Answered by the director, 2026-11-18: tidal and wave power** (water energy; fits floating solar and the sea). Registered as EN-11 direction, not yet designed | Order of the energy roadmap | Design tidal and wave power next; needs the environment model for tides first (roadmap phase 3) |
| OQ-12 | Balance values for every `TBD` placeholder: the director supplies them, or approves a balancing pass? | Placeholders shape play today | **Delegated 2026-11-20 (director: "you can balance yourself till find the best point for playing"):** the assistant runs balance passes and logs them in [balance-findings.md](balance-findings.md); values stay marked TBD until the director signs them off ([list of every placeholder](balance-placeholders.md)) |
| OQ-13 | Is the original design document to be added to the repository? | The physics report reviews a document that is not here | Not available |
| OQ-15 | The blue roll ring in the angle editor does nothing for power (a flat panel's output does not depend on twisting about its own normal). Remove it, or give it a use? | Clutter against orientation | Stays, drawn faint and labelled "no effect" |
| OQ-14 | Does the game still target the NASA Space Apps submission, a longer commercial path, or both? | Scope and priority of the roadmap | Long-term development, per the 2026-11-12 brief |
