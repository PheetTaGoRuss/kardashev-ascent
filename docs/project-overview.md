# Kardashev Ascent: complete project overview

*Written 15 November 2026 for a teammate who will summarize it again with AI. It covers what the game is, how it
plays, every system and its rules, the science and data behind it, how it is built, the team's rules, status, known
issues, the roadmap and the NASA Space Apps submission. Where a number is a game rule or a placeholder, it says so.*

---

## 1. In one paragraph

**Kardashev Ascent** is a scientifically grounded planetary-engineering game built in Unity (C#) by a team of three for
the **NASA Space Apps Challenge 2026** (challenge: **Space Mission Design Game**; Bangkok local event).

The game has three steps:
- **Land:** the player leads a robotic expedition to a real exoplanet from the NASA Exoplanet Archive, Mars, or a
  made-up Earth-like world, and chooses a landing site.
- **Build:** autonomous robots build solar power, batteries, chargers, workshops, research stations and refineries.
  They survey, mine and refine real materials, and measure the planet through hands-on science puzzles.
- **Grow:** the colony grows toward higher levels on the **Kardashev scale**, a measure of how much power a
  civilization uses.

Every system uses real physics where possible: a solar model checked against pvlib, NASA POWER climate data, real
orbits, gravity and air pressure, and refining chemistry with mass balance. Unsourced numbers are marked as game rules
or TBD.

Tagline: *"Run a robotic mission on a real exoplanet, with real physics and NASA data."*

---

## 2. Vision and design pillars (set by the game director, 12 November 2026)

**What the game is:** a simulation of planetary engineering, automation, exploration and civilization building.
Humanity sends robotic expeditions to other planets. They discover resources, build infrastructure and industry, study
what they do not understand, and grow into a civilization spanning planets. The long-term direction is ever larger
energy civilizations: up the Kardashev scale and, possibly, engineering at the scale of a star.

**Draws on:** colony management, factory automation and logistics, space exploration, scientific research and
experiment, managing a robotic workforce, energy infrastructure engineering, simulation of planetary environments, and
interplanetary civilization. It takes ideas from these genres but must have its own identity. It does not copy an
existing game.

**Ten design pillars:**
1. **Scientific authenticity with accessible gameplay.** The numbers are real; no degree is needed to play.
2. **Realistic simulation underneath, simple interaction on top.**
3. **The planet shapes engineering.** Gravity, the star, air, terrain and chemistry change what is worth building and
   where.
4. **Robots are autonomous and controllable.** They work without orders, and the player can always step in.
5. **Matter obeys physics and chemistry.** Nothing is created, destroyed or transformed for free.
6. **Energy is central.** Generating, storing, moving and using energy is the main thread of progress.
7. **Discovery creates opportunity.**
8. **Information arrives as observation.** The player learns through observations, reports and inspections, not
   through an all-knowing display.
9. **Systems are connected.** A mechanic that touches nothing else does not belong.
10. **Progress ends in civilization-scale engineering:** planets, orbits and stars.

**What it must not become:**
- **A spreadsheet:** playing well should not mean reading tables.
- **A copy of another game.**
- **Science fiction presented as science:** speculative content must be labelled.

---

## 3. How a game plays (player's journey)

1. **Main menu:** New game, Continue saved game, Quit.
2. **Choose a planet.** The planet chooser (key L) lists:
   - Viridia, a made-up temperate world
   - nine real exoplanets: Proxima Centauri b, TRAPPIST-1 e, TRAPPIST-1 f, Kepler-16 b (circumbinary), Kepler-452 b,
     TOI-700 d, LHS 1140 b, Kepler-186 f, Kepler-442 b
   - Mars, from the NASA fact sheet

   Each new game gets a new random map of continents and deposits for that planet. The seed is saved.
3. **Orbital brief before landing.** It gives rough values with error bars (game rules, TBD): mean temperature
   (±15 °C), gravity (±10 %), air pressure (±50 %), whether liquid water is possible, and a robot survival verdict:
   - **works:** 0–45 °C
   - **needs heating or cooling to charge:** −40 to 85 °C, from LFP battery and industrial electronics ranges
   - **cannot survive:** outside that

   Time stays stopped until the player lands.
4. **Land.** The player picks a land tile and presses a big **Land here** button. Landing needs flat, open ground:
   lowland, desert, dry basin, beach, forest or tundra. It cannot be sea, wetland, mountain, highland, volcanic rock,
   ice or canyon. The colony cannot move afterwards. Robots appear only after landing.
5. **Setup steps.** While time is stopped, a short tutorial asks for the essential buildings on the landing tile:
   **charging station first**, then solar panel, battery yard, Robot Workshop and Research Station. Skip places them
   automatically. Time starts when they are placed.
6. **Run the colony.**
   - Robots build sites from materials, mine, survey tiles and measure the planet.
   - The player places panels at good angles, keeps the grid out of brownout and expands.
   - The player refines materials, researches and opens mountains and canyons.
7. **Progress.** Milestones every 0.1 of the planet's Kardashev value, measured on a day's average power, unlock bigger
   solar tiers, robot kinds and modules.

Time speeds: pause, real time, 1 hour per second and 1 day per second (the fastest; "1 year per minute" was removed).

---

## 4. The planet

- **Hex-tiled sphere** (`HexSphere`). The player picks tiles; each tile is a building site with slots (61 on a typical
  tile, 4 rings of hex slots).
- **Terrain types:**

  | Group | Types |
  | --- | --- |
  | Water | deep ocean, shallow ocean, frozen sea, wetland |
  | Lowland and dry ground | lowland, desert, dry basin, beach |
  | High ground | highland, mountain, volcanic rock |
  | Cold ground | glacier (ice), tundra |
  | Other | forest, canyon |

  - **Forest, wetland, beach and volcanic rock** come from a second pass, and only on warm wet planets (volcanic rock
    can appear anywhere).
  - **Canyons** are carved as long rifts, like Valles Marineris on Mars.
  - **Rivers** were tried and removed (the director said they looked buggy).
- **Relief:** from orbit, land rises with elevation (exaggerated about 10×) and has cliff walls. Canyons are drawn deep
  below their rim. Mountains show peaks, forests tree crowns, volcanic rock low lava mounds. Close up, a site shows
  trees, reeds and rocks, which buildings clear.
- **Barriers:**
  - Mountains and canyons block driving, building and mining until the colony opens them:

    | Way | Ground | Materials | Robot hours |
    | --- | --- | --- | --- |
    | Tunnel | Mountain | 300 kg iron | 150 |
    | Blast a pass | Mountain | 1.2 t iron + 30 kg copper | 12 |
    | Bridge | Canyon | 1.5 t iron + 0.5 t rock | 60 |

  - A **cave** found by a survey halves a tunnel's cost and time.
  - Tunnels and blasting give the cut rock to the stock.
  - Robots can still reach a barrier's edge to work on it or survey it.
  - Amounts are game rules (TBD).
- **Planet chemistry:**
  - Metals scale with the star's metallicity [Fe/H].
  - Iron follows the planet's iron fraction.
  - "Water history" (whether it once had seas) decides copper and lithium ores.
- **Earth-like climate from NASA POWER:** cloud factor, air temperature, ground reflectance and aerosol haze by latitude
  and month (section 11).

---

## 5. Energy (the central system)

- **Solar model**, from orbit to yearly energy. Verified against **pvlib** and NASA POWER. It covers:
  - the star's position through the year (orbit, eccentricity, axial tilt, day length)
  - direct, sky (diffuse) and ground-reflected light
  - cloud and aerosol haze
  - cell temperature losses
  - the spectral match to the star's temperature
  - binary stars (Kepler-16 b) and tidally locked planets (fixed sun)
- **Solar panels:**
  - Placed in a site's slots, with tilt (β) and facing (γ).
  - New panels start flat (β = 0°) or copy the angles of a panel already on that tile. The game never sets the best
    angle for you.
  - The planner shows energy per year, capacity factor, EROI and energy payback time.
  - Best-angle advice and bonus unlock once the colony has **measured its latitude**. The rule of thumb is tilt ≈
    latitude, facing the equator.
  - Panels can be re-angled by a robot, or edited by dragging rings around a panel (angle editor). Angle layouts can be
    saved with a name, note, slot and latitude/longitude.
  - **Tiers:** a solar field (1 ha), then farm, park and array, unlocked by milestones. Bigger tiers cover more slots
    (1, 1, 3 and 7).
- **Floating solar** on shallow sea or wetland:
  - 1.5× the cost of a land panel (game rule).
  - About 4 % more output from cooler cells (game rule, TBD).
  - Built by a robot with a boat hull.
  - Needs the water experiments, so the colony knows water stays liquid.
- **Light-potential map** (key M): yearly sunlight per tile.
- **Power grid:**
  - One bus with a battery and the lander's **RTG** (radioisotope generator, from the NASA MMRTG fact sheet: 110 W at
    the start, 72 W after 17 years; the colony starts with about 440 W).
  - Brownouts when demand exceeds supply.
  - Battery yards (20 MWh each, LFP cells) store power for the night.
  - Colony demand grows with reliable power (it starts at about 100 kW needed).
