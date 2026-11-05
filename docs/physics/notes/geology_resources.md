# Geology, Mineral-Resource and Spectroscopy Claims: Kardashev Ascent fact-check

Scope: checks 10 claims in the game design doc about procedural ore placement on exoplanets "according to real geology". Each claim gets a verdict: CORRECT, NEEDS CORRECTION (with a fix), or ACCEPTABLE SIMPLIFICATION. Each also gets game-usable rules.

Research budget note: this was a 15-search pass. Some items are standard textbook facts that I did not re-source in this session: Na D and H-alpha wavelengths, Cu and Al resistivities, porphyry and VMS tectonic settings, lunar and Mercury obliquities, and hydride superconductor Tc values. Every such item is marked **[background, not re-verified this session]** and sits under Inferences or Gaps, not Cited Findings. The report writer should treat those as needing one citation check (for example NIST ASD, USGS deposit models, CRC Handbook) before publication.

---

## Claim 1: Heavy metals concentrate along volcanic arcs and hydrothermal veins (porphyry Cu, VMS, epithermal Au)

### Takeaway
ACCEPTABLE SIMPLIFICATION, close to CORRECT. Porphyry Cu-Mo-Au and epithermal Au-Ag form above subduction-zone magmatic arcs. VMS (Cu-Zn-Pb-Au-Ag) forms at submarine hydrothermal vents, at mid-ocean ridges and in back-arc and arc rifts. Suggested wording fix: "base and precious metals (Cu, Mo, Zn, Pb, Au, Ag)" instead of "heavy metals". Fe, Ni, Cr, PGE and U have different deposit systems.

### Cited Findings
- I ran no dedicated search for this claim in this session (budget went to the more contested claims). See Inferences and Gaps.

### Inferences
- **[background, not re-verified this session]** The standard deposit-model framework (USGS deposit models and Economic Geology reviews such as Sillitoe 2010, "Porphyry copper systems", Econ. Geol. 105:3–41) puts the settings as follows:
  - Porphyry Cu(-Mo-Au) forms at 1–6 km depth above oxidized, water-rich arc magmas at convergent margins.
  - High- and low-sulfidation epithermal Au-Ag forms at <1–1.5 km depth in the same arcs, often directly above porphyries.
  - VMS forms on the seafloor where seawater circulates through hot volcanic crust: mid-ocean ridges, back-arc basins and rifted arcs.
- All three need **liquid water** and **active magmatism**, and porphyry and epithermal deposits also need **subduction**, which means plate tectonics. That makes them strong procedural gates.
- Proposed game rules:
  - `porphyry_Cu_Mo_Au`: requires `plate_tectonics == true` AND `surface_or_subsurface_water == true`. Spawn in a band 100–300 km inboard of convergent plate boundaries. Weight 1.0 at young arcs; decay with erosion and age, since porphyries are mostly <~200 Myr old on Earth because uplift erodes them.
  - `epithermal_Au_Ag`: same belts, shallower; co-locate 0–5 km from porphyry nodes. It is more fragile, so require young arcs.
  - `VMS_Cu_Zn_Pb`: requires an ocean (or a former ocean) plus volcanism. Spawn along spreading ridges and back-arc rifts. A dried-out or "Mars-like" planet can still host fossil VMS in old ocean-floor terrains.
  - Stagnant-lid planets (no plate tectonics, like Mars or Venus today) should get far fewer porphyry and epithermal deposits. They should rely on magmatic sulfides (Ni-Cu-PGE) in large volcanic provinces and on impact-related deposits (see Claim 5).

### Gaps
- No primary USGS or Economic Geology source was fetched this session for porphyry, VMS or epithermal settings. One citation pass is recommended (USGS Scientific Investigations Report 2010-5070 series on deposit models; Sillitoe 2010).

---

## Claim 2: Lithium comes from salt flats in dry closed basins (Atacama, Uyuni), plus pegmatites (spodumene, Australia) and clays

### Takeaway
CORRECT. USGS formalizes continental Li-brine deposits as needing six features: (1) arid climate, (2) a closed basin with a salar or salt lake, (3) igneous or geothermal activity, (4) tectonic subsidence, (5) a suitable Li source, and (6) enough time. The doc's "arid closed basin + volcanic Li source" captures the core. Pegmatites and volcanic clays are correctly listed as the other two deposit types.

