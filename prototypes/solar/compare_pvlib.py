"""Check solar.py against pvlib, then write the C# test reference file.

Run from this folder:  .venv/Scripts/python compare_pvlib.py   (Windows)
                       .venv/bin/python compare_pvlib.py       (macOS/Linux)
Exits with code 1 if any check is outside its tolerance.
"""
from __future__ import annotations

import itertools
import math
import sys
from pathlib import Path

import numpy as np
import pandas as pd
import pvlib
from pvlib import atmosphere, irradiance, pvsystem, solarposition, temperature

import solar

ROOT = Path(__file__).resolve().parents[2]
CS_OUT = ROOT / "Assets/_Project/Tests/EditMode/Solar/PvlibReference.cs"

failures: list[str] = []
reference: dict[str, list[list[float]]] = {}


def check(name: str, ours, theirs, tol: float, unit: str = "") -> None:
    diff = float(np.max(np.abs(np.asarray(ours, float) - np.asarray(theirs, float))))
    ok = diff <= tol
    print(f"{'PASS' if ok else 'FAIL'}  {name:<44} max diff {diff:.3g} {unit} (tol {tol:g})")
    if not ok:
        failures.append(name)


def earth_mean_anomaly(days_since_j2000: float) -> float:
    return math.radians((357.5291 + 0.98560028 * days_since_j2000) % 360.0)


def earth_perihelion_longitude(days_since_j2000: float) -> float:
    """Sun's longitude at perihelion; drifts ~0.017 deg/yr (L - M of the low-precision solar formulas)."""
    return math.radians(282.9372 + 4.70935e-5 * days_since_j2000)


# Step 5: Kasten-Young air mass
zeniths = [0.0, 20.0, 45.0, 60.0, 75.0, 85.0, 89.5]
ours = [solar.air_mass_kasten_young(z) for z in zeniths]
theirs = atmosphere.get_relative_airmass(np.array(zeniths), model="kastenyoung1989")
check("air mass (Kasten-Young)", ours, theirs, 1e-9)
reference["AirMass"] = [[z, t] for z, t in zip(zeniths, theirs)]

# Steps 2-3: zenith and azimuth from latitude, declination, hour angle
rows, oz, tz, oa, ta = [], [], [], [], []
for lat, dec, h in itertools.product([-60, -23, 0, 13.75, 45, 70],
                                     [-23.44, 0, 10, 23.44],
                                     [-90, -45, -10, 0, 30, 75]):
    sun = solar.sun_direction(math.radians(lat), math.radians(dec), math.radians(h))
    if sun[2] <= 0.05:  # skip night and grazing sun (azimuth is ill-defined at the zenith too)
        continue
    zen = float(solarposition.solar_zenith_analytical(math.radians(lat), math.radians(h), math.radians(dec)))
    az = float(solarposition.solar_azimuth_analytical(math.radians(lat), math.radians(h), math.radians(dec), zen))
    if sun[2] > 0.9999:
        continue
    # pvlib's analytical azimuth flips to 180 at exactly h = 0 even when the sun is due north,
    # so noon is left out here and covered by a hand-checked C# test instead.
    if h == 0:
        continue
    oz.append(solar.zenith_deg(sun)); tz.append(math.degrees(zen))
    oa.append(solar.azimuth_deg(sun)); ta.append(math.degrees(az) % 360.0)
    rows.append([lat, dec, h, math.degrees(zen), math.degrees(az) % 360.0])
check("sun zenith", oz, tz, 1e-9, "deg")
check("sun azimuth (h != 0)", oa, ta, 1e-9, "deg")
reference["SunPosition"] = rows

# Step 4: angle of incidence
aoi_cases = [(0, 180, 30, 120), (30, 180, 30, 180), (45, 90, 60, 250), (90, 270, 70, 270),
             (20, 135, 10, 200), (60, 0, 50, 10), (35, 200, 85, 95), (10, 180, 0, 0)]
ours, theirs = [], []
for tilt, paz, zen, saz in aoi_cases:
    n = solar.panel_normal(math.radians(tilt), math.radians(paz))
    z, a = math.radians(zen), math.radians(saz)
    s = (math.sin(z) * math.sin(a), math.sin(z) * math.cos(a), math.cos(z))
    ours.append(solar.cos_incidence(n, s))
    theirs.append(max(0.0, math.cos(math.radians(float(irradiance.aoi(tilt, paz, zen, saz))))))