- **Kardashev meter:** shows both the absolute K and the planet-relative K.
- **Planned (not built):**
  - more energy sources: nuclear, geothermal, tidal, wave, ocean thermal
  - transmission losses
  - waste heat warming the planet (a model exists but is not in play)
  - thermal management
  - star-scale collection

---

## 6. Construction and materials

- **Buildings cost real materials, not power.**
  - A building's bill is the real material mass of the installation times **1 % supplied on site** (game rule). The rest
    stands for parts the colony can't make yet.
  - Rock stands in for glass, polymer and silicon (game rule).
  - **Solar panel (1 ha):** about 1 150 kg = 1 035 kg rock, 103.5 kg iron, 11.5 kg copper, from a c-Si module
    composition of glass 75 %, polymer 12 %, frame 9 %, silicon 3 %, copper 1 %.
  - **Battery yard (20 MWh):** about 410 kg of LFP cathode, including about 18 kg lithium and about 145 kg iron.
  - **Fixed bills:**

    | Building | Iron | Rock | Copper |
    | --- | --- | --- | --- |
    | Workshop | 600 | 400 | 30 |
    | Charger | 150 | 50 | 20 |
    | Relay | 60 | 30 | 10 |
    | Research Station | 200 | 100 | 25 |
    | Alumina plant | 400 | 200 | 15 |
    | Electrolysis cell | 300 | 150 | 60 |

  - **Lander cargo:** 2.5 t rock, 1.5 t iron, 100 kg copper, 25 kg lithium.
