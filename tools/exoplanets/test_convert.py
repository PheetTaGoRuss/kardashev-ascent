"""Unit tests for convert.py. Run: python -m unittest discover tools/exoplanets

Rows below are shaped like pscomppars CSV rows (strings, empty = missing). Their values are
textbook Sun/Earth numbers chosen so every expected result can be checked by hand; they are not
archive output.
"""
import math
import unittest

from convert import AU, EARTH_RADIUS, SOLAR_LUMINOSITY, convert, equilibrium_temperature, solar_day_hours

SUN_EARTH_ROW = {
    "pl_name": "Test b", "hostname": "Test", "sy_snum": "1", "sy_pnum": "1", "cb_flag": "0",
    "pl_orbper": "365.256", "pl_orbsmax": "1.0", "pl_orbeccen": "0.0167", "pl_insol": "1.0",
    "pl_eqt": "", "pl_rade": "1.0", "pl_bmasse": "1.0",
    "st_teff": "5772", "st_rad": "1.0", "st_mass": "1.0", "st_lum": "0.0", "st_spectype": "G2 V",
    "st_age": "4.6", "sy_dist": "", "discoverymethod": "Transit",
}


def prov(planet, field):
    return next(p["origin"] for p in planet["provenance"] if p["field"] == field)


class ConvertTests(unittest.TestCase):
    def test_units_from_archive(self):
        p = convert(SUN_EARTH_ROW, {"id": "t", "archiveName": "Test b"})
        self.assertAlmostEqual(p["solar"]["luminosityW"], SOLAR_LUMINOSITY, delta=1e20)
        self.assertAlmostEqual(p["solar"]["semiMajorAxisM"], AU, delta=1.0)
        self.assertAlmostEqual(p["solar"]["yearLengthHours"], 365.256 * 24.0, places=6)
        self.assertAlmostEqual(p["planet"]["insolationWm2"], 1361.0, places=6)
        self.assertAlmostEqual(p["planet"]["radiusM"], EARTH_RADIUS, places=3)
        self.assertEqual(prov(p, "solar.luminosityW"), "nasa")
        self.assertEqual(prov(p, "solar.obliquityDeg"), "assumed")

    def test_earth_gravity_and_escape_velocity(self):
        p = convert(SUN_EARTH_ROW, {"id": "t", "archiveName": "Test b"})
        self.assertAlmostEqual(p["planet"]["surfaceGravityMs2"], 9.80, delta=0.03)
        self.assertAlmostEqual(p["planet"]["escapeVelocityMs"], 11186, delta=15)

    def test_luminosity_falls_back_to_stefan_boltzmann(self):
        row = dict(SUN_EARTH_ROW, st_lum="")
        p = convert(row, {"id": "t", "archiveName": "Test b"})
        # 4 pi R_sun^2 sigma (5772 K)^4 = 3.828e26 W within 0.1 %
        self.assertAlmostEqual(p["solar"]["luminosityW"] / SOLAR_LUMINOSITY, 1.0, delta=1e-3)
        self.assertEqual(prov(p, "solar.luminosityW"), "derived")

    def test_semi_major_axis_falls_back_to_kepler_third_law(self):
        row = dict(SUN_EARTH_ROW, pl_orbsmax="")
        p = convert(row, {"id": "t", "archiveName": "Test b"})
        self.assertAlmostEqual(p["solar"]["semiMajorAxisM"] / AU, 1.0, delta=1e-4)
        self.assertEqual(prov(p, "solar.semiMajorAxisM"), "derived")

    def test_equilibrium_temperature_earth(self):
        # Bond albedo 0.306 -> 254.0 K
        self.assertAlmostEqual(equilibrium_temperature(SOLAR_LUMINOSITY, AU, 0.306), 254.0, delta=0.3)

    def test_missing_eccentricity_is_circular_and_marked_assumed(self):
        row = dict(SUN_EARTH_ROW, pl_orbeccen="")
        p = convert(row, {"id": "t", "archiveName": "Test b"})
        self.assertEqual(p["solar"]["eccentricity"], 0.0)
        self.assertEqual(prov(p, "solar.eccentricity"), "assumed")

    def test_tidally_locked_has_no_solar_day(self):
        p = convert(SUN_EARTH_ROW, {"id": "t", "archiveName": "Test b", "assumed": {"tidallyLocked": True}})
        self.assertTrue(p["flags"]["tidallyLocked"])
        self.assertEqual(p["solar"]["dayLengthHours"], 0.0)
        self.assertEqual(p["solar"]["obliquityDeg"], 0.0)

    def test_circumbinary_comes_from_cb_flag(self):
        row = dict(SUN_EARTH_ROW, sy_snum="2", cb_flag="1")
        p = convert(row, {"id": "t", "archiveName": "Test b"})
        self.assertTrue(p["flags"]["circumbinary"])
        self.assertTrue(any("circumbinary" in w for w in p["_warnings"]))

    def test_wide_companion_is_not_circumbinary(self):
        # Proxima Cen b: 3 stars in the system, but it orbits Proxima alone.
        row = dict(SUN_EARTH_ROW, sy_snum="3", cb_flag="0")
        p = convert(row, {"id": "t", "archiveName": "Test b"})
        self.assertFalse(p["flags"]["circumbinary"])
        self.assertEqual(p["star"]["count"], 3)
        self.assertEqual(p["_warnings"], [])

    def test_surface_class_from_radius(self):
        for radius, expected in (("1.0", "rocky"), ("1.6", "rocky"), ("1.63", "uncertain"), ("8.45", "gaseous")):
            p = convert(dict(SUN_EARTH_ROW, pl_rade=radius), {"id": "t", "archiveName": "Test b"})
            self.assertEqual(p["flags"]["surface"], expected, radius)

    def test_insolation_mismatch_warns(self):
        row = dict(SUN_EARTH_ROW, pl_insol="2.0")
        p = convert(row, {"id": "t", "archiveName": "Test b"})
        self.assertTrue(any("pl_insol differs" in w for w in p["_warnings"]))

    def test_air_temperature_derived_from_equilibrium(self):
        row = dict(SUN_EARTH_ROW, pl_eqt="255")
        p = convert(row, {"id": "t", "archiveName": "Test b"})
        self.assertAlmostEqual(p["solar"]["airTemperatureC"], 255 - 273.15, places=9)
        self.assertEqual(prov(p, "solar.airTemperatureC"), "derived")

    def test_greenhouse_offset_raises_air_temperature(self):
        row = dict(SUN_EARTH_ROW, pl_eqt="255")
        p = convert(row, {"id": "t", "archiveName": "Test b", "assumed": {"greenhouseOffsetK": 33.0}})
        self.assertAlmostEqual(p["solar"]["airTemperatureC"], 255 + 33 - 273.15, places=9)

    def test_solar_day_from_sidereal_rotation(self):
        self.assertAlmostEqual(solar_day_hours(23.9345, 365.256 * 24.0), 24.0, delta=0.001)
        self.assertAlmostEqual(solar_day_hours(5832.5, 224.701 * 24.0, retrograde=True) / 24.0, 116.75, delta=0.05)
        self.assertAlmostEqual(solar_day_hours(24.6229, 686.98 * 24.0), 24.6597, delta=0.001)   # Mars sol
        with self.assertRaises(ValueError):
            solar_day_hours(100.0, 100.0)

    def test_rotation_period_sets_derived_day_length(self):
        p = convert(SUN_EARTH_ROW, {"id": "t", "archiveName": "Test b", "assumed": {"rotationPeriodHours": 23.9345}})
        self.assertAlmostEqual(p["solar"]["dayLengthHours"], 24.0, delta=0.001)
        self.assertEqual(prov(p, "solar.dayLengthHours"), "derived")

    def test_hand_made_planet_is_all_assumed(self):
        entry = {"id": "v", "archiveName": None, "assumed": {
            "luminositySun": 1.0, "semiMajorAxisAu": 1.0, "yearLengthDays": 365.256,
            "starTeffK": 5772, "airTemperatureC": 25.0}}
        p = convert(None, entry)
        self.assertEqual(p["source"], "hand-made game planet")
        self.assertTrue(all(x["origin"] in ("assumed", "derived") for x in p["provenance"]))

    def test_game_config_converts(self):
        import json
        from pathlib import Path
        config = json.loads((Path(__file__).parent / "planets.json").read_text(encoding="utf-8"))
        viridia = next(e for e in config["planets"] if e["id"] == "viridia")
        p = convert(None, viridia)
        self.assertAlmostEqual(p["planet"]["insolationWm2"], 1361.0, delta=2.0)
        self.assertTrue(math.isclose(p["solar"]["obliquityDeg"], 23.44))
        self.assertEqual(p["flags"]["clouds"], "earth")

    def test_clouds_and_pressure_default_to_assumed_clear_sky_at_one_atmosphere(self):
        p = convert(SUN_EARTH_ROW, {"id": "t", "archiveName": "Test b", "assumed": {}})
        self.assertEqual(p["flags"]["clouds"], "none")
        self.assertEqual(prov(p, "flags.clouds"), "assumed")
        self.assertEqual(p["solar"]["surfacePressureEarth"], 1.0)
        self.assertEqual(prov(p, "solar.surfacePressureEarth"), "assumed")

    def test_terrain_seed_is_stable_and_overridable(self):
        a = convert(SUN_EARTH_ROW, {"id": "t", "archiveName": "Test b", "assumed": {}})
        b = convert(SUN_EARTH_ROW, {"id": "t", "archiveName": "Test b", "assumed": {}})
        self.assertEqual(a["look"]["terrainSeed"], b["look"]["terrainSeed"])
        self.assertTrue(0 <= a["look"]["terrainSeed"] < 2_000_000_000)
        self.assertEqual(prov(a, "look.terrainSeed"), "assumed")
        other = convert(SUN_EARTH_ROW, {"id": "u", "archiveName": "Test b", "assumed": {}})
        self.assertNotEqual(a["look"]["terrainSeed"], other["look"]["terrainSeed"])
        fixed = convert(SUN_EARTH_ROW, {"id": "t", "archiveName": "Test b", "assumed": {"terrainSeed": 7}})
        self.assertEqual(fixed["look"]["terrainSeed"], 7)

    def test_unknown_cloud_model_is_rejected(self):
        with self.assertRaises(ValueError):
            convert(SUN_EARTH_ROW, {"id": "t", "archiveName": "Test b", "assumed": {"clouds": "venus"}})


if __name__ == "__main__":
    unittest.main()
