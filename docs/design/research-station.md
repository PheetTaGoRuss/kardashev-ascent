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