- **Construction takes robot work:**
  - 2 h plus 4 h per square root of a tonne of materials (game rule). A 1 ha field takes about 6 robot-hours.
  - Up to **6 robots** work one site, each extra robot adding 60 % of the first one's pace.
  - Work never runs ahead of the materials delivered.
  - Sites rise from the ground, pulse while being built and pop when finished.
  - A site with materials missing shows red; once they are in stock, yellow.
  - Removing a finished building gives back half its materials; cancelling a site gives back everything put in.
  - The director briefly tried real-world times of days to weeks, then reverted to the formula above.
- **Footprints:**

  | Building | Slots |
  | --- | --- |
  | Workshop | 3 |
  | Battery yard | 2 |
  | Solar panel | 1–7 (by tier) |
  | Charger, relay, Research Station, refinery modules | 1 each |

  Multi-slot buildings are drawn as linked modules.

---

## 7. Robots (the workforce)

- The lander brings **three General robots**.
- **Robot kinds:** General, Construction, Mining, Research and Maintenance. Transport exists but has no work yet.
  Specialists work about 2× faster at their own job. Some unlock at milestones, and secondary roles unlock later.
- **Battery:** 2.4 kWh, from Perseverance's two 43 Ah Li-ion batteries at 28 V. Robots draw power for idling, driving
  and each kind of work.
  - Below 20 % they go to charge, up to 80 %.
  - Long trips charge up to what the trip needs.
  - Cold reduces capacity.
  - Batteries age by depth of discharge.
- **Charging (changed 15 November 2026):**
  - Robots can **only recharge at a charging station or battery yard**. The lander only repairs.
  - A charger on the landing tile is fed by the lander's RTG first, so robots can recover even before panels exist.
  - With no charger anywhere, robots keep working until empty, so the first crew can build one.
