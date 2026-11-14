# NASA data in Kardashev Ascent

Every NASA dataset the game uses, what it feeds, and how to rebuild it. Values from these sources are real; the
rest of the game's numbers are marked TBD or game rule in the code.

| NASA source | What the game takes | Where it shows up in play | How it gets in |
| --- | --- | --- | --- |
| [NASA Exoplanet Archive](https://exoplanetarchive.ipac.caltech.edu/) (Planetary Systems table) | Real planets and their stars: radius, mass, orbit (semi-major axis, eccentricity, period), star temperature, luminosity, metallicity | The planet chooser; each planet's sunlight, year length, gravity and chemistry | `tools/exoplanets/fetch_exoplanets.py` → `Assets/_Project/Data/Planets` (provenance tag on every value: NASA, calculated or assumed) |
| NASA Exoplanet Archive: the planets | Viridia (made up) plus ten from the archive: Proxima Centauri b, TRAPPIST-1 e and f, Kepler-16 b, Kepler-452 b, TOI-700 d, LHS 1140 b, Kepler-186 f, Kepler-442 b | The planet chooser (L) | `tools/exoplanets/planets.json` lists them by archive name |
| [NASA NSSDC Mars Fact Sheet](https://nssdc.gsfc.nasa.gov/planetary/factsheet/marsfact.html) (updated 2025-05-19) | Mars: semi-major axis, eccentricity, year, solar day, axial tilt, radius, mass, Bond albedo, surface pressure (6.36 mb), mean temperature (−59 °C) | Mars in the planet chooser: its sunlight, seasons, gravity and cold | `tools/exoplanets/planets.json` (`nasaSource`, `nasaFields` tag these values as NASA) |
| [NASA POWER](https://power.larc.nasa.gov/) monthly climatology 2001–2025: ALLSKY_SFC_SW_DWN ÷ CLRSKY_SFC_SW_DWN | Earth's cloud factor (share of clear-sky sunlight that gets through) by latitude and month | Earth-like planets: live panel output, yearly estimates, the "Clouds" value and its puzzle | `tools/nasa_power/fetch_cloud_factor.py` → `Simulation/Climate/CloudClimateTable.cs` |
| NASA POWER monthly climatology 2001–2025: **T2M** (air temperature at 2 m) | Earth's air temperature by latitude and month | Earth-like planets: panel cell temperature (hot cells lose output), robot battery cold losses, the "Air heat" value and its puzzle | `tools/nasa_power/fetch_air_temperature.py` → `Simulation/Climate/AirTemperatureTable.cs` |
| NASA POWER monthly climatology 2001–2025: **ALLSKY_SRF_ALB** (ground reflectance) | Earth's ground reflectance by latitude and month, used as the season's change (snow in winter) on top of each terrain's own | Earth-like planets: light the ground throws onto tilted panels, live and in the sky view | `tools/nasa_power/fetch_ground_and_haze.py` → `Simulation/Climate/GroundAndHazeTable.cs` |
| NASA POWER monthly climatology 2001–2025: **AOD_55** (aerosol optical depth at 550 nm, MERRA-2) | Earth's aerosol haze by latitude and month, turned into whole-spectrum haze (700 nm key wavelength, Molineaux et al. 1998; Ångström α = 1.3) and used as the change from Earth's mean | Earth-like planets: direct and sky light reaching panels by place and season, yearly estimates, the "Air haze" and "Sky glow" values and their puzzles | same script |
| NASA POWER (same climatology) | Reference for the clear-sky sunlight model | Checked against the game's solar model (with pvlib) | `docs/physics/physics-verification.md` |
| NASA MMRTG fact sheet | 110 W at the start, 72 W after 17 years | The lander's radioisotope generators | `Simulation/Energy/Rtg.cs` |
| JPL Mars 2020 (Perseverance) press kit | Two 43 Ah Li-ion batteries at 28 V = 2.4 kWh | Every robot's battery | `Simulation/Robots/RobotSpecs.cs` |

POWER rows are the mean of 8 longitudes per latitude (every 45°), so they mix land and ocean like Earth does.
Rebuild without network: `python tools/nasa_power/fetch_air_temperature.py --offline` (and the same for clouds).
