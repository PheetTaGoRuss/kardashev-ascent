# Tidal and wave energy

Status: **PROPOSED**. The game director chose tidal and wave energy as the next energy source after solar (open question OQ-11, answered 2026-11-18). Nothing here is built, and every number marked TBD is a placeholder for tuning, not a claim.

## What it must do in the game

A coastal colony gets a second source that does not need sunlight, so it keeps the lights on at night and on the dark side of a tidally locked world. It is slower and more place-bound than solar: the player has to find the right coast, survey it, and build with robots that can work on the shore and on water (the boat hull module already exists).

## The physics the game keeps

| Rule | Why | Source status |
| --- | --- | --- |
| Tides need an ocean **and** something to raise them: a moon, or a close star on an eccentric orbit. A tidally locked planet on a circular orbit has no tides from its star. | Equilibrium tide height scales with M_moon · R⁴ / (M_planet · d³). | `docs/physics/notes/energy_sources.md` section 6 (checked) |
| The whole planet has a hard ceiling. Earth dissipates 3.7 TW in tides, about 95 % of it in the oceans. The game caps total tidal power at 3.7 TW × (the planet's tidal strength relative to Earth). | Physics, not balance. | Egbert and Ray 2000 (checked) |
| Barrage energy per tide: E = ½ ρ g A h², with basin area A and tidal range h. | Standard formula. | Noted in the energy notes, **not yet re-verified** |
| Wave power per metre of wave front in deep water: P = ρ g² H² T / (64 π), with wave height H and period T. | Standard textbook result. | **Needs a source before it goes in code** (the notes have none for waves) |
| Tidal power diverts heat the tide would dissipate anyway, so it adds no waste heat. Wave power moves wind energy the sea already holds. | Already encoded in `Climate/WasteHeat.cs` for tidal. | Checked in code |

## What the player sees

1. **A moon in the planet data.** Each world record gets an optional moon (mass, distance). Worlds without one have no tides, and the planet card says so. This is new data and needs a save and data version step.
2. **Tide range per coast tile**, shown on the tile panel after a survey: tidal range in metres and mean wave height. A bay or estuary tile can amplify the range (a rule, TBD).
3. **Two buildings**, both on coast or shallow-sea tiles, both built by robots with the Boat module on water tiles:
   - **Tidal turbine array**: output follows the tide cycle, strongest at spring tides (moon and star aligned). Predictable, so the battery plan can use it.
   - **Wave converter line**: output follows the sea state, which varies with weather and season. Less predictable.
4. **A research step.** A tide gauge and a wave buoy are research-station experiments (like the existing ones). Without them the tile panel shows a wide range, and with them a precise number, the same way the solar measurements work.
5. **Day and night chart in the Power window**: the tidal curve (a smooth wave with a period set by the moon) next to solar, so the player can see the two fill each other's gaps.

## Open design choices for the director

1. **Moon source.** Real moons from the NASA data (few exoplanets have a known moon), or a generated moon per world flagged as an assumption? Recommendation: generate, and label it clearly.
2. **Barrage or free-flow?** A barrage (dam across a bay) gives more energy but is a big build and changes the coast. Free-flow turbines are small and incremental. Recommendation: free-flow first, barrage later.
3. **Does the sea matter for waves?** Wave height depends on wind and fetch. Recommendation: derive it from the existing wind field if there is one, otherwise a simple per-tile sea-state number marked TBD.
4. **Cap per planet.** Use 3.7 TW scaled by tidal strength, as the energy notes already suggest. No change unless you want a tighter game cap.

## Build plan if approved

1. Source and verify the wave formula and a tide-range model; write the unit tests first (physics rule).
2. Moon data and tidal range per tile (simulation only, with tests, save version bump).
3. Tidal and wave buildings through the existing build path (materials bill, robot hours, boat hull for water tiles).
4. Research experiments (tide gauge, wave buoy) and the tile panel numbers.
5. Power window chart and the guide book page.

Estimated size: about the size of floating solar, plus the moon data step.
