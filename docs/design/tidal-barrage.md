# Tidal barrage

Status: **PROPOSED**, built 2026-11-19 (EN-17) after the game director chose "build it, with effects": several slots across a channel, coast effects kept, one-way generation, milestone gate (3), basin survey experiment. The numbers below are the ones in the code. Every number marked TBD is a placeholder for tuning, not a claim.

## What it is, and why it comes after free-flow

A barrage is a dam across a bay or estuary with gates and turbines in it. The tide fills the basin behind it and the gates hold the water; the water is let out through the turbines at low tide (and sometimes in at high tide too). It makes much more power than a free-flow rotor, but it is a large, slow build that changes the coast. In the game it is the late tidal source: the player has already learned the tide with the tide gauge and has a working colony.

## Physics the game keeps

| Rule | Why | Source status |
| --- | --- | --- |
| Energy of one emptying: E = ½ ρ g A h², with basin area A and tidal range h. The water raised by h on average h/2 stores ρ g A h · h/2. This is the most a basin can give in one tide; a real plant gets a share of it. | Textbook hydraulics, derived from potential energy. | Derived here (no source fetched for per-tide plant figures) |
| Capacity factor is low, about 25 %. La Rance (France) is rated 240 MW and averages about 57 MW. Sihwa Lake (Korea) is rated 254 MW and makes 552 GWh a year, an average near 63 MW. | Tides come in bursts twice a day, and the turbines are sized for the peak. | [Libretexts, Methods of Harnessing Tidal Power](https://eng.libretexts.org/Bookshelves/Environmental_Engineering_(Sustainability_and_Conservation)/Energy_Alternatives/08%3A_Harnessing_Power_from_Oceans_and_Seas/8.03%3A_Tidal_Power/8.3.03%3A_Methods_of_Harnessing_Tidal_Power) and a Sihwa slide deck; the Sihwa average is a calculation from those figures |
| It needs a large tidal range. La Rance works with a range up to about 8 m. | Energy goes as the square of the range. | Same Libretexts page |
| The planet ceiling still holds: all tidal power on a world stays under 3.7 TW scaled by tidal strength. | Already encoded for the turbines (`Tides.PlanetCeilingW`). | Checked |
| A barrage diverts dissipation the tide would lose anyway, so it adds no waste heat. | Same rule as free-flow tidal. | Checked in code (`WasteHeat.cs`) |

## Scale problem the director should know about

A tile in the game is huge. On a frequency-12 planet one tile is hundreds of thousands of km², and even one of its 61 slots is thousands of km². A real barrage basin is tens of km² (La Rance's is about 22 km²). So the building cannot be sized from the tile area. The proposal below uses a fixed **basin share per barrage** (a game rule) and only uses the tile's tidal range, which the simulation already has per coast tile.

## What the player sees

1. **Where.** A barrage needs a coast tile with land around it (a bay), where the funnelled tidal range is at least a threshold (TBD, about 4 m, which is La Rance and above). The tile panel already shows the range after the tide gauge, so the player can find these places.
2. **The building.** One building, **Tidal barrage**, covering several slots in a row across the water (TBD, 3 slots), on shallow-sea slots next to land. Built by boat-hull robots, like the other sea buildings.
3. **Output.** Rated power from a game-rule basin (TBD, 22 km², La Rance-like) and the tile's range: energy per tide = ½ ρ g A h² times a plant share (TBD, 0.3), turned into power over the tide cycle. At 5 m range that is about 160 GWh a year per barrage (an average near 19 MW), and at La Rance's 8 m about 420 GWh (near 48 MW, against La Rance's real average of about 57 MW, which also generates on the incoming tide). The peak is several times the average. Output follows the tide cycle already in the simulation: peaks after high and low tide, near zero at mid-tide (two-way generation would be a later option).
4. **Cost and time.** Much more than a turbine: a great deal of rock and iron, and a long robot build (TBD, about ten times a turbine). A new bill with no new material, because the game has no cement.
5. **Unlock.** After the tide gauge, and gated by a milestone level (TBD) so it arrives late, like the larger solar tiers.
6. **Research.** A **basin survey** experiment at the research station (needs the tide gauge) that reports a bay's real basin and range. Without it the player only sees the tile's range.

## Effects on the coast (the part that needs a decision)

| Effect | Real world | Proposed rule (TBD) |
| --- | --- | --- |
| Boats cannot pass the dam | Barrages block shipping unless there are locks | Robots with a boat hull cannot cross a barrage's slots; a lock is a later add-on |
| The basin's tides weaken and its water changes | A barrage lowers the range inside the basin and changes the ecology | The tile's range drops by a share after it is built, and floating buildings in the basin lose a little output |
| Waste heat | None added | None added |

## Open design choices for the director

1. **Build it at all?** The proposal could stop at free-flow turbines and wave lines. The barrage adds a late big tidal source and the coast effects, at the cost of the scale problem above.
2. **Footprint.** Several slots across a channel (recommended), or one slot with a fixed basin.
3. **Do the coast effects stay?** Recommendation: yes, a small range drop and a boat block, so a barrage is a real trade.
4. **Two-way generation.** One-way first (simple); two-way as a research upgrade later.
5. **Gate.** Which milestone level it unlocks at.

## Build plan if approved

1. Source the energy-per-tide figures for a real plant (La Rance basin area and head) and check the formula and the 0.3 plant share against them.
2. Simulation: basin-share rule, barrage building, output from the tide cycle, save and load, tests first.
3. Coast effects: range drop and boat block, with tests.
4. Basin survey experiment, tile panel numbers, build button and guide page.
5. Test in Play mode on an ocean world with a strong moon.

Estimated size: about the size of the wave converter plus the coast effects.