- **Wear and repair:** work wears robots. Below 30 % condition they are repaired at the lander. Shutdowns can be from
  energy, mechanical wear or critical damage. Solar flares wear robots.
- **Movement:**
  - Robots drive tile to tile. Rough ground is slower; open water needs a boat hull; closed mountains and canyons can't
    be crossed.
  - Drive power scales with gravity.
  - A robot keeps enough charge to get back to a charger. It can be stranded if empty away from one.
  - The player can send a robot somewhere.
- **Work assignment:**
  - Groups, four operating programs, ranked task order, priorities, reservations and a task monitor.
  - Critical tasks can pull robots off other work.
  - Hand-out picks the best robot for each task by priority, suitability, distance and charge.
- **Colony network:**
  - The lander is the hub; **relays** extend the range, and a brownout takes the network down.
  - Robots out of range keep their findings until they sync.
  - **AI levels:** robots start at level 1. The Processor upgrade gives level 2 and the Advanced processor level 3.
    Higher levels let a cut-off robot keep working sensibly, and cost grid computing power.
- **Upgrades** (made at the Workshop):
  - bigger battery (1.5×)
  - faster drive (needs rare-earth magnets)
  - flare shielding
  - thermal insulation
  - Processor and Advanced processor (the advanced one needs 99.99 % aluminium)
  - boat hull
- **Performance:** a simulation step with 300 robots takes about 2 ms in a release build and about 20 ms in a debug
  build.

---

## 8. Resources: deposits, surveys, mining

- **Mined materials:** rock, iron, water ice, copper, lithium, rare earths, anorthosite and salt.
- **Made materials:** aluminium, sodium, alumina, 99.99 % aluminium and slag.
- **Where they are found** (game rules shaped by real geology):

  | Material | Where | Notes |
  | --- | --- | --- |
  | Rock | Every land tile | Always known |
  | Iron | Every land tile | Grade varies ±50 %, scaled by the planet's iron fraction |
  | Water ice | Glaciers, frozen seas, tundra (30 % as much) | Shores of liquid seas give endless sea water |
  | Copper | Highland, mountain and volcanic rock within 4 tiles of a sea, on a planet that had water (about 35 % of such tiles) | Poorer sulfide copper on high ground anywhere (about 12 %, half grade). The sea floor has copper robots can't reach. |
  | Lithium | Inland low ground (lowland, desert, dry basin) beside high ground, on a planet that had water (about 35 %) | Brines |
  | Rare earths | Canyons (rifts) most often, then mountains and volcanic rock; a cave doubles the chance | Deep ore: hidden until the **seismic survey** experiment |
  | Anorthosite | Highlands and mountains (about 50 %) | About 30 % alumina, the aluminium ore |
  | Salt | Dry basins and deserts on planets that had seas (evaporites, about 50 %) | Every shore of a liquid sea gives endless sea salt |

- **Survey:**
  - A robot surveys a tile (about 2 h). Sometimes (25 %) the result is a puzzle: estimate each material's grade from 5
    rock cores, one material at a time.
  - A rough answer gives a "±" value.
  - The tile panel shows each deposit as an icon with its grade (1 = an ordinary deposit on Earth).
- **Deposits run out:**
  - Each deposit holds its grade × a size per material (game rule): at grade 1, rock 1 000 t, iron 50 t, ice 50 t,
    copper 3 t, lithium 0.5 t, rare earths 0.2 t, anorthosite 200 t, salt 50 t.
  - Deposits in mountains, canyons and volcanic rock hold 3× as much.
  - Mining (50 kg per hour × grade) stops when a deposit is empty, with a report.
  - Sea water and sea salt never run out.
- **Starting on any planet** (dry planets like Mars, Proxima b and Kepler-452 b lack copper and lithium ore):
  - **Aluminium can replace copper** in building bills: 0.494 kg aluminium per kg copper, for the same resistance
    (61 % IACS conductivity; densities 2.70 and 8.96 g/cm³).
  - **Sodium-ion cells can replace lithium:** 3.31 kg sodium per kg lithium, for the same charge (molar masses 22.99 and
    6.94).
  - A waiting site takes whichever metal arrives first. The choice is saved per building, and salvage gives back what it
    was made of.

