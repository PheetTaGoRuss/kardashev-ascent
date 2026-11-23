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

## Seen in Play mode (2026-11-24)

- Mine tiles show their grade and kg/h as ranges ("about 99.1 kg/h, plus or minus 22.3") until the ore assay.
- A haul of 192 h is refused ("no chain of legs between chargers or shelters fits the battery": the biggest battery gives 1926 Wh above the return level; the round trip needs 27 kWh, mostly climbing).
- Contour against straight routes from the lander: for example tile 2743, climb 10 911 Wh against 23 268 Wh for 41 h more driving.
- Depot advice with mines 20 to 90 h away: "tile 312 saves 36 808 Wh a day". For a refused haul the advice names a site where a charger would make both halves fit one charge. With mines 190 h away no single site helps, so there is no advice.

## Depot with a charging bay (Decided 2026-11-24, director delegated)

A storage yard can be given a charging bay: a charging station on the yard's tile at its own, lower bill (iron 60, rock 10, copper 12, half the build time; TBD). A depot with a bay is a stop: robots recharge there and hauls are staged through it (L2 counts every finished charger and shelter as a stop). Depot advice reads "build a depot (storage yard) with a charging bay at tile N". The tile info shows an "Add charging bay" button on a yard's tile.

## Balance, day-night heater and what was seen (2026-11-24)

- **Balance (OQ-12):** `ClimbEffortShare` 0.15 -> 0.075; battery (2.4 kWh, Perseverance-class) and payload (490 kg) unchanged. A mine 2 hops away is hauled directly always, 3 hops in about 70 % of cases; farther mines need a depot with a charging bay every 2 to 3 hops. Full table in `balance-findings.md`.
- **Day-night temperature (save version 54):** the air temperature swings by an amplitude (10 K plus a tenth of the annual range, TBD) times the sine of the Sun's height; none on a tidally locked planet. Robot weather and the haul heater cost use it; the haul text suggests a later start when that saves heater energy ("starting in 6 h would save N Wh of heater"). In mild air the heater does not run, so night and day cost the same there; in the cold a unit test finds a start that saves energy.
- **A charging bay needs power:** on the wired grid a charger goes only where a power line reaches, so a depot far out needs a line to it first (the cable bill applies).
- **Seen in Play mode:** a mine 24 h (6 hops) away hauled "direct" (1 696 Wh round trip); a 40 h haul refused, then "staged via tile 5611" after two yards with charging bays were built on its power-lined route; on Viridia the air swung from 29.6 C to 23.7 C over half a day. Not seen in Play mode: a night haul costing more heater (the test planets in the chooser are mild or tidally locked); covered by a unit test.

## Mars and the day-night swing (Decided 2026-11-24, director delegated)

The 10 K plus a tenth of the annual range rule gave Mars about 10 K, far below what Curiosity measured: REMS saw daily air temperatures from about -70 C to 0 C at Gale crater, a range of about 70 K ([JPL PIA16913, "Steady temperatures at Mars' Gale crater"](https://www.jpl.nasa.gov/images/pia16913-steady-temperatures-at-mars-gale-crater/); REMS overview: Gomez-Elvira et al. 2014, JGR Planets 119). The amplitude now follows the air: 10 K + 25 K x (1 - pressure in Earth atmospheres, at most 1), so 10 K on Earth and 34.8 K on Mars (0.00628 atm), a 70 K range (game rule, TBD). In Play mode on Mars: air -35.1 C now and -82.9 C twelve hours later; a 10 h haul showed "starting in 21 h would save 135 Wh of heater"; heater energy was 11 kWh of a 16 kWh round trip on a 144 h haul.
