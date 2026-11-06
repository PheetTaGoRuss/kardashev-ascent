"""Kardashev Ascent - Phase 0 solar model (design doc, solar panel steps 1-9).

Reference implementation for the C# port in Assets/_Project/Scripts/Simulation/Solar.
Conventions: angles in radians unless the name ends in _deg; SI units;
local frame is (east, north, up); panel azimuth is clockwise from north.
"""
from __future__ import annotations

import math
from dataclasses import dataclass

AU = 1.495978707e11          # m
SOLAR_LUMINOSITY = 3.828e26  # W
STC_IRRADIANCE = 1000.0      # W/m^2, rating condition for panels


# Step 1 - orbit and light above the atmosphere

def solve_kepler(mean_anomaly: float, e: float, iterations: int = 10) -> float:
    """Eccentric anomaly E from M = E - e sin E (Newton iteration)."""
    E = mean_anomaly if e < 0.8 else math.pi
    for _ in range(iterations):
        E -= (E - e * math.sin(E) - mean_anomaly) / (1.0 - e * math.cos(E))
    return E


def true_anomaly(eccentric_anomaly: float, e: float) -> float:
    half = eccentric_anomaly / 2.0
    return 2.0 * math.atan2(math.sqrt(1.0 + e) * math.sin(half),
                            math.sqrt(1.0 - e) * math.cos(half))


def orbital_distance(a: float, e: float, nu: float) -> float:
    return a * (1.0 - e * e) / (1.0 + e * math.cos(nu))


def extraterrestrial_irradiance(luminosity: float, r: float) -> float:
    return luminosity / (4.0 * math.pi * r * r)


# Step 2 - star position on the sky

def declination(obliquity: float, ecliptic_longitude: float) -> float:
    return math.asin(math.sin(obliquity) * math.sin(ecliptic_longitude))


def hour_angle(local_solar_time: float, day_length: float) -> float:
    """0 at local noon, negative in the morning. Time and day length in the same unit."""
    return 2.0 * math.pi * local_solar_time / day_length - math.pi


# Step 3 - sun direction in the local (east, north, up) frame

def sun_direction(latitude: float, decl: float, h: float) -> tuple[float, float, float]:
    cd = math.cos(decl)
    return (-cd * math.sin(h),
            math.cos(latitude) * math.sin(decl) - math.sin(latitude) * cd * math.cos(h),
            math.sin(latitude) * math.sin(decl) + math.cos(latitude) * cd * math.cos(h))


def zenith_deg(sun: tuple[float, float, float]) -> float:
    return math.degrees(math.acos(max(-1.0, min(1.0, sun[2]))))


def azimuth_deg(sun: tuple[float, float, float]) -> float:
    return math.degrees(math.atan2(sun[0], sun[1])) % 360.0


# Step 4 - angle of incidence on the panel

def panel_normal(tilt: float, azimuth: float) -> tuple[float, float, float]:
    sb = math.sin(tilt)
    return (sb * math.sin(azimuth), sb * math.cos(azimuth), math.cos(tilt))


def cos_incidence(normal, sun) -> float:
    return max(0.0, normal[0] * sun[0] + normal[1] * sun[1] + normal[2] * sun[2])


# Step 5 - atmosphere

MAX_ZENITH_FOR_AIR_MASS = 90.0  # Kasten-Young gives 37.92 here; beyond it the formula breaks down


def air_mass_kasten_young(zenith_degrees: float, pressure_ratio: float = 1.0) -> float:
    """Relative air mass (Kasten & Young 1989) times p/p0 for altitude.
    Zenith is clamped to 90 deg so the result is never NaN; callers set DNI = 0 below the horizon."""
    z = min(zenith_degrees, MAX_ZENITH_FOR_AIR_MASS)
    cz = math.cos(math.radians(z))
    return pressure_ratio / (cz + 0.50572 * (96.07995 - z) ** -1.6364)


def direct_normal(g0: float, optical_depth: float, air_mass: float) -> float:
    return g0 * math.exp(-optical_depth * air_mass)


