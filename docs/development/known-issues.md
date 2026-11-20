# Known issues and limitations

Problems that are known and not yet fixed. Add an entry when you find something you will not fix in the current change; remove it in the pull request that fixes it.

Bugs that need tracking by several people also get a GitHub issue; link it here.

**First filled from the audit of 2026-11-12** (`main` at `abe383c`).

## 1. Structure

| # | Issue | Effect | Plan |
| --- | --- | --- | --- |
| KI-1 | `PlanetSimulation` still owns every system's bookkeeping (split into 14 files, one class) | New features still touch it | Architecture M1, second half |
| KI-2 | ~~Class called "stub"~~ | Fixed 2026-11-13: renamed `PlanetSimulation` | — |
| KI-3 | `IPlanetSimulation` hands out live `Workforce`, `DepositMap`, `MaterialStock`, `PowerHistory` and `SkyNotebook` objects | Presentation can change state without a command | Architecture M5 |
| KI-4 | ~~Namespace cycles~~ | Fixed 2026-11-13; `tools/check_layers.py` keeps it so | — |
| KI-5 | Unlocks are defined in three places | A new unlock can be added in one and missed in another | Data D5 |
| KI-6 | Robot numbers are split between `RobotSpecs` and `RobotCatalog` | Two places to look | Data D6 |
| KI-7 | Each building type still has its own set and counter in the orchestrator (names, footprints and costs now come from `BuildingCatalog`) | A new building still repeats some bookkeeping | Data D2 |

## 2. Saves and data

| # | Issue | Effect | Plan |
| --- | --- | --- | --- |
| KI-8 | Enums are saved as integers | Reordering an enum corrupts old saves without an error | Rule TD-16 now; string ids in data step D4 |
| KI-9 | Two files are named `planets.json` | The generated one can be edited by mistake and overwritten | Rename the input when the tool is next changed |
| KI-10 | One save file, one planet | Loading replaces the whole game | With the civilization layer (M9) |
| KI-11 | The Unity MCP package follows its `main` branch | A build can change without a change in this repository | Pin a version before a release |

## 3. Science and balance

| # | Issue | Effect | Plan |
| --- | --- | --- | --- |
| KI-12 | Fixed 2026-11-15: deposits hold grade × a size per material and run out as they are mined (sea water from a shore never does). The sizes are game rules (TBD) | Sizes are placeholders | Balance with OQ-12 |
| KI-13 | Manufacturing recipes (robots, upgrades) still use materials without a mass balance. Refining is fixed: leftover rock becomes slag | Mass is not conserved in manufacturing | Roadmap phase 2 (RS-09) |
| KI-14 | Robot, recipe, deposit and movement numbers are placeholders (`TBD` on 60 lines in Simulation) | Balance is arbitrary | Open question OQ-12 |
| KI-15 | Battery ageing uses an unsourced depth exponent and 0 % damage | A model presented with numbers that have no source | Source them in `physics-verification.md` |
| KI-16 | Air temperature of real planets is the equilibrium temperature, with no greenhouse term | Cold bias on planets with air | PL-09 |
| KI-17 | The waste-heat model is not connected to play | A verified model the player never meets | Roadmap phase 3 |
| KI-18 | The grid has no distance and no losses | Where a panel stands does not matter to the grid | EN-10 |
| KI-19 | Sources the physics report could not reach when written | Some values rest on summaries | Its last section lists each |
| KI-20 | Terrain stands in for geology | Ore rules are game rules shaped like geology | Stated in `DepositMap.cs`; revisit with the environment model |

## 4. Information and interface

| # | Issue | Effect | Plan |
| --- | --- | --- | --- |
| KI-21 | Panels show actual simulation state | Conflicts with the principle that the player learns by observation | Open question OQ-1; information steps I2–I5 |
| KI-22 | Keys are hard-coded across views | No rebinding; overlaps are found by hand (`A` pans the camera and Ctrl+A selects every slot) | When input is decided (technical decisions §3) |
| KI-23 | UI text is English strings in code and UXML | No localisation | When a second language is wanted |

