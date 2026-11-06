"""Turn NASA Exoplanet Archive rows into the game's planet data.

Pure functions, standard library only, so they can be unit-tested without network.
Every output value records where it came from:
  nasa     - copied from the archive (unit-converted only)
  derived  - computed from archive values with a physics formula
  assumed  - not measurable today; a game value from planets.json
"""
from __future__ import annotations

import math
import zlib
import re
from typing import Any

# Physical constants (SI). IAU 2015 nominal values where they exist.
SOLAR_LUMINOSITY = 3.828e26        # W
SOLAR_RADIUS = 6.957e8             # m
SOLAR_MASS = 1.98847e30            # kg  (GM_sun / G)
EARTH_RADIUS = 6.3781e6            # m   (equatorial)
EARTH_MASS = 5.9722e24             # kg
AU = 1.495978707e11                # m
SOLAR_CONSTANT = 1361.0            # W/m^2 at 1 AU
G = 6.67430e-11                    # m^3 kg^-1 s^-2
SIGMA = 5.670374419e-8             # W m^-2 K^-4
HOURS_PER_DAY = 24.0

# Archive columns the fetcher asks for (table pscomppars).
COLUMNS = [
    "pl_name", "hostname", "sy_snum", "sy_pnum", "cb_flag",
    "pl_orbper", "pl_orbsmax", "pl_orbeccen", "pl_insol", "pl_eqt",
    "pl_rade", "pl_bmasse",
    "st_teff", "st_rad", "st_mass", "st_lum", "st_spectype", "st_age",
    "sy_dist", "discoverymethod",
]

# Radius bins for "does this planet have a surface to build on" (Earth radii).
# Most planets above ~1.6 R_earth are not rocky (Rogers 2015, ApJ 801:41); the radius
# valley sits at ~1.5-2.0 R_earth (Fulton et al. 2017, AJ 154:109).
ROCKY_MAX_REARTH = 1.6
GASEOUS_MIN_REARTH = 2.0

# Game values that the archive cannot give. planets.json may override any of them per planet.
DEFAULT_ASSUMED = {
    "obliquityDeg": 0.0,
    "perihelionLongitudeDeg": 0.0,
    "dayLengthHours": 24.0,
    "opticalDepth": 0.32,
    "diffuseK": 0.3,
    "airTemperatureC": None,       # None: derive from equilibrium temperature + greenhouseOffsetK
    "greenhouseOffsetK": 0.0,      # surface minus equilibrium temperature; Earth ~ 288 - 255 = 33 K
    "rotationPeriodHours": None,   # sidereal spin; when set, the solar day is derived from it
    "retrograde": False,           # spins against its orbital motion (like Venus)
    "bondAlbedo": 0.3,
    "tidallyLocked": False,
    "clouds": "none",              # "earth": NASA POWER cloud climatology (Earth-like planets only); "none": clear sky
    "surfacePressureEarth": 1.0,   # surface pressure in Earth atmospheres; picks how panels cool (wind or radiation)
    "terrainSeed": None,           # noise seed for the planet's continents; None: from the planet id (crc32)
}


def _num(row: dict[str, Any], key: str) -> float | None:
    """Archive cell as float, or None when empty/missing/NaN."""
    value = row.get(key)
    if value is None or value == "":
        return None
    try:
        number = float(value)
    except (TypeError, ValueError):
        return None
    return None if math.isnan(number) else number