def diffuse_horizontal(g0: float, cos_zenith: float, optical_depth: float,
                       air_mass: float, k: float) -> float:
    """Game model: DHI = k * G0 * cos z * (1 - exp(-tau * AM)). k is clamped to 0..1 so GHI <= G0 cos z."""
    k = max(0.0, min(1.0, k))
    return k * g0 * cos_zenith * (1.0 - math.exp(-optical_depth * air_mass))


def global_horizontal(dni: float, dhi: float, cos_zenith: float) -> float:
    return dni * cos_zenith + dhi


# Step 6 - light on the panel (isotropic sky)

def plane_of_array(dni: float, dhi: float, ghi: float, cos_theta: float,
                   tilt: float, albedo: float, f_shade: float = 1.0) -> float:
    ct = math.cos(tilt)
    return (f_shade * dni * cos_theta
            + dhi * (1.0 + ct) / 2.0
            + albedo * ghi * (1.0 - ct) / 2.0)


# Step 7 - heat, spectrum and dust

def cell_temperature(t_air: float, poa: float, noct: float = 45.0) -> float:
    """NOCT model: Earth-like air, 1 m/s wind."""
    return t_air + (noct - 20.0) / 800.0 * poa


SIGMA = 5.670374419e-8
VACUUM_PRESSURE_EARTH = 0.01   # below: radiation only
AIR_PRESSURE_EARTH = 0.1       # above: NOCT only


def radiative_cell_temperature(poa: float, efficiency: float, absorptance: float = 0.9,
                               emissivity_front: float = 0.85, emissivity_back: float = 0.85) -> float:
    """No air: alpha*G*(1-eta) = (eps_f + eps_b)*sigma*T^4, degC. 1361 W/m^2 -> 317.5 K."""
    absorbed = max(0.0, absorptance * poa * (1.0 - efficiency))
    return (absorbed / ((emissivity_front + emissivity_back) * SIGMA)) ** 0.25 - 273.15


def convective_weight(surface_pressure_earth: float) -> float:
    """Share of the NOCT model: 0 at <= 0.01 x Earth, 1 at >= 0.1, linear in log10 between [inference]."""
    if surface_pressure_earth <= VACUUM_PRESSURE_EARTH:
        return 0.0
    if surface_pressure_earth >= AIR_PRESSURE_EARTH:
        return 1.0
    return (math.log10(surface_pressure_earth / VACUUM_PRESSURE_EARTH)
            / math.log10(AIR_PRESSURE_EARTH / VACUUM_PRESSURE_EARTH))


def cell_temperature_any(t_air: float, poa: float, noct: float,
                         surface_pressure_earth: float, efficiency: float) -> float:
    """NOCT in thick air, radiative balance in vacuum, blended in thin air."""
    w = convective_weight(surface_pressure_earth)
    if w >= 1.0:
        return cell_temperature(t_air, poa, noct)
    rad = radiative_cell_temperature(poa, efficiency)
    return rad if w <= 0.0 else w * cell_temperature(t_air, poa, noct) + (1.0 - w) * rad


def panel_power(poa: float, area: float, efficiency: float, t_cell: float,
                temp_coeff: float = -0.004, f_spec: float = 1.0, f_dust: float = 0.0) -> float:
    """f_dust is the fraction of light blocked by dust (0 = clean)."""
    return (poa * area * efficiency * max(0.0, 1.0 + temp_coeff * (t_cell - 25.0))
            * f_spec * (1.0 - f_dust))


# Step 9 - is it worth it

def eroi(lifetime_energy: float, embodied_energy: float) -> float:
    return lifetime_energy / embodied_energy


def payback_years(embodied_energy: float, energy_per_year: float) -> float:
    return embodied_energy / energy_per_year


# Step 8 - whole year

@dataclass
class Planet:
    luminosity: float = SOLAR_LUMINOSITY       # W
    semi_major_axis: float = AU                # m
    eccentricity: float = 0.0167
    obliquity: float = math.radians(23.44)
    perihelion_longitude: float = math.radians(282.94)  # equinox -> periapsis
    day_length_h: float = 24.0                 # solar day
    year_length_h: float = 365.256 * 24.0
    optical_depth: float = 0.32                # clear-sky broadband tau (game value)
    diffuse_k: float = 0.3                     # Earth ~0.3 (0.25-0.4); dusty Mars-like 0.6-0.8 [inference]
    air_temperature_c: float = 25.0
    tidally_locked: bool = False               # star fixed in the sky; day_length_h is ignored
    surface_pressure_earth: float = 1.0        # x Earth's; picks wind (NOCT) or radiative cooling


