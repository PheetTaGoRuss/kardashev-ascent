# Stellar spectra vs PV bandgap, irradiance on tidally locked and circumbinary planets, and the NASA Exoplanet Archive as a data source (Kardashev Ascent fact-check)

Research conditions (read first): in this session the network blocked both `exoplanetarchive.ipac.caltech.edu` and `ncbi.nlm.nih.gov`. Both direct curl and WebFetch failed with "connect_rejected" or "ENOTFOUND". Because of that, (a) no live TAP query of `pscomppars` could be run, and (b) all literature facts below come from web-search result snippets of the cited pages, not from full-text reads. The detailed-balance numbers in section 1 were **computed in this session** with a Python script, following the standard Shockley–Queisser (SQ) method (blackbody source, radiative recombination only, cell at 300 K, 1000 W/m² incident). They are labelled "computed" and should be treated as reproducible model output. They are not quotations from a paper. To finish the archive part, allow `exoplanetarchive.ipac.caltech.edu` in the session's network settings and rerun the query given in section 5.

## 1. Star type → light colour → best PV material (Wien, cutoff, SQ under stellar spectra, f_spec)

### Takeaway
VERDICT: CORRECT in direction, but some numbers NEED CORRECTION. Cooler stars move the detailed-balance optimum to lower bandgap: about 0.64–0.82 eV for M dwarfs (2600–3500 K), about 1.0 eV for K stars, and about 1.3–1.4 eV for G/F stars. The single-junction ceiling also drops, from about 30–31% (G/F blackbody) to about 22–25% (M blackbody). This matches the peer-reviewed result of Schopp et al. 2023 (0.7 eV → 23% for M dwarfs). Silicon keeps most of its performance under K stars but loses about 40–57% of its relative efficiency under M dwarfs. GaAs and CdTe lose 65–80%. Germanium is close to optimal under M dwarfs. The formulas (Wien b = 2.898e-3 m·K, λ_c ≈ 1240/E_g) are CORRECT. The bandgap values listed in the doc were not re-verified in this session, but they match the standard values used below.

