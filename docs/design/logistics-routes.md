# Logistics on the climb and weather costs

Decided 2026-11-24 (director delegated). Status: all four stages built (2026-11-24). Every number is a game rule, TBD.

## What changes

Hauls already exist (mission-operations.md part A): inventories per place, depots, demand-driven haul tasks. Two physics costs now exist that hauling ignored:
the **climb** (KI-42: `ClimbEffortShare x m g dh`, uphill only, no regeneration) and the **robot weather** (KI-43: a heater draws power outside the operating range).
Logistics now uses them.

## Stages

| Stage | What | Status |
| --- | --- | --- |
| L1 | Haul cost: for a leg, driving Wh, climbing Wh (planned at the high end of the relief range until surveyed) and heater Wh along the route; the planner ranks sources by driving hours plus the extra energy as hours of flat driving (Wh over drive power); the Operations window shows each haul's cost split | Built |
| L2 | Range check: a haul is refused only when no chain of legs between chargers or shelters fits the usable battery (after the climb reserve and the heater); otherwise it is staged through the stops; the Operations window shows "direct", "staged via tile ..." or "REFUSED". Robots recharge at the stops with the existing charging rules | Built |
| L3 | Depot siting: advice only, the top sites by Wh per day saved over hauling each mine's ore direct to the lander (a depot D turns the haul into mine-to-D plus D-to-lander); candidates lie within 2 hops of the mines' routes; the player builds a storage yard where they choose | Built |
| L4 | Contour-following routes: the route finder weighs the climb (no downhill regeneration) and the heater at the tile's mean annual air temperature against time with one weight (1 Wh is worth the time to drive 1 Wh at the drive power, TBD); times reported are the driving time along the route found; on for new missions, off for saves before version 53 | Built |

## Rules (L1)

- Cost hours = driving hours + (climb Wh + heater Wh) / drive power. Equal to the driving hours on flat ground in mild air; infinity when there is no route.
- The way back is empty: no cargo climb term, lighter drive power.
- Heater Wh uses the air temperature on each tile of the route at the time of planning, for a robot without insulation (cautious).
- Until the terrain relief survey is done, the climb is scaled by the high end over the centre of the relief range (RS-14).
