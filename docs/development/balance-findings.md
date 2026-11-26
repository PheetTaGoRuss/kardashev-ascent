# Balance findings

The game director delegated the balance pass (open question OQ-12, 2026-11-20): "you can balance yourself till find the best point for playing". This file keeps what was measured, what was changed and what is still open, so the next pass starts from facts. Values stay marked TBD in the code until the director signs them off.

## Method

A scripted player ("bot") runs the real simulation in a throwaway test (not committed): it lands as the tutorial does, orders mining, places panels and batteries, orders robots, and prints the colony every ten days. Three runs on the standard test planet (Earth-like, frequency 12):

1. **Everything free** (infinite materials, free building): isolates the demand and milestone rules from mining and labour.
2. **Real materials, three robots**: shows what limits a colony that does not grow its workforce.
3. **Real materials, robots ordered, nearest copper and lithium mined.**

## What was measured

| Finding | Evidence | Verdict |
| --- | --- | --- |
| The demand rule is fine. With free building and enough batteries the colony reaches milestone 1 (10 MW) on day 10 and milestone 2 (100 MW) on day 50; demand follows 85 % of the day's mean generation. | Run 1 | Keep `UsableShare` 0.85 and `GrowthDays` 2 |
| Batteries gate growth. With only the lander's 22 MWh the demand stays near 3 MW whatever the panels make, because the night browns the colony out and it cuts back. | Run 1, first attempt | Working as designed; the tutorial text already says to build batteries |
| **Copper was the wall.** The setup's charger, Workshop and Research Station use 75 kg of the lander's 100 kg of copper, leaving less than one robot (40 kg). The colony then stalls with sites that cannot be paid. | Runs 2 and 3 | **Changed: lander copper 100 → 250 kg** (`BuildCosts.LanderCargo`) |
| A barrage, wave line or turbine makes tens of MW at most, while each milestone is ten times the last. A barrage gated at milestone 3 (1 GW) would be pointless. | Milestone ladder (`Kardashev.PowerForMilestone`) | **Changed: barrage opens at milestone 1** (`Tides.BarrageMilestone`) |
| **Copper is out of reach.** Even with the larger cargo, the nearest copper in the run was 17 tiles away; a robot carries about 2.4 kWh and drives at 50 W, so the round trip does not fit one charge. The copper order was never worked in 100 days. | Run 3 | **Open, a design call below** |

## Open: copper range (needs a decision, or more play)

Robots can only work as far from a charger as one battery lasts (80 % out and back). Copper and lithium sit on specific tiles, often beyond that. The player is meant to build forward chargers, but nothing in the first-run guidance says so, and the bot (like a new player) never did. Options:

1. **Teach it**: a hint when a mining order is out of range ("build a charging station nearer, or a Relay"). Cheap, no balance change. Recommended first.
2. **Make copper commoner near the base** (raise `CopperShare`, or guarantee one copper tile within a few tiles of the landing site). Changes world generation, so saved worlds look different.
3. **Reduce the copper in robots** (40 kg each) so the lander cargo goes further.

## Done after the first pass: the range hint

The tile panel now says why an order can never be worked (`PlanetSimulation.WorkReachProblem`, shown under the mining and survey buttons): "Out of range: N h of driving from the nearest charger, and a robot can go M h out and back on one charge. Build a charging station nearer." Measured on Viridia (2446 land tiles) after landing with the standard essentials: **32 land tiles in range (1.3 %), 2080 out of range, 334 cut off by water or a barrier**. So with one charger a colony can reach a radius of about three tiles. That is a deliberate frontier, but it makes the forward charger the player's first real decision. **Decided 2026-11-20 (director): keep the radius of about three tiles.** A first-steps hint (step 5 of 6) introduces the forward charger and is done once a second charger stands. Not tuned: robot battery, drive power and hop time as such. Earlier open question, now answered: whether the radius is right (it follows from robot battery 2.4 kWh, drive power 50 W and the hop time per tile; none is tuned), and a tutorial step that introduces forward chargers.

## Pass 2 (2026-11-20): a smarter scripted player, and mining ×3

`Tests/EditMode/Game/BalanceBot.cs` (explicit; run it by name: `dotnet test tools/simtests/SimTests.csproj --filter "Name=Timeline"`) is the repeatable version of the bot: it orders robots before anything else, builds forward chargers toward copper and lithium, mines what the stock lacks, and adds panels and batteries as demand grows. Standard test planet, three robots to start, panels at the tier the milestones allow.

| Mining rate | Milestone 1 (10 MW) | Milestone 2 (100 MW) | What limited it |
| --- | --- | --- | --- |
| 50 kg/h per robot (old) | about day 65 to 70 | not within 280 days (about 40 MW demand) | **Rock**: panels need about a tonne per hectare and the stock sat at zero from day 90 on; robots could not even be ordered (200 kg of rock each) |
| **150 kg/h per robot (now)** | **about day 35 to 40** | **about day 155 to 160** | Iron by the end of the run |

Changed: `RobotSpecs.MiningKgPerHour` 50 → 150 (game rule, TBD). Reasoning: with the old rate a crew of twelve made about 14 t of rock a day against a tonne per panel hectare, so every milestone beyond the first was a wall of mining time, not play. The bot is a conservative player; a person should be faster.

Still open: demand lags generation (at day 280 the bot makes 295 MW at peak but uses 41 MW, because the demand rule follows 85 % of the mean over a day, and night and angles cut the mean to about a fifth of peak); each milestone costs ten times the last, so milestone 3 and later need the larger panel tiers and a bigger crew than the bot builds. Those are next.

