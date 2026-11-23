# Research Station and planet knowledge

Status: the direction is **CONFIRMED** by the game director (chat, 2026-11-14). Every number marked TBD below is a
**PROPOSED** game rule waiting for tuning in play.

## What the director asked for

> These things should be used in building as parameters, or as the knowledge of this planet. Add a research station
> which will be used to measure things and do experiments. You have to place it to measure things with a research
> robot, which takes time, because you have to make sure it is safe and doable on that planet, such as the boiling
> point of water or the surface temperature.

The director made these choices:

- **Knowledge use:** both. Facts gate some buildings, and buildings built with unknown values run with a safety margin.
- **Station role:** everything. All eight solar measurements and every experiment need a placed Research Station.
- **Experiments:** water boiling point, surface temperature, air pressure, gravity.
- **Before landing:** a short brief, such as the planet's average temperature and whether robots can survive there.

## How it works

1. **Before landing**, the planet chooser shows a "From orbit · before landing (rough)" section:
   - Mean temperature, ± 15 °C (TBD).
   - Whether robots can work there.
   - Gravity, ± 10 % (TBD).
   - Air pressure, ± 50 % (TBD).
   - Whether water would be liquid.
2. **After landing**, the setup tutorial asks for a Research Station as its fifth essential building (after the solar
   panel, battery yard, charging station and Robot Workshop).
3. **Measuring:** a robot (General, or a Research robot at twice the speed) goes to the station and measures there.
   - A latitude is measured only on a tile with a station.
   - The sky view (V) still finds the latitude anywhere, with the player's own instruments.
4. **Experiments** run at the station:

   | Experiment | Robot hours (TBD) | Needs first | Finds |
   | --- | --- | --- | --- |
   | Air pressure | 4 | — | The station tile's pressure in kPa (the planet's pressure, thinner on high ground) |
   | Water boiling point | 8 | Air pressure | Boiling point at that pressure (Antoine equation), or "can't be liquid" below the triple point |
   | Surface temperature | 24 | — | Coldest and warmest monthly mean at the station (the climate model); robot verdict |
   | Gravity | 4 | — | A 1 m pendulum's period and g |

5. **Knowledge gates buildings.** Floating solar needs both the boiling point and the surface temperature, and is refused
   where water boils away.

   Gates proposed for later buildings:
   - **Fission reactor:** water boiling point (coolant), surface temperature and air pressure.
   - **Steam and geothermal:** water boiling point.
   - **Tall structures:** gravity.

6. **Safety margins** while values are unknown (PROPOSED, TBD):
   - **Solar panels** deliver a share of their real output: 1 − the combined spread of the unknown solar values for a
     30° equator-facing panel at the base. This is the same "±" the Research window shows. The share never drops below
     50 %. Measuring a value shrinks the spread and lifts the output.
   - **Batteries** charge only to 80 % of capacity until the surface temperature is known. Energy already stored above
     that stays.

## Robots' working range (game rule built on datasheets, TBD)

| Part | Range | Effect in the game |
| --- | --- | --- |
| LFP cells, charging | 0 to 45 °C | Within it, robots work freely |
| LFP cells, discharging | −20 to 60 °C | — |
| Industrial-grade electronics | −40 to 85 °C | Within it but outside the charging range: robots need heating or cooling |
| Anything beyond | — | Robots would not survive outside |

## Code

- `Simulation/Planet/Experiments.cs`: the experiments, their physics and the orbital brief.
- `Simulation/Game/PlanetSimulation.Research.cs`: stations, orders, results, gates and margins.
- Save version 29: `KnownExperiments` and `ExperimentOrders`.
- Tests: `Tests/EditMode/Game/ResearchStationTests.cs`.

## Open questions

- Should a Research robot be required, rather than any General robot?
- Should experiments sometimes end in a puzzle, like measurements? RS-13 says research always ends in its own puzzle.
- Should the margins be shown on the HUD as well as in the Research window?

## Research tied to the physics (Decided 2026-11-23, director delegated)

