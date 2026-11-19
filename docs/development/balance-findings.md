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

## Not yet balanced

A smarter bot that builds forward chargers and relays is needed before tuning mining rates, robot costs and build times. Until then those numbers stay as placeholders.