## Pass 3 (2026-11-20): bot that moves its mines and caps its crew

Two bot fixes that showed up as game findings: the bot kept mining an exhausted base tile (deposits are finite, so a real colony must move its mines outward, which is intended), and it ordered robots without counting the ones still being made (it reached 62). With 16 robots, deposits re-targeted inside charger range, mining at 150 kg/h:

| Milestone | Day (bot) | Note |
| --- | --- | --- |
| 1 (10 MW) | about 55 | forward charger built by day 30 |
| 2 (100 MW) | about 145 | iron held at the bot's stock cap, so rock and iron are balanced |
| 3 (1 GW) | not reached by day 700 | the bot stops at about 130 MW: it only builds on the 12 tiles nearest the base that are inside charger range, so it runs out of room, not materials (rock piles up from day 270) |

Reading: materials no longer limit the first two milestones. Beyond that growth needs more land, which means more forward chargers, relays and a wider crew; that is the intended expansion loop, and the bot does not play it. Next balance work needs a bot that expands outward (a charger chain with panel sites on each new tile); until then milestone 3 and later cannot be judged.

## Pass 4 (2026-11-20): an expanding bot, tried and set aside

A bot variant that scans every in-range tile and builds a forward charger whenever it is out of room (or its ore is out of range) did get past the 12-tile limit (milestone 2 around day 190, 170 MW demand by day 480), but its policy was poor: it placed a charger roughly every day (47 by day 50, 108 by day 450), which ate rock and left 65 sites queued and stuck. That is a bot policy problem, not a game one, so it was not merged. What it does show: with expansion the colony keeps growing past 100 MW, so milestone 3 (1 GW) is a matter of playing the expansion well, not a wall. A fair test needs a bot that plans a charger chain once per direction and builds panels on each new tile before extending again; worth doing only if the director asks for a tuned late game.

## Not yet balanced

A smarter bot that builds forward chargers and relays is needed before tuning mining rates, robot costs and build times. Until then those numbers stay as placeholders.

## Wired grid (2026-11-20): effect on the balance runs

New missions are wired (`SimulationRunner`): generators off the grid make nothing and stations and storage only go where power lines reach. The balance bot (`BalanceBot`) predates it, so its runs describe the unwired game; the simulation tests leave `WiredGrid` off. A line costs 15 Cu, 30 Fe and 20 rock (TBD) and is built by a robot, so the forward-charger chain now needs a line chain first. Expect the early expansion to be slower than the bot shows; re-tune once the bot lays lines.

## 2026-11-20: mission operations (P5 pass)

`tools/balance_sheet.py` was run again: 99 placeholder constants are listed (new ones: haul payload 490 kg, handling 0.25 h and 60 W, storage yard 100 t and sealed store 20 t, plant buffer sizes, starter-rule thresholds, the slag stand-in ratio). All stay TBD; none was tuned.

What the 60-day runs showed (values stay TBD):

- **Hauling is cheap on a light planet and dear on a heavy one.** The same 400 kg haul costs about 1.5 times more energy per kg-km on LHS 1140 b (1.87 g) than on Mars, and about 5 times more per hop. A smelter beside the mine saves real energy there, as intended.
- **Mars without copper.** With the lander's copper gone, three alumina and three aluminium batches (about 800 h of plant time) were enough to build a charging station at an outpost with aluminium wiring inside 60 days.
- **Industry was missing from "used power".** Fixed (KI-40); K reads a little higher while the Workshop and refineries run.
- **Step cost.** 300 robots and 26 plants: about 2.2 ms per 0.25 h step.

## 2026-11-23: the cable bill, option B (real mass times a share)

The decision of 2026-11-20 was the flat bill now and real mass times a tuned share later (option B). Run: a balance sheet test with one Mining robot on the nearest copper tile (grade 0.295, 18 h from the lander), a forward charging station and a battery yard on that tile, the whole planet surveyed, 8 game days.

| Day | Copper mined (cumulative) |
| --- | --- |
| 1 | 487 kg |
| 2 | 885 kg (the deposit was exhausted) |
| 3 to 8 | no more |

So one mine yields about **450 kg of copper a day** while it lasts (a day of mining at 150 kg ore an hour times the grade). The real conductor mass of one tile of 500 mm2 copper line is 1 333 500 kg, so a line at "one day of one mine" would be a share of 3.4e-4 (450 kg).

Finding: 450 kg is more than the **250 kg of copper the lander carries**, and stations only go where the grid reaches, so a full-day line would block the first copper mine (a deadlock). The share is therefore set to **8.4e-5 (112.5 kg a tile, about a quarter of a mine day)**: the lander's copper pays for two lines. The full-day rule needs the lander's starting copper raised to about 1 t (a balance change for the director). All values TBD. Effects: a power line or a high-voltage line costs 112.5 kg of copper (was 15 and 60), an AC line three times that (three conductors); iron and rock of the lines and the converters are unchanged.

## 2026-11-24: the haul economy (OQ-12), robot battery, payload, climb and charger spacing

Question: with the climb term on (KI-42) and the range check (L2), most distant mines are refused. Target set by the design session: a mine 2 to 3 hops from the lander can be hauled directly; anything farther can be reached through one or two charging depots.

