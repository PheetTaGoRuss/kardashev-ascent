# Logistics on the climb and weather costs

Decided 2026-11-24 (director delegated). Status: stage L1 built. Every number is a game rule, TBD.

## What changes

Hauls already exist (mission-operations.md part A): inventories per place, depots, demand-driven haul tasks. Two physics costs now exist that hauling ignored:
the **climb** (KI-42: `ClimbEffortShare x m g dh`, uphill only, no regeneration) and the **robot weather** (KI-43: a heater draws power outside the operating range).
Logistics now uses them.

## Stages

| Stage | What | Status |
| --- | --- | --- |
| L1 | Haul cost: for a leg, driving Wh, climbing Wh (planned at the high end of the relief range until surveyed) and heater Wh along the route; the planner ranks sources by driving hours plus the extra energy as hours of flat driving (Wh over drive power); the Operations window shows each haul's cost split | Built |
| L2 | Range check: refuse or stage a haul whose round-trip energy exceeds a robot's battery, routing through a charger or shelter | Not started |
| L3 | Depot siting: suggest the depot tile that minimises the energy of hauling from the mines and to the sites, on contours and in shelter | Not started |
| L4 | Contour-following routes: the route finder weighs climb energy as well as time, so a haul goes round a ridge when that is cheaper | Not started |

## Rules (L1)

- Cost hours = driving hours + (climb Wh + heater Wh) / drive power. Equal to the driving hours on flat ground in mild air; infinity when there is no route.
- The way back is empty: no cargo climb term, lighter drive power.
- Heater Wh uses the air temperature on each tile of the route at the time of planning, for a robot without insulation (cautious).
- Until the terrain relief survey is done, the climb is scaled by the high end over the centre of the relief range (RS-14).