### Cited Findings
- USGS "Lithium brines: A global perspective" (Munk et al.) lists six common characteristics of Li-brine systems: arid climate; closed basin containing a salar, a salt lake or both; associated igneous and/or geothermal activity; tectonically driven subsidence; suitable lithium sources; and sufficient time to concentrate brine — [USGS](https://www.usgs.gov/publications/lithium-brines-global-perspective); [USGS Pubs](https://pubs.usgs.gov/publication/70104988)
- Li-rich playas sit in closed basins surrounded by ash-tuff flows that have been partly hydrothermally altered to Li-rich clays such as hectorite. Geothermal activity and gypsum and halite associations are common — [USGS GEMSC spectral mapping of Li-rich playas](https://www.usgs.gov/centers/gemsc/science/spectral-characteristics-and-mapping-lithium-rich-playas-use-western-us-basin)
- Brine occurrence is a diagnostic requirement, but not all brines are Li-rich, and not all closed basins develop shallow brine fields. Proximity to volcanic source rocks is used as an extra criterion — [USGS Lithium source-to-sink project](https://usgs.gov/centers/gmeg/science/lithium-source-sink-genesis-and-evolution-li-brines-and-clays)
- The Li source can be extinct. For the Bristol and Cadiz basins (California), Li is postulated to come from ancient geothermal fluids that no longer exist in the basin — [USGS Pubs 70210347](https://pubs.usgs.gov/publication/70210347)
- Felsic volcanic and volcaniclastic packages can hold up to ~2,000 ppm Li (Salton Trough study) — [GSA Cordilleran 2025 abstract](https://gsa.confex.com/gsa/2025CD/webprogram/Paper409752.html)
- Clayton Valley, Nevada, is the type example of a closed arid basin Li-brine in the US — [Munk & Chamberlain USGS MRERP report](https://webarchive.library.unt.edu/web/20130215215826/http:/minerals.usgs.gov/mrerp/reports/Munk-G10AP00056.pdf)

### Inferences
- **[background, not re-verified this session]** Australian hard-rock spodumene pegmatites (for example Greenbushes) make Australia the largest Li producer by mass (USGS Mineral Commodity Summaries). Clay-hosted Li includes Thacker Pass, Nevada, a McDermitt caldera lake-sediment deposit.
- Proposed game rules:
  - `Li_brine`: requires `aridity_index high` (evaporation > precipitation) AND a topographically closed basin (no drainage outlet) AND felsic volcanism or geothermal activity within ~100 km (current or past) AND `liquid_water_history == true`. Score ∝ basin_area × aridity × source_proximity. Planets with no hydrological cycle get no brines.
  - `Li_clay` (hectorite/smectite): closed basin or caldera lake sediments adjacent to rhyolitic volcanics. Needs past water but not present aridity.
  - `Li_pegmatite` (spodumene): old continental crust with granite intrusions, typically in orogenic belts. It is water-independent at the surface, but it needs evolved (felsic, differentiated) crust, which in turn needs plate tectonics or long crustal recycling. Basalt-only worlds get almost none.

### Gaps
- Brine Li grades (for example Atacama ~1,500 ppm, Uyuni lower with high Mg/Li) were not sourced this session.

---

## Claim 3: He-3 accumulates in regolith of airless, unmagnetized bodies via solar-wind implantation; lunar ~1–20 ppb; depends on TiO2/ilmenite and maturity; Earth lacks it. Mercury?

### Takeaway
CORRECT overall, with the numbers refined. The lunar global mean is ~4.2 ppb. High-Ti maria (Tranquillitatis, Procellarum) may exceed 20 ppb. He-3 correlates (r = 0.94 in 21 Apollo samples) with TiO2 × solar-wind flux / optical maturity index. **Mercury does receive solar-wind helium in spite of its magnetic field.** Its field is weak: it shields the equator under moderate solar wind, but ions precipitate at high latitudes (cusps) and reach lower latitudes during strong solar-wind events. Mercury's hot dayside also drives implanted He back out into the exosphere, so retention is lower than on the Moon.

### Cited Findings
- Fa & Jin (2007, Icarus) modeled lunar He-3 as non-uniform, dominated by the high-Ti mare basalts of Mare Tranquillitatis and Oceanus Procellarum, which may exceed 20 ppb, about 5× the global average of 4.2 ppb (Fegley & Swindle) — [Crawford, "Lunar Resources: A Review" (arXiv:1410.6865)](https://arxiv.org/pdf/1410.6865)
- Apollo samples show a linear relation between He-3 abundance and (TiO2 × solar-wind flux)/OMAT, with r = 0.94 across 21 regolith samples from Apollo 11–17 — [arXiv:2402.02614](https://arxiv.org/pdf/2402.02614); [Crawford review](https://arxiv.org/pdf/1410.6865)
- Fa & Jin (2010, Chinese Science Bulletin; Chang'E-1 microwave radiometer) estimated a lunar He-3 inventory of ~6.6×10^8 kg (3.7×10^8 nearside, 2.9×10^8 farside). These figures come via a blog summary; check the primary paper before quoting — [Lunar Networks blog](https://lunarnetworks.blogspot.com/2010/12/change-1-maps-moons-helium-3-inventory.html)
- Total He retention in the lunar regolith is not well known, so inventory estimates carry real uncertainty — [Crawford review](https://arxiv.org/pdf/1410.6865)
- USGS (Johnson et al.) mapped estimated solar-wind-implanted He-3 on the Moon — [USGS Pubs 70021905](https://pubs.usgs.gov/publication/70021905)
- **Mercury:** solar-wind He++ precipitates onto the surface, and a large fraction is implanted into the regolith as neutral He. Models show the weak intrinsic field keeps solar-wind ions off the equatorial regions during moderate solar wind, with precipitation concentrated toward the poles — [Pfleger et al. 2015, PSS](https://wurz.space.unibe.ch/Pfleger_PSS_2015.pdf)
- Mercury exosphere models assume a He-saturated regolith with an implantation-driven source of ~2.5×10^23 He/s (+2.5/−1.25 ×10^23) — [Hener et al. 2025, JGR](https://wurz.space.unibe.ch/Hener_JGR_2025.pdf)
- MESSENGER ion-cyclotron-wave data give a He density profile consistent with a thermal He exosphere **of solar-wind origin** — [Weichbold et al. 2025, JGR](https://wurz.space.unibe.ch/Weichbold_JGR_2025.pdf)
- Mercury surface temperatures span ~100–700 K, which controls He implantation and release — [Biber et al. 2020, NIMB](https://wurz.space.unibe.ch/Biber_NIMB_2020.pdf)

### Inferences
- The "1–20 ppb" range in the doc is an acceptable simplification. Better: "global mean ~4 ppb; up to >20 ppb in mature high-Ti mare regolith."
- "Regolith maturity" must be read correctly. In the Fa & Jin relation, He-3 rises as OMAT **falls**: low OMAT means a more mature, longer-exposed soil. More exposure time means more He-3.
- Mercury's He exosphere is in steady state with a saturated regolith. A dayside at ~700 K degasses grains, so standing He-3 concentration is likely lower than on the Moon, and it is latitude-dependent. No measured Mercury He-3 ppb value exists, and the isotope ratio is not directly measured.
- The solar-wind flux per area scales as 1/r². At 0.39 AU Mercury gets ~6.6× the 1 AU flux. **[derived from inverse-square law, not sourced]**
- Proposed game rules:
  - `He3_regolith_ppb = k × flux_factor × TiO2_wt% × exposure_age / thermal_loss`, clamped 0–30 ppb.
  - `flux_factor = (1 AU / a)^2 × stellar_wind_factor × (1 − magnetic_shielding(lat))`.
  - `magnetic_shielding`: 0 for unmagnetized bodies. For weak dipoles (Mercury-like) use ~0.9 at the equator falling to ~0.2 at the poles. For strong dipoles (Earth-like) use ~1.
  - Any atmosphere with surface pressure > ~1 mbar sets flux to the surface to ≈0 **[inference]**.
  - `thermal_loss` rises steeply above ~400 K surface temperature, which penalizes hot dayside regolith.
  - Bodies inside a giant planet's magnetosphere (Galilean moons) get modified fluxes. Flag them as "low/uncertain".

### Gaps
- No measured He-3 ppb values for Mercury exist. I found no isotope-specific Mercury He-3 source.
- I did not fetch the Fa & Jin 2007 Icarus paper directly. Its numbers come via review articles.

---

## Claim 4: Water ice in permanently shadowed polar craters of airless bodies (LCROSS 5.6% at Cabeus; Mercury radar-bright deposits). Requires low obliquity.

### Takeaway
CORRECT. LCROSS found 5.6 ± 2.9 wt% water ice in the Cabeus impact-site regolith (Colaprete et al. 2010, Science 330:463). MESSENGER confirmed nearly pure buried water ice in Mercury's radar-bright polar craters (Lawrence et al. 2013). The low-obliquity requirement is correct: it is what makes permanent shadow possible.

### Cited Findings
- On 9 Oct 2009, the LCROSS Centaur impact into a permanently shadowed region of Cabeus gave a water-ice concentration of **5.6 ± 2.9% by mass** at the impact site. The maximum combined water vapor and ice in the field of view was 155 ± 12 kg. Light hydrocarbons, sulfur-bearing species and CO2 were also seen. Colaprete et al. 2010, Science 330:463–468 — [Univ. Arizona record](https://experts.arizona.edu/en/publications/detection-of-water-in-the-lcross-ejecta-plume/); [paper PDF](https://www.chem.uci.edu/~unicorn/old/H2A/handouts/PDFs/LCROSSPaper.pdf)
- The 5.6% refers to the regolith at the impact site, as estimated from the excavated mass. It is not a crater-wide average — [AAAS summary](https://www.aaas.org/news/science-lcross-impact-ejects-minerals-and-frozen-water-crater-moon)
- Mercury: MESSENGER neutron data show a H-rich layer more than tens of cm thick beneath a 10–30 cm H-poorer surface layer, best matched by nearly pure water ice. Total polar water is estimated at 2×10^16 to 10^18 g, likely delivered by comets or volatile-rich asteroids — [Lawrence et al. 2013 record](https://academiccommons.columbia.edu/doi/10.7916/D8Z89PKS)
- Nearly all craters >10 km within 10° of Mercury's north pole host radar-bright deposits coinciding with persistent shadow (Chabot et al. 2013). Ice is exposed in the coldest deposits and buried under dark (likely organic-rich) material elsewhere — [NASA JPL](https://www.jpl.nasa.gov/news/nasa-spacecraft-finds-new-mercury-water-ice-evidence/)
- Mercury's very low axial tilt allows some polar craters to stay in year-round shadow — [NASA JPL](https://www.jpl.nasa.gov/news/nasa-spacecraft-finds-new-mercury-water-ice-evidence/)

### Inferences
- **[background, not re-verified this session]** Obliquity values: Moon ~1.5° to the ecliptic, Mercury ~0.03°. PSR temperatures in Cabeus-type craters can be <~40 K (LRO Diviner, Paige et al. 2010).
- Proposed game rules:
  - `PSR_fraction(lat)` is nonzero only if `obliquity < ~5°` **[inference; threshold is a game simplification]**. It grows toward the poles, within ~10° of the pole for Mercury-like cases and ~2–10° for Moon-like cases, and it grows with crater density.
  - `ice_wt%`: 0–10% (typical 1–6%) in PSRs if the body is airless (or has a thin exosphere) AND has a volatile delivery history (cometary impacts or solar-wind H).
  - Bodies with mean surface temperature far below ~110 K (outer-system moons) have global ice. Do not gate those on PSRs.
  - Mercury-type close-in planets: ice is possible only in polar craters. Use a "buried under dark lag" variant that needs digging (gameplay hook).
  - Moderate-obliquity bodies (for example Mars-like ~25°) get no PSR ice. Use polar caps and mid-latitude ground ice instead.

### Gaps
- No sourced obliquity cutoff exists. The "<~5°" threshold is my simplification, since the actual PSR area also depends on crater depth and topography.

---

## Claim 5: Deep-mantle minerals exposed in large impact craters, e.g., Sudbury Ni (impactor vs. melted crust?)

### Takeaway
NEEDS CORRECTION in framing. Sudbury's Ni-Cu-PGE ores come from the **impact melt sheet of terrestrial target crust**, not from the impactor and not from exposed deep mantle. Geochemistry shows bulk continental crust plus mafic rocks, a small chondritic (impactor) trace in the crater fill, and **no mantle component**. The doc's stated consensus ("melted crust, not impactor") is correct. "Deep-mantle minerals exposed" is wrong for Sudbury.

### Cited Findings
- Sudbury crater-fill (Onaping Formation) and offset-dike siderophile data need contributions from bulk continental crust, mafic rocks and a chondritic component. "A mantle component is absent, but the involvement of mid to lower crust is implied." The most probable meteoritic source is an ordinary or enstatite chondrite, or a comet with a chondritic refractory component — [Petrus, Ames & Kamber 2015, Terra Nova (record)](https://zendy.io/title/10.1111/ter.12125); [summary](https://cosmochemistry-papers.com/2014/11/20/on-the-track-of-the-elusive-sudbury-impact-geochemical-evidence-for-a-chondrite-or-comet-bolide/)
- Darling et al. (2010, GCA) found a major contribution of Huronian supracrustal material (up to 12 km pre-impact thickness) is needed for the melt chemistry. That chemistry can be explained without an average lower-crust or meteoritic component — [St Andrews research portal](https://research-portal.st-andrews.ac.uk/en/publications/shallow-impact-isotopic-insights-into-crustal-contributions-to-th/)
- A Goldschmidt abstract describes the ~3 km melt sheet as entirely crustal. It proposes the melt may have scavenged metals from pre-existing LIP-related magmatic sulfide deposits — [Goldschmidt 2016](https://goldschmidtabstracts.info/2016/1468.pdf)
- (Ultra)mafic rocks at mid-crustal depth were proposed as the likely Ni-Cu-PGE-Co source. Other work favors a lower-crustal source — [EGU2020-11 abstract (marked not presented)](https://meetingorganizer.copernicus.org/EGU2020/EGU2020-11.html)
- PGE enrichments at Sudbury could come from impactor input or from re-concentration during impact-driven igneous processes — [Cr-isotope study, arXiv:1612.06922](https://arxiv.org/pdf/1612.06922)
- Impact age ~1.85 Ga — [Sudbury Basin overview](https://www.wikipedia.com/wiki/Sudbury_Basin); [Crater Explorer](https://craterexplorer.ca/sudbury-impact-structure/)

### Inferences
- The mechanism to model is: a big impact melts a large volume of target crust, and the melt sheet differentiates. Immiscible sulfide liquid then scavenges Ni, Cu and PGE from that melt and pools at the base and in offset dikes. Ore grade depends on **target composition** (mafic or ultramafic rocks and sulfur present), not on the impactor.
- Suggested replacement text: "Large impacts can create giant melt sheets that concentrate Ni-Cu-PGE sulfides from the planet's own crust (Sudbury, ~1.85 Ga). The largest basins can also excavate lower-crust and possibly mantle rocks."
- **[background, not re-verified this session]** Lunar South Pole–Aitken basin studies suggest exposure of deep crust or upper-mantle material, which is a better example for "exposing deep materials".
- Proposed game rules:
  - `impact_NiCu_PGE`: craters with diameter ≥ ~100 km (Sudbury ~130–250 km original) on bodies with differentiated crust containing mafic rocks AND sulfur. Ore sits at the melt-sheet base or rim ("footwall") and in radial dikes.
  - Small chance of impactor-derived metal only for iron-meteorite impacts into weak targets. Keep it a rare "meteoritic Fe-Ni" node, low tonnage.
  - `deep_excavation`: basins ≥ ~1,000 km may expose lower-crust or mantle minerals (olivine, pyroxene) in central and rim zones. Scannable via 1 µm and 2 µm bands (Claim 8).
  - Old, heavily cratered stagnant-lid worlds get relatively more impact-sulfide nodes than porphyries.

### Gaps
- I did not fetch a 2020s synthesis paper on Sudbury ore-metal sourcing. The mid- vs. lower-crust source debate is unresolved in what I found.

---

## Claim 6: BIF only on planets that had oxygenic photosynthesis (GOE ~2.4 Ga)

### Takeaway
NEEDS CORRECTION. BIFs require an **anoxic, Fe(II)-rich (ferruginous) ocean** plus some oxidation mechanism. Most were deposited in the Neoarchean to Paleoproterozoic, around and **before** the GOE (~2.4–2.3 Ga), when the atmosphere had essentially no free O2. Oxidants could include oxygenic photosynthesis (local "oxygen oases"), anoxygenic photoferrotrophy and possibly UV photo-oxidation (debated). So BIF is evidence of photosynthetic or photochemical Fe oxidation in an anoxic ocean. It does not require an oxygenated atmosphere. An abiotic UV route remains debated.

### Cited Findings
- Konhauser et al. 2017, "Iron formations: A global record of Neoarchaean to Palaeoproterozoic environmental history", Earth-Science Reviews 172:140–177. It discusses Fe(II) oxidation either directly via **photoferrotrophy** or indirectly via **oxygenic photosynthesis**, and it treats iron formations deposited **before** the GOE — [WHOI archive](https://darchive.mblwhoilibrary.org/entities/publication/6e9b2109-9cf1-5949-bb2d-e169dd660fc2)
- It is generally accepted that photosynthetic marine planktonic bacteria oxidized dissolved Fe(II) to deposit IFs throughout the Archean and early Paleoproterozoic. Partial Fe(II) oxidation under anoxic or low-O2 conditions was central — [Wang et al. 2023, GCA](https://reinhard.gatech.edu/uploads/5/4/1/0/54106417/wang.gca.2023.pdf)
- Atmospheric O2 did not accumulate globally before the GOE at ~2.33 Ga — [Konhauser et al. 2017 via WHOI](https://darchive.mblwhoilibrary.org/entities/publication/6e9b2109-9cf1-5949-bb2d-e169dd660fc2/full)
- Photoferrotrophy as an ancient photosynthesis surviving in modern environments — [Frontiers in Microbiology 2017](https://www.frontiersin.org/articles/10.3389/fmicb.2017.00323/full)
- **UV photo-oxidation, against:** if Fe(II) entered an anoxic photic zone from shallow vents, the photochemical contribution to precipitation would have been negligible. Oxide-facies BIF most likely formed by rapid, non-photochemical, probably biological oxidation (Konhauser et al. 2007, EPSL 258:87–100) — [Univ. Tübingen PDF](https://uni-tuebingen.de/securedl/sdl-eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJpYXQiOjE3MzE5NjIyODMsImV4cCI6MTczMjA1MjI4MywidXNlciI6MCwiZ3JvdXBzIjpbMCwtMV0sImZpbGUiOiJmaWxlYWRtaW4vVW5pX1R1ZWJpbmdlbi9GYWt1bHRhZXRlbi9NYXROYXQvRmFjaGJlcmVpY2hlL0dlb3dpc3NlbnNjaGFmdGVuL0FyYmVpdHNncnVwcGVuL0FuZ2V3YW5kdGVfR2Vvd2lzc2Vuc2NoYWZ0ZW4vR2VvbWlrcm9iaW9sb2dpZS9QdWJsaWNhdGlvbnMvMjAwNy9Lb25oYXVzZXJfZXRhbF9FUFNMMjAwNy5wZGYiLCJwYWdlIjo4NzQ2OH0.uAffIMNIin-q4VLNuR8N-s0szoCSA5HHnRXKTAjwUXc/Konhauser_etal_EPSL2007.pdf)
- **UV photo-oxidation, for:** isotope experiments (Nie, Dauphas, Greenwood et al.) find UV photo-oxidation needs no O2 or biology and remains a viable BIF pathway. A ~3.8 Ga Isua BIF (IF-G) falls on the experimental fractionation line. Quantum yield ~0.07 Fe atoms oxidized per absorbed photon — [Nie et al. record](https://bvbvf1.bib-bvb.de/vufind/PrimoRecord/cdi_proquest_miscellaneous_1884102041)
- Later work argues for decoupling photochemical Fe(II) oxidation from shallow-water BIF deposition — [Univ. Copenhagen record](https://test01.ku.dk/:obvius/pureproxy/3974268/en/publications/decoupling-photochemical-feii-oxidation-from-shallow-water-bif-de)

### Inferences
- The causal logic in the doc is inverted. A fully **oxygenated** ocean shuts BIF formation down, because Fe(II) cannot be transported in solution. BIFs mark a transitional, anoxic-ocean era.
- Suggested replacement text: "Banded iron formations form in iron-rich, oxygen-poor oceans where sunlight-driven processes (photosynthetic microbes, oxygen-producing or not, and possibly UV light) oxidize dissolved iron. On Earth they peak around 2.5–2.4 Ga, near the Great Oxidation Event."
- Proposed game rules:
  - `BIF`: requires a past ocean (`ocean_history == true`) AND an anoxic deep ocean phase AND a hydrothermal Fe(II) supply (volcanism) AND (a `life_photosynthetic` flag OR strong stellar UV reaching the ocean surface).
  - Weight: life flag ×1.0; UV-only ×0.3 (debated pathway).
  - Abundance is large (giant Fe ore, ~30% Fe). Place it in old (>2 Gyr) sedimentary basins on continental platforms.
  - Planets that never had oceans get no BIF. Planets that oxygenated very early get thinner BIF records.
  - Optional educational flag: "BIF present → biosignature candidate, not proof."

### Gaps
- I could not confirm the bibliographic details of the specific 2017 Nature Geoscience paper by Nie et al. The isotope work cited is from the same group.

---

## Claim 7: Element substitutions (PV, batteries, magnets, wiring, electrolysers, space elevator)

### Takeaway
Mostly CORRECT, with these specific fixes:
- **Electrolysers:** say "PEM uses Pt/Ir; alkaline uses Ni-based catalysts." Note that AEM electrolysis is also Ni-friendly.
- **Space elevator:** state the requirement as **specific strength**, ~30–50 MN·m/kg (GPa per g/cm³) for a tapered Earth cable, not as "50–100 GPa". A lunar elevator through L1 is feasible with existing fibers (Zylon, Kevlar, Dyneema), but it would be >50,000 km long.
- **Cu → Al:** Al has ~1.6× the resistivity of Cu but ~30% of its density. That makes Al **better per kg**, and it is used for overhead power lines.

### Cited Findings
- Lower lunar gravity and no atmosphere give a lunar elevator much less stringent tensile-strength requirements — [Lunar space elevator overview](https://wikipedia.com/wiki/Lunar_elevator)
- In a dimensionless relative-strength metric α, a constant-width lunar "Spaceline" becomes feasible at α ≈ 3. An Earth-based elevator needs α ≳ 50. Kevlar and Dyneema are already strong enough for a Spaceline, and Zylon is above that threshold — [Penoyre & Sandford 2019, "The Spaceline" (arXiv:1908.09339)](https://arxiv.org/pdf/1908.09339)
- Zylon has the highest relative strength of mass-manufactured materials. A lunar elevator cable must be >50,000 km long and pass through the Earth–Moon L1 point — [Centauri Dreams 2025](https://www.centauri-dreams.org/2025/03/07/spaceline-a-design-for-a-lunar-space-elevator/)
- Long CNT ribbons have not been manufactured successfully. CNTs are described as the only known material strong enough for an Earth elevator — [Universe Today](https://www.universetoday.com/articles/developers-say-lunar-elevator-could-be-built-within-a-decade)
- Remaining lunar-elevator unknowns are multilayer strength, defects and radiation effects — [arXiv:1908.09339](https://arxiv.org/pdf/1908.09339)

### Inferences
- **[background, not re-verified this session]** Standard values and claims to cite before publication:
  - **Cu vs. Al:** ρ_Cu ≈ 1.68×10⁻⁸ Ω·m and ρ_Al ≈ 2.65×10⁻⁸ Ω·m (≈1.6×). Densities are 8.96 vs. 2.70 g/cm³. Per unit mass, Al conducts about 2× better, so the game should make Al the default for long power lines and Cu for compact motors and electronics. Verdict: CORRECT.
  - **PV:** perovskite stability and degradation is the main barrier; CORRECT. CdTe depends on Te, a by-product of Cu refining that is very scarce; CORRECT. III-V multijunction (GaAs-based) cells hold the efficiency records, ~47% under concentration and the high-30s % at 1 sun, and they are used in space but are expensive; CORRECT. Note that crystalline Si needs no critical metals beyond Ag for contacts, and Cu-plated contacts are emerging. Ag is a good "scarce element" hook for the game.
  - **Batteries:** Na-ion has lower gravimetric energy density than Li-ion ("heavier per kWh"); CORRECT. Vanadium redox flow and iron-air both suit stationary storage, not vehicles; CORRECT. Suggested addition: LFP (Li-iron-phosphate) avoids Co and Ni.
  - **Magnets:** NdFeB, often with Dy and Tb for high-temperature coercivity, can be replaced by ferrite magnets (larger and heavier), electrically excited synchronous generators (Cu-heavy wound rotor, used in some direct-drive wind turbines) or induction machines (Cu or Al cages). CORRECT as listed.
  - **Electrolysers:** PEM needs Pt (cathode) and Ir (anode). Alkaline uses Ni and Ni-Fe and is mature at industrial scale. ACCEPTABLE, with the PEM vs. alkaline label added. Alkaline trades lower current density and slower load-following for no PGMs.
  - **Space elevator:** a CNT tensile strength of "50–100 GPa" at density ~1.3–1.4 g/cm³ corresponds to ~35–75 MN·m/kg, so the doc's range is in the right ballpark only if it means specific strength. Recommend displaying "specific strength (MYuri)". Zylon is ~3.7–3.8 MN·m/kg and Kevlar ~2.5 (background values).
  - "No substitute" for an Earth elevator is fair as of 2026. Graphene, hBN and diamond nanothreads are only theoretical candidates.
- Proposed game rule: elevator feasibility index = material_specific_strength / (g × R_sync-ish characteristic length). As a simplification, require `specific_strength ≥ 0.6 × g_surface × r_stationary_orbit / 1e6` (MN·m/kg) **[inference, approximate scaling]**. Use this to unlock elevators on low-g, fast-spinning bodies earlier, and use L1 "spacelines" for tidally locked moons.

### Gaps
- I did not fetch primary sources (NREL efficiency chart, CRC resistivity tables, Pearson 2005 lunar elevator NASA report) for the numerical values in Inferences.

---

## Claim 8: Spectroscopy mini-game (Na D, H-alpha, reflectance mapping, LIBS); which is accurate for satellite and drone scanning?

### Takeaway
CORRECT distinction, and the doc should use it. **Emission lines** (Na D 589.0/589.6 nm, H-alpha 656.3 nm) come from hot or excited gas: flames, stars, plasma. On a planet surface they appear only in **LIBS** (laser-induced breakdown spectroscopy, as in ChemCam), which works at a few meters. **Orbital and drone mineral mapping** uses **reflectance absorption bands** in sunlight (VNIR/SWIR):
- olivine ~1.05 µm (composite);
- pyroxene ~1 µm and ~2 µm;
- hydrated minerals and OH/H2O ~1.4, 1.9 and 2.7–3.0 µm (M3 detected 2.8–3.0 µm OH/H2O on the Moon).

For element-level orbital mapping, gamma-ray and neutron spectroscopy is the realistic tool. The MESSENGER neutron spectrometer detected Mercury's polar H, for example.

### Cited Findings
- Moon Mineralogy Mapper (Chandrayaan-1) found absorption near **2.8–3.0 µm** distributed across the Moon, strongest at cooler high latitudes and at some fresh feldspathic craters. Such silicate-body features are attributed to OH- and/or H2O-bearing materials. Pieters et al. 2009, Science 326:568–572 — [USGS Pubs](https://pubs.usgs.gov/publication/70037065); [USGS](https://www.usgs.gov/publications/character-and-spatial-distribution-ohh2o-surface-moon-seen-m3-chandrayaan-1)
- Cassini VIMS (1999) independently showed the 3 µm absorption — [NSS summary of NASA release](https://nss.org/nasa-instruments-reveal-water-molecules-on-lunar-surface)
- Orbital **neutron spectroscopy** (MESSENGER NS) mapped hydrogen (water ice) at Mercury's poles — [Lawrence et al. 2013 record](https://academiccommons.columbia.edu/doi/10.7916/D8Z89PKS)
- USGS uses spectral (reflectance) mapping to identify Li-rich playa surfaces in western US basins — [USGS GEMSC](https://www.usgs.gov/centers/gemsc/science/spectral-characteristics-and-mapping-lithium-rich-playas-use-western-us-basin)

### Inferences
- **[background, not re-verified this session]** Further standard facts:
  - Na D lines are 588.995 and 589.592 nm; H-alpha is 656.28 nm (air wavelengths; NIST ASD). CORRECT.
  - ChemCam on Curiosity (and SuperCam on Perseverance) uses LIBS at up to ~7 m range. CORRECT.
  - Olivine shows a broad composite band near 1.05 µm with no 2 µm band. Low-Ca pyroxene has bands near 0.9 and 1.9 µm, and high-Ca pyroxene near 1.0 and 2.3 µm.
  - Phyllosilicates show metal-OH bands at 2.2–2.35 µm plus 1.4 and 1.9 µm water bands. CRISM on MRO mapped these on Mars.
  - In a planetary reflectance context, Li itself has no diagnostic VNIR band. Li mapping uses host clays and evaporites (proxy minerals).
- Recommendation for "satellite/drone scanning": make the mini-game a **reflectance spectrum with absorption dips** (match band centers to a mineral library). Keep the **emission-line** puzzle (Na D, H-alpha) for:
  - (a) stellar spectra and star scanning, which feeds the [Fe/H] mechanic in Claim 10;
  - (b) a close-range "laser zap" LIBS tool on rovers and drones (element ID: Na 589, Li 670.8 nm, Ca, Fe lines);
  - (c) planetary exospheres. Mercury's and the Moon's Na exospheres glow at 589 nm in resonance scattering.
- Proposed game rules: orbital scan resolution grows with orbit altitude (coarse, mineral classes only). A drone scan gives a fine map. A rover LIBS shot gives element-level ppm ranges. Gamma/neutron orbital scans give H (ice), K, Th and Fe maps at very coarse resolution, which suits He-3 and ice targeting.

### Gaps
- No session-sourced citation for the exact wavelengths or band positions listed under Inferences. Cite NIST ASD and the RELAB / USGS Spectral Library before publication.

---

## Claim 9: Island of stability (Z~114, N~184) and room-temperature superconductors as "light fantasy"

### Takeaway
The game's "light fantasy" flag is FAIR. Searches for natural superheavy elements have been negative. Measured superheavy half-lives rise with neutron number but remain short. Ambient-pressure room-temperature superconductivity is still unachieved as of 2026:
- LK-99 (2023) was refuted, its signals attributed to Cu2S impurities and ferromagnetism.
- Dias's hydride claims (2020 CSH; 2023 N-doped LuH) were retracted.
- Genuine high-Tc hydrides (H3S, LaH10) need ~150–200 GPa (megabar) pressures.

Wording suggestion: "Hypothetical stable superheavy element (none found in nature)" and "room-temperature superconductor (only achieved under megabar pressures; ambient versions remain unrealized)".

### Cited Findings
- Early experiments to find superheavy nuclei in nature or at accelerators were unsuccessful, which led to more sensitive methods and to synthesis approaches — [APS April Meeting 2013 abstract](https://meetings-archive.aps.org/apr/2013/q3/3/)
- Shell effects were predicted to stabilize Z≈114–126 against fission. Predicted half-lives range from minutes to years, and some old calculations predicted longer. Measured half-lives increase with neutron number as N=184 is approached — [Scientific American](https://www.scientificamerican.com/article/the-quest-for-superheavy-elements-and-the-island-of-stability); [Physics 5, 115 (2012)](https://physics.aps.org/articles/v5/115)
- Chapman 2020 (Phil. Trans. A) reviews searches for superheavy elements in nature — [record](https://eresearch.qmu.ac.uk/items/a52a24b1-a19e-489a-b674-0a6f8617d9e7/full)
- Nature retracted Dias's 2020 carbonaceous sulfur hydride room-temperature claim in Sept 2022 and later retracted the 2023 N-doped lutetium hydride claim (~20 °C at 1 GPa). A third paper was retracted by PRL — [Science News](https://www.sciencenews.org/article/room-temperature-superconductor-retracted-ranga-dias); [Chemistry World](https://www.chemistryworld.com/news/third-room-temperature-superconductivity-paper-retracted-as-groups-claims-lie-in-tatters/4018402.article)
- LK-99 was rejected by replication worldwide. Its behavior was attributed to impurities and ferromagnetism — [Chemistry World feature](https://chemistryworld.com/features/superconductivity-the-search-and-the-scandal/4019292.article)
- As of 2026, ambient-pressure room-temperature superconductivity is still unachieved. H3S and LaH10 superconduct up to near room temperature only at millions of atmospheres. This comes from a secondary blog — [2026 status blog](https://b1ue-blog.pages.dev/en/article/room-temp-superconductor-status-2026)

### Inferences
- **[background, not re-verified this session]** H3S Tc ≈ 203 K at ~150 GPa (Drozdov et al. 2015, Nature). LaH10 Tc ≈ 250 K at ~170 GPa (Drozdov et al. 2019). The heaviest known flerovium isotopes have half-lives of seconds or less.
- Game note: if the game lets superheavies form in neutron-star-merger debris, flag it explicitly as fantasy. Even predicted island nuclei would mostly not survive the ~Gyr timescales needed to end up in planets.

### Gaps
- No primary 2025–2026 journal review on superconductivity status was found. The "still unachieved" status rests on news coverage of the retractions plus one 2026 blog. I found no credible 2025–2026 report of a confirmed ambient room-temperature superconductor.

---

## Claim 10: Planet element abundances depend on stellar metallicity; giant-planet–metallicity correlation; rocky planet composition tracks host Fe/Mg/Si (Adibekyan 2021)

### Takeaway
CORRECT, with one nuance. Adibekyan et al. 2021 (Science 374:330, doi:10.1126/science.abg8794) found a correlation that is **not 1:1**. Rocky planets' iron fractions span a wider range than their stars' and tend to be **iron-enriched** relative to the stellar prediction. The giant-planet occurrence rate ∝ 10^(2[Fe/H]) (Fischer & Valenti 2005; ~3% at solar, ~25% at 3× solar metallicity). Below solar metallicity, occurrence flattens to ~1%.

### Cited Findings
- Adibekyan et al. 2021 (Science; 21 rocky exoplanets with precise mass and radius): planet iron-mass fractions correlate with host-star composition but not one-to-one. The planets span a wider Fe range and are on average more Fe-rich. Example: a star-predicted ~30% Fe vs. a planet at ~60%. The authors attribute this to disk chemistry and formation processes. There is an apparent gap separating super-Earths from super-Mercuries — [arXiv:2102.12444](https://arxiv.org/abs/2102.12444v2); [Subaru Telescope](https://subarutelescope.org/en/results/2021/10/14/2997.html); [IAC](https://iac.edu.es/en/outreach/news/rocky-exoplanets-and-their-host-stars-may-have-similar-composition); [Inverse](https://inverse.com/science/what-are-planets-made-of)
- The volume is reported as 374 or 375 by different sources; use the DOI 10.1126/science.abg8794 — [Univ. Bern BORIS](https://boris.unibe.ch/166471)
- Fischer & Valenti 2005 (ApJ 622:1102; 1,040 FGK stars, −0.5 < [Fe/H] < 0.5): giant-planet fraction f = 0.03 × (Z/Z☉)^2.0, i.e., ∝10^(2[Fe/H]) — [VizieR record](https://dc.g-vo.org/rr/q/lp/custom/CDS.VizieR/J/ApJ/622/1102); [arXiv:1911.05830](https://arxiv.org/pdf/1911.05830)
- Occurrence rises to ~25% at 3× solar heavy-element content — [Cambridge IAU proc.](https://resolve.cambridge.org/core/services/aop-cambridge-core/content/view/C27959B6172037CBD4B84DBE6ECB3A39/S1743921310001080a.pdf/metallicity-and-planet-formation-observations.pdf)
- Johnson et al. 2010 (1,194 stars) gives an exponent β = 1.2 ± 0.2, shallower than 2. Hot-Jupiter studies give other values — [arXiv:1911.05830](https://arxiv.org/pdf/1911.05830)
- For −1.0 < [Fe/H] < 0.0, occurrence is statistically consistent with a constant ~1% (Sozzetti et al.) — [arXiv astro-ph/0601656 / related](https://www.arxiv.org/pdf/astro-ph/0601656)
- The small-planet frequency depends much more weakly on metallicity — [arXiv:1605.04310](https://arxiv.org/pdf/1605.04310)

### Inferences
- Proposed game rules:
  - `P(giant_planet) = 0.03 × 10^(β·[Fe/H])` with β = 1.2–2.0, floored at 0.01 for [Fe/H] < 0.
  - Rocky planet core-mass fraction = `CMF_star(Fe/Mg/Si) × U(1.0, 2.0)`. Use the host's Fe/(Mg+Si) to set the baseline, then add scatter skewed iron-rich, per Adibekyan.
  - Planets far above the stellar prediction are "super-Mercury": a big Fe-Ni core and a thin mantle, so fewer silicate ores and more metal.
  - Scale the global abundance of siderophile and lithophile resources by 10^[Fe/H]. Planets around metal-poor stars ([Fe/H] < −0.5) are resource-poor overall but can still host ice and He-3.
  - Couples to Claim 8: scanning the host star's emission and absorption lines (Fe I lines, Na D) reveals [Fe/H] before landing, which is a good educational loop.

### Gaps
- Adibekyan's sample is small (21 planets). The physical cause of the Fe enrichment is unresolved. Treat the scatter range in the rules as a game choice, not a measured distribution.