---

## 9. Industry: the Workshop and real refining

- **Robot Workshop:** builds new robots and upgrades (one job at a time per Workshop, with robot work hours and grid
  energy).
- **Refinery modules** go in slots **touching a Workshop**. Each finished module runs one batch at a time on grid
  power, and batches pause in a brownout. More modules make more at once.
- **Processes.** Chemistry and energy are real; batch sizes and times are game rules. Mass is kept in every process:
  input = output + slag + vented gas.

  | Process | Module | Input → output | Energy | Time per batch | Notes |
  | --- | --- | --- | --- | --- | --- |
  | Alumina from anorthosite | Alumina plant | 157 kg anorthosite → 40 kg alumina | 6 kWh per kg alumina | 3 days | Anorthosite ~30 % Al₂O₃, 85 % recovery; sinter and leach |
  | Alumina from rock | Alumina plant | 336 kg rock → 40 kg alumina | — | 4 days | Basalt ~14 % Al₂O₃ |
  | Aluminium 99.7 % (Hall–Héroult) | Electrolysis cell | 38.6 kg alumina → 20 kg aluminium | 13.5 kWh per kg (270 kWh a batch) | 7 days | 1.93 kg alumina per kg; oxygen vented |
  | Aluminium 99.99 % (three-layer refining) | Electrolysis cell | → 10 kg | 17.5 kWh per kg | 10 days | |
  | Sodium from salt (Downs cell) | Electrolysis cell | 25.4 kg salt → 10 kg sodium | 10 kWh per kg | 4 days | 2.54 kg salt per kg; chlorine vented |
  | Sodium from rock | Electrolysis cell | 225 kg rock → 5 kg sodium | 30 kWh per kg | 10 days | For planets with no salt |

- **Queue and Auto:** batches can be queued, or **Auto** keeps a chosen amount in stock.
- **Manufacturing window (U):**
  - Tabs: Robots, Refining, Queue, Reports.
  - Each recipe is a card with a picture, a big name with an "i", the needs and output as material icons (red = not
    enough), time, energy and power, and the reason it can't run.

---

## 10. Research and knowledge

- **Unknown planet values:** latitude, air haze, sky glow, clouds, ground shine, air heat, dust and star colour. They
  start as estimates with a spread.
- **Research Station (key T):** a robot measures each value there and it takes time. Measurements sometimes come back
  as puzzles using real instruments:
  - **latitude** from a noon shadow: a 1 m stick, h = arctan(stick ÷ shadow), φ = 90° − h + δ
  - or from the celestial pole with a quadrant
  - **declination** from tilt and orbit position: sin δ = sin ε · sin λ

  Each puzzle has numbered steps and a worked example.
- **Safety margins:** until values are known, panels are designed at 1 − the spread of unknowns (at least 50 %), and
  batteries charge only to 80 % until the surface temperature is known.
- **Experiments,** shown as a **research tree:**
  1. **Survey the Research Station's tile:** the root, needed first.
  2. **Air pressure,** then **Water boiling point:** the Antoine equation (Dortmund Data Bank constants), within about
     0.1–0.25 °C of IAPWS steam tables. Below water's triple point (611.657 Pa) liquid water is impossible, as on Mars.
  3. **Surface temperature:** the coldest and warmest months.
  4. **Gravity:** a 1 m pendulum, T = 2π √(L/g), 2.006 s on Earth. Then the **Seismic survey**, which reveals deep ore
     (rare earths).
- **Knowledge gates buildings:** for example, floating solar needs to know water stays liquid.
- **Science calculator (key K):** opens any time. Clicking a number copies it. It keeps a history and `ans`.
- **Guide book (key J):**
  - Pages on every material, ground type, building, robot and research item, built from the game's own rules (tested
    against the deposit generator).
  - Drawn pictures of each material and ground.
  - A page on starting a planet with no copper or lithium.
- **Information style:** short lines with an "i" button for the full explanation, after Oxygen Not Included. Hover
  cards show a description for anything with one, and for buildings on a site.

---

## 11. NASA data and other sources