check("cos(angle of incidence)", ours, theirs, 1e-12)
reference["Incidence"] = [list(c) + [t] for c, t in zip(aoi_cases, theirs)]

# Step 6: plane-of-array irradiance, isotropic sky
poa_cases = [(30, 180, 35, 160, 850, 900, 120, 0.2), (0, 180, 50, 220, 600, 500, 115, 0.25),
             (60, 180, 70, 200, 400, 250, 110, 0.8), (90, 90, 40, 100, 700, 650, 100, 0.3),
             (25, 160, 80, 300, 150, 60, 35, 0.2)]
ours, theirs = [], []
for tilt, paz, zen, saz, dni, ghi, dhi, alb in poa_cases:
    n = solar.panel_normal(math.radians(tilt), math.radians(paz))
    z, a = math.radians(zen), math.radians(saz)
    s = (math.sin(z) * math.sin(a), math.sin(z) * math.cos(a), math.cos(z))
    ours.append(solar.plane_of_array(dni, dhi, ghi, solar.cos_incidence(n, s), math.radians(tilt), alb))
    theirs.append(float(irradiance.get_total_irradiance(tilt, paz, zen, saz, dni, ghi, dhi,
                                                         albedo=alb, model="isotropic")["poa_global"]))
check("plane-of-array irradiance", ours, theirs, 1e-9, "W/m2")
reference["PlaneOfArray"] = [list(c) + [t] for c, t in zip(poa_cases, theirs)]

# Step 7: cell temperature (NOCT / Ross) and power (PVWatts DC)
cells = [(0, 0, 45), (25, 800, 45), (35, 1000, 48), (-10, 600, 42), (40, 300, 45)]
ours = [solar.cell_temperature(t, g, n) for t, g, n in cells]
theirs = [float(temperature.ross(g, t, noct=n)) for t, g, n in cells]
check("cell temperature (NOCT)", ours, theirs, 1e-9, "C")
reference["CellTemperature"] = [list(c) + [t] for c, t in zip(cells, theirs)]

powers = [(1000, 25, 1.6, 0.20), (800, 45, 1.6, 0.20), (500, 10, 2.0, 0.22), (200, 60, 1.0, 0.18)]
ours = [solar.panel_power(g, a, e, tc) for g, tc, a, e in powers]
theirs = [float(pvsystem.pvwatts_dc(g, tc, a * e * 1000.0, -0.004)) for g, tc, a, e in powers]
check("panel power (PVWatts DC)", ours, theirs, 1e-9, "W")
reference["PanelPower"] = [list(c) + [t] for c, t in zip(powers, theirs)]

# Steps 1-2 for Earth: orbit distance and declination over 2026
times = pd.date_range("2026-01-01 05:00", "2026-12-31 05:00", freq="13D", tz="UTC")
days_j2000 = (times.to_julian_date() - 2451545.0).to_numpy()
earth = solar.Planet()
ours_r, ours_dec, rows = [], [], []
for n in days_j2000:
    nu = solar.true_anomaly(solar.solve_kepler(earth_mean_anomaly(n), earth.eccentricity), earth.eccentricity)
    ours_r.append(solar.orbital_distance(1.0, earth.eccentricity, nu))
    ours_dec.append(math.degrees(solar.declination(earth.obliquity, nu + earth_perihelion_longitude(n))))
theirs_r = solarposition.nrel_earthsun_distance(times).to_numpy()
# Declination from SPA's sun position seen from (0 N, 0 E): sin(dec) = sin(zenith) * cos(azimuth)
equator = solarposition.get_solarposition(times, 0.0, 0.0)
theirs_dec = np.degrees(np.arcsin(np.sin(np.radians(equator["zenith"])) * np.cos(np.radians(equator["azimuth"])))).to_numpy()
check("Earth-Sun distance (vs NREL SPA)", ours_r, theirs_r, 2e-4, "AU")
check("declination (vs NREL SPA)", ours_dec, theirs_dec, 0.02, "deg")
reference["EarthOrbit"] = [[n, r, d] for n, r, d in zip(days_j2000, theirs_r, theirs_dec)]

# Steps 1-3 end to end: Bangkok, hourly on 4 days, vs pvlib's full solar position
lat, lon = 13.75, 100.5
times = pd.DatetimeIndex([]).tz_localize("UTC")
for day in ["2026-03-20", "2026-06-21", "2026-09-23", "2026-12-21"]:
    times = times.append(pd.date_range(f"{day} 00:00", periods=12, freq="1h", tz="UTC"))
