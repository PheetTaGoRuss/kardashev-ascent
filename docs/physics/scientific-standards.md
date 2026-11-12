# Scientific standards

The rules every physical, chemical or geological model in Kardashev Ascent follows. Scientific accuracy is what sets the game apart, so these rules outrank convenience.

Checked results, corrections and test values for the models that exist are in [`physics-verification.md`](physics-verification.md) (Thai) and the research notes in [`notes/`](notes/). This document is the standard; those are the evidence.

## 1. Principles

| Principle | What it requires |
| --- | --- |
| Conservation of mass | A process's outputs, byproducts and waste weigh what its inputs weighed |
| Conservation of energy | Energy in equals energy out plus energy stored plus losses, and the losses go somewhere (usually heat) |
| Thermodynamics | No conversion exceeds its thermodynamic limit; heat flows from hot to cold; waste heat is counted |
| Stoichiometry | Chemical recipes balance by element |
| Material properties | Density, melting point, conductivity and similar values come from references |
| Temperature and pressure limits | A process runs only where its conditions can exist |
| Efficiency and losses | Every conversion has an efficiency below one, with a source |
| Units | Every quantity has a unit; formulas are dimensionally consistent |
| Numerical stability | Results do not depend on frame rate and do not produce NaN or infinity |
| Uncertainty | Where science is unsure, the game says so |

## 2. How detailed a model should be

The simulation does not model molecules. Choose the simplest model that keeps the constraint a player can feel.

- **Simplify, defensibly.** Use an accepted reduced model and name it (Kasten–Young air mass, NOCT cell temperature, Sagan's K).
- **Aggregate.** A thousand identical panels are one area; a solar farm is not a thousand objects.
- **Model what matters.** A variable earns its place by changing a decision.
- **Keep the constraint.** A simplification may drop precision. It may not drop a limit (night, the Carnot limit, a finite deposit).
- **Allow inspection.** The player can ask how a number was produced; the tile panel already shows formulas.
- **Write it down.** Each simplification and assumption is stated beside the model and in `physics-verification.md`.

## 3. Classes of scientific content

Everything the game presents as science belongs to one class, and the class is visible to the player wherever it matters.

| Class | Definition | Examples | In the game |
| --- | --- | --- | --- |
| 1. Established | Measured and reproduced | Photovoltaics, orbital mechanics, Li-ion batteries, RTGs | Presented as fact, with real numbers |
| 2. Theoretical or experimentally difficult | Sound physics, not yet built at scale | Fusion power plants, space-based solar power | Presented as possible, with the open problems named |
| 3. Speculative | Not excluded by known physics, no path known | Megastructures around stars | Presented as speculation |
| 4. Fictional | Invented for the game | Any hypothetical material | Presented as fiction; never as an experimental result |

Rules:

- Never present class 3 or 4 content as class 1.
- A hypothetical material has a written sheet: properties, what it enables, what it cannot do, risks. The sheet is internally consistent and the game holds to it.
- A game rule is not science. Name it as a game rule (the per-planet Type I target is the standing example: the interface shows absolute K beside it).

A second, independent label says where a number came from (measured, derived, assumed, sourced, game rule, placeholder); see [`../architecture/data-architecture.md`](../architecture/data-architecture.md), section 5.

## 4. Units and conventions

| Quantity | Unit in code | Notes |
| --- | --- | --- |
| Time | hours | The one deliberate exception to SI |
| Energy | Wh | With power in W |
| Power, irradiance | W, W/m² | |
| Mass | kg | |
| Length, area | m, m² | |
| Angles | radians | Degrees in data files and on screen |
| Temperature | K for stars and radiative balance, °C for air and equipment | The name carries the unit |
| Fractions, efficiencies | 0–1 | |
| Numbers | `double` | `float` only in Presentation |
| Directions | The planet frame in `Planet/PlanetFrame.cs` | +Y north, longitude 0 toward +Z |

Shared constants live in `Solar/SolarConstants.cs` and match the constants listed at the top of `physics-verification.md`. Do not redefine a constant locally.

## 5. Adding or changing a formula

1. **Source first.** Find a published source. Record the formula, its range of validity, its source and the expected test values in `physics-verification.md` (or a note under `notes/` for background research).
2. **Say what it is.** Label the content class (section 3) and every constant's provenance.
3. **Implement it as a pure function** in a tier 1 namespace, in `double`, with units in the names.
4. **Test it against numbers that did not come from the implementation**: a reference library, a published table, a hand calculation. State the tolerance and why.
5. **Guard the edges.** Zero day length, sun below the horizon, zero capacity, zero pressure. The model returns a defined value at every edge.
6. **Keep prototypes in step.** A formula in `prototypes/solar/solar.py` and its C# port change together, and both test suites run.
7. **Then use it** in a colony system.

An AI developer does not add a formula from memory. If no source can be reached, the value is a placeholder marked `TBD`, the gap is listed in `physics-verification.md`, and the pull request says so.

## 6. Numerical rules

- The simulation advances in sub-steps of at most 0.25 h. A model must give the same totals whether a day is simulated in one call or many.
- Use closed-form solutions where they exist (exponential decay, Kepler's equation by Newton iteration) instead of accumulating small steps.
- Never divide by a quantity that can be zero without a stated result for zero.
- No NaN or infinity leaves a model. Tests include the edge inputs.
- The simulation is deterministic: the same planet, seed and commands give the same state. Randomness comes from a seed stored with the planet; no clock, no unseeded generator.
- Comparisons of physical values in tests use a tolerance, absolute or relative, chosen from the model's accuracy.

## 7. Balances to check

| Balance | Statement | Status |
| --- | --- | --- |
| Electricity, per step | Generation + battery discharge = served demand + robot charging + battery charge + build-part power + curtailed | Holds by construction in `PowerGrid`; no dedicated test |
| Energy over a run | Sum of generation = sum of all uses + change in stored energy + conversion losses | No test |
| Mass in manufacturing | Inputs = product + byproducts + waste | Not modelled: leftover mass disappears |
| Deposits | Mined mass reduces what remains | Not modelled: deposits never run out |
| Planetary heat | Waste heat raises temperature by the T⁴ balance | Model and tests exist; not connected to play |

New systems add their balance to this table with a test.

## 8. Known gaps in the science

Summarised from `physics-verification.md` and from the code as audited on 2026-11-12. Details stay in those sources.

- Sources that could not be reached when the physics report was written, and values it marks as inferred, are listed in its last section.
- Robot battery ageing: the depth-of-discharge exponent and the damage from reaching 0 % are unsourced placeholders.
- Robot power use, wear rates, repair costs, mining rates, recipes, deposit shares and route times are placeholders (`TBD` in code).
- Air temperature for real exoplanets is the equilibrium temperature without a greenhouse term, labelled assumed.
- Terrain stands in for geology: there is no plate tectonics, so ore rules use distance to sea and elevation as game rules.
- Refining recipes cite background figures that the code itself marks as not re-sourced.
