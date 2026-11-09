# Solar measurements: research robots and reasoning puzzles

Requested 2026-11-09. Decisions taken in chat:

- Re-angling a built panel uses sliders with a live gain, and **a robot has to go and turn it** (any change to a built
  panel's setup). Done in stage A below.
- Research robots measure **every parameter that affects a solar panel**, and the game shows **how much each one can
  change the output**.
- The measurement minigame is a **reasoning puzzle**: the player reads real data and works the number out with the
  formula. A close answer gives a precise value; a rough one gives a wide range.

## What a panel's output depends on (the model in `Simulation/Solar`)

P = POA · A · η · [1 + γ (T_cell − 25)] · f_spec · (1 − f_dust), with
POA = f_shade·DNI·cos θ + DHI·(1 + cos β)/2 + ρ_g·GHI·(1 − cos β)/2.

| Parameter | Where it enters | Measured by (puzzle) |
|---|---|---|
| Latitude φ | sun path, so cos θ and the best angle | Noon shadow of a rod: h = atan(H / L), φ = 90° − h + δ |
| Atmosphere optical depth τ | DNI and GHI through air mass | Langley plot: ln I against air mass m, the slope is −τ |
| Diffuse share k | DHI | Shaded and unshaded sensor: DHI / GHI |
| Clouds | cloud factor on the light | Light meter log over days, compared with the clear-sky curve |
| Ground albedo ρ_g | reflected light on tilted panels | Up and down sensors: ρ = reflected / incoming |
| Air temperature T_air | cell temperature T_cell | Weather station day and night readings: daily mean and swing |
| Dust f_dust | light lost on the glass | Clean and dusty reference cells side by side |
| Star spectrum f_spec | how well silicon matches the star | Spectrometer: peak wavelength, then Wien's law for T_star |

How much each one matters is **not written here as fixed numbers**. The Research window works it out for the
selected tile: it runs the panel model with each parameter at the low and high end of its current uncertainty and
shows the change in yearly energy. This keeps the numbers tied to the real model and the real planet.

## How it plays

1. Until a parameter is measured, the game uses an estimate with a range (from the planet data the colony brought).
   Panel previews show a range ("3.8–4.6 GWh a year") instead of one number.
2. In the Research window the player orders a measurement on a tile. A Research robot (General robots at half rate)
   drives there and sets up the instrument for some hours (a new task, like a survey).
3. When the data is in, the puzzle opens: the readings, a sketch of the setup, the formula with blanks. The player
   enters the value. The closer the answer, the narrower the range kept.
4. A measured value narrows previews everywhere it applies (latitude per tile, clouds per region, spectrum once).

The ground survey becomes a puzzle the same way: core samples with depths and grades, and the player estimates the
deposit's grade.

## Stages

- **A. Re-angle built panels (done):** sliders with the live gain in the panel's box; a robot drives out and turns
  the panel (task kind `Adjust`, save version 17). Sites under construction take new angles at once.
- **B. Parameter book (done, except per-panel ranges in the panel box):** every parameter above has an estimate and a range; previews show ranges; the Research window
  lists the parameters with their effect on the selected tile. A `Measure` task for Research robots.
- **C. Latitude puzzle:** the noon shadow, replacing the instant sky-view sighting as the main way.
- **D. Light puzzles:** Langley plot (τ), shaded sensor (k), light log (clouds).
- **E. Ground and heat puzzles:** albedo, air temperature, dust.
- **F. Star spectrum puzzle**, and the ground survey puzzle.

Effect sizes, robot hours and puzzle tolerances are placeholders until tuned in play.
