# Physics of Planetary-Surface Energy Sources (Kardashev Ascent design-doc verification)

Scope: verdicts on 16 design-doc claims, with game-usable formulas, typical numbers and unit-test values. Verdict key: **CORRECT** / **NEEDS CORRECTION** (fix given) / **ACCEPTABLE SIMPLIFICATION**.
Method note: unit-test values below were computed in Python this session from the stated constants (shown so they can be re-derived). Two primary sources (ntrs.nasa.gov, world-nuclear.org) could not be fetched from this session (network allowlist), so a few numbers rely on search-result summaries of those pages; this is flagged where relevant.

### Verdict summary table

| # | Claim | Verdict | Key fix |
|---|---|---|---|
| 1 | Wind ½ρAv³, Betz 16/27, Cp 0.45–0.50, Titan/Mars densities | CORRECT (Titan wind speeds: very low, confirmed) | Mars surface ρ is ~0.015–0.020 depending on site/season; use 0.020 as fact-sheet value |
| 2 | Hydro ηρgQh, any liquid, Titan methane rivers | CORRECT | Note Titan methane hydro ≈ 6% of Earth water per unit Q·h |
| 3 | RTG Pu-238 87.7 y, 110 We, ~6%, ~4%/y | ACCEPTABLE SIMPLIFICATION | Efficiency is ~5.5%; degradation 2.5–4.8 %/y depending on source; fuel alone is 0.79 %/y |
| 4 | CSP needs DNI ~1800–2000 kWh/m²/yr; useless under haze | CORRECT in principle; threshold not verified from primary source | CSP uses only DNI, so diffuse-dominated skies (Titan, Venus) kill it |
| 5 | Earth heat flow ~47 TW; Io ~1e14 W | CORRECT | Io: ~0.6–1×10¹⁴ W depending on method |
| 6 | Tidal needs ocean + moon; Earth dissipation 3.7 TW | CORRECT | Practical extractable << 3.7 TW |
| 7 | OTEC ΔT ≥ 20 °C, Carnot 6–7 %, net ~3 % | CORRECT | Real plants 1–3 % |
| 8 | Day–night thermoelectric on tidally locked worlds | ACCEPTABLE SIMPLIFICATION (inference) | TE ~10–20 % of Carnot at ZT 1–2; heat engines better at scale |
| 9 | Io flux tube 1–5 MA, 1e12–1e13 W | ACCEPTABLE (current CORRECT; power: ~10¹² W best sourced) | Use ~1×10¹² W, ~1–3 MA |
| 10 | U-235/U-238/Th-232 half-lives, Oklo, formula | CORRECT | Formula correct; game must also handle "planet age" vs "time ago" (see below) |
| 11 | Fusion D-T / D-He3 / D-D, lunar He-3 ppb | CORRECT | Lunar He-3: measured ≤ ~10 ppb, global mean ~4 ppb, modelled >20 ppb in high-Ti mare |
| 12 | SBSP beamed microwave ≥/≈50 % DC→DC; NASA OTPS 2024 | NEEDS CORRECTION (end-to-end sunlight→grid is ~10–20 %) | Distinguish link efficiency from sunlight-to-grid |
| 13 | Storage numbers | CORRECT with refinements | Iron-air RTE ~40–55 %; Na-ion ~160 Wh/kg (CATL gen-1) |
| 14 | Combustion needs oxidizer; Titan CH4 can't burn | CORRECT | Inverse is true: on Titan an imported O2 is the "fuel" |
| 15 | EROI table / Weissbach controversy | CORRECT that it is contested | Values strongly method-dependent |
| 16 | Superconductors on Titan; YBCO Tc ~92–93 K vs Titan 93.65 K | CORRECT that Titan is (just) above YBCO Tc — NEEDS CORRECTION if game implies passive HTS on Titan | Need cryocooler; Hg-1223 (Tc 133 K) is the only passive option |

---

## 1. Wind power (P = ½ρAv³, Betz limit, Titan & Mars)

### Takeaway
CORRECT. Power scales linearly with density and cubically with wind speed, so Titan's ~4.3× Earth density is overwhelmed by its near-calm surface winds (<1 m/s), and Mars' ~1/60 density needs ~4× Earth wind speed to match. Betz 16/27 and practical Cp ~0.45–0.47 are confirmed.