def slug(name: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")


class Builder:
    """Collects values and their provenance for one planet."""

    def __init__(self) -> None:
        self.values: dict[str, dict[str, Any]] = {}
        self.provenance: list[dict[str, str]] = []
        self.warnings: list[str] = []

    def put(self, group: str, field: str, value: Any, origin: str, note: str = "") -> None:
        self.values.setdefault(group, {})[field] = value
        self.provenance.append({"field": f"{group}.{field}", "origin": origin, "note": note})


def stellar_luminosity(row: dict[str, Any]) -> tuple[float, str, str] | None:
    """L in W. st_lum is log10(L/L_sun); fall back to L = 4 pi R^2 sigma T^4."""
    log_l = _num(row, "st_lum")
    if log_l is not None:
        return 10.0 ** log_l * SOLAR_LUMINOSITY, "nasa", "st_lum = log10(L/Lsun)"
    radius, teff = _num(row, "st_rad"), _num(row, "st_teff")
    if radius is not None and teff is not None:
        r = radius * SOLAR_RADIUS
        return 4.0 * math.pi * r * r * SIGMA * teff ** 4, "derived", "4*pi*R^2*sigma*Teff^4 from st_rad, st_teff"
    return None


def semi_major_axis(row: dict[str, Any]) -> tuple[float, str, str] | None:
    """a in m. Falls back to Kepler's third law a^3 = G M P^2 / (4 pi^2), planet mass neglected."""
    a_au = _num(row, "pl_orbsmax")
    if a_au is not None:
        return a_au * AU, "nasa", "pl_orbsmax (au)"
    period_d, mass = _num(row, "pl_orbper"), _num(row, "st_mass")
    if period_d is not None and mass is not None:
        p = period_d * 86400.0
        a = (G * mass * SOLAR_MASS * p * p / (4.0 * math.pi ** 2)) ** (1.0 / 3.0)
        return a, "derived", "Kepler III from pl_orbper, st_mass"
    return None


def solar_day_hours(rotation_hours: float, orbit_hours: float, retrograde: bool = False) -> float:
    """Solar day from the sidereal spin: 1/D = 1/P_rot - 1/P_orb (prograde) or + (retrograde).
    Earth 23.9345 h -> 24.000 h; Venus 5832.5 h retrograde -> 116.75 days."""
    inverse = 1.0 / rotation_hours + (1.0 if retrograde else -1.0) / orbit_hours
    if inverse <= 0.0:
        raise ValueError("spin equals the orbit: the planet is tidally locked (set tidallyLocked instead)")
    return 1.0 / inverse


def terrain_seed(planet_id: str) -> int:
    """Stable seed for a planet's terrain noise (crc32 of its id, kept in int32 range for C#)."""
    return zlib.crc32(planet_id.encode("utf-8")) % 2_000_000_000


def equilibrium_temperature(luminosity: float, distance: float, albedo: float) -> float:
    """T_eq = [L (1 - A) / (16 pi sigma d^2)]^(1/4), K. Full heat redistribution."""
    return (luminosity * (1.0 - albedo) / (16.0 * math.pi * SIGMA * distance * distance)) ** 0.25


def convert(row: dict[str, Any] | None, entry: dict[str, Any], retrieved: str = "") -> dict[str, Any]:
    """One planet in the game's JSON shape. row is None for hand-made planets (all values assumed)."""
    assumed = {**DEFAULT_ASSUMED, **entry.get("assumed", {})}
    b = Builder()
    row = row or {}
    name = entry.get("archiveName")

    # ---- star -------------------------------------------------------------------------
    lum = stellar_luminosity(row) if row else None
    if lum is None:
        if "luminositySun" not in assumed:
            raise ValueError(f"{entry['id']}: no stellar luminosity in archive or planets.json")
        lum = (assumed["luminositySun"] * SOLAR_LUMINOSITY, "assumed", "planets.json luminositySun")
    b.put("solar", "luminosityW", lum[0], lum[1], lum[2])

    teff = _num(row, "st_teff")
    if teff is not None:
        b.put("star", "teffK", teff, "nasa", "st_teff")
    elif "starTeffK" in assumed:
        b.put("star", "teffK", float(assumed["starTeffK"]), "assumed", "planets.json")
    for col, field, scale in (("st_rad", "radiusM", SOLAR_RADIUS), ("st_mass", "massKg", SOLAR_MASS)):
        v = _num(row, col)
        if v is not None:
            b.put("star", field, v * scale, "nasa", col)
    if row.get("st_spectype"):
        b.put("star", "spectralType", row["st_spectype"], "nasa", "st_spectype")
    age = _num(row, "st_age")
    if age is not None:
        b.put("star", "ageGyr", age, "nasa", "st_age")
    star_count = int(_num(row, "sy_snum") or assumed.get("starCount", 1))
    b.put("star", "count", star_count, "nasa" if row else "assumed", "sy_snum")
    # cb_flag = 1 only when the planet orbits both stars (P-type). A multi-star system with
    # cb_flag = 0 (e.g. Proxima Cen b) has a distant companion whose light is negligible.
    cb_flag = _num(row, "cb_flag")
    circumbinary = bool(cb_flag) if cb_flag is not None else bool(assumed.get("circumbinary", False))
    if circumbinary:
        b.warnings.append(
            f"{entry['id']}: circumbinary (cb_flag = 1). Archive stellar values describe the primary only; "
            "use Simulation.Solar.BinaryStar (Kepler16 holds both stars from Doyle et al. 2011).")

    # ---- orbit ------------------------------------------------------------------------
    a = semi_major_axis(row) if row else None
    if a is None:
        if "semiMajorAxisAu" not in assumed:
            raise ValueError(f"{entry['id']}: no semi-major axis in archive or planets.json")
        a = (assumed["semiMajorAxisAu"] * AU, "assumed", "planets.json semiMajorAxisAu")
    b.put("solar", "semiMajorAxisM", a[0], a[1], a[2])

    ecc = _num(row, "pl_orbeccen")
    if ecc is not None:
        b.put("solar", "eccentricity", ecc, "nasa", "pl_orbeccen")
    else:
        b.put("solar", "eccentricity", float(assumed.get("eccentricity", 0.0)), "assumed",
              "not in archive; circular orbit assumed")

    period_d = _num(row, "pl_orbper")
    if period_d is not None:
        b.put("solar", "yearLengthHours", period_d * HOURS_PER_DAY, "nasa", "pl_orbper (days)")
    else:
        if "yearLengthDays" in assumed:
            period_d = float(assumed["yearLengthDays"])
            b.put("solar", "yearLengthHours", period_d * HOURS_PER_DAY, "assumed", "planets.json")
        else:
            mass = _num(row, "st_mass") or assumed.get("starMassSun")
            if mass is None:
                raise ValueError(f"{entry['id']}: no orbital period and no stellar mass for Kepler III")
            period_s = 2.0 * math.pi * math.sqrt(a[0] ** 3 / (G * mass * SOLAR_MASS))
            period_d = period_s / 86400.0
            b.put("solar", "yearLengthHours", period_d * HOURS_PER_DAY, "derived", "Kepler III")

    # ---- rotation (never measured for exoplanets) --------------------------------------
    locked = bool(assumed["tidallyLocked"])
    b.put("flags", "tidallyLocked", locked, "assumed", "rotation is not measured for exoplanets")
    clouds = str(assumed["clouds"])
    if clouds not in ("none", "earth"):
        raise ValueError(f"{entry['id']}: clouds must be 'none' or 'earth', not {clouds!r}")
    b.put("flags", "clouds", clouds, "assumed",
          "earth = NASA POWER cloud climatology by latitude and month; none = clear sky (cloud cover unknown)")
    b.put("flags", "circumbinary", circumbinary, "nasa" if cb_flag is not None else "assumed",
          "cb_flag: planet orbits both stars")
    if locked:
        # A synchronously rotating planet has no solar day; 0 means "infinite" (JsonUtility has no null).
        b.put("solar", "dayLengthHours", 0.0, "assumed", "tidally locked: star fixed in the sky, 0 = no solar day")
        b.put("solar", "obliquityDeg", 0.0, "assumed", "tidally locked: obliquity damped to ~0")
    else:
        if assumed["rotationPeriodHours"] is not None:
            year_hours = b.values["solar"]["yearLengthHours"]
            day = solar_day_hours(float(assumed["rotationPeriodHours"]), year_hours, bool(assumed["retrograde"]))
            b.put("solar", "dayLengthHours", day, "derived",
                  "1/D = 1/P_rot -/+ 1/P_orb from the assumed sidereal rotation period")
        else:
            b.put("solar", "dayLengthHours", float(assumed["dayLengthHours"]), "assumed", "solar day, game value")
        b.put("solar", "obliquityDeg", float(assumed["obliquityDeg"]), "assumed", "axial tilt, game value")
    b.put("solar", "perihelionLongitudeDeg", float(assumed["perihelionLongitudeDeg"]), "assumed",
          "equinox -> periapsis, game value")

    # ---- atmosphere (game values) ------------------------------------------------------
    b.put("solar", "opticalDepth", float(assumed["opticalDepth"]), "assumed", "broadband tau, game value")
    b.put("solar", "surfacePressureEarth", float(assumed["surfacePressureEarth"]), "assumed",
          "surface pressure in Earth atmospheres, game value")
    b.put("solar", "diffuseK", float(assumed["diffuseK"]), "assumed", "diffuse-light k, game value")

    # ---- planet body -------------------------------------------------------------------
    insol = _num(row, "pl_insol")
    flux_derived = lum[0] / (4.0 * math.pi * a[0] ** 2)
    if insol is not None:
        b.put("planet", "insolationWm2", insol * SOLAR_CONSTANT, "nasa", "pl_insol (Earth flux) x 1361 W/m^2")
        mismatch = abs(insol * SOLAR_CONSTANT / flux_derived - 1.0)
        if mismatch > 0.25:
            b.warnings.append(f"{entry['id']}: pl_insol differs from L/(4 pi a^2) by {mismatch:.0%}; "
                              "archive values come from different papers.")
    else:
        b.put("planet", "insolationWm2", flux_derived, "derived", "L / (4 pi a^2)")

    radius_e = _num(row, "pl_rade") if row else None
    mass_e = _num(row, "pl_bmasse") if row else None
    if radius_e is None and "radiusEarth" in assumed:
        radius_e = float(assumed["radiusEarth"])
        b.put("planet", "radiusM", radius_e * EARTH_RADIUS, "assumed", "planets.json")
    elif radius_e is not None:
        b.put("planet", "radiusM", radius_e * EARTH_RADIUS, "nasa", "pl_rade")
    if mass_e is None and "massEarth" in assumed:
        mass_e = float(assumed["massEarth"])
        b.put("planet", "massKg", mass_e * EARTH_MASS, "assumed", "planets.json")
    elif mass_e is not None:
        b.put("planet", "massKg", mass_e * EARTH_MASS, "nasa", "pl_bmasse (may be M sin i)")
    if radius_e is not None:
        if radius_e <= ROCKY_MAX_REARTH:
            surface = "rocky"
        elif radius_e < GASEOUS_MIN_REARTH:
            surface = "uncertain"
        else:
            surface = "gaseous"
            b.warnings.append(f"{entry['id']}: radius {radius_e:.2f} R_earth, almost certainly no solid surface; "
                              "build on a hypothetical moon or treat the planet as fictional rocky.")
        b.put("flags", "surface", surface, "derived",
              f"radius bins: rocky <= {ROCKY_MAX_REARTH}, gaseous >= {GASEOUS_MIN_REARTH} R_earth (Rogers 2015; Fulton 2017)")
    if radius_e is not None and mass_e is not None:
        r, m = radius_e * EARTH_RADIUS, mass_e * EARTH_MASS
        b.put("planet", "surfaceGravityMs2", G * m / (r * r), "derived", "g = GM/R^2")
        b.put("planet", "escapeVelocityMs", math.sqrt(2.0 * G * m / r), "derived", "v = sqrt(2GM/R)")

    albedo = float(assumed["bondAlbedo"])
    b.put("planet", "bondAlbedo", albedo, "assumed", "game value")
    teq = _num(row, "pl_eqt")
    if teq is not None:
        b.put("planet", "equilibriumTempK", teq, "nasa", "pl_eqt (albedo assumption varies by paper)")
    else:
        teq = equilibrium_temperature(lum[0], a[0], albedo)
        b.put("planet", "equilibriumTempK", teq, "derived", "[L(1-A)/(16 pi sigma a^2)]^(1/4)")

    if assumed["airTemperatureC"] is None:
        offset = float(assumed["greenhouseOffsetK"])
        b.put("solar", "airTemperatureC", teq + offset - 273.15, "derived",
              f"equilibrium temperature + greenhouse offset {offset:g} K (assumed)")
    else:
        b.put("solar", "airTemperatureC", float(assumed["airTemperatureC"]), "assumed", "game value")

    if assumed["terrainSeed"] is not None:
        b.put("look", "terrainSeed", int(assumed["terrainSeed"]), "assumed", "terrain noise seed, game value")
    else:
        b.put("look", "terrainSeed", terrain_seed(entry["id"]), "assumed", "terrain noise seed from the planet id")

    out: dict[str, Any] = {
        "id": entry["id"],
        "displayName": entry.get("displayName") or name or entry["id"],
        "archiveName": name or "",
        "source": "NASA Exoplanet Archive, table pscomppars" if row else "hand-made game planet",
        "retrieved": retrieved if row else "",
        "solar": b.values.get("solar", {}),
        "star": b.values.get("star", {}),
        "planet": b.values.get("planet", {}),
        "flags": b.values.get("flags", {}),
        "look": b.values.get("look", {}),
        "provenance": b.provenance,
    }
    out["_warnings"] = b.warnings
    return out
