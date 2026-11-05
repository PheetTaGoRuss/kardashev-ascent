# Solar panel irradiance model: scientific verification (Kardashev Ascent, steps 1-9 + NASA POWER validation)

Method note: many primary-source hosts were unreachable from this session's network (DNS-blocked: power.larc.nasa.gov, pvlib-python.readthedocs.io, ssd.jpl.nasa.gov, ise.fraunhofer.de, iea-pvps.org, ntrs.nasa.gov, arxiv.org, cran). To compensate, pvlib-python 0.16.1 was installed locally and every formula was re-implemented and checked numerically against pvlib (which implements NREL SPA, Kasten-Young, Ineichen-Perez, Bird, isotropic sky). Lines tagged **[pvlib 0.16.1 computation]** are this researcher's own numerical results (scripts: `/tmp/claude-0/-home-user/cdb85ff7-7afe-567a-bada-8d8de6c7b447/scratchpad/ref.py`, `game.py`, `integ.py`, scratchpad is session-temporary). They are reproducible by anyone with `pip install pvlib`. Literature numbers come from search-result abstracts and indexes, not from full texts.

## Q1. Orbit and top-of-atmosphere flux: G0 = L/(4πr²), r = a(1−e²)/(1+e cos ν), Kepler via Newton

### Takeaway
**CORRECT.** Standard two-body result. Two implementation details matter: (a) get ν from E with the half-angle atan2 form so the quadrant is always right, and (b) start Newton at E0 = M (or E0 = π when e > 0.8) so it converges at high eccentricity. With IAU nominal L☉ = 3.828e26 W and 1 AU, the formula gives 1361.17 W/m², which matches the IAU nominal TSI of 1361 W/m².

