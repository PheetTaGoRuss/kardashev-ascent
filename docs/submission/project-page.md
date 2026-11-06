# Project page (draft, #15)

> **ทีม:** ร่างข้อความหน้า Project บนเว็บ Space Apps (ภาษาอังกฤษ) ต้องแก้หลังเลือก challenge ทางการ (#14)
> และเติมส่วน "Built before the event" ตามคำตอบของผู้จัด ส่วนที่มี **TODO** ต้องตัดสินใจหรือเติมเอง

## Title
Kardashev Ascent

## Challenge
**TODO (#14):** official 2026 challenge name and how the project answers it.

## Summary (≈100 words)
Kardashev Ascent is a strategy game about powering a colony with starlight on real exoplanets. Planets come from the NASA Exoplanet Archive, and every value is tagged as measured, calculated or assumed, so players learn what science knows and what it doesn't. Panels follow tested solar physics: sun position, air mass, cell temperature, the star's colour, and Earth's real cloud records from NASA POWER. Players measure light, place panels, keep the battery alive through the night, and turn spare power into growth, climbing the Kardashev scale ten times the power at a time.

## How it works
- **Real planets.** A Python tool queries the NASA Exoplanet Archive (`pscomppars` table via TAP), converts units, fills gaps with physics (gravity, escape velocity, luminosity from Stefan–Boltzmann) and labels every value `nasa`, `derived` or `assumed`. The game shows the labels.
- **Solar physics.** Sun position, Kasten–Young air mass, direct and diffuse light, plane-of-array irradiance, NOCT and radiative cell temperature, and PVWatts-style power. Checked against pvlib in a Python prototype (14 checks), then ported to C# with 500+ unit tests.
- **The star's colour.** Shockley–Queisser detailed-balance efficiency under a blackbody star: silicon makes about 43 % of its Sun output under a 2600 K star.
- **Clouds.** Monthly all-sky ÷ clear-sky factors by latitude from NASA POWER (2001–2025 climatology) on Earth-like planets.
- **Tidal locking.** On locked planets the star never moves; the surface is an "eyeball" with open ground under the star.
- **Energy economics.** Embodied energy, payback and EROI per panel (IEA PVPS 2024, Bhandari et al. 2015).
- **Kardashev scale.** K = (log₁₀ P − 6)/10 (Sagan). The game shows absolute K next to a per-planet "planet K" and is explicit that the per-planet target is a game rule. Type I is about 5.8 % of the starlight a planet intercepts, not all of it.

## NASA data used
- **NASA Exoplanet Archive**, Planetary Systems Composite Parameters (`pscomppars`), retrieved 2026-11-05: star luminosity, temperature, radius and mass; orbit size, period and eccentricity; planet radius, mass, insolation and equilibrium temperature.
- **NASA POWER**, Meteorological and Solar Monthly & Annual Climatologies (January 2001 – December 2025): all-sky and clear-sky surface shortwave irradiance by latitude and month.

**TODO:** check the exact citation wording each data service asks for and paste it here.

## Other sources
- pvlib-python (validation of the solar model)
- Shockley & Queisser (1961) detailed-balance limit
- Sagan's interpolation of the Kardashev scale; humanity's 2024 primary energy from the Energy Institute (≈19 TW, K ≈ 0.73)
- Doyle et al. (2011), Kepler-16 system parameters
- Rogers (2015); Fulton et al. (2017), rocky vs gaseous radius limits
- Full list with checks: `docs/physics/physics-verification.md` in the repository

## Built before the event
**TODO (#14):** what the organisers allow, and a clear list of what existed before the hackathon weekend versus what was built during it (the git history has dates).

## Use of AI
**TODO:** confirm the Space Apps 2026 rule on AI disclosure. Draft: "We used Claude Code (Anthropic) as a coding assistant for implementation, testing and documentation. Physics claims were checked against published sources and pvlib, and every formula has unit tests."

## Links
- Repository: https://github.com/PheetTaGoRuss/kardashev-ascent
- Playable Windows build: **TODO** (itch.io restricted page or GitHub Releases)
- Demo video: **TODO**

## Team
**TODO:** names and roles.
