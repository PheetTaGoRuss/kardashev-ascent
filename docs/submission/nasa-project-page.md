# NASA Space Apps 2026: project page text

Copy each section into the matching field on the team's **Project** tab
(spaceappschallenge.org → profile icon → My Team → Project → Edit).

**Deadline: Sunday 15 November 2026, 23:59 Bangkok time.** Press **Save Progress** often. To finish:

1. Tick **Terms and Conditions**.
2. Tick **Confirm All Work is Original**.
3. Press **Submit for Judging**.

Fill in everything in `[square brackets]` before you paste.

---

## Challenge

**Space Mission Design Game**

## Project Name

**Kardashev Ascent**

## High-Level Project Summary

Kardashev Ascent is a planetary engineering game. You run a robotic mission on a real exoplanet taken from the NASA
Exoplanet Archive, or on Mars, and grow it from one lander into a civilization measured on the Kardashev scale.

Each decision is a real mission trade-off:
- **Power and placement:** where to land and how to tilt each solar panel.
- **Materials:** what to mine and how to refine it, at a real energy cost.
- **Measurement:** what to measure before trusting your designs.
- **Robots and charging:** how to keep the robots charged and in range.

Underneath, the simulation uses real physics:
- **Starlight:** the solar position and irradiance models are checked against pvlib.
- **Earth's climate:** NASA POWER data for clouds, temperature, ground reflectance and haze.
- **Planets:** real orbits, gravity and air from NASA.
- **Materials:** chemistry with mass balance for refining.

The challenge asks for a game where you design, manage and simulate a mission, live with limited resources, and see
how each choice shapes the outcome. Kardashev Ascent turns power, mass, time, communications and risk into choices
the player makes and then feels. It is important because students rarely get to experience a whole mission as one
connected system. Here they learn by doing, with numbers that are real.

## Link to Project Demo

`[link to the 7-slide demo PDF: docs/submission/demo-slides.pdf on GitHub, or a public Google Drive link]`

## Link to Final Project

`[https://github.com/PheetTaGoRuss/kardashev-ascent]` (the repository must be made **public** before submitting.
Add the link to a playable Windows build if you publish one as a GitHub Release.)

## Detailed Project Description

**What it does.** The player leads a robotic expedition to another world: the nine real exoplanets in the chooser (NASA Exoplanet
Archive: Proxima Centauri b, TRAPPIST-1 e and f, Kepler-452 b, TOI-700 d, LHS 1140 b, Kepler-186 f, Kepler-442 b,
Kepler-16 b), Mars, or a made-up Earth-like world. The mission runs in these stages:

1. **Mission briefing from orbit.** Before landing the player sees rough values with error bars: temperature, gravity, air pressure, and whether
   robots can survive. A landing site has to be chosen on flat, safe ground.
2. **Power design.** Solar panels are placed on hex tiles. Their tilt and facing decide their yield, which comes from a real solar
   model: the star's position through the year, direct, sky and ground-reflected light, cloud, haze, and cell
   temperature. The panel planner shows the yearly energy, capacity factor, energy payback and EROI. Batteries have to
   carry the colony through the night, or there is a brownout.
3. **Measure before you trust.** The colony starts with uncertain planet values, for example latitude, haze, cloud and
   ground reflectance. A Research Station and its robots measure them through hands-on puzzles, such as finding the
   latitude from a noon shadow. Experiments (air pressure, boiling point of water, surface temperature, gravity with a
   pendulum, a seismic survey) unlock buildings and remove safety margins. This is how real missions reduce
   uncertainty.
4. **Robots and logistics.** Autonomous robots work with limited batteries. They recharge only at chargers, drive
   real routes around mountains and canyons, and stay in contact through a relay network. Crews share work, and
   specialist robots are faster.
5. **Mass, materials and industry.**
   - **Mining.** Robots survey and mine finite deposits, placed by geology rules. For example, copper forms near old
     seas and lithium in dry salt basins.
   - **Refining.** Refining follows real chemistry, step by step: alumina from anorthosite, then Hall–Héroult
     electrolysis at about 13.5 kWh per kg of aluminium; sodium from salt in a Downs cell. Mass is kept at every step.
   - **Dry planets.** On planets without copper or lithium ore, aluminium wiring and sodium-ion cells stand in, at
     real conductivity and charge ratios.
6. **Progress.** Milestones on the Kardashev scale measure how much power the colony can sustain.

Every system feeds the others: power limits refining and charging; charging limits robot range; terrain limits
routes; measurements limit how close to the edge you can design. So no choice is "correct" in isolation, which
is the lesson of mission design.