### Cited Findings
- Betz limit is 16/27 ≈ 0.593, optimum at downstream/upstream velocity ratio 1/3; real machines always fall below it — [QMUL "Power in the wind"](https://ph.qmul.ac.uk/sites/default/files/u75/Power%20in%20the%20wind.pdf); [Springer text](https://springerlink.bibliotecabuap.elogim.com/content/pdf/10.1007/978-3-642-27151-9_4)
- Utility-scale turbines reach roughly 75–80 % of Betz at peak (→ Cp ≈ 0.45–0.47) — [Wikipedia: Betz's law](https://www.Wikipedia.org/wiki/Betz_limit)
- A 2026 arXiv preprint argues the ceiling is different (~78 %); unreviewed, single claim, ignore for game — [arXiv 2602.08045](https://arxiv.org/pdf/2602.08045)
- Large-turbine Cp analyses use a power-law (Hellmann exponent) wind profile that varies with site and stability — [Energies 2025 (RePEc)](https://ideas.repec.org/a/gam/jeners/v18y2025i3p740-d1584687.html)
- Huygens HASI surface: T = 93.65 ± 0.25 K, p = 1467 ± 1 hPa — [Fulchignoni et al. 2005, Nature 438:785 (ESA)](https://sci.esa.int/web/cassini-huygens/-/55210-fulchignoni-et-al-2005)
- Titan surface wind <1 m/s (ESA); 0.3 ± 0.1 m/s from parachute drift; ≤0.25 m/s mean within 1 m of ground from probe cooling — [Space.com](https://space.com/1825-view-titan-strong-winds-soft-ground-lightning.html); [arXiv 1702.00667 (Huygens bounce)](https://arxiv.org/pdf/1702.00667)
- Titan near-surface profile shows a weak planetary boundary layer — [Univ. Cologne Huygens data](https://geomet.uni-koeln.de/en/research/group-saur/space-physics/titan/huygens-data)

### Inferences
- **Titan density check** (ideal gas, computed): ρ = pM/(RT) = 146 700 × 0.0280 / (8.314 × 93.65) = **5.28 kg/m³** (pure N₂); with ~5 % CH₄ (M ≈ 27.4 g/mol) 5.16 kg/m³. Design-doc 5.3 kg/m³ is CORRECT. Note 1467 hPa ≈ 1.45 bar ("1.5 bar" OK).
- **Mars density**: ideal-gas at 610 Pa, 210 K, CO₂ gives 0.0154 kg/m³; the common fact-sheet value 0.020 kg/m³ corresponds to colder/higher-pressure conditions. Either is an ACCEPTABLE SIMPLIFICATION; use a range 0.012–0.020.
- **Formula for game**: `P_turbine = 0.5 * rho * (π D²/4) * v_hub³ * Cp * η_drivetrain`, Cp = 0.45, η_drivetrain ≈ 0.9–0.95; clamp to rated power above rated speed, zero below cut-in (~3 m/s on Earth; scale cut-in by (ρ_Earth/ρ)^(1/3) for other atmospheres).
- **Hub-height scaling**: power law `v(z) = v_ref (z/z_ref)^α`, α ≈ 1/7 (0.143) neutral open terrain; or log law `v(z) = v_ref · ln(z/z0)/ln(z_ref/z0)`, z0 ≈ 0.03 m grass. Unit test: 10 m → 100 m gives ×1.389 (α=1/7) and ×1.396 (log, z0=0.03).
- **Unit tests** (raw kinetic power density ½ρv³, before Cp):
  - Earth ρ=1.225, v=8 m/s → **313.6 W/m²** (×0.45 Cp → 141 W/m² swept)
  - Titan ρ=5.3, v=1 m/s → **2.65 W/m²**; v=0.3 → 0.072 W/m²
  - Mars ρ=0.020, v=10 m/s → **10.0 W/m²**
  - Equal-to-Earth(8 m/s) wind speed: Titan **4.91 m/s**, Mars **31.5 m/s** (v_eq = 8·(1.225/ρ)^(1/3))
- Game numbers (land-based Earth, typical industry values; not re-verified this session): capacity factor 25–45 % onshore, 40–55 % offshore; lifetime 25–30 y; land-use power density ~1–3 W/m² of farm area (spacing-limited) vs ~100+ W/m² of swept area.

### Gaps
- No primary source fetched for NREL ATB wind capacity factors this session; numbers above are standard ranges to be verified against NREL ATB 2024/2025.
- Mars 0.020 kg/m³ figure not confirmed from a NASA fact sheet in this session.

---

## 2. Hydropower (P = η ρ g Q h; Titan methane)

### Takeaway
CORRECT. The formula is fluid-agnostic, so any flowing liquid works, and Cassini radar confirmed liquid-filled river canyons on Titan. But Titan methane gives only ~6 % of Earth-water power for the same Q and h (ρ 0.45×, g 0.138×).

### Cited Findings
- Vid Flumina: >400 km river flowing into Ligeia Mare, announced Dec 2012 — [Discover Magazine](https://www.discovermagazine.com/cassini-finds-a-new-nile-river-on-titan-13980); [Wikipedia: Vid Flumina](https://en.wikipedia.org/wiki/Vid_Flumina)
- Poggiali et al. 2016 (GRL, doi:10.1002/2016GL069679) used Cassini radar altimetry (May 2013) to make the first direct detection of liquid in Vid Flumina's tributary canyons; canyons 240–570 m deep, slopes >40°; liquid surface at Ligeia Mare's level — [Sky & Telescope](https://skyandtelescope.org/astronomy-news/titans-flooded-canyons-1008201623/); [Sci.News](https://www.sci.news/featurednews/cassini-liquid-methane-filled-canyons-titan-04095.html)
- Canyons thought to be cut by liquid methane draining into the main river — [ESA](https://sci.esa.int/web/cassini-huygens/-/58303-methane-flooded-canyons-on-titan)

### Inferences
- **Formula**: `P = η · ρ · g · Q · h`, η ≈ 0.85–0.92 (large turbines).
- **Unit tests** (η=0.9, Q=1 m³/s, h=100 m): Earth water (ρ=1000, g=9.81) → **882.9 kW**; Titan liquid methane (ρ=450, g=1.352) → **54.8 kW** (6.2 % of Earth).
- "Higher gravity → more power per dam" is CORRECT for fixed Q and h, but on high-g worlds sustainable mountain relief is lower and precipitation-driven Q depends on the hydrological cycle; ACCEPTABLE SIMPLIFICATION for a game.
- Titan seas are methane–ethane–nitrogen mixtures; density depends on composition (pure liquid CH₄ ~450 kg/m³ near 94 K; ethane-rich liquid denser). Use 450–550 kg/m³.
- Typical Earth numbers (industry-standard, not re-verified): capacity factor 35–55 %, lifetime 50–100 y.

### Gaps
- Liquid methane density at 94 K and Ligeia Mare composition not sourced this session.

---

## 3. RTG (Pu-238, MMRTG)

### Takeaway
ACCEPTABLE SIMPLIFICATION. 87.7 y half-life and ~110 We / ~2000 Wt (≈5.5 % efficiency) are confirmed; total power-decline rate is source-dependent: NASA planning uses 3.8–4.8 %/y, but the NASA fact-sheet end-of-design-life value (72 W at 17 y) implies ~2.5 %/y average. Pu-238 decay alone is 0.79 %/y.

### Cited Findings
- MMRTG: 4.8 kg PuO₂, ~2000 W thermal and ~110 W electric at start in deep space; estimated 72 W at 17-year design life — [NASA MMRTG fact sheet](https://science.nasa.gov/wp-content/uploads/2024/02/mmrtg-factsheet-updated-5-18-20-1.pdf); [JPL Mars 2020 press kit](https://www.jpl.nasa.gov/news/press_kits/mars_2020/launch/mission/spacecraft/power/)
- Curiosity decline is close to linear, roughly 1 W per 80 sols — [Planetary Society (Lakdawalla)](https://www.planetary.org/articles/0514-book-excerpt-curiosity-mmrtg)
- DOE breakdown of ~3.8 %/y: fuel decay ~0.8 %, hot-junction drop ~0.5 %, thermoelectric degradation ~2.5 % — [DOE/JPL Fleurial presentation](https://www.energy.gov/sites/prod/files/2014/03/f10/fleurial.pdf)
- NASA planning value 4.8 %/y for MSL MMRTG on Mars (3.8 % case also shown) — [NASA New Frontiers RPS description](https://newfrontiers.larc.nasa.gov/NF4/PDF_FILES/02-Zakrajsek.pdf)
- 2022 IEEE analysis of F1 (Curiosity) degradation suggests heat-loss ~1 %/y and degradation slowing — [AMiner record](https://aminer.org/pub/62f42ef290e50fcafdda549d/understanding-degradation-and-heat-losses-in-the-mmrtg-and-an-improved-analysis)
- Next-gen eMMRTG expected ~2.5 %/y including fuel decay (not yet validated) — [Ice Giants study, arXiv 2511.13946](https://arxiv.org/pdf/2511.13946)

### Inferences
- **Formula**: `P_e(t) = P_e0 · exp(−λ_eff · t)` with λ_eff = ln2/87.7 + k_TE; game defaults: P_e0 = 110 W, k_TE ≈ 0.017–0.04 /y. Fuel-only factor per year = 1 − exp(−ln2/87.7) = **0.787 %**.
- Unit tests: fuel only after 14 y → 110·exp(−0.0079·14) = **98.5 W**; fact-sheet implied average rate (72/110)^(1/17) → **2.46 %/y**.
- Efficiency 110/2000 = **5.5 %** (design doc's "~6 %" OK). Specific power ~2.8 We/kg (45 kg unit; mass figure from standard references, not re-verified).

### Gaps
- No single authoritative current Curiosity flight-unit annual rate; values span 1.6–4.8 %/y.

---

## 4. CSP solar tower with molten-salt storage

### Takeaway
CORRECT in principle: CSP concentrates only the direct beam (DNI), so diffuse-dominated skies (Titan haze, Venus clouds, persistent overcast) make it useless while PV still gets diffuse light. The 1800–2000 kWh/m²/yr threshold is the conventional industry rule of thumb but I could not find a primary source for it this session.

### Cited Findings
- NREL: only a small benefit from storage between 6 and 9 hours of rated capacity, less beyond 9 h — [NREL 2014 news](https://nrel.gov/news/program/2014/8306.html)
- Typical molten-salt storage 6–12 h, some designs 15+ h (secondary source) — [Sustainability Directory](https://energy.sustainability-directory.com/area/csp-applications/)
- Gemasolar demonstrated capacity factors >70 %; CSP with storage generally 50–60 %+ — [Reuters Events](https://www.reutersevents.com/renewables/node/60763)
- DLR reference plant modelling used DNI 2518 kWh/m²/yr (site input, not threshold) — [DLR 2022](https://elib.dlr.de/136930/1/2022_CSP_ReferenecPowerPlant_AIP.pdf)
- LCOE of molten-salt towers driven mainly by storage hours — [Hoffmann & Madaly 2015](https://sterg.sun.ac.za/wp-content/uploads/2019/05/2015_02_Hoffmann_and_Madaly_-_FINAL_2015_31_46-51.pdf)

### Inferences
- **Game formula**: `E_annual = DNI_annual · A_aperture · η_solar-to-electric`, η_annual ≈ 15–18 % for towers (receiver ~85–90 %, field optical ~55–65 %, Rankine ~40 %; industry-typical, not re-verified). Gate: `if DNI_annual < 1800 kWh/m²/yr → not viable`. On a planet with diffuse fraction > ~0.5, set DNI ≈ (1−diffuse fraction)·GHI.
- Molten salt (60/40 NaNO₃/KNO₃) operates ~290–565 °C; storage round-trip thermal efficiency ~>95 % (industry-typical, not re-verified).

### Gaps
- No primary source found for the 1800–2000 kWh/m²/yr DNI threshold or for NREL ATB CSP parameters (ATB page not retrieved).

---

## 5. Geothermal (internal heat; Io)

### Takeaway
CORRECT. Earth's surface heat flow is 47 ± 2 TW (~0.09 W/m²). Io's total is ~0.6–1×10¹⁴ W (~2 W/m²), so "~1e14 W" is acceptable as an order of magnitude.

### Cited Findings
- Earth surface heat flux 47 ± 2 TW from 38 347 measurements — [Davies & Davies 2010, Solid Earth 1:5](https://se.copernicus.org/articles/1/5/2010/)
- Estimates span 43–49 TW; ~0.09 W/m² mean — [Wikipedia: Earth's internal heat budget](https://en.wikipedia.org/wiki/Earth%27s_internal_heat_budget)
- Io volcanic sources account for ~62×10¹² W (~59 ± 7 % of total thermal emission → total ~1×10¹⁴ W) — [Veeder et al. 2012, Icarus 219:701](https://ui.adsabs.harvard.edu/abs/2012Icar..219..701V)
- Io global mean heat flow 1.5–4.0 W/m²; astrometric 2.24 ± 0.45 W/m² — [arXiv 1810.00725](https://www.arxiv.org/pdf/1810.00725)

### Inferences
- Io: 2.24 W/m² × 4.19×10¹³ m² ≈ **9.4×10¹³ W** (computed). Use 1×10¹⁴ W.
- "Young planets hotter" is CORRECT qualitatively (more primordial heat + more short-lived/long-lived radiogenic heat; U-235 and K-40 more abundant early — cf. §10). Game: `Q_radiogenic(age) ∝ Σ_i c_i exp(−λ_i·age)`.
- Geothermal plant numbers (industry-typical, not re-verified): capacity factor 75–95 %, lifetime 30+ y, conversion efficiency ~10–20 % (flash/binary).

### Gaps
- Didn't retrieve an IRENA/NREL ATB source for geothermal capacity factor this session.

---

## 6. Tidal power

### Takeaway
CORRECT. Earth's total tidal dissipation is 3.7 TW (2.5 TW in M2), ~95 % in oceans; this is a physical ceiling, and practical extraction is a small fraction.

### Cited Findings
- Space-geodetic estimates converge on 3.7 TW total, 2.5 TW for M2 — [Egbert & Ray 2000, Nature 405:775](https://www.doi.org/10.1038/35015531)
- ~1 TW (25–30 %) in deep ocean near rough topography, rest mainly in shallow seas — [NASA SVS TOPEX press](https://svs.gsfc.nasa.gov/stories/topex/press.html)
- Munk & Wunsch 1998 split: 3.5 TW ocean, 0.2 TW solid-Earth; Moon 3.2 TW, Sun 0.5 TW — [Wikipedia: Tidal heating](https://en.wikipedia.org/wiki/Tidal_heating)

### Inferences
- Game: tidal-force scaling ∝ M_moon / d³ (equilibrium tide height ∝ (M_moon/M_planet)·R_planet⁴/d³). Require ocean + (moon or close star for tidally-locked-but-eccentric orbits). A tidally locked planet with circular orbit has no tides from its star.
- Barrage: `E_tide = ½ ρ g A h² ` per tide (A basin area, h range); capacity factor ~20–30 % (La Rance-type; not re-verified).

### Gaps
- No source fetched for practical extractable tidal resource estimates.

---

## 7. OTEC

### Takeaway
CORRECT. ΔT ≈ 20 °C gives Carnot ~6–7 % and net ~3 % (real plants 1–3 %); thus tropics only.

### Cited Findings
- ΔT ~20 °C: Carnot ~6–7 %, net after parasitic loads ~3 % — [IEEE Technology Navigator](https://technav.ieee.org/topic/ocean-thermal-energy-conversion/)
- Power scales ~quadratically with ΔT (linear heat transfer × Carnot ∝ ΔT/T) — [National Academies 2013](https://www.nationalacademies.org/read/18278/chapter/7)
- Patent: Carnot 6.4–8.5 % typical tropical, actual 2–3 % — [USPTO 8424307](https://image-ppubs.uspto.gov/dirsearch-public/print/downloadPdf/8424307)

### Inferences
- Formula: `η_Carnot = ΔT / T_warm`; `η_net ≈ 0.4·η_Carnot − pumping` ; unit test ΔT=25 K, T=300 K → η_C = **8.3 %**; ΔT=20 K, T=298 K → 6.7 %.
- Gate: `ΔT_surface-1000m ≥ 20 K`.

### Gaps
- No primary NREL source for "3 % net" retrieved.

---

## 8. Day–night thermoelectric on tidally locked planets

### Takeaway
ACCEPTABLE SIMPLIFICATION (largely inference; no specific source retrieved). Thermoelectrics with ZT ~1–2 reach ~10–20 % of Carnot; heat engines (Rankine/Stirling/Brayton) reach 40–60 % of Carnot, so at utility scale a heat engine is more plausible; thermoelectrics fit small/no-moving-part units (as in RTGs, §3).

### Cited Findings
- Real-world thermoelectric anchor: MMRTG converts ~2000 Wt to ~110 We (~5.5 %) at ΔT of several hundred K — [NASA MMRTG fact sheet](https://science.nasa.gov/wp-content/uploads/2024/02/mmrtg-factsheet-updated-5-18-20-1.pdf)

### Inferences
- Standard TE efficiency formula: `η = (ΔT/T_h) · (√(1+ZT̄) − 1) / (√(1+ZT̄) + T_c/T_h)`. For ZT̄=1, T_h=400 K, T_c=300 K → η ≈ 0.25·0.414/(1.414+0.75) ≈ **4.8 %** (19 % of Carnot). ZT̄=2 → ≈ 6.9 %.
- On a tidally locked planet the terminator ΔT is spread across ~thousands of km; the practical design is a working-fluid loop or using day-side surface vs a buried/radiative cold sink, not a solid TE spanning hemispheres. Also, a thick atmosphere redistributes heat and shrinks ΔT.

### Gaps
- No peer-reviewed source on tidally locked exoplanet day–night ΔT retrieved this session (exoplanet climate models, e.g. TRAPPIST-1/LHS 3844 thermal phase curves, would supply this).

---

## 9. Io–Jupiter flux tube ("moon dynamo")

### Takeaway
ACCEPTABLE SIMPLIFICATION. Currents of ~1–3 MA are well sourced (5 MA is a secondary-source upper value); power is best sourced at ~10¹² W, so the design doc's upper end 10¹³ W is high. As a game energy source it is sci-fi but physically grounded: a conducting moon moving through a planetary magnetic field is a unipolar inductor.

### Cited Findings
- Goldreich & Lynden-Bell 1969: Io as unipolar inductor, ~10⁶ A across each flux-tube foot — [ApJ 156:59](https://ui.adsabs.harvard.edu/abs/1969ApJ...156...59G/abstract)
- Acuña et al. 1981: ~2.8×10⁶ A in Io flux tube (from Voyager) — cited in [NASA NTRS 19810007409 (Io interaction review; author attribution not verified)](https://ntrs.nasa.gov/api/citations/19810007409/downloads/19810007409.pdf)
- Io local interaction delivers ~10¹² W; auroral emission ~10¹⁰ W (main spot) to 10¹¹ W (whole footprint) — [EPSC 2010-353](https://meetingorganizer.copernicus.org/EPSC2010/EPSC2010-353.pdf)
- Large fraction of Alfvén-wave power dissipated by Joule heating (Saur et al. 1999) — [Chust et al. 2005, PSS](https://space.physics.uiowa.edu/~dag/publications/2005_AreIosAlfvenWingsFilamented_GalileoObservations_PSS.pdf)
- Power dissipated in torus is ~3.6× that carried to Jovian ionosphere by Birkeland currents — [Liu 2016, arXiv 1603.05734](https://ar5iv.labs.arxiv.org/html/1603.05734)
- Observations do not match the simple unipolar-inductor picture exactly (Alfvén-wing regime instead) — [NASA NTRS 19810007409](https://ntrs.nasa.gov/api/citations/19810007409/downloads/19810007409.pdf)

### Inferences
- Game formula (unipolar inductor): EMF `U = v_rel · B · 2R_moon`; Io: v_rel ≈ 57 km/s, B ≈ 2×10⁻⁶ T (≈1900 nT), 2R ≈ 3.6×10⁶ m → U ≈ 4×10⁵ V; P ≈ U·I ≈ 4×10⁵ V × 2.5×10⁶ A ≈ 10¹² W (consistent with EPSC figure). Inputs are standard textbook values, not re-verified.
- Energy comes from Io's orbital energy/Jupiter's rotation; gate requires: strong planetary magnetic field + close conducting moon + plasma/ionosphere. Treat as late-game "exotic" tech.

### Gaps
- No consolidated modern total power budget found; 10¹³ W not supported by sources retrieved.

---

## 10. Fission & uranium isotopic evolution (Oklo)

### Takeaway
CORRECT. Formula and half-lives are right; computed U-235 fraction 2.0 Gy ago = 3.67 %, 1.7 Gy ago = 2.89 %, consistent with Oklo (~2 Gy, water-moderated). For exoplanets the game should compute enrichment from planet age assuming solar-system-like starting ratio.

### Cited Findings
- Oklo discovered 1972 via U-235 deficit (0.717 % vs 0.720 % expected); fission ~2 Gy ago; water as moderator — [IAEA Bulletin 59-2](https://www.iaea.org/sites/default/files/publications/magazines/bulletin/bull59-2/5922627.pdf); [IAEA feature](https://www.iaea.org/node/51357)
- Secondary source: ~1.8 Gy ago, ran ≥500 000 y; ~3 % fissile at formation — [News24](https://www.news24.com/news24/2-billion-year-old-nuclear-reactor-in-gabon-20150605)

### Inferences (computed this session)
- Constants: λ235 = ln2/0.7038 Gy = **0.984864 Gy⁻¹**; λ238 = ln2/4.468 Gy = **0.155136 Gy⁻¹**; Δλ = **0.829728 Gy⁻¹**; λ232 (Th, t½ = 14.05 Gy) = 0.04933 Gy⁻¹. Present atom ratio r₀ = 0.7204/99.2742 = **0.007257**.
- Formula (correct): `r(t_ago) = r₀ · exp(Δλ · t_ago)`, atom fraction `f = r/(1+r)` (ignores U-234, 0.0055 %).
- **Unit tests (t_ago, ratio, U-235 atom %)**: 0 → 0.00726, 0.720 %; 1.0 Gy → 0.01664, **1.637 %**; 1.7 Gy → 0.02974, **2.888 %**; 2.0 Gy → 0.03814, **3.674 %**; 3.0 Gy → 0.08745, **8.042 %**; 4.5 Gy → 0.30360, **23.29 %**.
- **Planet-age variant** (assume material had solar-system ratio at formation 4.568 Gy ago: r_init = 0.32122, 24.31 %): `r(age) = 0.32122 · exp(−0.829728·age)`. Unit tests: age 1 Gy → 12.29 %; 2 Gy → 5.76 %; 3 Gy → 2.60 %; 4.568 Gy → 0.720 %; 6 Gy → 0.221 %; 8 Gy → 0.042 %; 10 Gy → 0.008 %. Caveat: real initial ratio depends on galactic chemical evolution and time between nucleosynthesis and planet formation; this is a game simplification.
- Gameplay: young planets (<~2.5 Gy) could host natural reactors / run light-water reactors on unenriched U (LWR needs ~3–5 %); old planets (>~5 Gy) need enrichment even for heavy-water reactors (which run on 0.72 %). Thorium is barely depleted (Th fraction falls only ~20 % in 4.5 Gy) → thorium breeders are the "old planet" option.
- Waste/incidents: standard (fission products dominate heat for ~300 y; transuranics 10⁴–10⁵ y); not sourced this session.

### Gaps
- Th-232 half-life (14.05 Gy) and U-234 abundance taken from standard nuclear data, not fetched this session (check NNDC/IAEA NuDat).

---

## 11. Fusion (D-T, D-He3, D-D) and He-3 sources

### Takeaway
CORRECT. D-T needs nTτ ≈ 3×10²¹ keV·s·m⁻³ at ~14 keV; D-He3 is ~18× harder (≈5×10²² at ~58 keV). Lunar He-3 is sparse: global mean ~4 ppb, measured maxima ~10 ppb, modelled >20 ppb in high-Ti mare (design-doc "10–20 ppb" is high-end; "1–50 ppb" too wide at the top).

### Cited Findings
- Triple products: D-T ≈ 2.8×10²¹ m⁻³·s·keV at T_i 13.6 keV; D-He3 ≈ 5.1×10²² at 57.7 keV (difficulty ×18) — [arXiv 2410.18054](https://arxiv.org/pdf/2410.18054)
- Q > 10 reactor needs nTτ_E > 3×10²¹ m⁻³ keV s — [SNU lecture](https://ocw.snu.ac.kr/sites/default/files/NOTE/Lecture_2.3_Fusion%20energy.pdf)
- Lawson values vary with definition (ignition vs engineering break-even) — [Wikipedia: Lawson criterion](https://en.wikipedia.com/wiki/Confinement_time)
- Lunar He-3: Fa & Jin 2007 (Icarus 190:15) modelled >20 ppb in Mare Tranquillitatis/Oceanus Procellarum; global average ~4.2 ppb (Fegley & Swindle 1993); highest measured ~10 ppb; inventory 6.6×10⁸ kg (blog repost of abstract) — [Crawford, Lunar Resources review, arXiv 1410.6865](https://arxiv.org/pdf/1410.6865); [Lunar Networks](https://lunarnetworks.blogspot.com/2010/12/change-1-maps-moons-helium-3-inventory.html)

### Inferences
- Reaction data (standard nuclear physics, not re-fetched): D+T → α(3.5 MeV)+n(14.1 MeV), Q=17.6 MeV, 80 % in neutron — CORRECT; D+He3 → α(3.6)+p(14.7), Q=18.3 MeV — CORRECT; D+D → T(1.01)+p(3.02) or He3(0.82)+n(2.45), ~50/50 — CORRECT; side D-D in D-He3 fuel gives a few % of power as neutrons — CORRECT qualitatively. T must be bred from Li-6 (n + Li-6 → T + α + 4.8 MeV).
- Game gates: D-T requires lithium; D-He3 requires He-3 source (regolith of airless body exposed to stellar wind, or gas-giant atmosphere where ³He/⁴He ~10⁻⁴); D-D requires only deuterium (oceans).

### Gaps
- Gas-giant He-3 abundance (Galileo probe ³He/⁴He ≈ 1.66×10⁻⁴) not fetched this session.

---

## 12. Orbital solar power beamed by microwave

### Takeaway
NEEDS CORRECTION. "~50 % or less" is only plausible for the DC→RF→rectenna link; sunlight-to-grid end-to-end is ~10–20 % with current tech. NASA OTPS (Jan 2024) found LCOE $0.61/kWh (RD1) and $1.59/kWh (RD2) vs $0.02–0.05 for terrestrial renewables.

### Cited Findings
- NASA OTPS 2024: lifecycle cost $0.61/kWh (RD1, innovative) and $1.59/kWh (RD2, mature) vs $0.02–0.05 terrestrial renewables; combined improvements could reach $0.03–0.08/kWh; launch assumed $1000/kg; lifecycle emissions 26 and 40 gCO₂e/kWh — [SpaceNews](https://spacenews.com/nasa-report-offers-pessimistic-take-on-space-based-solar-power/); [NASA NTRS slides (not retrievable this session)](https://ntrs.nasa.gov/api/citations/20240008752/downloads/SBSP%20presentation%20slides%20for%20public%20shortened.pdf)
- OTPS reference design assumes ~90 % antenna radiation efficiency, ~98 % atmospheric transmission — [NewSpace Economy](https://newspaceeconomy.ca/2024/01/15/report-space-based-solar-power-nasa-2024/)
- End-to-end SBSP efficiency ~10–20 % with current tech; microwave links at 2.45/5.8 GHz lose <1 % to atmosphere; rectennas can exceed 80 % RF→DC — [IEEE Technology Navigator](https://technav.ieee.org/topic/space-power-stations/)
- ESA SOLARIS is a preparatory programme to inform a decision by Member States; Thales Alenia (2024 IAC) modelled the full efficiency chain — [IAC-24 paper](https://dl.iafastro.directory/event/IAC-2024/paper/86663/); [TechXplore](https://techxplore.com/news/2022-11-esa-wireless-power-space.pdf)

### Inferences
- Game formula: `η_e2e = η_PV · η_DC→RF · η_antenna · η_beam_capture · η_atm · η_rectenna · η_grid` e.g. 0.30·0.80·0.90·0.90·0.98·0.85·0.97 ≈ **0.15**. Link only (DC→DC) ≈ 0.80·0.90·0.90·0.98·0.85 ≈ 0.54.
- GEO SBSP sees sunlight >99 % of the year (only short equinox eclipses) → capacity factor ~90–99 %; rectenna power density typically limited to ≲230 W/m² (peak) for safety (classic DOE/NASA 1979 reference; not re-verified).

### Gaps
- Exact OTPS end-to-end efficiency number not retrieved (ntrs.nasa.gov blocked in this session's network settings).

---

## 13. Storage technologies

### Takeaway
CORRECT with refinements: Li-ion RTE ~83–90 %, vanadium flow ~75 %, pumped hydro ~80 % (70–85 % range fine), iron-air ~40–55 %, hydrogen ~30–40 % (power-to-power). Na-ion is lower energy density (~160 Wh/kg gen-1 CATL vs ~250+ Wh/kg Li-ion NMC cells).

### Cited Findings
- PNNL Energy Storage Cost & Performance Database v.2024 covers Li-ion (LFP/NMC), vanadium flow, lead-acid, zinc, hydrogen, PSH, gravity, CAES, thermal; v.2024 reflects 2023 values (hydrogen and gravity still 2021) — [PNNL ESGC](https://www.pnnl.gov/projects/esgc-cost-performance); [PNNL LCOS](https://www.pnnl.gov/projects/esgc-cost-performance/lcos-estimates)
- PNNL GridPIQ RTE table (older): vanadium redox 75 %, pumped hydro 81 % (DOE 2013), Li-ion 83 % (2014) — [PNNL GridPIQ](https://gridpiq.pnnl.gov/v2-beta/doc/technologies/es/es-efficiency/)
- PNNL hydrogen bi-directional system: electrolyzer + salt-cavern storage + fuel cell, for long-duration — [PNNL hydrogen](https://www.pnnl.gov/projects/esgc-cost-performance/hydrogen-bi-directional)
- Form Energy iron-air: >100 h discharge at nameplate; RTE estimates ~40–50 % (2026 report), modelled 45–55 % (Sacra), vs Li-ion 80–90 % — [Energy-Storage.news](https://www.energy-storage.news/google-bets-big-on-30gwh-of-form-energys-iron-air-battery-storage-despite-efficiency-trade-offs/); [Sacra](https://sacra.com/c/form-energy/)
- CATL first-gen Na-ion: 160 Wh/kg cell, next gen target >200 Wh/kg — [CnEVPost 2021](https://cnevpost.com/2021/07/29/catl-unveils-first-gen-sodium-ion-battery-with-energy-density-of-160wh-kg/)

### Inferences — game table (sourced RTE where cited above; specific energies are industry-typical, not re-verified unless noted)
| Tech | RTE | Specific energy | Typical duration | Lifetime |
|---|---|---|---|---|
| Li-ion (LFP/NMC) | 83–90 % | 160–280 Wh/kg cell | 2–8 h | 10–20 y / 3000–8000 cycles |
| Na-ion | ~85–90 % | ~160 Wh/kg (CATL gen-1) | 2–8 h | similar |
| Vanadium flow | ~75 % | ~15–25 Wh/kg electrolyte | 4–12 h | 20–30 y |
| Iron-air (Form) | ~40–55 % | n/a (stationary) | ~100 h | ~20 y |
| Pumped hydro | 70–85 % (~81 %) | E = ρgh·V: 0.27 Wh/kg at 100 m | 6–24 h | 50–100 y |
| Hydrogen (P2P) | 30–40 % | 33.3 kWh/kg H₂ (LHV) | days–seasons | 20–30 y |
| Molten salt (thermal) | >95 % thermal; electric depends on cycle | ~0.1–0.15 kWh_th/kg | 6–15 h | 30 y |
- Pumped hydro unit test: `E = ρ g h V`; 1 m³ water, h = 100 m, g = 9.81 → 0.981 MJ = **0.2725 kWh**; on Titan with methane (450, 1.352) → 0.0169 kWh.

### Gaps
- PNNL 2024 per-technology RTE values not retrieved; hydrogen 30–40 % not directly sourced this session.

---

## 14. Combustion needs an oxidizer (Titan)

### Takeaway
CORRECT. Combustion is a redox reaction; Titan's atmosphere is ~95 % N₂ / ~5 % CH₄ with no free O₂, so hydrocarbons cannot burn there. Inverse gameplay: on Titan, imported/electrolysed oxygen is the scarce "fuel", and hydrocarbons are the abundant reductant.

### Cited Findings
- Huygens confirmed nitrogen–methane atmosphere at 93.65 K, 1467 hPa (context) — [Fulchignoni et al. 2005 (ESA)](https://sci.esa.int/web/cassini-huygens/-/55210-fulchignoni-et-al-2005)

### Inferences
- CH₄ + 2O₂ → CO₂ + 2H₂O, ΔH ≈ −890 kJ/mol (55.5 MJ/kg CH₄; per kg of CH₄+O₂ mixture only ~11 MJ/kg) — standard thermochemistry. On Titan, water ice is "rock"; O₂ must come from electrolysis of ice (energy-negative). Biomass on any planet likewise needs an oxidizing atmosphere (or oxidizer supply) to burn.
- Alternative Titan chemical energy: acetylene hydrogenation (C₂H₂ + 3H₂ → 2CH₄, ~334 kJ/mol), proposed as a possible Titan biochemical energy source (McKay & Smith 2005; not fetched this session).

### Gaps
- Precise Titan atmospheric CH₄ mole fraction near surface (~5.7 %, Niemann et al. 2005) not fetched.

---

## 15. EROI of energy sources

### Takeaway
CORRECT that EROI is contested: Weissbach et al. 2013 (buffered, Germany) put nuclear/hydro/coal/gas an order of magnitude above PV and wind, but critics and other studies give much higher PV/wind values; the World Nuclear Association itself recalculated Weissbach's nuclear 105 down to ~70. Use ranges, not single numbers.

### Cited Findings
- Weissbach et al. 2013 (Energy 52:210): nuclear, hydro, coal, gas (in that order) ~one order of magnitude more effective than PV and wind; storage ("buffering", modelled with pumped hydro) reduces EROI markedly; PV, biomass and buffered wind below "economic threshold" — [IDEAS/RePEc](https://ideas.repec.org/a/eee/energy/v52y2013icp210-221.html); [Weissbach preprint](https://festkoerper-kernphysik.de/Weissbach_EROI_preprint.pdf?ref=vc.ru)
- WNA: Weissbach nuclear EROI 105; WNA recalculation 70 (fuel inputs 60 % higher); Vattenfall-based 56 and 50 for two Swedish plants — [World Nuclear Association](https://world-nuclear.org/information-library/energy-and-the-environment/energy-return-on-investment) (via search summary; page not fetchable this session)
- Kunz, Hagens & Balogh 2014 examined how renewable output variability affects net-energy/EROI — [landscapepartnership.org PDF](https://landscapepartnership.org/maps-data/climate-context/cc-resources/ClimateSciPDFs/EROI.pdf)

### Inferences — game ranges (literature-typical; values marked † are from memory of Weissbach 2013 Fig. and must be checked against the preprint)
| Source | EROI range | Notes |
|---|---|---|
| Hydro | ~35–100 (Weissbach† ~35–50) | site-dependent |
| Nuclear (LWR) | ~15–105 (Weissbach 75–105†; WNA 70) | enrichment method dominates |
| Coal | ~20–40 (Weissbach† ~30) | excludes CO₂ externalities |
| Gas CCGT | ~20–30 (Weissbach† ~28) | |
| Wind | ~16 unbuffered / ~4 buffered (Weissbach†); other studies ~20+ | |
| Solar PV | ~4 unbuffered / ~2 buffered in Germany (Weissbach†); other meta-analyses ~8–30 for sunnier sites | most contested |
| CSP (desert) | ~9–19 (Weissbach†) | |
- "~7:1 minimum for modern industrial society" (Hall-type threshold) appears only in a secondary source this session — treat as heuristic.

### Gaps
- Could not retrieve Hall et al. 2014 (Energy Policy), Murphy & Hall 2010, Raugei et al. 2015 critique, or Bhandari et al. 2015 PV meta-analysis; numbers marked † need verification.

---

## 16. Superconducting transmission, Al vs Cu, HVDC losses

### Takeaway
Claim that Titan's surface (93.65 K) is slightly ABOVE YBCO's Tc (~92–93 K) is CORRECT — so NEEDS CORRECTION if the game lets YBCO cables run passively on Titan. Practical HTS operation needs T well below Tc (~65–77 K) for useful critical current; Titan only cuts cryocooler work (ΔT ~20–30 K instead of ~220 K on Earth). Hg-1223 (Tc 133 K ambient record since 1993; a 2026 pressure-quench claim of 151 K) would superconduct passively on Titan. HVDC ~3 %/1000 km CORRECT.

### Cited Findings
- YBCO Tc ≈ 93 K (Wu et al. 1987 PRL); Tc depends on oxygen content, peaks ~95 K near x ≈ 0.07 — [APS News](https://www.aps.org/publications/apsnews/200307/prl-2.cfm); [Wikipedia: YBCO](https://en.wikipedia.org/wiki/Yttrium_barium_copper_oxide)
- Titan surface T = 93.65 ± 0.25 K — [Fulchignoni et al. 2005 (ESA)](https://sci.esa.int/web/cassini-huygens/-/55210-fulchignoni-et-al-2005)
- Hg-1223 ambient-pressure record Tc 133 K since 1993; 2026 PNAS (Deng, Chu et al.) claims 151 K at ambient pressure via pressure-quench (metastable; stable ≥3 days at 77 K; degrades above 200 K) — [Physics World](https://physicsworld.com/?p=127303); [APS Physics](https://physics.aps.org/articles/v19/37)
- Hg-1223 up to 153 K at 150 kbar (Chu group, Nature 1993) — [Nature 365:323](https://www.nature.com/articles/365323a0.pdf)
- HVDC ±800 kV losses ~3 %/1000 km vs ~7 % HVAC; other sources 3–5 %/1000 km — [arXiv 1207.4096 "The Global Grid"](https://arxiv.org/pdf/1207.4096); [Thunder Said Energy](https://thundersaidenergy.com/downloads/high-voltage-transmission-cables-power-parameters/)

### Inferences
- Al vs Cu resistivity: ρ_Cu ≈ 1.68×10⁻⁸ Ω·m, ρ_Al ≈ 2.65×10⁻⁸ Ω·m at 20 °C → ratio **1.58** (design doc "~1.6×" CORRECT; standard handbook values, not fetched). Al wins on conductance per kg (~2×) and cost, hence overhead lines use Al (ACSR).
- Resistivity falls with temperature: on cold worlds normal conductors are much better (Cu residual-resistivity ratio can be ~50–100 near 20–90 K; at ~90 K Cu resistivity ≈ 0.3× room-T — handbook-typical, not fetched). Game: `ρ(T) ≈ ρ_293 · [1 + α (T − 293)]`, α_Cu = 0.00393/K, valid down to ~100 K (gives ~0.24× at 94 K; slight overestimate of benefit).
- HVDC game formula: `loss_fraction = 0.03 · (L / 1000 km)` + ~0.7 % per converter station (converter loss typical, not sourced).
- Cryocooler penalty: ideal Carnot work per W removed = (T_amb − T_cold)/T_cold; at 77 K from Earth 300 K → 2.9 W/W ideal (real ~10–20 W/W); from Titan 94 K → 0.22 W/W ideal (~13× cheaper).
- LN₂ cannot exist passively on Titan surface: N₂ boils at 77 K at 1 atm (~81 K at 1.5 bar), below 94 K.

### Gaps
- Al/Cu resistivities, N₂ boiling point at 1.5 bar, and converter-station losses taken from standard handbooks, not fetched this session.
