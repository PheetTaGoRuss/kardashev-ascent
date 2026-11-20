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