spa = solarposition.get_solarposition(times, lat, lon)
eot = solarposition.equation_of_time_spencer71(times.dayofyear)
hour_angles = solarposition.hour_angle(times, lon, eot)
ours_z, theirs_z = [], []
for t, h, z in zip(times, hour_angles, spa["zenith"]):
    if z >= 85.0:
        continue
    n = t.to_julian_date() - 2451545.0
    nu = solar.true_anomaly(solar.solve_kepler(earth_mean_anomaly(n), earth.eccentricity), earth.eccentricity)
    dec = solar.declination(earth.obliquity, nu + earth_perihelion_longitude(n))
    ours_z.append(solar.zenith_deg(solar.sun_direction(math.radians(lat), dec, math.radians(h))))
    theirs_z.append(z)
check("Bangkok sun zenith, full chain (vs SPA)", ours_z, theirs_z, 0.25, "deg")

# Step 8 against docs/physics/physics-verification.md (clear-sky POA, kWh/m2/yr; the panel is a POA meter)
def poa_year(lat_deg, tau, k, days=0):
    lat = math.radians(lat_deg)
    planet = solar.Planet(optical_depth=tau, diffuse_k=k)
    meter = solar.Panel(latitude=lat, tilt=abs(lat), azimuth=math.pi if lat >= 0 else 0.0,
                        efficiency=1.0, temp_coeff=0.0)
    return solar.annual_yield(planet, meter, days=days).energy_wh / 1000.0


replica = [(0.4, 0.3, 2497.0), (0.3, 0.3, 2714.0), (0.3, 0.5, 2967.0), (0.2, 0.3, 2970.0), (0.1, 0.3, 3278.0)]
check("Bangkok clear-sky POA vs report (game replica)",
      [poa_year(13.75, t, k) / ref - 1.0 for t, k, ref in replica], [0.0] * len(replica), 0.01, "(fraction)")
lats = [13.75, 35.0, 60.0, -33.9]
check("24-day x 48-slot sum vs 365-day x 48 (own model)",
      [poa_year(lat, 0.3, 0.4) / poa_year(lat, 0.3, 0.4, days=365) - 1.0 for lat in lats], [0.0] * 4, 0.005, "(fraction)")
ecc_planet = solar.Planet(eccentricity=0.6, perihelion_longitude=1.0)
ecc_panel = solar.Panel(latitude=math.radians(30), tilt=math.radians(30))
check("auto samples (72 when e > 0.3) vs 2880 days, e = 0.6",
      solar.annual_yield(ecc_planet, ecc_panel).energy_wh / solar.annual_yield(ecc_planet, ecc_panel, days=2880).energy_wh - 1.0,
      0.0, 0.001, "(fraction)")
check("air mass is finite at and below the horizon",
      [solar.air_mass_kasten_young(z) for z in (90.0, 95.0, 120.0)], [37.91960837783621] * 3, 1e-9)

# Annual yield from the prototype (C# port must match it exactly)
cases = {
    "EarthBangkok": (solar.Planet(optical_depth=0.43, diffuse_k=0.3),
                     solar.Panel(latitude=math.radians(13.75), tilt=math.radians(13.75))),
    "EccentricTilted": (solar.Planet(luminosity=0.6 * solar.SOLAR_LUMINOSITY, semi_major_axis=0.8 * solar.AU,
                                     eccentricity=0.3, obliquity=math.radians(60), perihelion_longitude=1.0,
                                     day_length_h=30.0, year_length_h=500 * 30.0, optical_depth=0.5,
                                     diffuse_k=0.35, air_temperature_c=5.0),
                        solar.Panel(latitude=math.radians(45), tilt=math.radians(30), azimuth=math.radians(170),
                                    albedo=0.6, pressure_ratio=0.8, f_spec=0.95, f_dust=0.05)),
    "VeryEccentric": (solar.Planet(eccentricity=0.6, perihelion_longitude=1.0),
                      solar.Panel(latitude=math.radians(30), tilt=math.radians(30))),
    "TidallyLocked": (solar.Planet(luminosity=0.0017 * solar.SOLAR_LUMINOSITY, semi_major_axis=0.0485 * solar.AU,
                                   eccentricity=0.02, obliquity=0.0, perihelion_longitude=0.0, day_length_h=0.0,
                                   year_length_h=11.186 * 24.0, optical_depth=0.3, diffuse_k=0.3,
                                   air_temperature_c=-20.0, tidally_locked=True),
                      solar.Panel(latitude=math.radians(20), tilt=math.radians(20), azimuth=math.radians(240),
                                  longitude=math.radians(-35))),
}
print()
annual_rows = []
for name, (planet, panel) in cases.items():
    y = solar.annual_yield(planet, panel)
    print(f"annual yield {name:<16} {y.energy_wh / 1000:8.1f} kWh/yr per m2   capacity factor {y.capacity_factor:.3f}")
    annual_rows.append((name, planet, panel, y))