## 5. Tests and tooling

| # | Issue | Effect | Plan |
| --- | --- | --- | --- |
| KI-24 | ~~No continuous integration~~ | Fixed 2026-11-13: `.github/workflows/tests.yml` (not yet seen to run on GitHub) | — |
| KI-25 | No performance test; mass balance is only partly tested | Regressions of these kinds are invisible | Testing standards §8 |
| KI-35 | New terrain changes how existing planets look (not their saves): a saved building on a tile that is wetland now still loads, but new land buildings cannot go there | Surprises after an update | Terrain is not saved; revisit if saves must keep their old maps |
| KI-34 | Stepping 300 robots takes about 5 ms per 0.25 h step (measured 2026-11-13 outside Unity) | Fine at the slower clock speeds; at "1 d/s" (96 steps a second) about half a second of work per real second, and "1 y/min" would not keep up | Aggregate simulation of background work (workforce stage H) |
| KI-26 | No Presentation tests | UI changes are checked by hand | Testing standards §6 and §8 |
| KI-27 | Not every save version has its own migration test | An old save could fail to load unnoticed | Add when touching save code |

## 6. Documentation

| # | Issue | Effect | Plan |
| --- | --- | --- | --- |
| KI-28 | `docs/simulation-interface.md` stops at the colony overview | Robots, resources, manufacturing, footprints and save versions 4–16 are undocumented there | Roadmap phase 0 |
| KI-29 | `docs/design/robotic-workforce-v2-audit.md` still says that `main` is at `274f999` and that nothing in the workforce stack is merged; PRs #50–#61 are merged | Misleading status | The feature registry holds the current status; correct the text when the document is next edited |
| KI-30 | The original design document is not in the repository | Its claims cannot be checked | Open question OQ-13 |
| KI-31 | Documents are partly Thai and partly English | Readers need both | Open question OQ-6 |

## 7. Work outside `main`

| # | Issue | Effect | Plan |
| --- | --- | --- | --- |
| KI-32 | `feature/building-footprints` is 9 commits ahead of `main` with no open pull request | Solar measurements, system windows and the colony network are unmerged and will drift | Roadmap phase 0 |
| KI-33 | Uncommitted industrial-production work in the main checkout | Could be lost; has no specification | Open question OQ-10 |
| KI-37 | ~~Copper and lithium are often beyond a robot's one-charge range from the landing tile, and nothing told the player to build a forward charger | A new colony cannot get copper and stalls (balance run 2026-11-20) | Fixed 2026-11-20: the tile panel says "Out of range", a first-steps hint teaches the forward charger, and the director kept the radius of about 3 tiles. See `balance-findings.md` |
| KI-36 | ~~"Skip: place the rest for me" in the landing setup places one small panel (about 28 kW at noon) against a 100 kW base demand | The 2 MWh lander battery runs out in about a day, the colony browns out, the robot network goes offline and robots stop until the sun returns. Looks like a robot stall; it is not (found 2026-11-19) | Fixed 2026-11-19: Skip now adds panels until the average output covers the base demand (30 % margin) |
| KI-38 | Storage buildings (yard, sealed store, gas tank) exist in the simulation but the build bar cannot place them yet. Meanwhile, with logistics on, a tile outside the lander cannot keep lithium or sodium (no sealed store), and mining those there stops with a logged "no room" report | Remote lithium cannot be mined until the Presentation phase (P4) adds the buttons | Mission operations P4 |
| KI-39 | Flight rules (P3) have no editor yet (P4), and three simplifications: colony values (grid battery, depot stock, measured surface temperature) are unknown to a cut-off robot instead of last-known with an age; "stellar flare active" reads whether the star is a flaring red dwarf, not a flare in progress; plants read the colony state while the grid is up | A cut-off robot can only use its own readings; rules about flares fire all the time on red dwarfs | Mission operations P4 / P5 |