### Cited Findings
- IAU 2015 Resolution B3 adopts a nominal TSI of 1361 W/m², described as the solar-cycle-23 average (±1 W/m²). These nominal values are conversion constants, not best estimates. Kopp & Lean (2011) give 1360.8 ± 0.5 W/m² at the 2008 solar minimum, with about 0.1 % change from minimum to maximum. The older canonical value was 1365.4 W/m². — [IAU 2015 B3 (arXiv 1510.07674 mirror)](https://www.arxiv-vanity.com/papers/1510.07674/); [Kopp & Lean 2011, NASA ESPO record](https://espo.nasa.gov/node/30086)
- L☉/(4π·AU²) with L☉ = 3.828e26 W and AU = 1.495978707e11 m equals **1361.17 W/m²** [pvlib 0.16.1 computation].
- Earth with S = 1361 and e = 0.0167 gives G0 = **1407.6 W/m² at perihelion** and **1316.7 W/m² at aphelion** (±3.4 %) [pvlib 0.16.1 computation]. pvlib's `get_extra_radiation` gives the same peak-to-trough range (its annual TOA maximum was about 1410 W/m²).
- Mars with S = 1361, a = 1.52371 AU and e = 0.0934 gives a mean G0 of 586.2 W/m², 713.2 W/m² at perihelion and 490.3 W/m² at aphelion [own arithmetic; a and e are standard JPL values, but the JPL page was not reachable].
- Kepler solver test values (Newton, |ΔE| < 1e-14, ν = 2·atan2(√(1+e)·sin(E/2), √(1−e)·cos(E/2))) [pvlib-independent numpy computation]:

| M (rad) | e | E (rad) | ν (deg) | r/a |
|---|---|---|---|---|
| 0.5235988 (30°) | 0.0167 | 0.5320710051 | 30.974396 | 0.98560864 |
| 0.5235988 (30°) | 0.5 | 0.9220066053 | 81.411338 | 0.69788876 |
| 1.5707963 (90°) | 0.9 | 2.2634151064 | 167.700306 | 1.57469925 |
| 1.0 | 0.2 | 1.1853242039 | 79.029260 | 0.92480067 |

### Inferences
- For exoplanets, use G0 = L★/(4πr²) directly from the archive's stellar luminosity. Do not scale 1361 by (1/r)² unless L = 1 L☉.
- Treating the star as a point source is fine for planets at a/R★ ≫ 1. For ultra-short-period planets, the finite stellar disk and limb darkening break this assumption, but that is out of scope.

### Gaps
- The JPL "Approximate Positions of the Planets" table could not be fetched (DNS-blocked), so its element values are not quoted from the source.

## Q2. Declination sin δ = sin ε sin λ with λ = ν + λ_p; hour angle h = 2πt/D − π; solar vs sidereal day

### Takeaway
**CORRECT for Earth with λ_p ≈ 283° (282.94°), provided λ is the Sun's geocentric (apparent) ecliptic longitude**, which is the correct input to sin δ = sin ε sin λ. The value 102.94° is Earth's *heliocentric* longitude of perihelion, exactly 180° away. Using 103° in this formula gives errors up to about 47° in declination. Validated against NREL SPA: with λ_p = 282.94°, a fixed ε = 23.44° and e = 0.0167, the maximum declination error over 2023 is **0.43°** (RMS 0.31°). The hour-angle formula is **CORRECT** if t is local *apparent solar* time and D is the *solar* day, not the sidereal day.

### Cited Findings
- Earth's longitude of perihelion is about 102.9° (102.947°), and perihelion falls around January 3. — [Wikipedia: Earth's orbit](https://en.wikipedia.org/wiki/Orbit_of_Earth); [Wikipedia: Longitude of periapsis](https://en.wikipedia.org/wiki/Longitude_of_periapsis)
- In the CESM orbital code, the "longitude of perihelion" is about 283°, which is 180° different from 103°. The 180° is added because the calculation is geocentric (the Sun is treated as orbiting Earth). — [CESM forum: orb_mvelp](https://bb.cgd.ucar.edu/cesm/threads/orb_mvelp.3775/)
- Numerical check [pvlib 0.16.1 computation, SPA apparent sun longitude and geocentric declination as truth, daily over 2023, perihelion 2023-01-04 16:17 UTC]:
  - λ_p = 282.94°: max |δ error| **0.430°**, RMS 0.310°.
  - λ_p = 102.94°: max |δ error| **46.9°**, RMS 32.9°, which is wrong.
  - For comparison, Cooper (1969) δ = 23.45° sin(360(284+n)/365) has a max error of 1.08°, and Spencer (1971) has a max error of 0.33°.
- Solar day from sidereal rotation P_rot and orbital period P_orb: 1/D_sol = 1/P_rot − 1/P_orb for prograde spin, and 1/D_sol = 1/P_rot + 1/P_orb for retrograde spin [own arithmetic]. Test values:
  - Earth: 86164.0905 s sidereal gives **86400.0 s**.
  - Mars: 88642.66 s sidereal gives **88775.2 s** (1 sol).
  - Venus: 243.0226 d retrograde with a 224.701 d orbit gives **116.75 d**.
  - Tidally locked planet: P_rot = P_orb, so D_sol → ∞ and h is fixed by longitude (permanent day side).

### Inferences
- **Correction or clarification for the design doc:** write "λ_p = longitude of the *Sun* (as seen from the planet) at periapsis, measured from the planet's vernal equinox = ϖ_helio + 180°". For exoplanets this value is a free "season phase" game parameter. The exoplanet archive's argument of periastron ω is measured in the sky plane and has no relation to the planet's equinox, so it must not be used here.
- The formula assumes ε is the planet's obliquity relative to its own orbital plane. For ε > 90° (retrograde spin, e.g. Venus 177°), the same formula still works, but the spin sense must also flip in the solar-day formula.
- Using a fixed-length solar day ignores the equation of time (up to ±16 min on Earth; pvlib returns EoT = 14.64 min for 17 Oct). This only shifts when solar noon happens and has a negligible effect on daily or annual energy, so it is an **ACCEPTABLE SIMPLIFICATION**. On highly eccentric worlds, the true-solar-day length changes noticeably around the orbit. Keeping t as *apparent* solar time (h computed from the Sun's actual direction) avoids drift.
- The residual 0.43° error mostly comes from the fixed ε and λ_p and ignoring nutation and aberration. This is negligible for energy: δ error × sin terms give much less than 1 % irradiance change.

### Gaps
- No authoritative almanac page explicitly labeling 282.94° as "Sun's geocentric longitude at perihelion" was fetched. Correctness rests on the 180° relationship plus the numerical SPA validation above, which is strong evidence.

## Q3. Sun unit vector in ENU: s = (−cos δ sin h, cos φ sin δ − sin φ cos δ cos h, sin φ sin δ + cos φ cos δ cos h)

### Takeaway
**CORRECT** (East, North, Up; h < 0 in the morning, so the east component is positive). Zenith and azimuth match pvlib's analytical solar zenith and azimuth to better than 1e-4°. "Up ≤ 0 means night" is geometrically right. Refraction lifts the apparent Sun by about 0.5° at the horizon, so the true terminator differs slightly (negligible energy).

### Cited Findings
- Test vectors [pvlib 0.16.1 computation, φ = 13.75°] (az = atan2(E, N), clockwise from north):

| δ (°) | h (°) | ENU | zenith (°) | azimuth (°) |
|---|---|---|---|---|
| 10 | −45 | (0.696364, 0.003156, 0.717682) | 44.1366 | 89.7403 |
| −20 | 30 | (−0.469846, −0.525647, 0.709183) | 44.8316 | 221.7918 |
| 23.44 | 0 | (0, 0.168317, 0.985733) | 9.6900 | 0.0000 (Sun north of zenith) |
| 0 | 90 | (−1, 0, 0) | 90.0000 | 270.0000 |

- pvlib's `solar_azimuth_analytical` returned 180° for the δ = 23.44°, h = 0 case, although the Sun is north of zenith there (the correct value is 0°, as the ENU vector shows). Do not use that function as a unit-test oracle at solar noon when δ > φ [pvlib 0.16.1 computation].

### Inferences
- The vector form is better than angle-based formulas because it has no azimuth quadrant ambiguity. Keep it.

### Gaps
- None.

## Q4. Panel normal n = (sin β sin γ, sin β cos γ, cos β), γ clockwise from north; cos θ = max(0, n·s)

### Takeaway
**CORRECT.** Identical to pvlib `irradiance.aoi_projection`. Clamping at 0 is correct for single-sided panels.

### Cited Findings
- Test values, n·s compared with pvlib `aoi_projection`, all identical to 6 decimals [pvlib 0.16.1 computation]:
  - tilt 30°, γ 180°, Sun zenith 40° and azimuth 150°: **0.941749**
  - tilt 13.75°, γ 180°, Sun (20°, 200°): **0.989154**
  - tilt 90°, γ 90°, Sun (60°, 100°): **0.852869**

### Inferences
- Optional realism step: the incidence-angle modifier (glass reflection loss above about 60° AOI, e.g. the ASHRAE or Martin-Ruiz models in pvlib) typically costs about 2–4 % annually. Omitting it is an acceptable simplification.
- For bifacial panels, do not clamp. Use rear irradiance with a bifaciality factor of about 0.7.

### Gaps
- No sourced number for annual IAM loss was retrieved; the 2–4 % above is the researcher's estimate.

## Q5. Kasten–Young air mass and Beer–Lambert DNI with broadband τ; Earth and Mars τ values

### Takeaway
**Air mass: CORRECT.** pvlib's default `kastenyoung1989` is literally `1/(cos z + 0.50572·(6.07995 + (90 − z))^−1.6364)`, the same as 96.07995 − z, and requires *apparent* (refracted) zenith. **Pressure scaling AM·p/p0: CORRECT for Rayleigh scattering**, but only approximate for aerosol and water vapor, which have smaller scale heights. **DNI = G0·e^(−τ·AM) with one broadband τ: ACCEPTABLE SIMPLIFICATION.** Broadband τ is not constant: it falls as AM rises because the beam reddens. Over the range z < 80°, the Ineichen-equivalent τ spans roughly ±25 % around its median at a site. Recommended values are **τ ≈ 0.40–0.45 for humid tropical clear sky (Bangkok/Phuket)**, **≈ 0.3–0.35 for dry high-altitude sites**, and about 0.2 only for exceptionally clean, dry air. Mars dust is typically **τ ≈ 0.3–1**, and global storms reach **τ > 5**.

### Cited Findings
- pvlib `get_relative_airmass` source code: `am = 1.0/(np.cos(zenith_rad) + 0.50572*((6.07995 + (90 - z)) ** -1.6364))`. The 'kastenyoung1989' model is the default and "requires apparent sun zenith". Output is relative (not pressure-adjusted) air mass at sea level, and pvlib returns NaN for z > 90. — [pvlib atmosphere.py source](https://github.com/pvlib/pvlib-python/blob/main/pvlib/atmosphere.py) (read from installed v0.16.1)
- Kasten–Young AM test table [pvlib 0.16.1 computation, game formula identical to pvlib]:

| z (°) | KY 1989 | sec z |
|---|---|---|
| 0 | 0.9997 | 1.0000 |
| 30 | 1.1540 | 1.1547 |
| 48.19 | 1.4980 | 1.5000 |
| 60 | 1.9943 | 2.0000 |
| 70 | 2.9031 | 2.9238 |
| 80 | 5.5860 | 5.7588 |
| 85 | 10.3058 | 11.4737 |
| 88 | 19.4332 | 28.6537 |
| 90 | 37.9196 | ∞ |

- Effective broadband τ ≡ −ln(DNI/G0)/(AM·p/p0), back-computed from pvlib's Ineichen–Perez clear-sky model with the climatological (SoDa) Linke turbidity, 10-min steps over 2023, z < 80° [pvlib 0.16.1 computation]:
  - **Bangkok**: median 0.446 (p10 0.344, p90 0.544); 0.441 at AM1.5
  - **Phuket**: median 0.422 (p10 0.343, p90 0.494)
  - **Golden, CO (1830 m)**: median 0.326 (p10 0.216, p90 0.425)
  - Bangkok monthly Linke turbidity is 3.6 (Jan) to 5.2 (Aug), and Phuket's is 3.7–4.7.
- pvlib's Bird model with AOD500 = 0.1, precipitable water 1.42 cm, O3 0.34 cm, z = 48.19° and G0 = 1361 gives DNI 878.4, GHI 684.0, DHI 98.4 W/m². That corresponds to an effective τ of 0.292 [pvlib 0.16.1 computation; usable as a regression test value].
- Mars: "for both rovers, typical values of atmospheric opacity, or tau, hover between 0 and 1" — [Planetary Society, Aug 2007](https://planetary.org/articles/1113). The 2007 global storm drove opacity to "never-before-seen values above 5" at Spirit and Opportunity — [Planetary Society tau graphic](https://planetary.org/space-images/atmospheric-opacity-tau-for-spirit-and-opportunity-during-the-2007-dust-storm). A NASA MER update reported Opportunity τ falling from 3.2 to 3.0 between sols 1288 and 1291 (Aug 2007) — [SpaceRef/NASA MER status](https://spaceref.com/?p=112009).
- Lemmon et al. 2015 (Icarus 251:96–111) is the authoritative MER record: more than 2200 sols for Spirit and more than 3500 sols for Opportunity, τ from Pancam solar imaging corrected for window dust, covering local, regional and global storms. It reports cirriform clouds at Opportunity near Ls 50° and 115°. Dust mean radius is about 1.5 µm (1.47 ± 0.21 µm Gusev, 1.52 ± 0.18 µm Meridiani). — [U. Arizona record of Lemmon et al. 2015](https://experts.arizona.edu/en/publications/dust-aerosol-clouds-and-the-atmospheric-optical-depth-record-over-3/); companion multi-year gridded climatology: [Montabone et al. 2015, Icarus 251:65](https://ui.adsabs.harvard.edu/abs/2015Icar..251...65M)

### Inferences
- **Recommended fix (optional, better realism):** make τ an explicit function of AM, or use the Linke-turbidity form DNI = G0·exp(−0.8662·T_L·AM·δ_R(AM)) as in ESRA and Ineichen. The game can expose T_L (Earth 2–7) as the planet's "haze" stat and map it to τ_eff ≈ 0.09·T_L near AM1.5. This mapping is inferred from the Bangkok numbers: T_L ≈ 4.5 gives τ ≈ 0.44.
- The pressure scaling is physically justified for Rayleigh scattering only, but it is a reasonable game rule for thin or thick atmospheres in general.
- Clamp z ≤ 90° (AM ≤ 37.92) and return DNI = 0 below the horizon. pvlib returns NaN past 90°, so a naive port can propagate NaN into sums.
- On Mars, AM must use Mars' scale height (about 11 km) relative to its radius (3390 km). The Earth-tuned KY formula overestimates horizon air mass slightly differently, which matters only below about 10° elevation.

### Gaps
- Kasten & Young 1989 (Applied Optics 28:4735) full text not fetched; coefficients confirmed via pvlib source only.
- Exact Lemmon 2015 numbers (baseline seasonal τ per site, wavelength 880 nm) and the 2018 planet-encircling storm peak (τ > 10 at Opportunity in NASA press releases) were not retrievable from accessible sources.

## Q6. POA (isotropic Liu–Jordan) and the game's diffuse model DHI = k·G0·cos z·(1 − e^(−τAM))

### Takeaway
**POA transposition: CORRECT.** It is the isotropic model exactly as in pvlib `irradiance.isotropic` and `get_total_irradiance(model='isotropic')`, plus the ground-reflected term. Isotropic sky slightly underestimates tilted diffuse, because it ignores circumsolar and horizon brightening; Perez/Hay-Davies add a few percent. **The game's diffuse model: physically reasonable and energy-conserving, and k ≈ 0.3 is right for Earth.** Ineichen-derived k has a median of **0.31** in Bangkok and Phuket and **0.38** in Golden, CO, and Bird gives 0.31. **k = 0.5 overestimates Earth clear-sky DHI by about 60 %**, so set the Earth default to k ≈ 0.3. Energy is conserved because GHI = G0 cos z·[T_b + k(1 − T_b)] ≤ G0 cos z for k ≤ 1. Part of what the broadband τ removes is *absorption* (water vapor, O3, aerosol absorption), which never becomes diffuse; that is why k < 0.5.

### Cited Findings
- pvlib implements the isotropic sky-diffuse model `isotropic(surface_tilt, dhi)` with the formulation documented from Loutzenhiser et al. (empirical validation of transposition models). — [pvlib irradiance.py source](https://github.com/pvlib/pvlib-python/blob/main/pvlib/irradiance.py) (read from installed v0.16.1)
- Implied k = DHI/(G0·cos z·(1 − DNI/G0)) from Ineichen–Perez clear sky, z < 80°, 2023 [pvlib 0.16.1 computation]:
  - **Bangkok**: median 0.310 (p10 0.273, p90 0.329)
  - **Phuket**: median 0.306 (p10 0.275, p90 0.318)
  - **Golden, CO**: median 0.379 (p10 0.257, p90 0.461)
  - **Bird** (AOD500 0.1): k = 0.306, and GHI/(G0 cos z) = 0.754 at z = 48.19°.
- Peak clear-sky values (Ineichen) [pvlib 0.16.1 computation]:
  - Bangkok: GHI 981 W/m², DNI 817, DHI 173 (3 Apr, 12:20 local)
  - Phuket: GHI 1010, DNI 857, DHI 158
  - Golden: GHI 1062, DNI 914, DHI 185
- Mars: the standard engineering model (Appelbaum & Flood, NASA TM-102299, 1989; update TM-103623, 1990) does **not** use k(1 − T_b). It computes global irradiance from a "normalized net flux function" f(z, τ, albedo) derived from radiative-transfer calculations, beam from Beer's law with optical depth, and diffuse = global − beam. — [NTRS TM-102299](https://ntrs.nasa.gov/citations/19890018252); [NTRS update 1990](https://ntrs.nasa.gov/citations/19910005804); method summary in the [marsrad R package docs](https://archive.linux.duke.edu/cran/web/packages/marsrad/readme/README.html), which also note that the model rests on Viking-era optical depth data.

### Inferences
- **Correction recommended:** make k a per-atmosphere parameter. Use Earth-like ≈ 0.3 (0.25–0.4). For dusty Mars-like atmospheres use a much larger k (about 0.6–0.8). Mars dust (r ≈ 1.5 µm) is strongly forward-scattering and only weakly absorbing in the visible, so most beam light removed by dust still reaches the ground as diffuse. This is why Appelbaum–Flood global irradiance stays substantial at τ ≈ 1–2. The 0.6–0.8 range is an inference; calibrate it against Appelbaum–Flood f-tables if obtainable.
- For a vacuum (no atmosphere): τ = 0, DHI = 0 and DNI = G0. Ground reflection stays nonzero; use the regolith albedo (about 0.1 for the Moon, 0.25 for Mars).
- Energy conservation holds for any k ∈ [0, 1]. Clamp k to that range in code.

### Gaps
- Appelbaum–Flood f(z, τ) numeric tables were not retrievable (NTRS and CRAN blocked), so no Mars k calibration number can be cited.
- ESRA model formulas were not fetched separately. Ineichen–Perez (closely related, Linke-turbidity based) was used as the reference.

## Q7. NOCT cell temperature and power: T_cell = T_air + (NOCT − 20)/800·POA; P = POA·A·η_ref[1 − 0.004(T_cell − 25)]·f_spec·(1 − f_dust)

### Takeaway
**CORRECT as the standard simple NOCT model** (Duffie & Beckman / Ross form). The NOCT definition (800 W/m², 20 °C air, 1 m/s wind) is confirmed by pvlib. **−0.4 %/°C is within the c-Si range of −0.3 (newer) to −0.5 (older) %/°C**. For a modern PERC or TOPCon default, about −0.30 to −0.35 %/°C is more representative. NOCT = 45 °C is typical, but measured NOCTs vary from 42 to 52 °C. **In thin or no atmosphere the NOCT model is INVALID and NEEDS CORRECTION:** replace it with a radiative energy balance.

### Cited Findings
- pvlib `noct_sam` (SAM model, Gilman et al., section 10.6): NOCT is "determined at conditions of 800 W/m² irradiance, 20 C ambient air temperature and 1 m/s wind", and the default wind speed of 1.0 m/s is the wind speed at module height used to determine NOCT. The SAM version also includes a wind adjustment and a module-efficiency term, which the game omits. — [pvlib temperature.py source](https://github.com/pvlib/pvlib-python/blob/main/pvlib/temperature.py) (installed v0.16.1)
- "For crystalline silicon technologies, γ is in the range of −0.3 %/°C (newer technologies) to −0.5 %/°C (older technologies)." — [Sandia PVPMC: Normalized Efficiency](https://pvpmc.sandia.gov/modeling-guide/5-ac-system-output/pv-performance-metrics/normalized-efficiency/)
- NREL NOCT test work (IEC 61215) found previously reported NOCTs of 42.4–52.3 °C across three modules and documented problems with the NOCT procedure. — [NREL presentation (docs.nrel.gov fy10osti/49505)](https://docs.nrel.gov/docs/fy10osti/49505.pdf)
- Test values, NOCT = 45 °C and γ = −0.4 %/°C [own arithmetic]:
  - POA 1000, T_air 25: T_cell 56.25 °C, derate 0.875
  - POA 800, T_air 20: 45.00 °C, derate 0.920
  - POA 1000, T_air 35: 66.25 °C, derate 0.835
- Vacuum radiative equilibrium, α·G·(1 − η) = (ε_front + ε_back)·σ·T⁴, with α = 0.9 and η = 0.2 [own arithmetic]:
  - G = 1361, ε = 0.85 both faces: **T = 317.5 K (44.4 °C)**
  - Rear insulated (ε_back = 0): **377.6 K (104.5 °C)**
  - G = 590 (Mars distance), both faces: **257.7 K (−15.5 °C)**

### Inferences
- **Fix for airless or thin worlds:** compute T_cell from the radiative balance, adding sky (about 3 K) and ground IR terms if desired. Blend toward the NOCT model as surface pressure increases. Mars (about 600 Pa) has very weak convection, so cells run much hotter *relative to the air*, while the air itself is about −60 °C. The net effect is often cold, high-efficiency cells.
- Duffie & Beckman's fuller NOCT relation multiplies the rise by (1 − η/τα), because energy leaving as electricity does not heat the cell. Omitting it overestimates T_cell by about 2–3 °C at η = 20 %, which is negligible for a game.
- f_spec: AM1.5 is the rating spectrum. Under a cool M-dwarf, c-Si (band gap 1.12 eV, about 1100 nm cutoff) loses a sizable fraction of photons beyond the cutoff. f_spec should be computed from the stellar effective temperature (blackbody fraction usable above 1.12 eV) rather than held fixed. No source was fetched for this.

### Gaps
- Duffie & Beckman text not accessible online; equations quoted from general knowledge of the standard form. The pvlib/SAM docstring confirms the NOCT reference conditions.
- No sourced current market-average temperature coefficients for TOPCon or HJT; the −0.30 to −0.35 %/°C value is unsourced background.

## Q8. Annual integration: 12–24 representative days × 48 steps vs fine-resolution integration

### Takeaway
**ACCEPTABLE SIMPLIFICATION, with errors under 1 % for clear-sky POA.** 12 days on the 15th of each month (weighted by days in month) × 48 mid-point steps is within **0.15 %** of a 365-day × 1-min reference at latitudes from 14° to 60°. 24 evenly spaced days is within **0.02 %**. The Duffie & Beckman "recommended average days" (Table 1.6.1) do *worse* (+0.16 % to +0.70 %). They are tuned to reproduce monthly-mean *extraterrestrial horizontal* irradiation, not tilted POA.

### Cited Findings
- Game model replica (τ = 0.3, k = 0.4, ρ = 0.2, tilt = |lat|, equator-facing, Earth orbit) [pvlib-free numpy computation]. Reference is 365 d × 1440 steps; values are bias vs reference:

| Latitude | Reference (kWh/m²/yr) | 12 days on 15th ×48 mid | D&B days ×48 | 24 days ×48 | 12 days ×24 |
|---|---|---|---|---|---|
| 13.75° | 2828.6 | +0.01 % | +0.16 % | −0.00 % | −0.00 % |
| 35° | 2581.9 | +0.05 % | +0.33 % | +0.01 % | +0.08 % |
| 60° | 1796.6 | +0.15 % | +0.70 % | +0.02 % | +0.22 % |
| −33.9° | 2601.2 | −0.01 % | +0.10 % | +0.01 % | +0.02 % |

  - Left-endpoint vs mid-point time sampling changed results by less than 0.1 %.

### Inferences
- Sample representative days uniformly in *time* (mean anomaly), not true anomaly. On highly eccentric planets (e > 0.3), use more samples near periastron or importance-weight by dt. Otherwise the r⁻² peak is under-resolved.
- Use 48 steps per *solar* day of that planet. For very long solar days (Venus-like), 48 steps is still fine because the integrand is smooth. Tidally locked worlds need no diurnal loop.
- The representative-day method is only accurate against *clear-sky* references. Against real hourly weather (TMY or NASA POWER hourly), cloud variability dominates the error. Use NASA POWER all-sky/clear-sky ratios as a monthly "cloud factor" rather than more time steps.

### Gaps
- No published benchmark comparing representative-day vs hourly integration for tilted surfaces was fetched. The numbers above are the researcher's own simulation.

## Q9. EROI and energy payback time (EPBT) for crystalline-silicon PV: literature values for balancing

### Takeaway
Current c-Si PV EPBT is **about 1 year** at central-European irradiation, and **0.44–1.42 years** worldwide depending on insolation (Fraunhofer ISE; IEA PVPS 2024 reports a non-renewable EPBT of about 1 year). The older harmonized meta-analysis (Bhandari 2015, 13 % mono-Si) gives EPBT 1.0–4.1 years and EROI 8.7–34.2. For balancing, use **EPBT ≈ 1 yr × (1700 / local POA kWh/m²/yr)** and **EROI ≈ lifetime/EPBT ≈ 20–30** for a 25–30 year life.

### Cited Findings
- Fraunhofer ISE Photovoltaics Report:
  - 2021 edition: rooftop systems with 19.9 %-efficient China-made modules have EPBT of 1–1.3 years in Europe, and 0.44 years (India) to 1.42 years (Canada) worldwide. EPBT is defined in primary-energy-equivalent terms.
  - 2025 edition (29 May 2025): EPBT "is dependent on the geographical location".
  - German-language update (14 July 2026): reduced silicon use per watt has shortened EPBT to about 1 year depending on location, and over about 20 years systems produce many times their embodied energy.
  - Sources: [Fraunhofer ISE Photovoltaics Report (PDF)](https://www.ise.fraunhofer.de/content/dam/ise/de/documents/publications/studies/Photovoltaics-Report.pdf); [report landing page](https://www.ise.fraunhofer.de/de/veroeffentlichungen/studien/photovoltaics-report.html). Values are from search snippets; the PDF itself was DNS-blocked.
- IEA PVPS Task 12 LCA fact sheet (June 2024 release) covers mono-Si PERC/TOPCon at 20.9 % module efficiency and reports a non-renewable EPBT (NREPBT) of about 1 year for the evaluated systems. The 2022 fact sheet (2021 data) used 20.0 % mono-Si BSF, with no PERC data yet. — [pv magazine on IEA PVPS fact sheet, 2024](https://www.pv-magazine.com/2024/06/27/iea-pvps-releases-fact-sheet-on-environmental-life-cycle-assessment-of-pv-systems); [IEA PVPS T12-23 fact sheet 2022 (PDF)](https://www.iea-pvps.org/wp-content/uploads/2022/11/Fact-Sheet-IEA-PVPS-T12-23-LCA-update-2022.pdf)
- Bhandari et al. 2015 (Renew. Sustain. Energy Rev. 47:133–141) reviewed 232 references. Harmonized assumptions were PR 0.75, 30-year life, insolation 1700 kWh/m²/yr, and module efficiency 13.0 % mono-Si / 12.3 % poly-Si. Mean harmonized EPBT was 1.0–4.1 yr, with mono-Si the longest, and mean harmonized EROI was 8.7–34.2. Embedded energy varied more than efficiency or PR. — [IDEAS/RePEc record](https://ideas.repec.org/a/eee/rensus/v47y2015icp133-141.html); [ADS](https://ui.adsabs.harvard.edu/abs/10.1016/j.rser.2015.02.057/abstract)
- Koppelaar (2017) is a later critique of harmonization methods in this EROI literature. It was cited in reference lists; numbers not retrieved. — [IDEAS/RePEc record (Bhandari page lists it)](https://ideas.repec.org/a/eee/rensus/v47y2015icp133-141.html)

### Inferences
- Bhandari's 2015 numbers reflect roughly 2010-era manufacturing (13 % efficiency). Modern 21–23 % modules with thinner wafers explain the drop to about 1 year. Use 2024–2026 values for the "present-day" tech tier and Bhandari's for an "early" tier.
- Units: EPBT in the literature uses primary-energy-equivalent output (electricity × about 1/0.3). An electricity-to-electricity EROI is therefore roughly 3× lower than a primary-energy one. Pick one convention in-game and state it. A game rule could be E_embodied ≈ 1 yr × 1700 kWh/m²/yr × η × PR, scaled per m².
- On other planets, EPBT scales inversely with annual POA yield: a Mars-equator panel at about 40 % of Earth's yield gives an EPBT of about 2.5 years. Launch and transport energy should be added for off-world deployment.

### Gaps
- Exact current Fraunhofer EPBT table (by technology and location) and IEA PVPS 2024 per-location numbers could not be fetched (hosts blocked); values above are from search-result summaries.
- No authoritative recent single EROI figure for modern mono-PERC/TOPCon was found; EROI ≈ lifetime/EPBT is an inference.

## Q10. Reference test values and NASA POWER Earth validation (Bangkok, Phuket); is about 2500 kWh/m²/yr clear-sky POA plausible?

### Takeaway
**Yes, 2500 kWh/m²/yr clear-sky POA at tilt = latitude in Bangkok is plausible.** pvlib Ineichen–Perez with climatological Linke turbidity gives **2418 kWh/m²/yr POA** (GHI 2364) for Bangkok and **2474** for Phuket (GHI 2456). The game's 2500 is about 3 % high, consistent with a slightly low τ (the game replica needs τ ≈ 0.4 with k = 0.3 to reach 2497). **It is NOT a realistic *all-sky* value.** Central Thailand actual GHI is about 5.2–5.3 kWh/m²/day (about 1900–1950 kWh/m²/yr), so clouds remove roughly 20 %. The NASA POWER API could not be queried from this session (host blocked), so NASA POWER Bangkok and Phuket numbers are a **gap**. The endpoint format below is verified from client-library docs.

### Cited Findings
- **NREL SPA reference case (Reda & Andreas 2004), reproduced exactly by pvlib `spa_python`:**
  - Inputs: 2003-10-17 12:30:30 local (UTC−7), lat 39.742476°, lon −105.1786°, elevation 1830.14 m, P = 820 mbar, T = 11 °C, ΔT = 67 s.
  - Outputs: **topocentric apparent zenith 50.11162°**, zenith without refraction 50.12795°, **azimuth 194.34024°**, equation of time 14.64151 min.
  - [pvlib 0.16.1 computation; original values are the SPA paper's worked example]
- Clear-sky annual totals, 5-min steps, 2023, Ineichen–Perez with SoDa Linke turbidity, albedo 0.2, isotropic sky [pvlib 0.16.1 computation]:

| Site | GHI | DNI | DHI | POA at tilt = latitude, south-facing | TOA horizontal | Haurwitz GHI |
|---|---|---|---|---|---|---|
| Bangkok (13.75 N, 100.5 E) | 2364 | 2601 | 523 | **2418** | 3559 | 2611 |
| Phuket (7.88 N, 98.39 E) | 2456 | 2729 | 501 | **2474** | 3623 | 2659 |

  - All values are kWh/m²/yr. Haurwitz (cloud-free, turbidity-free) is an upper-bound-like simple model.
- Game-model replica at Bangkok, POA tilt = latitude, kWh/m²/yr [pvlib 0.16.1 computation]:

| τ | k = 0.3 | k = 0.5 |
|---|---|---|
| 0.1 | 3278 | 3380 |
| 0.2 | 2970 | 3154 |
| 0.3 | 2714 | 2967 |
| 0.4 | 2497 | 2809 |

  - These match Ineichen (2418) when τ ≈ 0.42–0.45 and k ≈ 0.3, consistent with Q5 and Q6.
- NASA POWER endpoints, parameters and units:
  - Clients (pvlib `iotools.nasa_power`, R `nasapower`) use the POWER API with communities "AG", "RE" (Renewable Energy, for solar and wind system design) and "SB".
  - The climatology temporal API returns long-term averages: 22-year for solar parameters, 30-year for meteorology. Dates are ignored for climatology, and at most 20 parameters are allowed per point request.
  - ALLSKY_SFC_SW_DWN is "All Sky Surface Shortwave Downward Irradiance" (CERES SYN1deg) in **kW-hr/m²/day**. CLRSKY_SFC_SW_DWN is the clear-sky counterpart, same units. Annual values come as the "ANN" column.
  - Sources: [nasapower vignette (rOpenSci)](https://docs.ropensci.org/nasapower/articles/nasapower.html); [nasapower manual](https://packages.ropensci.org/nasapower/doc/manual.html); [pvlib nasa_power source](https://pvlib-python.readthedocs.io/en/stable/_modules/pvlib/iotools/nasa_power.html), which points to power.larc.nasa.gov/parameters/ as the authoritative parameter list.
  - One third-party page lists MJ/m²/day for the same parameter, which conflicts with kWh/m²/day. Check the units in the returned JSON header (1 kWh = 3.6 MJ). — [search result summary, clawhub page](https://clawhub.ai/ruiduobao/nasa-power-download)
- Endpoint to use (format confirmed by client libraries, not fetched live): `https://power.larc.nasa.gov/api/temporal/climatology/point?parameters=ALLSKY_SFC_SW_DWN,CLRSKY_SFC_SW_DWN,ALLSKY_SFC_SW_DNI,ALLSKY_SFC_SW_DIFF,T2M&community=RE&longitude=100.5&latitude=13.75&format=JSON`
  - The response has `properties.parameter.<NAME>.{JAN..DEC,ANN}` in kWh/m²/day.
  - Annual kWh/m²/yr = ANN × 365.25.
- Thailand context: central Thailand's most common GHI is about 5.2–5.3 kWh/m²/day — [Aenert Thailand renewable map 2022](https://aenert.com/fileadmin/default/templates/images/Thailand/2022_map/aenert_map_Thailand_renewable23.pdf). A Bangkok model study gives 15.7–23.2 MJ/m²/day, about 4.4–6.4 kWh/m²/day (unit conversion is the researcher's) — [SWU repository](https://ir.swu.ac.th/entities/publication/4edb6862-ea36-4bc1-b171-7cb5f7e53f2a/full).
- Earth constants usable in tests:
  - S = 1361 W/m² (IAU nominal) — [IAU B3](https://www.arxiv-vanity.com/papers/1510.07674/)
  - Perihelion around Jan 3, ϖ_helio = 102.947°, so λ_p(Sun) = 282.947° — [Wikipedia: Earth's orbit](https://en.wikipedia.org/wiki/Orbit_of_Earth)
  - e = 0.0167 and ε = 23.44° are standard J2000-era values; this note's SPA validation used exactly these.

### Inferences
- Suggested unit-test tolerances:
  - Kepler: 1e-10 rad
  - ENU vs SPA (with SPA-derived δ and EoT): < 0.01°
  - Simplified game δ vs SPA: < 0.5°
  - Kasten–Young: 1e-4
  - Annual clear-sky POA Bangkok (τ 0.43, k 0.3): within ±5 % of 2418 kWh/m²/yr
  - Representative-day integrator vs fine integration: < 1 %
- For NASA POWER validation, compare the game's clear-sky GHI against CLRSKY_SFC_SW_DWN × 365.25. Expect agreement to roughly ±10 %, since CERES clear-sky uses actual aerosol rather than SoDa Linke climatology. Use ALLSKY/CLRSKY monthly ratios as the game's Earth "cloud factor".
- "AM1.5G ≈ 1000 W/m²" refers to a 37°-tilted surface under the ASTM G173 reference atmosphere, not to the horizontal GHI at z = 48.19°. In the Bird test above, horizontal GHI at AM1.5 is only 684 W/m². Do not test horizontal GHI at AM1.5 against 1000.

### Gaps
- **NASA POWER Bangkok and Phuket ALLSKY and CLRSKY annual values: not obtained.** power.larc.nasa.gov must be allowed in the session's network settings, or the query above must be run from a normal machine. No reliable published copy of these exact values was found via search.
- ASTM G173 / IEC 60904-3 AM1.5G (integrated about 1000 W/m², 37° tilt) is quoted from background knowledge; the standard documents were not fetched.