**Correction of a premise.** The robot battery is not 350 Wh. `RobotSpecs.BatteryCapacityWh` is 2.0 x 43 Ah x 28 V = 2 408 Wh (Perseverance's two packs; Curiosity's pair is about the same, 2 x 42 Ah at 28 V). The 350 Wh figure came from an old comment in `HaulPhysics.cs`, now corrected. The usable charge for one leg is 80 % of it, 1 926 Wh. Payload is 490 kg (the Apollo lunar rover's rating) against the 1 025 kg robot; Curiosity weighs about 900 kg and Perseverance 1 025 kg, so the payload is rover-class already.

**Run** (a test on the Earth-like test planet, seed 7, contour routes, lander at tile 471; each row is the share of tiles at that many hops whose loaded leg out and empty leg back each fit one charge; the climb is the cost term set by `ClimbEffortShare`):

| Hops | Battery x1, climb x1 (0.15) | x1, climb x0.5 (0.075) | x1, climb x0.25 | x2, climb x1 | x3, climb x1 |
| --- | --- | --- | --- | --- | --- |
| 1 | 100 % | 100 % | 100 % | 100 % | 100 % |
| 2 | 100 % | 100 % | 100 % | 100 % | 100 % |
| 3 | 14 % | 71 % | 100 % | 100 % | 100 % |
| 4 | 0 % | 7 % | 13 % | 20 % | 53 % |
| 6 | 0 % | 0 % | 0 % | 4 % | 54 % |
| 8 | 0 % | 0 % | 0 % | 0 % | 33 % |

Mean round trip at 3 hops: driving 1 579 Wh, climbing 1 743 Wh (at the old 0.15). Climbing is the larger half, and a hop is 4 h at 50 W, 200 Wh flat.

**Choice.** Not a bigger battery: it is already rover-class, and doubling it would have no real counterpart. The climb share is the free game rule (the real m g dh is for a robot the game does not model), so **ClimbEffortShare 0.15 -> 0.075**. A mine at 2 hops is then hauled directly in all cases and at 3 hops in about 70 %. Farther mines need chargers: a leg of 2 to 3 hops fits one charge, so depots with charging bays every 2 to 3 hops (about 600 to 900 km) reach 8 hops with two or three bays. Payload and battery stay. A test (`HaulCostTests`) keeps 60 % direct at hops 2 and 3. All TBD; the climb share and the 80 % usable share are the knobs.

**Also built with this pass:** a day-night temperature swing so a night haul costs more heater than a day haul (decided 2026-11-24): amplitude = 10 K plus a tenth of the annual range (the amplitude the temperature puzzle measures; TBD), none on a tidally locked planet; the haul text suggests starting later when that saves heater energy. Save version 54.

## 2026-11-24: weather (PL-07), two game years on each planet type

A test colony (3 robots, one panel at the base, default simulation) run for 2 game years per planet type, weather on against weather off.

| Planet type | Solar from weather | Wind-storm days a year | Dust-storm days a year | Robots lost |
| --- | --- | --- | --- | --- |
| Earth-like 1 atm, NASA clouds | +0.7 % (the mean cloud factor is the climatology; panels are slightly nonlinear in light) | 6.5 | 0 | 0 of 3 |
| Cold Earth-like (-25 C mean) | +0.7 % | 6.5 | 0 | 0 of 3 |
| Mars-like 0.006 atm | -5.0 % (one global storm in this seed's two years; 45 dust-storm days a year on average) | 11.5 | 45.5 | 0 of 3 |
| Hot thick air 2 atm | 0.0 % (no cloud climatology for non-Earth worlds) | 6.5 | 0 | 0 of 3 |
| Airless | 0 (no weather) | 0 | 0 | 0 |

Nothing absurd: solar on Earth-like planets does not fall (the climate was already in it); a storm day is 2 % of days by construction (6.5 in a year against the expected 7.3); Mars loses a few per cent in two years but nearly all the sun for weeks in a global storm (tau up to 11); no robot is lost because the colony robots shelter at the lander. A robot left out in the open with an empty battery is still destroyed by the KI-43 rule, and the wind only makes its heater cost more (up to 3.4 times at 20 m/s). Not measured: a long field campaign with exposed robots, since the starting colony keeps its robots at the base. Wind turbines are listed as next, not built.

Caveat: the Mars 60-day aluminium logistics scenario runs with weather off; with the dust on, that chain would take longer, which the next logistics balance should look at.

## 2026-11-25: weather W8, Mars with weather on and the exposed field campaign

**Mars aluminium outpost, 60 days, weather on** (`Mars_WithWeatherOn_TheSameSiteIsBuilt_AndNoRobotIsLost`; the weather-off test stays as the control). First run: all 3 robots destroyed in the open at -46 to -59 C within 17 h of the start (the weather-off control also lost 2 of 3 but finished the site first, which hid it). Causes found: the trip-home reserve counted the drive but not the heater; a cold battery that cannot charge still asked the grid for power (an energy-ledger leak, 440 W). Fixes use existing planning: the reserve includes the heater, the cold battery's smaller capacity and the cargo, and a cold battery asks for no charge power. The site is built within 60 days with no robot lost. No cited figure or physics was changed.

**Exposed field campaign** (one hauling robot, 30 days, three weather seeds): see KI-46 in `known-issues.md`.

| Run | Robots lost of 3 runs | Cause |
| --- | --- | --- |
| Mars southern winter | 0 (the planner refuses every haul beyond the lander's short cold-battery range, so no robot leaves) | the cold capacity share (65 % at the -20 C operating limit) cut the leg budget |
| Earth-like 60 deg N winter | 3 of 3 (each run loses both robots in 16 to 65 h, near the base) | OPEN: a loaded robot may do the leg out and have too little for the way home when the drop is not a charger; fixing the plan check removes the 2 to 3 hop direct-haul balance of 2026-11-24, which needs re-tuning |

Dust warnings (W8.3): one WATCH entry when the dusty season starts and one when a global storm begins, with the estimated optical depth and the sunlight share left.

## 2026-11-25: KI-46 closed, honest haul numbers

| Check | Before the strict round-trip rule | After |
| --- | --- | --- |
| Mines 2 hops from the lander hauled directly | 100 % (legs checked one by one) | 20 % on the test surface |
| Mines 3 hops away hauled directly | 71 % at climb 0.075 (the 2026-11-24 table) | 0 %: from 3 hops the player builds a depot with a charger |
| Field campaign, Earth-like 60 N winter, 3 seeds | robots lost 3 of 3 | 0 lost, 1500 kg hauled in each |
| Field campaign, Mars southern winter, 3 seeds | 0 lost, no haul possible (cold capacity) | 0 lost, no direct haul is possible; with a depot and charging bay at the refused tile (the advice) the haul runs: 1500 kg in each of 3 runs, 0 lost |
| Heavy planet haulage per kg-km | about g over the radius | at least that: loads are smaller (quarter loads), so the robot's own mass is spread over fewer kg |

The climb share (0.075) is unchanged by decision; the numbers above are the honest cost of the safety rule.

## 2026-11-25: weather-aware planning (P1 to P3) and wind turbines (T1 to T3)

**P1, waves on the real wind.** The wave model's wind is now the weather's daily 10 m wind raised to 19.5 m by the 1/7 power law (x1.10; Hsu, Meindl and Gilhousen 1994). Long-run wave energy flux on an Earth-like world, sea tiles, one game year: old rule 2373 W/m, real wind 2985 W/m, ratio 1.26 (inside the +-30 % limit; reported, not tuned: the real wind has more weight in the gales because the flux goes as the fourth power of the wind).

**P-test, field campaign with forecast planning** (3 seeds, 30 days, one hauling robot):

| Scenario | Planning | Hauled kg per seed | Robot-hours sheltering | Haul-hours waiting for a forecast storm | Robots lost |
| --- | --- | --- | --- | --- | --- |
| Earth-like 60 N winter | old rule | 1500 / 1500 / 1500 | 958 | 0 | 0 |
| Earth-like 60 N winter | forecast | 1500 / 1500 / 1500 | 958 | 0 | 0 |
| Mars southern winter | old / forecast | 0 / 0 / 0 (no direct haul is possible) | 594 | 0 | 0 |
| Mars winter, advice followed | old / forecast | 1500 / 1500 / 1500 | 1496 | 0 | 0 |

No difference in these 30 days: a storm is 2 % of days, and a forecast at the 84th percentile only fires when today's wind is already about 1.85 sigma high; in two game years at 60 N the days a storm is forecast on a route equal the days a storm is on (23 in 730). The unit tests cover the wait, the reason and Go anyway. The robot-hours sheltering are the robots at their target, not the planner's doing.

**T-balance, one NPS 100C-21 turbine against the same real mass (18 500 kg) of solar panels at a 45 N land tile, two game years:**

| Planet type | Turbine kWh a year | Capacity factor | Solar kWh a year, same mass | Turbine / solar |
| --- | --- | --- | --- | --- |
| Earth-like 1 atm | 242 134 | 27.6 % | 474 246 | 0.51 |
| Cold Earth-like | 242 134 | 27.6 % | 474 246 | 0.51 (the same: the test's mean temperature argument does not change the modelled air temperature) |
| Mars-like 0.006 atm | 2 002 | 0.2 % | 353 194 | 0.01 |
| Hot thick air 2 atm | 343 051 | 39.1 % | 565 338 | 0.61 |
| Airless | 0 | 0 | 823 991 | 0 |

Not absurd by the stated test: a turbine never beats solar by 3x, and the Earth-like capacity factor (27.6 %) is far above 5 %. A real 100 kW turbine at 7 m/s mean wind has a capacity factor of 34 % on the datasheet's own table, and the game reproduces that table within 5 %. Wind is worth building where air is thick and wind is steady and for the night, not for the energy per kg.

### Closing round (2026-11-25)

- **Forecast planning has no effect, by construction.** Our forecast is persistence of an AR process (today's anomaly times rho^k), so a storm is only seen on the day it comes; the 84th percentile bound rarely reaches the storm threshold ahead of time. Accepted as honest; no better forecaster is planned.
- **KI-47 (cold Earth-like temperature).** NASA T2M is now moved by a uniform offset to the planet's own mean (an assumption). Cold Earth-like (-25 C): the turbine row is now 277 547 kWh a year, capacity factor 31.7 % (was 27.6 %, the same as Earth: the temperature was ignored), turbine / solar 0.50. The cold Earth-like 60 N winter campaign loses 0 robots; the planner refuses the direct haul (hauled 0). Clouds are untouched (tested).
- **Dust sizing advisor.** Estimated (marked as such), shown in the Power window and in the dust WATCH entries. A brownout test through a seeded global storm is not possible in this model: the colony's demand grows toward its Kardashev target and the colony browns out long before any storm, so the test checks the advice's bookkeeping instead.

## 2026-11-25: robot wrecks and storage chemistry

**Chemistry, two game years, 1000 kg of each in an open pile on an inland 45 N tile** (sodium and lithium 100 kg in an open wreck pile, the one place they can lie unsealed; percent lost, and percent left for the metals):

| Planet type | Iron lost | Copper, aluminium, rock lost | Water ice lost | Salt lost | Sodium left | Lithium left |
| --- | --- | --- | --- | --- | --- | --- |
| Earth-like 1 atm | 3.1 % | 0 | 100 % (melts, runs off) | 35 % (brine) | 0 % | 0 % |
| Cold Earth-like | 0 | 0 | 0.1 % | 35 % | 0.2 % | 29 % |
| Mars-like 0.006 atm | 0 | 0 | 18 % (sublimates) | 0 | 99.8 % | 100 % |
| Hot thick air 2 atm | 0 | 0 | 100 % (melts) | 0 | 0 % | 0 % |
| Airless | 0 | 0 | 100 % (sublimates, heat-limited) | 0 | 100 % | 100 % |

The environment ledger closes to under 1e-6 kg in every run. Against the absurd list: iron lost on Earth-like is 3.1 % in 2 years inland (1.5 % a year; a coast or very damp tile loses about a tenth), under 10 %; sodium does not survive open humid air; ice on Mars does sublimate when warm (18 % in two years); a wreck never holds more than the robot's bill (a General robot's bill is 642 kg; a cold wreck holds 642 kg, a wreck at 700 or 1600 C holds the same 642 kg as slag and parts, above 2862 C nothing). Caveats: the lander and any store with a seal are inert in these runs by rule, so the losses are the open-pile worst case; the rates for sodium, lithium, salt brine and the 0.05 m2/kg iron surface are game rules or assumptions (TBD); there is no iron smelter in the game yet, so rust cannot go back as ore.

### Sublimation, cited model (2026-11-25)

The earlier damping was replaced. Under air, sublimation follows Ingersoll (1970, Science 168): free convection of the lighter vapour layer plus diffusion, E = 0.17 d(rho_v) D (g d(rho)/rho / nu^2)^(1/3), checked against Mars chamber rates by Chittenden et al. 2008 (Icarus); in a vacuum it is Hertz-Knudsen; both are capped by the heat the surface can take in (200 W/m2). At Phoenix conditions (800 Pa CO2, g 3.71) the model gives 0.06 mm of ice a sol at -45 C, 0.25 mm at -35 C, 1 mm at -25 C, 3.4 mm at -15 C and 6.8 mm (heat-limited) at 0 C: millimetres per sol at the daytime temperatures of sunlit ice, consistent with the Phoenix trench chunks vanishing in about 4 sols (Smith et al. 2009, Science 325), and negligible at the night temperatures.

| Planet type | Ice lost in 2 years (open pile, 45 N tile) |
| --- | --- |
| Earth-like 1 atm | 100 % (it melts and runs off) |
| Cold Earth-like (-25 C mean) | 4.2 % |
| Mars-like 0.006 atm | 52 % (was 18 % with the damping) |
| Hot thick air 2 atm | 100 % (melts) |
| Airless, warm (-50 C = 223 K) | 100 % (sublimates, heat-limited) |
| Airless, cold (-153 C = 120 K) | 0 % (it keeps its ice) |

## 2026-11-25: robot rescue (R1), field campaign

One robot forced low (25 % of what its cold battery can hold: it cannot get home) 1 or 2 tiles from the lander in the worst season, 3 weather seeds, 72 hours; the balance report is `RescueBalanceTests` (run by hand). A tile is 4 hours of driving here, so a helper at the lander is 4 h from the nearest tile.

| Scenario | Distance | Rescues requested / succeeded (planner's pick) | Method | Energy | Rescuers lost | Robots lost, no rescue (baseline) | Robots lost, with rescue |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Earth-like 60 N winter | 1 tile | 2 of 3 runs / 1 | jump | 447 Wh | 0 | 5 | 5 |
| Earth-like 60 N winter | 2 tiles | 1 / 0 (refused: the helper cannot come back) | tow tried | 204 Wh | 0 | 2 | 2 |
| Mars southern winter | 1 and 2 tiles | 0 / 0: the stranded robot has under an hour in the cold and the helper is 4 h away | none | 0 | 0 | 3 | 3 |

What it says: the rescue never kills the rescuer (the helper keeps 1.6 times its way home and the transfer stops when it would go below it; an earlier version with a thinner margin lost the helper too, found by this run), never makes the losses worse than no rescue, and costs 200 to 450 Wh against a robot bill of 642 kg. It is not a miracle: on Mars in winter a robot that runs dry has under an hour, and in 60 N winter a robot two tiles out is out of reach of a robot with a 2 kWh battery; the log says so ("no robot can reach tile X before it freezes in N h") and the player is told to build a charger or depot nearer. A tow is dearer than a jump (the stranded robot is 1025 kg of cargo) and the stranded robot must live to the shelter, so the planner picks the jump when the deadline is short. The baseline losses (5 of 9 robots in 60 N winter) are not from the rescue: the same robots die without it.

Also in this round: telemetry records (robots' charge and condition, the grid battery, the power made, each turbine) cost 2720 records and 0.024 ms per tick over two years; task priorities (high, normal, low) already existed per robot and group, so R3 only records that computing priorities wait on the ColonyNetwork.

### Hibernation and cold soak replace the one-hour timer (S1, 2026-11-25)

A robot with an empty battery outside shelter hibernates; its body follows the air with tau = m cp / G = 375 kJ/K / 2 W/K = **52 hours** (642 kg at a mass-weighted 0.58 kJ/kg K) and it is destroyed only when its body leaves the storage range, -40 to +60 C (electronics -55 to +125 C, LFP cell -40 to +60 C; the narrower). On a -59 C Mars night an empty robot takes about 57 hours from a warm start to reach -40 C; in the rescue runs the planner's deadline was 19 to 21 hours. The destruction cause is cold soak or heat soak. The earlier rescue table is replaced (3 seeds, 72 h, robot forced low or empty):

| Scenario | Distance | Rescues requested / succeeded (planner's pick) | Method | Energy | Robots lost with rescue | Robots lost, no rescue |
| --- | --- | --- | --- | --- | --- | --- |
| Earth-like 60 N winter | 1 tile | 5 / 3 | tow | 612 Wh | 0 | 0 |
| Earth-like 60 N winter | 2 tiles | 2 / 1 | tow | 204 Wh | 0 | 0 |
| Mars southern winter | 1 and 2 tiles | 0 / 0: the helper cannot afford it (cold batteries lock a third of their capacity: the jump needs 2 to 3 kWh, the helper has 1) | none | 0 | 0 | 0 |
| Earth-like 45 N summer, truly empty | 1 and 2 tiles | 0 / 0: same reason (4 h away, 411 Wh to spare of 1129 needed) | none | 0 | 0 | 0 |

Robots lost fell from 5 and 3 to 0 in every row: in 72 hours nothing reaches its storage limit, and the soaking robots are still alive when the run ends (the Play run with no helper at all lost them after about 57 hours on Mars). The planner no longer refuses for lack of time on Mars; it refuses for lack of energy and says so with the numbers, and gives the S3 advice (a charging station on the tile halfway). A rescue costs 200 to 600 Wh. The field campaigns (haulers with the KI-46 rule) never strand a robot, so they are identical with and without rescue: 0 robots lost in all of them.

## Deposit depletion by planet type (B4, 2026-11-25)

Two-year BalanceBot run (`DepletionByPlanetType`, explicit), a scripted colony that mines rock, iron, copper and lithium. Reported, not tuned.

| Planet | Deposits used up | Rock moved | Tailings | Mining energy | Generated | Share |
| --- | --- | --- | --- | --- | --- | --- |
| Earth-like | 0 | 3 735 t | 1 657 t | 4.1 MWh | 33 945 MWh | 0.0 % |
| Mars-like (cold, far) | 0 | 35 t | 3 t | 0.1 MWh | 22 705 MWh | 0.0 % |
| Hot (close) | 0 | 3 597 t | 1 657 t | 3.9 MWh | 48 066 MWh | 0.0 % |

Nothing runs out in two years, and mining takes under 0.1 % of generation: neither "absurd" threshold is near. Mining power (the robot mine power) is tiny next to the panel fields, so the energy cost of poor ore is not a constraint yet; the deposits are what limits a long game. The scripted bot never reaches a milestone (it does not research), so the "ran out before milestone 2" test is checked by its stock never reaching zero. On Mars the cold range keeps robots near the base, so almost nothing is mined: a travel and charging limit, not a deposit limit.

## Comminution energy and the progression bot (2026-11-25)

Crushing and grinding now costs energy by Bond's law (copper at 0.5 % ore: about 2.6 MWh per tonne of copper). Re-run of the two-year table: Earth-like 25 MWh of mining energy (21 MWh crushing) against 33 945 MWh generated = 0.1 %; Mars-like 0.1 MWh; Hot 25 MWh of 48 066 MWh = 0.1 %. Not absurd, because the scripted colony mines little copper (it stops at 1 500 kg).

The progression bot (`ProgressionReachesMilestoneTwo`, Explicit) does **not** reach milestone 1 on either world in two years. First blockers found, in the order they bite (none fixed by loosening physics):
1. Chargers on the landing tile are fed by the lander RTGs only (about 440 W in all, about 90 W per robot), so robots that dock there spend almost all their time charging. Chargers on other tiles are grid-fed, but robots prefer the nearer RTG dock.
2. The starting 144 kg of copper is the only copper until a copper mine runs, and a charger costs 12 kg of it: panels, batteries and robots ordered first leave none for the forward chargers that make the copper mine reachable (task state: "too far: no robot can drive there and back to a charger").
3. A forward charger (Build task) can itself be "too far" when placed more than a robot's round trip from an existing charger.
4. Mars-like (-60 C): two of the three starting robots cold-soaked on a neighbouring tile by day 20 with an empty battery; no robots left, so nothing is ever mined.
These may be bot-order flaws or design gaps; the bot has not found a build order that gets past 1.

## Progression bot after the load order, islands and the cold trip budget (2026-11-25)

Changes: load order (baseline, robots below 20 %, other charging, construction, growth; shed from the bottom), island grids (a wired source plus chargers off the lander grid), KI-46 for every outdoor task counting the heater and the cold-locked battery (found by tracing the Mars losses: a warm battery that cools outdoors loses about 750 Wh of usable energy, and the 4-hour leg to tile 483 was approved on the warm numbers), the 20 % return reserve counting that lock.

Result: Mars-like keeps its robots (5 at the end, 1 286 t of rock mined) where all were lost by day 5 before. Neither world reaches milestone 1 in two years. Next blocker, both worlds: "Build@496 / Mine@496: too far: no robot can drive there and back to a charger": the forward charger toward the copper is itself beyond a round trip. The leapfrog outpost (a one-way trip for a full kit) is not built yet. Iron cell in Play: not a leak: a batch waits in the cell while the grid browns out (a unit test now proves the rust stays in the cell and is all accounted for).

## Progression bot with the real defaults and leapfrog outposts (2026-11-25)

The bot now plays the new-game defaults (wired grid, logistics, nothing wired for it). Findings in order:
1. A power line costs 112.5 kg of copper a tile (the starting stock is 144 kg), so any cabled charger or panel field away from the lander is out of reach until copper is mined. Island outposts (a panel costs 11.5 kg of copper, a station 20) avoid it.
2. With outposts built toward the ore, Viridia mines copper (1 500 kg in stock by day 110) with no blockers listed. Crushing (Bond) then takes 5 to 12 % of generation.
3. Viridia still reaches no milestone in two years: batteries need lithium (about 9 kg each), the lithium deposit (tile 484, two tiles out) shows "Travelling" for 700 days and nothing is mined, so the battery stays 22 MWh and the colony cannot grow past about 1 MW of demand.
4. Mars-like: the first outpost is refused ("its battery would run out 5 520 Wh short of finishing the build"): two panels and a station take about 8 kWh of build work at 200 W, 3.3 times a cold robot battery. Design gap: the build power, the kit size, or a robot that can charge from a panel before the station stands.
Not done: islands are lossless in the bot's runs only because the outposts are single tiles; line faults and ratings are not modelled on islands.

## Lithium, staged outposts (2026-11-25, later)

The Viridia "Travelling for 700 days" was a misreading: the robots worked at the lithium tile, but with logistics on, lithium is a sealed-store material and a tile with no sealed store takes no ore ("No room for lithium on tile N: build a sealed store there"; the work hours are still spent: KI-48). With a sealed store built at the mine, the bot reached milestone 1 on day 79 (M2 never; demand 8 MW). No robot travels continuously for more than about 60 hours over 60 days (a regression test pins it under 97 h).

The staged panel-first one-way check (panel built from the battery, trickle-charge from it, then the station and the rest, hour by hour) is stricter than the first version and finds no feasible outpost on either world: with the kit riding as cargo the outbound leg of one tile costs roughly twice the drive energy, so a full 2.4 kWh robot arrives with too little to build the first panel (Viridia, tiles 485/495/473/474 short by 276 to 973 Wh or stage 1 unfinished; Mars-like short by 0.8 to 2.6 kWh). With the earlier, looser check (build energy only) the bot built outposts and reached M1. Design gap for the director: robot battery vs kit mass (a Construction robot with a bigger battery, a second robot carrying the kit, or a smaller first stage).

## Small loads open the outposts (2026-11-26)

The quarter-load floor on hauls is lifted: a load the battery cannot carry is split to the largest load that fits, down to 5 kg (more trips are the cost). Kit hauls to a nearer tile now work (tile 473: 155 kg trips, staged via 473 for 485). Result with the strict staged one-way check:
- Viridia: milestone 1 on day 90. At two years: demand 12.6 MW, day-average power 20.9 MW (M2 needs 100 MW), battery 242 MWh, 16 robots, Fe 20 t, Cu 1 500 kg, Li 1 500 kg, no queue and no shortfall. The bot stops growing: it only builds panels on powered tiles, and a cable costs 112 kg of copper a tile.
- Mars-like: no milestone. Every outpost candidate is out of reach even for an empty robot (a 5 kg load): round trips of 2.3 to 4.0 kWh against about 1.9 kWh of battery reach, of which 1.0 to 1.8 kWh is the heater. One robot was lost to cold at day 272.

## One lumped body (2026-11-26)

The three thermal models (heater by air temperature, the battery's own 1 h time constant, the 52 h hibernation body) are replaced by one lump: C dT/dt = -G (T - T_air) + P_internal + P_heater, the battery shares the body temperature, the heater runs only when the body would pass its hold limit and then draws exactly what holds it (G (limit - air) - internal heat), shelter is a heated bay, hibernation is the empty-battery case. Internal heat is 30 W of electronics plus 15 % of drive and work power (assumptions, TBD; motor and drive efficiency about 85 %). Trip planning (the leg budget per leg with the body temperature on it, KI-46 range checks, hauls, the staged outpost check) uses the same model; rescue and warnings use the steady-state heater.

| | before | after |
|---|---|---|
| Viridia milestone 1 | day 90 | day 54 (60 after the bot fix below) |
| Mars-like robots lost in two years | one (day 272) | none |
| Mars-like rock moved / generation | 1 027 t / 5 667 MWh | 25 439 t / 22 177 MWh |
| Mars-like milestone 1 | never | never (see below) |

Fixed on the way (with tests): a haul planned in a warm hour that no longer fitted stayed blocked forever (it is now cut to what fits), and the bot's own home stock targets reserved all the copper, so cable sites at other tiles never got any.

Viridia, five game years: milestone 1 on day 60, then 37 MW day-average power from about day 250 to day 1 826 (504 panels, 12 powered tiles, battery 402 MWh); milestone 2 needs 100 MW. The limit is copper: stock 0 kg, the copper mine (tile 496) is out of reach again, so 19 sites wait. The 112 kg per tile of cable is not the limit; copper income is.

Mars-like, two years: no milestone 1 (0.59 MW day-average against 4.3 MW). Copper 135 kg at landing is gone by day 30 (home charger, first panels, outposts); the second outpost (tile 485) is 16 kg of copper short, the copper tile (496) stays out of reach, and 20 t of lithium was mined into the sealed store at tile 484 with no haul home (crushing took 32 % of generation for nothing).

## Stuck robots, lander aluminium, the ore ledger (2026-11-26)

Two stuck-robot bugs found by the five-year bot, both fixed with tests: (1) a rescue helper was sent to the shelter every step by the rescue code and called off to charge by the upkeep every 4 h (the charger gave it nothing), so the order never ended: orders now end after 72 h with a back-off; (2) a robot with a fraction of a watt-hour (or none) set out for the workshop or sat on the road to a charger and was not counted as stranded (the test was an exactly empty battery): the stranded test is now 0.2 % of capacity, and a robot goes for maintenance only if its battery can drive there.

The lander now brings 100 kg of aluminium ([rule] TBD; old saves are not granted any), so the aluminium stand-in works from day 0 on every planet.

| two game years | before | after |
|---|---|---|
| Viridia milestone 1 | day 92 | day 82 |
| Mars-like milestone 1 | never | day 63 (day-average 16.0 MW at the end; milestone 2 needs 43.1 MW) |
| Mars-like crushing energy | 7 164 MWh (26 % of generation) | 45 MWh (0.0 %) |

Where the Mars crushing went (ledger, before the bot rule): lithium at tile 484, 2.72 GWh for 21.7 t mined, of which 20 t sat in the sealed store at the mine and only 1.5 t reached the base (hauls from 484 carry little); copper at tile 496, 17.8 MWh for 2.9 t. So the waste was lithium mined into a full store faster than hauls could bring it home; the bot now stops mining an ore whose pile at the mine tile passes 2 t.

## Haul payload and the Viridia plateau (2026-11-26, later)

The haul report now shows the longest leg against the battery budget. Finding: the best payload per trip from the copper mine (tile 496) to the base is the full 490 kg, so the longest leg is not what keeps loads small (the bot's rule to split the longest leg when the payload is under 100 kg never fires). The 11-12 kg hauls are sized by demand: each waiting site asks for its own few kilograms and the planner makes one small haul per demand (at most 4 at a time). Viridia over five years: milestone 1 on day 82, then a plateau at 34.7 MW day-average power (milestone 2: 100 MW) with 19 sites waiting, copper stock 0 and 2 t of copper piled at the mine. Two bot findings: a copper target at the base reserves the stock the kits need (a target of 200 kg starved the first outpost, the colony never started), and with no target nothing brings the mined copper home except site hauls. A mine whose store is full now pauses (no robot hours; log "Store full at tile N: waiting for hauls."). Mars-like, two years: milestone 1 on day 63 as before; day-average power 16.0 MW against 43.1 MW for milestone 2, copper mining 0.5 kg a day (tile 496 is out of reach of the haul chain).

## KI-46 at dispatch, and the reservation order (2026-11-26, later)

Cause of the Mars R2 and R3 deaths (traced hour by hour): a haul was dispatched to a robot on whatever charge it had. The plan checked a round trip against the battery's per-leg budget, but the dispatch checked only the way to the pickup, not the whole first segment (empty leg to the pickup, loaded leg to the next charger, or to the drop and the way back when there is none). Robots left the base at about 40 % charge, reached the outpost at tile 473 with 460 Wh, and died on the empty leg to the charger 8 h away (cold-soaked at -69 C). The rule now: at dispatch a haul needs 1.1 times the energy of that segment above the 20 % reserve, at the robot's present charge and the body temperature on the way, and a robot that is short of it charges for the haul (even below the usual 80 % of capacity). With no charger anywhere the old rule stands. Together with the two-phase reservation (sites and plant inputs first, stock targets are buffers):

| two game years | before | after |
|---|---|---|
| Viridia milestone 1 | day 82 | day 52 |
| Mars-like milestone 1 | day 63 | day 98 (no robot lost; day-average power 12.9 MW against 43.1 MW for milestone 2) |

## Mines push their output; the copper bootstrap is the limit (2026-11-26, later)

A mine's store at least one full load (or any waiting demand at the base) now gets a haul home even with no stock target (one at a time per mine and ore); many sites at one destination were already served by one demand per destination (a test pins at most 4 hauls, with loads up to the payload). Two years: Viridia milestone 1 day 54, Mars-like day 96 (no loss), day-average power 12.6 MW against 43.1 MW for milestone 2. Viridia five years: milestone 1 day 54, then 14.1 MW day-average power (milestone 2: 100 MW) with copper stock 0 and the copper mine (tile 496) out of the haul chain's reach: the chain stops at tile 485 because each outpost needs 43 kg of copper and the starting copper is spent on home panels first. Copper arriving: 0.1 kg a day. Lithium is no longer the issue (3.3 t at the base).

## Bisect of the five-year Viridia regression (2026-11-26)

Five-year Viridia, same bot per commit: cc613ed 34.65 MW and 127 panels; 15d47bc (#230) 14.26 MW and 30 panels; db7973f (#231) 14.05 MW and 37 panels; db7973f with mine pushes off 14.26 MW and 30 panels (so the pushes are not the cause). Variants of #230: dispatch check off 35.79 MW and 409 panels; old charge trigger no change; old bot stock targets 12.51 MW and 27 panels (not the cause); dispatch margin 1.0 instead of 1.1: 38.61 MW and 236 panels. Cause: the dispatch rule asked 1.1 times the segment energy above the 20 % reserve, while the plan's leg budget already keeps the reserve (0.6 of capacity); robots had to charge to about 88 % before every far haul, which is above the normal 80 % stop, so hauls and kits were served far less often. Margin 1.0 keeps the Mars protection (a robot at 40 % cannot take a segment of 861 Wh above its reserve) and restores the economy.

Mars-like with a two-panel outpost kit is slow because the kit is 2.1 t of rock, 357 kg of iron and 43 kg of copper at 85 to 105 kg a trip in the cold (25 to 30 trips of 16 h); a one-panel kit on cold worlds halves it.

The 12 kg copper loads: not a planner bug. Demand at the base for copper (about 69 kg for six waiting panels) is one demand, but each haul takes what the source holds, and the copper pile at the outpost arrives at 0.1 to 0.7 kg a day from the one mine that is partly out of the chain's reach; the loads are the trickle.
