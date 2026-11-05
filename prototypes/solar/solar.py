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

def air_mass_kasten_young(zenith_degrees: float, pressure_ratio: float = 1.0) -> float:
    """Relative air mass (Kasten & Young 1989) times p/p0 for altitude."""
    cz = math.cos(math.radians(zenith_degrees))
    return pressure_ratio / (cz + 0.50572 * (96.07995 - zenith_degrees) ** -1.6364)


def direct_normal(g0: float, optical_depth: float, air_mass: float) -> float:
    return g0 * math.exp(-optical_depth * air_mass)


def diffuse_horizontal(g0: float, cos_zenith: float, optical_depth: float,
                       air_mass: float, k: float) -> float:
    """Game model: DHI = k * G0 * cos z * (1 - exp(-tau * AM))."""
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
    return t_air + (noct - 20.0) / 800.0 * poa


def panel_power(poa: float, area: float, efficiency: float, t_cell: float,
                temp_coeff: float = -0.004, f_spec: float = 1.0, f_dust: float = 0.0) -> float:
    """f_dust is the fraction of light blocked by dust (0 = clean)."""
    return (poa * area * efficiency * (1.0 + temp_coeff * (t_cell - 25.0))
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
    diffuse_k: float = 0.4
    air_temperature_c: float = 25.0


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


@dataclass
class AnnualYield:
    energy_wh: float
    capacity_factor: float


def annual_yield(planet: Planet, panel: Panel, days: int = 24, slots: int = 48) -> AnnualYield:
    """Energy per year from `days` representative days x `slots` time steps per day."""
    normal = panel_normal(panel.tilt, panel.azimuth)
    dt_h = planet.day_length_h / slots
    day_weight = (planet.year_length_h / planet.day_length_h) / days
    total_wh = 0.0
    for d in range(days):
        M = 2.0 * math.pi * (d + 0.5) / days
        nu = true_anomaly(solve_kepler(M, planet.eccentricity), planet.eccentricity)
        r = orbital_distance(planet.semi_major_axis, planet.eccentricity, nu)
        g0 = extraterrestrial_irradiance(planet.luminosity, r)
        decl = declination(planet.obliquity, nu + planet.perihelion_longitude)
        day_wh = 0.0
        for s in range(slots):
            h = hour_angle((s + 0.5) * dt_h, planet.day_length_h)
            sun = sun_direction(panel.latitude, decl, h)
            if sun[2] <= 0.0:
                continue
            am = air_mass_kasten_young(zenith_deg(sun), panel.pressure_ratio)
            dni = direct_normal(g0, planet.optical_depth, am)
            dhi = diffuse_horizontal(g0, sun[2], planet.optical_depth, am, planet.diffuse_k)
            ghi = global_horizontal(dni, dhi, sun[2])
            poa = plane_of_array(dni, dhi, ghi, cos_incidence(normal, sun),
                                 panel.tilt, panel.albedo, panel.f_shade)
            t_cell = cell_temperature(planet.air_temperature_c, poa, panel.noct)
            day_wh += panel_power(poa, panel.area, panel.efficiency, t_cell,
                                  panel.temp_coeff, panel.f_spec, panel.f_dust) * dt_h
        total_wh += day_wh * day_weight
    rated_w = panel.area * panel.efficiency * STC_IRRADIANCE
    return AnnualYield(total_wh, total_wh / (rated_w * planet.year_length_h))