@dataclass
class Panel:
    latitude: float
    tilt: float
    azimuth: float = math.pi                   # facing south
    area: float = 1.0                          # m^2
    efficiency: float = 0.20
    noct: float = 45.0
    temp_coeff: float = -0.004
    albedo: float = 0.2
    pressure_ratio: float = 1.0
    f_spec: float = 1.0
    f_dust: float = 0.0
    f_shade: float = 1.0
    longitude: float = 0.0                     # only matters on tidally locked planets


@dataclass
class AnnualYield:
    energy_wh: float
    capacity_factor: float


ECCENTRIC_ORBIT = 0.3   # above this, sample more of the orbit (fast pass near periapsis)


def sample_days(planet: Planet) -> int:
    return 72 if planet.eccentricity > ECCENTRIC_ORBIT else 24


def _instant_power(planet: Planet, panel: Panel, normal, g0: float, sun) -> float:
    """Panel power (W) for one star direction; 0 when the star is below the horizon."""
    if sun[2] <= 0.0:
        return 0.0
    am = air_mass_kasten_young(zenith_deg(sun), panel.pressure_ratio)
    dni = direct_normal(g0, planet.optical_depth, am)
    dhi = diffuse_horizontal(g0, sun[2], planet.optical_depth, am, planet.diffuse_k)
    ghi = global_horizontal(dni, dhi, sun[2])
    poa = plane_of_array(dni, dhi, ghi, cos_incidence(normal, sun), panel.tilt, panel.albedo, panel.f_shade)
    t_cell = cell_temperature_any(planet.air_temperature_c, poa, panel.noct,
                                  planet.surface_pressure_earth * panel.pressure_ratio, panel.efficiency)
    return panel_power(poa, panel.area, panel.efficiency, t_cell, panel.temp_coeff, panel.f_spec, panel.f_dust)


def annual_yield(planet: Planet, panel: Panel, days: int = 0, slots: int = 48) -> AnnualYield:
    """Energy per orbit from `days` points spread evenly in mean anomaly (0 = 24, or 72 when e > 0.3).
    Rotating planets: `slots` time steps across each of those days.
    Tidally locked planets: the star stays put, at hour angle h = panel longitude - sub-stellar
    longitude (0 here, no libration), so each sample stands for 1/days of the orbit."""
    if days <= 0:
        days = sample_days(planet)
    if not planet.tidally_locked and planet.day_length_h <= 0.0:
        raise ValueError("day_length_h must be > 0 unless the planet is tidally locked")

    normal = panel_normal(panel.tilt, panel.azimuth)
    total_wh = 0.0
    for d in range(days):
        M = 2.0 * math.pi * (d + 0.5) / days
        nu = true_anomaly(solve_kepler(M, planet.eccentricity), planet.eccentricity)
        r = orbital_distance(planet.semi_major_axis, planet.eccentricity, nu)
        g0 = extraterrestrial_irradiance(planet.luminosity, r)
        decl = declination(planet.obliquity, nu + planet.perihelion_longitude)
        if planet.tidally_locked:
            sun = sun_direction(panel.latitude, decl, panel.longitude)
            total_wh += _instant_power(planet, panel, normal, g0, sun) * planet.year_length_h / days
            continue
        dt_h = planet.day_length_h / slots
        day_wh = 0.0
        for s in range(slots):
            sun = sun_direction(panel.latitude, decl, hour_angle((s + 0.5) * dt_h, planet.day_length_h))
            day_wh += _instant_power(planet, panel, normal, g0, sun) * dt_h
        total_wh += day_wh * (planet.year_length_h / planet.day_length_h) / days
    rated_w = panel.area * panel.efficiency * STC_IRRADIANCE
    return AnnualYield(total_wh, total_wh / (rated_w * planet.year_length_h))