| NASA source | Used for |
| --- | --- |
| **NASA Exoplanet Archive** (Planetary Systems table) | Nine real planets and their stars: radius, mass, orbit (semi-major axis, eccentricity, period), star temperature, luminosity, metallicity. Each value is tagged NASA, calculated or assumed. Tool: `tools/exoplanets/fetch_exoplanets.py`. |
| **NASA NSSDC Mars Fact Sheet** (updated 2025-05-19) | Mars: orbit, year, day, tilt, radius, mass, Bond albedo, surface pressure 6.36 mb, mean temperature −59 °C |
| **NASA POWER** monthly climatology 2001–2025 | Cloud factor (ALLSKY ÷ CLRSKY surface shortwave), air temperature at 2 m (T2M), ground reflectance (ALLSKY_SRF_ALB), aerosol optical depth (AOD_55, MERRA-2, converted to whole-spectrum haze), by latitude and month, averaged over 8 longitudes. Drives Earth-like panel output, battery cold and the measurement puzzles. Tools in `tools/nasa_power/`. |
| **NASA MMRTG fact sheet** | The lander's RTG power |
| **JPL Mars 2020 (Perseverance) press kit** | The robot battery: 2.4 kWh |

**Other references:**
- **Solar:** pvlib (solar model check).
- **Water and air:** Dortmund Data Bank (Antoine constants), IAPWS (steam tables, triple point), ISO 2533 (standard
  atmosphere), CODATA (G).
- **Materials:** IACS (copper conductivity), IUPAC molar masses, LFP cell datasheets (A123), c-Si module composition
  (Michigan State University study), CATL sodium-ion energy density.
- **Refining:** International Aluminium Institute (smelting energy), Hall–Héroult, Bayer and Downs process figures.
- **Geology:**
  - upper-crust composition (Rudnick & Gao 2003)
  - Martian chlorides (Osterloo et al. 2008)
  - rare-earth geology (carbonatites in rifts; Mountain Pass, Bayan Obo)
- **Sound:** Kenney Interface Sounds and Sci-Fi Sounds packs (CC0). The ambience, alarm and chords are made in code.

All formulas and checks are in `docs/physics/physics-verification.md`.

---

## 12. Interface and controls

- **Look:** inspired by factory games such as Mindustry, but not copied. Square, near-opaque grey panels with an amber
  strip and amber highlights. Panels can be dragged by a grip.
- **On screen:**
  - **HUD:** time and date, power, battery, material chips, Kardashev milestone and K, time controls and Calc.
  - **Tab bar:** Overview (C), Power (O), Robots (G), Manufacturing (U), Research (T), Network (N), Guide (J).
  - **Tile panel:** sunlight, best angle, ground, deposits, survey, mining, and opening barriers.
  - **Build bar:** Power, Stations and Industry tabs, each building with an "i".
- **Controls:**

  | Action | Keys |
  | --- | --- |
  | Turn the camera | Drag, or WASD / arrows |
  | Zoom | Mouse wheel, or Q / E |
  | Back to the colony | R |
  | Flashlight | F |
  | Light-potential map | M |
  | Open a tile's site | Click the tile |
  | Place a building | Drag it from the build bar, or P on a selected slot |
  | Remove a building | Del |
  | Next building | Tab |
  | Best angle | B |
  | Select every slot | Ctrl+A |
  | Sky view over a panel | V |
  | All controls | H |
  | Pause menu (sound, save, load) | Esc |
  | Save / load | F5 / F9 |
  | God mode (debug view) | F10 |
- **Sound:** button clicks, placing, building complete, puzzle right and wrong, discoveries, brownout alarm, milestone
  chord, space ambience. Volume and mute are in the pause menu.

---

## 13. How it is built (technical)

- **Engine:** Unity **6000.6.8f1** only, with the UI Toolkit. Language: C#.
- **Two layers:**
  - **Simulation** (`Assets/_Project/Scripts/Simulation/`) is pure C#. It has no `MonoBehaviour` and no UnityEngine
    references (enforced by `noEngineReferences` and `tools/check_layers.py`), and uses `double`.
  - **Presentation** (`Assets/_Project/Scripts/Presentation/`) handles visuals, camera and UI. It reads the simulation
    only through the `IPlanetSimulation` interface. It never puts game rules in the UI.
  - Simulation subsystems don't reference each other; the orchestrator `PlanetSimulation` (split into partial files)
    passes values between them.