Rule (OQ-4, OQ-5): research gives **knowledge, not buildings**. Until a quantity is measured the game shows an **estimate with a
range** ("about 5.2 kPa, plus or minus 50 %"), never the exact value, and buildings are designed for the cautious end of the range.
Measuring replaces the estimate with the exact value and the margin disappears. All ranges, hours and costs below are game rules, TBD.
God mode (F10) still shows true values; play shows only what the colony observed.

| # | Measurement | Instrument (flavour) | Robot hours | Needs first | Estimate it replaces | What it reveals and what changes |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Air pressure (exists; extended) | Barometer | 4 | none | Orbit brief: pressure plus or minus 50 % | Air density, so the corona regime (Peek, Paschen or vacuum) and the cable voltage derate. Until measured, cables are derated at the cautious (low) end of the pressure range, so a line is rated lower than it needs to be. |
| 2 | Surface temperature (exists; extended) | Ground and air thermometers over a day | 24 | none | Orbit brief: mean temperature plus or minus 15 C | The real coldest and warmest months, so the cable thermal rating and the robot night margin. Until measured, robot weather warnings use the cautious end (the colder or hotter bound of the estimate) and the warning says the temperature is an estimate. |
| 3 | Terrain relief survey (new) | Laser altimeter (the way MOLA mapped Mars) | 12 | Gravity | Assumed relief scale: Earth's relief scaled by 1/g, plus or minus 30 % (Earth twins and Mars: plus or minus 10 %) | The measured highest and lowest point, so the climb term. Until measured, the Research window shows the highest point as a range. (Also built, 2026-11-24: a robot keeps a climb reserve for getting back up to its nearest charger, scaled by the high end over the centre of the relief range until the survey is done.) |
| 4 | Tide gauge (exists; extended) | Tide gauge on the shore | 20 | Gravity | Tide range plus or minus 50 % (moon mass and orbit seen only from orbit) | The measured tide range on coasts, so the tidal output. Until measured, the Research window shows the open-sea tide as a range. (Turbines already need the tide gauge before they can be built, so no output share is needed.) |
| 5 | Ore assay (new) | Sample assay at a surveyed mine | 16 | none (a surveyed tile with ore) | Ore grade shown plus or minus 25 % | The measured grade of every surveyed ore tile, so mine output and the real cost of the cable bill. Until measured, grade is available as a range through `GradeEstimate` (the tile info shows "0.31 ± 0.08" until the assay, exact after). |

How it works in the code: a quantity has a `KnowledgeEstimate` (value, half range). Known: half range 0. The simulation always runs on the true
value; knowledge decides what is shown and which end of the range the safety margins use. Experiment orders, robot work, saving and the
station gate are the existing ones (a finished, powered Research Station on a surveyed tile; any General robot). New experiments append to
`ExperimentKind` (saved as integers).

**Puzzle (RS-13), decided 2026-11-23 (director delegated): "fit the data".** Finishing an experiment makes the result known with the full range; the experiment's puzzle then offers a noisy series, physically correct for the planet, that the player fits with one to three sliders (residuals drawn). No pass or fail: the fit sets how much of the range is left (the instrument's precision, 10 %, for a perfect fit up to 100 %). "Fit for me" gives the least-squares answer but leaves 30 %. Models: air pressure p = p0 exp(-h/H) with H = RT/(Mg); temperature mean + amplitude sine over a day; relief a spike rejection threshold; tide amplitude and phase with the moon's period; ore assay the number of samples (range t s / sqrt n, 0.5 h each, not yet charged to robot time); gravity T = 2 pi sqrt(L/g); boiling ln P = a - b/(T+233); seismic t = 2d/v; wave heave sine; basin parabola. Save version 51 keeps pending puzzles and range shares; older saves stay exact. Built (`FitPuzzle`, `FitPuzzleView`).

**Efficiency gains (OQ-5, "small, cited"):** not in this batch. Candidate: each measurement lifts one related system's efficiency by a
few percent with a cited reason (for example a measured soil temperature lets cables be rated closer to the limit). Needs a source for each.