### Cited Findings
- Schopp, Abdikamalov, Mostovyi et al., "Interstellar photovoltaics," *Scientific Reports* 13 (2023), DOI 10.1038/s41598-023-43224-5. For single-junction cells, the optimal bandgap is ≈0.7 eV with ≈23% SQ efficiency for the coolest red dwarfs (M0, M5.5Ve, M8V). For the hottest type (O5V) the optimum gives 47% SQ efficiency. The abstract's ">12 eV" optimum for O5V looks like a typo, but this could not be confirmed. — [Schopp et al. 2023 (DOAJ record)](https://doaj.org/article/dff31f41694d49aa907876d00c2689b4); [eScholarship PDF](https://escholarship.org/content/qt96b5296b/qt96b5296b.pdf?t=s5wljp)
- In the same paper, a narrow-gap organic blend PM2:COTIC-4F (E_g = 1.14 eV) has a theoretical efficiency of 22.6% under the Sun (G2V) and 12.6% under Proxima Cen (M5.5Ve). A wide-gap PM6:o-IDTBR blend falls from about 18–19% (G2V) to 0.9% (M5.5Ve). — [Schopp et al. 2023](https://doaj.org/article/dff31f41694d49aa907876d00c2689b4); [phys.org summary](https://phys.org/news/2023-10-solar-panels-proxima-centauri.html)
- The ultimate-efficiency bandgap scales with stellar temperature as about 2.2 k_B·T_s. That gives about 1.1 eV for the Sun (silicon), which is why Si underperforms around M dwarfs. — [Interstellar photovoltaics, PMC10522670 (search snippet)](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC10522670/)
- An earlier arXiv paper argues that silicon's near-IR response suits M-star emission better than wide-gap cells do. — [Photovoltaic Power Generation in the Stellar Environments, arXiv:1012.0721](https://arxiv.org/pdf/1012.0721)
- Red dwarfs emit far less total light, so absolute power per panel is much lower even at good efficiency. — [Universe Today](https://www.universetoday.com/163512/will-solar-panels-work-at-proxima-centauri/)
- The NREL Best Research-Cell Efficiency Chart page (last update seen: May 14, 2025) flags the latest record for each technology. A 2024 revision added a hybrid-tandem category and merged the III-V multijunction subcategories. — [NREL chart](https://www.nrel.gov/pv/cell-efficiency.html); [NREL 2024 news](https://www2.nrel.gov/news/program/2024/popular-nrel-cell-efficiency-chart-now-better-presents-tandem-photovoltaics)

**Computed in this session (detailed balance, blackbody source scaled to 1000 W/m², T_cell = 300 K, step absorptance, radiative limit). Use as game data and unit-test values:**

| T_eff (K) | Wien λ_max (nm) | Optimal E_g (eV) | Max SQ η | η Si 1.12 | η GaAs 1.42 | η CdTe 1.50 | η Ge 0.67 | Ultimate-eff. optimum E_g (≈2.17 kT) |
|---|---|---|---|---|---|---|---|---|
| 2600 (≈TRAPPIST-1) | 1115 | 0.64 | 21.8% | 12.7% | 6.6% | 5.4% | 21.7% | 0.49 |
| 3000 (≈Proxima) | 966 | 0.72 | 23.5% | 17.3% | 10.7% | 9.2% | 23.3% | 0.56 |
| 3500 (early M) | 828 | 0.82 | 25.2% | 22.0% | 15.8% | 14.1% | 24.0% | 0.65 |
| 4500 (K) | 644 | 1.02 | 27.9% | 27.6% | 24.0% | 22.7% | 23.1% | 0.84 |
| 5778 (Sun, G2) | 502 | 1.27 | 30.2% | 29.7% | 29.8% | 29.3% | 20.6% | 1.08 |
| 6500 (F) | 446 | 1.41 | 31.3% | 29.6% | 31.3% | 31.1% | 19.2% | 1.21 |

**f_spec = η(star blackbody) / η(5778 K blackbody), computed:**

| T_eff | Si | GaAs | CdTe | Ge |
|---|---|---|---|---|
| 2600 | 0.43 | 0.22 | 0.19 | 1.05 |
| 3000 | 0.58 | 0.36 | 0.31 | 1.13 |
| 3500 | 0.74 | 0.53 | 0.48 | 1.16 |
| 4500 | 0.93 | 0.81 | 0.78 | 1.12 |
| 5778 | 1.00 | 1.00 | 1.00 | 1.00 |
| 6500 | 1.00 | 1.05 | 1.06 | 0.93 |

Photon cutoff unit tests (λ_c = 1239.84/E_g nm): Ge 0.67 eV → 1850 nm; Si 1.12 → 1107 nm; 1.34 → 925 nm; GaAs 1.42 → 873 nm; CdTe 1.50 → 827 nm. Wien peaks (b = 2.897771955e-3 m·K): 2566 K → 1129 nm; 3042 K → 953 nm; 3311 K → 875 nm; 4450 K → 651 nm; 5772 K → 502 nm.

### Inferences
- **Recommended simple f_spec model for the game (blackbody detailed balance).**
  `Jsc = q·Φ(E>E_g; T_star)·(G/σT⁴)`, `J0 = q·Φ(E>E_g; T_cell=300 K)`, `η = max_V [V·(Jsc − J0(e^{V/V_t} − 1))]/G`, where Φ is the hemispherical blackbody photon flux `2π(kT)³/(h³c²)·∫x²/(eˣ−1)dx`. Compute it once offline per (T_star, E_g), then bake it into a lookup table or a 2D curve in Unity. That is cheaper and more faithful than a runtime integral.
  An even cheaper proxy is the "ultimate efficiency" `u = E_g·Φ(>E_g)/σT⁴`. It captures the spectral-mismatch shape but has no voltage losses, so it peaks at 43.9% for every T. Normalising u by its 5778 K value gives a rough f_spec.
- **Wording correction for the design doc.** "Red dwarfs need low-bandgap material" is right. More precisely: the optimum is about 0.65–0.8 eV, not just "lower than Si". Si (1.12 eV) remains usable (f ≈ 0.4–0.75). Wide-gap cells (CdTe, GaAs, wide-gap perovskite ~1.6+ eV) are poor choices under M stars. A perovskite tuned down to its ~1.2 eV floor still sits above the M-dwarf optimum.
- **Multijunction note.** Tandem or multijunction stacks would be re-optimised for each spectrum. A reasonable game simplification is to apply a multiplier to the single-junction f_spec rather than model each subcell.
- **Real stellar spectra differ from blackbodies.** M-dwarf spectra contain strong molecular absorption bands (TiO, H₂O). This is why Schopp et al. used real/model spectra, and their numbers (0.7 eV / 23%) agree with the blackbody table to about ±1 point. "Blackbody detailed balance" is therefore an ACCEPTABLE SIMPLIFICATION.
- **Blackbody vs AM1.5G.** A 5778 K blackbody gives about 30% at about 1.3 eV. The doc's AM1.5G figure (~33.7% at 1.34 eV) is higher because the atmospheric absorption bands reshape the spectrum. If the game uses "AM1.5" as the f_spec reference, the ratio is consistent only if both the numerator and the denominator are computed with the same model. Recommendation: use the 5778 K blackbody as the reference and call it "Sun-like" in game text. Reserve AM1.5G for the efficiency numbers shown to the player.

### Gaps
- The SQ limit of 33.7% at 1.34 eV under AM1.5G (Rühle 2016 and others) is widely quoted but was not re-verified from a source in this session.
- The current NREL records (e.g. the 47.6% four-junction concentrator cell from Fraunhofer ISE, the one-sun multijunction record, the perovskite/Si tandem record of about 34–35%) could not be confirmed. The search did not surface the chart's values. Read them directly from nrel.gov/pv/cell-efficiency.
- The full text of Schopp et al. (their per-spectral-type table) was blocked (PMC host), so only the abstract-level numbers are cited.

## 2. Red dwarf flares damaging equipment (Proxima Cen, TRAPPIST-1)

### Takeaway
VERDICT: ACCEPTABLE SIMPLIFICATION. Frequent, energetic flares, strong UV/XUV, and extreme stellar-wind pressure are well documented for Proxima Cen and TRAPPIST-1. Equipment damage (UV/particle degradation of cells, electronics upsets) is a reasonable game extrapolation. Measured energetic-particle fluxes at these planets were not found and should be presented as modelled, not observed.

### Cited Findings
- **Proxima Cen superflare (Howard et al. 2018, Evryscope).** In March 2016 the star brightened about 68× in optical light, about 10^33.5 erg. The authors list 23 other large flares of 10^30.6–10^32.4 erg over about two years and predict at least 5 superflares per year. They estimate the surface UV-C would be about 100× the lethal dose for UV-hardy microorganisms, assuming no ozone. — [Howard et al. 2018, arXiv:1804.02001](https://arxiv.org/pdf/1804.02001.pdf)
- **Proxima Cen ALMA flare (MacGregor et al. 2018).** On 24 March 2017 the star's millimetre brightness rose about 1000× within about 10 s. — [NRAO release](https://public.nrao.edu/news/2018-alma-flare-proxima/)
- **2021 multiwavelength campaign (MacGregor et al. 2021).** This was the first campaign with simultaneous mm observations (ALMA plus TESS, Evryscope and others). A 2025 follow-up built the first mm flare-frequency distribution for an M dwarf from about 50 h of ALMA data. — [MacGregor CoolStars 2021 slides](https://zenodo.org/records/4563381/files/CoolStars2021_MacGregor.pdf); [arXiv:2503.21890](https://web3.arxiv.org/pdf/2503.21890)
- **Stellar wind (Garraffo, Drake & Cohen 2016, ApJL 833, L4).** Wind pressure at Proxima b is more than 2000× Earth's. It swings by 1–3 orders of magnitude within about a day, changing the magnetopause standoff distance by a factor of 2–5. — [arXiv:1609.09076](https://arxiv.org/pdf/1609.09076)
- **XUV (Ribas et al.).** Slides report that Proxima's past XUV was about 60× the Sun's and that Proxima b's XUV dose is a large multiple of Earth's, roughly 8–25× according to the slide summary, citing Ribas et al. 2017 A&A. Treat these values as approximate. — [Ribas ESLAB 2017 slides](https://www.cosmos.esa.int/documents/1566003/1582277/07dec0945_Ribas_ESLAB_2017.pdf/75bca136-6ee0-049f-1967-5372bb80adda)
- **TRAPPIST-1 (Vida et al. 2017, K2).** Over about 80 days, 42 flares of 1.26×10^30–1.24×10^33 erg were detected (about 12% complex/multi-peak). The rotation period is about 3.295 d. The authors conclude that the flaring probably continuously alters the planets' atmospheres. — [Vida et al. 2017, arXiv:1703.10130](https://arxiv.org/pdf/1703.10130)
- Wheatley et al. 2017 is cited by Vida et al. for significant X-ray/EUV emission from TRAPPIST-1. — [Vida et al. 2017](https://arxiv.org/pdf/1703.10130)

### Inferences
- **Flare rate for game data.** TRAPPIST-1 shows about one detected flare per about 1.9 days (42 / 80 d, at the K2 sensitivity limit). Proxima shows at least 5 superflares (>10^33 erg) per year, plus frequent smaller flares. A power-law flare-frequency distribution (more small flares, few large ones) is the physically right shape for a random-event generator.
- **Optical flares and power output.** Optical flare energy briefly raises the bolometric output, by at most percent-level for large flares. Its main gameplay effect should be damage or degradation, not a power bonus.

### Gaps
- No measured stellar-energetic-particle (proton) fluxes at Proxima b or TRAPPIST-1e were found. The literature uses models (e.g. Fraschetti et al. 2019 for TRAPPIST-1), but none were retrieved in this session.
- No source was found on PV or electronics degradation rates under M-dwarf flare environments. Any game damage numbers are design choices.
- The "flare every ~28 h" figure for TRAPPIST-1 seen in some sources could not be traced.

## 3. Tidally locked planets: fixed star, cos z formula, libration, Proxima b / TRAPPIST-1e parameters, thermoelectric use

### Takeaway
VERDICT: formula CORRECT. For zero obliquity, zero eccentricity and synchronous rotation, cos z = cos(lat)·cos(lon − lon_sub), and the panel gets G·max(0, cos z) for a horizontal panel. Proxima b and TRAPPIST-1e are commonly *assumed* to be tidally locked, which is plausible but unconfirmed. With eccentricity, the star librates in longitude by about ±2e radians, and a 3:2 spin–orbit resonance is an alternative for Proxima b. Day–night thermoelectric power is physically plausible in principle. However, atmospheric heat transport strongly reduces the contrast on planets with thick atmospheres, so it should be a niche, low-efficiency mechanic.

### Cited Findings
- **Proxima b orbit (Met Office UM study, Boutle et al. 2017).** a = 0.0485 AU, P = 11.186 d, irradiance 881.7 W/m² = 0.646 S⊕. They modelled e = 0 for the 1:1 case and e = 0.3 for the 3:2 resonance case. — [Boutle et al. 2017, arXiv:1702.08463](https://web3.arxiv.org/pdf/1702.08463)
- **Proxima b, other values.** About 65% of Earth's irradiation and T_eq = 234 K. The planet is "likely" tidally locked, but this is not certain; a non-locked state (e.g. 3:2) would cause tidal heating. — [Wikipedia: Proxima Centauri b](https://en.wikipedia.org/wiki/Proxima_Centauri_b); discovery paper [Anglada-Escudé et al. 2016 context via Ribas et al. 2016 A&A](https://www.aanda.org/articles/aa/full_html/2016/12/aa29577-16/aa29577-16.html)
- **TRAPPIST-1e (Agol et al. 2021 via TEPCat).** P = 6.101013 ± 0.000035 d, a = 0.02925 ± 0.00025 AU, e = 0 (+0.085 upper bound), T_eq = 246.1 ± 3.5 K. Wikipedia lists e = 0.00510 ± 0.00058. An A&A 2020 table gives T_eq = 249.7 ± 2.4 K. — [TEPCat TRAPPIST-1e](https://astro.keele.ac.uk/jkt/tepcat/planets/TRAPPIST-1e.html); [Wikipedia TRAPPIST-1e](https://en.wikipedia.org/wiki/TRAPPIST-1e); [A&A 2020 Table 4](https://www.aanda.org/articles/aa/full_html/2020/08/aa37392-19/T4.html)
- **Tidally locked climate (Merlis & Schneider 2010).** Day-to-night heat transport "efficiently reduces temperature contrasts" in both slow and fast rotators. Winds are roughly isotropic and divergent in the slow-rotation regime and mostly zonal when rotation is fast. — [Merlis & Schneider 2010, arXiv:1001.5117](https://arxiv.org/pdf/1001.5117)
- **Heat engine view (Koll & Abbot).** Tidally locked atmospheres act as global heat engines that constrain wind speeds. Rocky planets can sustain large day–night gradients, and the nightside, terminator and poles are roughly uniformly cold. — [Koll & Abbot, arXiv:1605.01066](https://www.arxiv.org/pdf/1605.01066); [UChicago dissertation](https://knowledge.uchicago.edu/records/sx2bt-y5f93); [Connecting the dots III, arXiv:1605.09545](https://arxiv.org/pdf/1605.09545)

### Inferences
- **Unit-test values for cos z = cos(lat)·cos(Δlon):** (0°, 0°) → 1.0; (0°, 60°) → 0.5; (45°, 0°) → 0.7071; (30°, 45°) → 0.6124; (0°, 90°) → 0.0 (terminator); (60°, 120°) → −0.25 → clamp to 0 (nightside).
- **Libration (derived from the equation of centre, ν − M ≈ 2e·sin M).** For a synchronously spinning planet on an eccentric orbit, the substellar longitude oscillates by about ±2e rad per orbit, with insolation varying as (1−e²)²/(1+e cos ν)². Example values:
  - TRAPPIST-1e with e ≈ 0.005: about ±0.6°, which is negligible.
  - Proxima b with e = 0.1: about ±11.5° and a flux swing of about ±20%.
  - Game simplification: `lon_sub(t) = 2e·sin(2πt/P)`.
- **Equilibrium temperatures computed for unit tests** (T_eq = 278.6 K·S^{1/4}·(1−A)^{1/4}, full redistribution):
  - Proxima b, S = 0.65: 250 K at A = 0; 229 K at A = 0.3. The paper's 234 K uses its own albedo and redistribution assumptions.
  - Working backwards, TEPCat's 246.1 K for TRAPPIST-1e implies S ≈ 0.61 (A = 0). This conflicts with the commonly quoted ~0.65 S⊕, so check the archive's pl_insol.
  - Lesson: archive pl_eqt values mix conventions, so do not assert exact equality in unit tests.
- **Thermoelectric and heat-engine gameplay.** A Carnot limit of 1 − T_cold/T_hot with, say, a 300 K dayside and a 200 K nightside gives about 33% maximum. Real thermoelectrics reach about 5–10% of that difference in practice (general engineering knowledge, not sourced here). This is plausible as a low-yield, always-on base-load source, strongest on thin-atmosphere or airless locked planets where contrasts are large. Terminator wind turbines are plausible for the same heat-engine reason. Note that "winds blow toward the dayside at the surface" is the textbook slow-rotator pattern.

### Gaps
- Newer Proxima b values (Faria et al. 2022 ESPRESSO: refined P and m sin i ≈ 1.07 M⊕) and current archive e values were not retrieved.
- The text of Joshi et al. 1997 (the classic synchronous-rotation GCM) was not retrieved.
- No quantitative source on thermoelectric efficiency for exoplanet day–night harvesting was found. The Carnot estimate above is an inference.

## 4. Circumbinary Kepler-16b; S-type vs P-type; Alpha Centauri AB

### Takeaway
VERDICT: CORRECT. Kepler-16b is a P-type (circumbinary) planet orbiting a K + M binary.
- Binary period ≈ 41.08 d; planet period ≈ 228.8 d.
- The panel formula G_total = Σ G_i·max(0, cos θ_i) is the correct superposition.
- Computed insolation ≈ 0.31 S⊕ on average, varying between about 0.28 and 0.35 S⊕ (±13%) over the ~50 d synodic cycle.
- Star B contributes only about 4%.

One NEEDS CORRECTION item: the "~6 h partial / ~2 h total eclipse" figures in popular sources describe eclipses as seen from **Earth**, not from the planet. From the planet, the two suns line up roughly every 25 days (half the 50 d synodic period), because the orbits are nearly coplanar.

### Cited Findings
- **Kepler-16 masses (Doyle et al. 2011, Science).** M_A = 0.6897 ± 0.0035 M☉, M_B = 0.20255 ± 0.00065 M☉. Bender et al. found 0.654 ± 0.017 and 0.1959 ± 0.0031 M☉, and a 2025 MNRAS paper revisited the dynamical masses. — [arXiv:1205.0259](https://www.arxiv.org/pdf/1205.0259); [Wikipedia: Kepler-16](https://en.wikipedia.org/wiki/Kepler-16)
- **Orbits.** The stars orbit each other every about 41 d. The planet's period is about 229 d (228.78 d) on a near-circular orbit (e = 0.007) at about 0.7 AU. The planet mass is about 0.33 M_J, Saturn-like. — [Springer Encyclopedia of Astrobiology](https://link.springer.com/referenceworkentry/10.1007/978-3-642-27833-4_5295-3); [Flux variation on circumbinary planets, arXiv:2008.04992](https://arxiv.org/pdf/2008.04992)
- **Stars.** Kepler-16A: L = 0.148 L☉, T_eff = 4450 ± 150 K. Kepler-16B: T_eff ≈ 3311 K, L ≈ 0.0057 L☉. — [Wikipedia: Kepler-16](https://en.wikipedia.org/wiki/Kepler-16)
- **Eclipses seen from Earth.** The primary eclipse is partial, lasts about 6 h and dims by about 0.15 mag. The secondary is a total occultation of B, lasts about 2 h and dims by about 0.02 mag. — [Wikipedia: Kepler-16](https://en.wikipedia.org/wiki/Kepler-16)
- **Flux variability.** Even with near-zero e, Kepler-16b "experiences significant variability in its incident flux". — [arXiv:2008.04992](https://arxiv.org/pdf/2008.04992)
- **P-type definition.** A circumbinary (P-type) planet orbits both stars. — [Wikipedia: Circumbinary planet](https://en.wikipedia.org/wiki/Circumbinary_planet)

**Computed in this session** (circular coplanar orbits; a_bin = 0.2243 AU and a_p = 0.7047 AU from Kepler's third law with the masses and periods above; this matches the published ~0.705 AU). The barycentric radii are a_A = 0.0509 AU and a_B = 0.1734 AU.

| Quantity | Min | Mean | Max |
|---|---|---|---|
| S_A (S⊕) | 0.259 | 0.300 | 0.346 |
| S_B (S⊕) | 0.0074 | 0.0122 | 0.0202 |
| S_total (S⊕) | 0.279 | 0.312 | 0.354 |

- T_eq (mean S, full redistribution) is about 208 K at A = 0 and about 190 K at A = 0.3.
- The synodic binary period seen from the planet is 1/(1/41.079 − 1/228.776) = 50.07 d.
- The maximum angular separation of the two suns is about 17.7°.
- The suns have different colours (4450 K vs 3311 K), so f_spec should be applied **per star**: G_eff = Σ_i G_i·max(0, cos θ_i)·f_spec(T_i, E_g).

### Inferences
- **Mutual stellar eclipses seen from Kepler-16b.** These are expected about twice per 50 d synodic cycle, because the planet's orbit is nearly coplanar with the binary. When B passes in front of A, the drop is small: R_B/R_A ≈ 0.35 by area ratio, and B is a fraction of A's disk. When A covers B, the planet loses B's roughly 4% contribution. Overall, eclipse dimming of a few percent up to about 10% is a reasonable game value. This is derived from geometry, not quoted.
- **Gameplay.** For a panel, compute each star's own direction vector and sum the terms. Using one "combined star" at the barycentre is an ACCEPTABLE SIMPLIFICATION only for long-timescale averages (error ≤ about 13%). For time-resolved gameplay, model both stars.

### Gaps
- Dvorak (1986) as the origin of the S-type/P-type terminology was not retrieved in this session. The definitions themselves are standard: S-type orbits one star of the binary, P-type orbits both.
- No sourced values were retrieved for **Alpha Centauri AB** (S-type example: binary period about 79.9 yr, a ≈ 23 AU, eccentricity ≈ 0.52, A ≈ G2V, B ≈ K1V; these are standard values but unverified here). The current planet status (e.g. the 2025 JWST candidate around α Cen A) was also not verified.
- Doyle et al.'s own quoted planet T_eq (often 170–200 K) was not retrieved.

## 5. NASA Exoplanet Archive: TAP endpoint, ps vs pscomppars, columns, and current values

### Takeaway
VERDICT: CORRECT as a data source. The endpoint is `https://exoplanetarchive.ipac.caltech.edu/TAP/sync?query=<ADQL>&format=csv|json` (async jobs go to `/TAP/async`).
- `ps` (Planetary Systems) has one row per planet *per reference*. Filter it with `default_flag=1` to get one default solution per planet.
- `pscomppars` (Planetary Systems Composite Parameters) has one row per planet, with each column filled from whichever reference provides it. This makes it convenient for a game, but the columns may be **internally inconsistent** because they come from different papers.
- `st_lum` is log10(L/L☉), and `pl_insol` is in units of Earth flux.
- Live values could not be pulled in this session because the host was blocked.

### Cited Findings
- **TAP access.** TAP is the archive's programmatic access route. The sync endpoint is `/TAP/sync` and the async endpoint is `/TAP/async`. Sync is blocking but fine for these table sizes (thousands of rows). Example queries use `from ps` (with `default_flag=1` to show only the default solution) and `from pscomppars`. — [Archive TAP guide](https://exoplanetarchive.ipac.caltech.edu/docs/TAP/usingTAP.html); [nexsciTAP architecture](https://caltech-ipac.github.io/nexsciTAP/tap_architecture.html)
- **Table history.** PS and PSCompPars replaced the retired Confirmed Planets, Composite Parameters and Extended Planet Data tables in 2021. Old API queries must be ported. — [astroquery NExScI docs](https://sources.debian.org/src/astroquery/0.4.11%2Bdfsg-4/docs/ipac/nexsci/nasa_exoplanet_archive.rst)
- **Column units.** `st_lum` = "Stellar Luminosity [log10(Solar)]". `pl_insol` = "Insolation Flux [Earth flux]". — [Archive column docs (Mission Stars / PS)](https://exoplanetarchive.ipac.caltech.edu/docs/API_mission_stars.html); [PSCompPars columns](https://exoplanetarchive.ipac.caltech.edu/docs/API_PSCompPars_columns.html); [PS columns](https://exoplanetarchive.ipac.caltech.edu/docs/API_PS_columns.html)
- **Kepler-452 in the archive.** The archive's stellar values (T_eff about 5430–5580 K, R about 0.8 R☉ in some solutions) differ from the discovery paper, and the planet carries a "Confirmed Controversial"-type flag. — [Archive overview: Kepler-452](https://exoplanetarchive.ipac.caltech.edu/overview/Kepler-452)

**Column schema.** Names and units below are from the archive's PS/PSCompPars column documentation as remembered. Only `st_lum` and `pl_insol` were confirmed by search snippet in this session; the rest must be re-checked against [API_PS_columns](https://exoplanetarchive.ipac.caltech.edu/docs/API_PS_columns.html) before coding.

| Column | Meaning | Unit |
|---|---|---|
| pl_name / hostname | planet / host name | string |
| sy_snum | number of stars in system | count |
| pl_orbper | orbital period | days |
| pl_orbsmax | semi-major axis | au |
| pl_orbeccen | eccentricity | — |
| pl_orbincl | inclination | deg |
| pl_insol | insolation flux | Earth flux (S⊕) — confirmed |
| pl_eqt | equilibrium temperature | K (assumptions vary per reference) |
| pl_rade / pl_radj | radius | Earth / Jupiter radii |
| pl_bmasse | "best" mass (true mass, M sin i, or M sin i/sin i; see pl_bmassprov) | Earth masses |
| pl_dens | density | g/cm³ |
| st_teff | stellar effective temperature | K |
| st_rad / st_mass | stellar radius / mass | R☉ / M☉ |
| st_lum | stellar luminosity | **log10(L/L☉)** — confirmed |
| st_spectype | spectral type | string |
| st_age | age | Gyr |
| st_met | metallicity | dex (ratio given in st_metratio, [Fe/H] or [M/H]) |
| sy_dist | distance | pc |
| discoverymethod | discovery method | string |
| pl_projobliq / pl_trueobliq | projected / true **stellar** obliquity (spin–orbit angle) | deg — NOT the planet's axial tilt |
| st_rotp (if present) | stellar rotation period | days |

Ready-to-run query (failed here only because of the network block):
`https://exoplanetarchive.ipac.caltech.edu/TAP/sync?query=select+pl_name,hostname,sy_snum,pl_orbper,pl_orbsmax,pl_orbeccen,pl_orbincl,pl_insol,pl_eqt,pl_rade,pl_radj,pl_bmasse,pl_dens,st_teff,st_rad,st_mass,st_lum,st_spectype,st_age,st_met,sy_dist,discoverymethod+from+pscomppars+where+pl_name+in+('Proxima+Cen+b','TRAPPIST-1+e','Kepler-16+b','Kepler-452+b','TOI-700+d','LHS+1140+b')&format=csv`

**Reference values from papers (not live pscomppars):**
- **Proxima Cen b:** P 11.186 d, a 0.0485 AU, S 0.646 S⊕, T_eq 234 K. — [Boutle 2017](https://web3.arxiv.org/pdf/1702.08463); [Wikipedia](https://en.wikipedia.org/wiki/Proxima_Centauri_b)
- **TRAPPIST-1 e:** P 6.101013 d, a 0.02925 AU, e ≈ 0, T_eq 246.1 K. — [TEPCat](https://astro.keele.ac.uk/jkt/tepcat/planets/TRAPPIST-1e.html)
- **Kepler-16 (AB) b:** P ≈ 228.78 d, a ≈ 0.70 AU, e 0.007, M ≈ 0.33 M_J, sy_snum = 2. — [Springer](https://link.springer.com/referenceworkentry/10.1007/978-3-642-27833-4_5295-3); [arXiv:2008.04992](https://arxiv.org/pdf/2008.04992)
- **Kepler-452 b:** P 384.843 d, R 1.63 R⊕, S ≈ 1.10 S⊕, host T_eff 5757 ± 85 K (Jenkins 2015). T_eq is 265 K (Wikipedia) vs 220 K (an archive entry), a conflict. — [Jenkins et al. 2015, arXiv:1507.06723](https://www.arxiv.org/pdf/1507.06723); [Wikipedia](https://en.wikipedia.org/wiki/Kepler-452b)
- **TOI-700 d:** P 37.42 d, T_eq ≈ 269 K. Gilbert et al. 2020 give R 1.19 ± 0.11 R⊕ and S ≈ 0.86 S⊕; a Spitzer follow-up gives R 1.144 R⊕. — [Gilbert et al. 2020, arXiv:2001.00952](https://web3.arxiv.org/abs/2001.00952)
- **LHS 1140 b (Cadieux et al. 2024):** M 5.60 ± 0.19 M⊕, R 1.730 ± 0.025 R⊕, P 24.73723 d, a 0.0946 AU. Likely a water world or mini-Neptune, not a bare rocky super-Earth. — [Cadieux et al. 2024, arXiv:2310.15490](https://arxiv.org/pdf/2310.15490)

### Inferences
- **Which table to use.** Use `pscomppars` for game seeding (one row per planet), but recompute derived quantities in-game for consistency. For example: `S = 10^st_lum / pl_orbsmax²` (S⊕, with a in AU) and `T_eq = 278.6·S^{1/4}·(1−A)^{1/4}`. Compare these with pl_insol and pl_eqt within a tolerance of about 10–20%, not exact equality.
- **Circumbinary hosts.** For systems with sy_snum ≥ 2 (e.g. Kepler-16), `st_*` columns describe only one star (normally the primary). The second star's parameters must come from a hand-curated table.
- **Planet obliquity.** The archive has no planetary axial-tilt or rotation column, as far as known. `pl_trueobliq` is the stellar spin–orbit angle. The game must assume zero obliquity and tidal locking for close-in M-dwarf planets as a design choice.
- **Missing values.** Null values are common, e.g. pl_insol or pl_eqt missing for Kepler-16b. The importer must handle NaN or empty CSV fields.

### Gaps
- **Rate limits.** No published TAP rate limit was found. Archive guidance on throttling or bulk use was not located. A conservative approach is to cache the results and query once per build, not at runtime.
- **Live pscomppars values** for all six planets could not be retrieved because the host was blocked. Rerun the query above once `exoplanetarchive.ipac.caltech.edu` is allowed.
- **Exact column existence and units** for pl_projobliq, pl_trueobliq and st_rotp, and the precise definitions of pl_bmasse and st_met, need confirmation from API_PS_columns.html.
- **TRAPPIST-1e insolation.** The ~0.65 S⊕ figure was not confirmed; the TEPCat T_eq implies about 0.61.