- **Editor scripts:** scene setup (`MainSceneSetup`), planet import, Windows build. Scenes are built in code, never
  edited as text.
- **Saves:** versioned JSON (`SimulationState`, now version 34), with a test for loading older versions. Enums in saves
  can only gain members at the end.
- **Tests:**
  - About **950 automated tests** (NUnit EditMode), also runnable outside Unity with
    `dotnet test tools/simtests/SimTests.csproj`.
  - Python tests for the data tools.
  - GitHub Actions CI runs on every pull request.
  - Every physics formula has a unit test that matches `physics-verification.md`.
- **Prototypes:** `prototypes/solar/` is a Python solar model checked with pvlib.
- **Tools:**
  - `tools/exoplanets` (fetch planets)
  - `tools/nasa_power` (fetch climate tables, works offline)
  - `tools/simtests` (tests outside Unity)
  - `tools/check_layers.py`
  - Git setup scripts
- **Repository:** `github.com/PheetTaGoRuss/kardashev-ascent`. It is private and must be made public for judging.
  - About 86 pull requests, with work starting on 5 November 2026.
  - Docs index: `docs/README.md`.
  - Key docs:
    - `game/game-vision.md`
    - `development/feature-registry.md`
    - `architecture/system-architecture.md`
    - `development/development-workflow.md`
    - `development/roadmap.md`
    - `development/known-issues.md`
    - `nasa-data.md`
    - `physics/physics-verification.md`

---

## 14. Team rules and workflow

- **Team:** 3 people. They use many AI coding-assistant sessions (locally and in the cloud), coordinated through
  GitHub issues, pull requests and `docs/`.
- **Pull requests:** never push to `main`; everything goes through a pull request.
- **Workflow:**
  1. Read `AGENTS.md` and the docs.
  2. Check the architecture and specs.
  3. Read the existing code and tests.
  4. Find what each system reads and writes, and what must be saved.
  5. Propose a short plan; the director approves game changes.
  6. Work only in scope.
  7. Add tests.
  8. Verify the science, with sources first.
  9. Update the docs, especially `feature-registry.md`.
  10. Report the files changed, test results, limits and remaining work.
- **Statuses:**
  - **Design:** CONFIRMED (director approved), PROPOSED, REJECTED or FUTURE. Only the director confirms.
  - **Implementation:** Not started, Designed, In progress, Implemented, Tested or Verified.
- **Numbers:** unsourced numbers are marked TBD; non-physics rules are labelled "game rule".
- **Not allowed:** duplicate systems, silently changing confirmed decisions, unsourced formulas, rewriting working
  systems without a shown problem, Unity in the Simulation layer, game rules in Presentation, or claiming work is done
  before checking it.

---

## 15. Status: what is built (as of 15 November 2026)

**Built and tested:**
- **Planet and map:**
  - planet and terrain generation with canyons and relief
  - real planets
  - landing flow, orbital brief and setup tutorial
- **Energy:**
  - verified solar model, panels with angles, tiers and floating solar
  - angle editor and layouts
  - light map, grid, batteries and RTG
  - Kardashev milestones
- **Construction and robots:**
  - construction from materials with crews
  - the full robot workforce (batteries, charging at chargers only, wear, movement, programs, network, AI levels,
    upgrades, boats)
- **Resources and industry:**
  - surveys with puzzles, finite deposits and mining
  - stand-in metals for dry planets
  - barriers (tunnel, blast, bridge) and caves
  - rare earths
  - the Workshop and real multi-step refining in modules, with Auto
- **Research and knowledge:**
  - Research Station measurements and puzzles, experiments research tree and safety margins
  - science calculator
  - guide book with pictures
- **Interface and saves:**
  - remade Manufacturing window, hover cards, sound
  - save and load (v34)

**Recent director decisions (15 November):**
- Faster build times, then reverted to the original formula.
- The lander no longer charges robots.
- Real refining.
- Canyons and barriers.
- Starting cargo restored to the original.

---

## 16. Known issues and limits