**How it works.** The game is built in Unity 6 (C#). The simulation is a separate pure C# layer with no engine code,
and the Unity layer only displays it. Every physics formula has a unit test: about 950 automated tests, also run on
every change by GitHub Actions. The solar model was prototyped in Python and checked against pvlib. NASA data comes in
through Python tools: NASA POWER climatology tables and the Exoplanet Archive planet records. A guide book (J) explains
every material, ground type, building and robot from the game's own rules, and "i" buttons and hover cards explain
each number.

**Benefits.** Students meet power, mass, time, communications and risk as linked constraints instead of separate
textbook topics. Mistakes are safe and show why they happened: a brownout, a stranded robot, a batch paused for lack
of power.

**What we hope to achieve.**
- A classroom-ready mission sandbox, with team play for up to four people (each with a role: power, robots, science,
  industry).
- Mission reports and scenario challenges.
- More energy sources and orbital engineering later on.

**Tools.**
- **Game:** Unity 6000.6.8f1 (C#, UI Toolkit).
- **Data tools:** Python (NASA POWER and Exoplanet Archive fetchers, a solar prototype checked with pvlib).
- **Testing and CI:** NUnit and .NET test runner, GitHub and GitHub Actions.
- **Sound:** Kenney sound packs (CC0).
- **Development assistant:** an AI coding assistant (see the SI section).

[Optional, recommended for honesty: "Development began on 5 November 2026 as a prototype; during the hackathon
(14–15 November) we added … ." Ask your Local Lead or the Space Apps team whether work from before the event is
allowed (your GitHub issue #14), and state it clearly here.]

## Use of Super Intelligence (SI)

Yes. We used an AI coding assistant throughout development:
- **Code and tests:** it wrote and refactored most of the C# simulation and UI code and its unit tests, following our
  design direction.
- **Science and documentation:** it helped research scientific values and sources (each value is cited or marked TBD
  in the code), and drafted documentation and this submission text.

The game idea, design decisions and direction came from our team: the game director approved every feature.
- **Labelling:** AI assistance is disclosed here in the submission form; `docs/` records the team's decisions.
- **Images:** no AI-generated images, video or audio are in the game. Screenshots are real gameplay. Sounds are CC0
  packs or made in code.
- **Slides:** the demo slides were drafted with AI assistance and say so on the title slide.
- **No NASA branding:** none of the generated content uses NASA logos or branding.

## NASA Data

| NASA source | How the game uses it |
| --- | --- |
| **NASA Exoplanet Archive** (Planetary Systems table) | Nine real exoplanets and their stars: radius, mass, orbit, period, star temperature, luminosity and metallicity. These set each world's sunlight, year, gravity and chemistry. |
| **NASA NSSDC Mars Fact Sheet** | Mars: orbit, day, tilt, radius, mass, albedo, surface pressure (6.36 mb) and mean temperature. |
| **NASA POWER**, monthly climatology 2001–2025 | Cloud factor (ALLSKY / CLRSKY surface shortwave), air temperature at 2 m (T2M), surface albedo (ALLSKY_SRF_ALB) and aerosol optical depth at 550 nm (AOD_55, MERRA-2), by latitude and month. These drive live solar-panel output, battery cold losses and the measurement puzzles on Earth-like worlds. |
| **NASA MMRTG fact sheet** | The lander's radioisotope generator: 110 W at start, 72 W after 17 years. |
| **JPL Mars 2020 (Perseverance) press kit** | The robot battery: two 43 Ah Li-ion batteries at 28 V, 2.4 kWh. |

Details and rebuild scripts are in `docs/nasa-data.md` in the repository.

## Space Agency Partner Data and Other References

**Software and data:**
- **pvlib-python:** reference for checking the solar model.
- **Unity 6** (Unity Technologies): game engine. **NUnit:** tests.
- **Kenney:** Interface Sounds and Sci-Fi Sounds packs (CC0). See `docs/credits.md`.

**Science sources** (cited in code comments and `docs/physics/physics-verification.md`):
- **Solar and water:** solar position and irradiance models (checked with pvlib); the Antoine equation for water
  (Dortmund Data Bank); IAPWS steam tables; ISO 2533 standard atmosphere.
- **Materials and cells:** IACS copper conductivity standard; IUPAC molar masses; LFP and sodium-ion cell data.
- **Refining:**
  - aluminium smelting energy (International Aluminium Institute)
  - Hall–Héroult and Bayer process figures
  - Downs-cell sodium production
- **Geology:**
  - upper-crust composition (Rudnick & Gao 2003)
  - Martian chloride deposits (Osterloo et al. 2008)
  - rare-earth geology: carbonatites in rifts, Mountain Pass and Bayan Obo

**Partner data:** none used directly. [If you add any partner data, list it here.]