# Write the C# reference used by the EditMode tests
def lit(x: float) -> str:
    s = repr(float(x))
    return s if any(c in s for c in ".eE") else s + ".0"


def table(name: str, rows: list[list[float]], columns: str) -> str:
    body = ",\n".join("            new[] { " + ", ".join(lit(v) for v in row) + " }" for row in rows)
    return f"        /// <summary>{columns}</summary>\n        public static readonly double[][] {name} =\n        {{\n{body}\n        }};\n"


parts = [
    table("AirMass", reference["AirMass"], "zenith deg, air mass"),
    table("SunPosition", reference["SunPosition"], "latitude deg, declination deg, hour angle deg, zenith deg, azimuth deg"),
    table("Incidence", reference["Incidence"], "panel tilt deg, panel azimuth deg, sun zenith deg, sun azimuth deg, cos(incidence)"),
    table("PlaneOfArray", reference["PlaneOfArray"], "tilt deg, panel azimuth deg, zenith deg, sun azimuth deg, DNI, GHI, DHI, albedo, POA W/m2"),
    table("CellTemperature", reference["CellTemperature"], "air C, POA W/m2, NOCT C, cell C"),
    table("PanelPower", reference["PanelPower"], "POA W/m2, cell C, area m2, efficiency, power W"),
    table("EarthOrbit", reference["EarthOrbit"], "days since J2000, Earth-Sun distance AU (NREL SPA), declination deg (NREL SPA)"),
]
annual = []
for name, p, q, y in annual_rows:
    annual.append(
        "            new AnnualCase(\"" + name + "\",\n"
        f"                new PlanetSolarParams({lit(p.luminosity)}, {lit(p.semi_major_axis)}, {lit(p.eccentricity)}, "
        f"{lit(p.obliquity)}, {lit(p.perihelion_longitude)}, {lit(p.day_length_h)}, {lit(p.year_length_h)}, "
        f"{lit(p.optical_depth)}, {lit(p.diffuse_k)}, {lit(p.air_temperature_c)}, {str(p.tidally_locked).lower()}),\n"
        f"                new PanelParams({lit(q.latitude)}, {lit(q.tilt)}, {lit(q.azimuth)}, {lit(q.area)}, "
        f"{lit(q.efficiency)}, {lit(q.noct)}, {lit(q.temp_coeff)}, {lit(q.albedo)}, {lit(q.pressure_ratio)}, "
        f"{lit(q.f_spec)}, {lit(q.f_dust)}, {lit(q.f_shade)}, {lit(q.longitude)}),\n"
        f"                {lit(y.energy_wh)}, {lit(y.capacity_factor)})")

cs = f"""// <auto-generated>
// Generated by prototypes/solar/compare_pvlib.py with pvlib {pvlib.__version__}. Do not edit by hand.
// Expected values come from pvlib, except Annual, which comes from the Python prototype (solar.py).
// </auto-generated>
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Tests.Solar
{{
    public readonly struct AnnualCase
    {{
        public readonly string Name;
        public readonly PlanetSolarParams Planet;
        public readonly PanelParams Panel;
        public readonly double EnergyWh;
        public readonly double CapacityFactor;

        public AnnualCase(string name, PlanetSolarParams planet, PanelParams panel, double energyWh, double capacityFactor)
        {{
            Name = name; Planet = planet; Panel = panel; EnergyWh = energyWh; CapacityFactor = capacityFactor;
        }}

        public override string ToString() => Name;
    }}

    public static class PvlibReference
    {{
{chr(10).join(parts)}
        public static readonly AnnualCase[] Annual =
        {{
{("," + chr(10)).join(annual)}
        }};
    }}
}}
"""
CS_OUT.parent.mkdir(parents=True, exist_ok=True)
CS_OUT.write_text(cs, encoding="utf-8", newline="\n")
print(f"\nwrote {CS_OUT.relative_to(ROOT)}")

if failures:
    print(f"\n{len(failures)} check(s) failed: {', '.join(failures)}")
    sys.exit(1)
print("\nall checks passed")