- **Balance:** many numbers are placeholders marked TBD: robot, recipe, deposit, movement and building times.
- **Mass balance:** robots and upgrades still use materials without a full mass balance; refining is balanced.
- **Physics gaps:**
  - Real planets' air temperature has no greenhouse term (a cold bias).
  - The waste-heat model isn't in play.
  - The grid has no distances or losses.
  - Terrain stands in for geology.
- **Interface:**
  - Panels show actual simulation state rather than only observed values (open question OQ-1).
  - Keys are hard-coded.
  - English only.
- **Engineering:**
  - Presentation code has no automated tests.
  - Saves store enums as integers.
  - One save holds one planet.
- **Visuals:** a bridge's deck may not line up with the canyon, and the close-up site view doesn't show the tunnel or
  bridge.
- **Early game:** power is tight until the first panels are up, because the RTG gives only about 440 W.

---

## 17. Roadmap (proposed order; the director sets priorities)

| Phase | Name | What it covers |
| --- | --- | --- |
| 0 | Land open work | Merge feature branches, docs |
| 1 | Structure | Orchestrator split, definitions, role interfaces, CI (partly done) |
| 2 | Material economy | Material definitions, recipes with mass and energy balance, inventories per place, storage, transport robots, industrial chains, recycling. Deposits with depletion are done. |
| 3 | Environment | Per-tile temperature and pressure, equipment reading the environment, waste heat, named grid loads |
| 4 | Information | Observation records, reports, panels showing only known state |
| 5 | Energy breadth | A second energy source (nuclear, geothermal, wind…), transmission losses |
| 6 | Water | Extraction, desalination, storage, electrolysis, cooling |
| 7 | Research and technology | Tech model, samples, unknown substances, an AI scientist assistant, events |
| 8 | Scale | Thousands of robots and buildings, balance pass |
| 9 | Beyond one planet | Several colonies, interplanetary transport |
| 10 | Civilization-scale engineering | Orbits, megastructures, star-scale energy |

**Ideas for the Space Mission Design challenge:**
- team play for up to four, with roles (power, robots, science, industry)
- mission scenarios
- end-of-mission reports
- leaderboards

**Open questions for the director (selection):**
- How much permanent UI should stay (OQ-1)?
- One colony per planet (OQ-2)?
- Is god mode a debug or teaching tool (OQ-4)?
- Do milestones remain the unlocks once research exists (OQ-5)?
- Which buildings need staff (OQ-7)?
- What can destroy a robot (OQ-9)?
- Which energy source comes second (OQ-11)?
- Who sets the balance values (OQ-12)?
- Is the target the competition, a longer project, or both (OQ-14)?

---

## 18. NASA Space Apps 2026 submission

- **Challenge:** Space Mission Design Game. It asks for an interactive game to design, manage and simulate a mission
  with trade-offs in power, mass, budget, communications, data and time.
- **How we answer it:**
  - Power, mass and materials, time, communications (relay network) and risk (temperature, brownouts, stranded robots)
    are linked choices.
  - Measurement reduces uncertainty, as on real missions.
  - Failure explains itself.
- **NASA form (due 15 November 2026, 23:59 Bangkok time):** the text for every field is in
  `docs/submission/nasa-project-page.md`. The **repository must be public**.
- **Demo:** the 7-slide deck `docs/submission/demo-slides.pdf`, regenerated by `make_slides.py` with the team names.
- **Bangkok video (LINE @droneth):**
  - **Due:** Monday 16 November, 18:00. Late videos are accepted until 17 November, 18:00, with a possible deduction.
  - **Format:** 5–7 minutes, in English, every member's face visible, slides shown.
  - **Also send:** 10–20 photos and videos, plus the project name, team name and full member names.
  - **Materials:** the script, photo list, LINE message and checklist are in `docs/submission/video-and-line.md`.
- **To check:** work began on 5 November, before the 14 November event. Ask the Local Lead whether that is allowed, and
  disclose it.
- **AI use:**
  - An AI coding assistant helped write code, tests and docs under the team's direction (disclosed in the submission form).
  - There are no AI-generated images, video or audio in the game.
  - The slides say they were drafted with AI assistance.
