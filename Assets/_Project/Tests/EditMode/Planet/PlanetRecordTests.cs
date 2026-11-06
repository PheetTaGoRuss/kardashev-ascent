using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Planet
{
    /// <summary>planets.json records → solar model values and the labels the game shows (issue #11).</summary>
    public class PlanetRecordTests
    {
        /// <summary>Shaped like the Proxima Cen b entry of Assets/_Project/Data/Planets/planets.json.</summary>
        static PlanetRecord Proxima()
        {
            var r = new PlanetRecord { id = "proxima-cen-b", displayName = "Proxima Centauri b", archiveName = "Proxima Cen b" };
            r.solar.luminosityW = 5.780320630408608e23;
            r.solar.semiMajorAxisM = 7252504771.536;
            r.solar.eccentricity = 0.0;
            r.solar.yearLengthHours = 268.4316;
            r.solar.dayLengthHours = 0.0;
            r.solar.obliquityDeg = 0.0;
            r.solar.airTemperatureC = -55.15;
            r.star.teffK = 2900.0;
            r.star.spectralType = "M5.5 V";
            r.star.count = 3;
            r.planet.radiusM = 6505662.0;
            r.flags.tidallyLocked = true;
            r.flags.surface = "rocky";
            r.provenance = new List<PlanetRecord.ProvenanceRecord>
            {
                new PlanetRecord.ProvenanceRecord { field = "solar.luminosityW", origin = "nasa", note = "st_lum" },
                new PlanetRecord.ProvenanceRecord { field = "planet.surfaceGravityMs2", origin = "derived", note = "g = GM/R^2" },
                new PlanetRecord.ProvenanceRecord { field = "flags.tidallyLocked", origin = "assumed", note = "rotation is not measured" },
            };
            return r;
        }

        [Test]
        public void ToSolarParams_ConvertsDegreesAndKeepsSI()
        {
            var r = new PlanetRecord();
            r.solar.obliquityDeg = 23.44;
            r.solar.perihelionLongitudeDeg = 282.94;
            r.solar.surfacePressureEarth = 0.006;
            PlanetSolarParams p = r.ToSolarParams();
            Assert.AreEqual(23.44 * Math.PI / 180.0, p.Obliquity, 1e-12);
            Assert.AreEqual(282.94 * Math.PI / 180.0, p.PerihelionLongitude, 1e-12);
            Assert.AreEqual(SolarConstants.SolarLuminosity, p.Luminosity);
            Assert.AreEqual(SolarConstants.AstronomicalUnit, p.SemiMajorAxis);
            Assert.AreEqual(0.006, p.SurfacePressureEarth, 1e-12);
            Assert.IsFalse(p.TidallyLocked);
        }

        [Test]
        public void TidallyLocked_HasNoSolarDay()
        {
            PlanetSolarParams p = Proxima().ToSolarParams();
            Assert.IsTrue(p.TidallyLocked);
            Assert.AreEqual(0.0, p.DayLengthHours);
            Assert.AreEqual(268.4316, p.YearLengthHours, 1e-9);
        }

        [Test]
        public void Proxima_GetsAboutTwoThirdsOfEarthsLight()
        {
            // L / 4πa² ÷ 1361 W/m²; the archive's pl_insol is 0.65 S⊕.
            Assert.AreEqual(0.641, Proxima().InsolationEarth, 0.005);
            Assert.AreEqual(1.021, Proxima().RadiusEarth, 0.005);
        }

        [TestCase("rocky", SurfaceKind.Rocky, true)]
        [TestCase("uncertain", SurfaceKind.Uncertain, true)]
        [TestCase("gaseous", SurfaceKind.Gaseous, false)]
        public void Surface_DecidesWhetherTheColonyCanLand(string surface, SurfaceKind kind, bool playable)
        {
            var r = new PlanetRecord();
            r.flags.surface = surface;
            Assert.AreEqual(kind, r.Surface);
            Assert.AreEqual(playable, r.IsPlayable, r.WhyNotPlayable);
        }

        [Test]
        public void Circumbinary_IsNotPlayableYet()
        {
            var r = new PlanetRecord();
            r.flags.circumbinary = true;
            StringAssert.Contains("two stars", r.WhyNotPlayable);
        }

        [Test]
        public void Clouds_EarthOrClearSky()
        {
            var r = new PlanetRecord();
            Assert.AreEqual(CloudModel.None, r.Clouds);
            r.flags.clouds = "earth";
            Assert.AreEqual(CloudModel.EarthClimatology, r.Clouds);
        }

        [Test]
        public void OriginOf_ReadsTheProvenanceLabels()
        {
            PlanetRecord r = Proxima();
            Assert.AreEqual(ValueOrigin.Nasa, r.OriginOf("solar.luminosityW"));
            Assert.AreEqual(ValueOrigin.Derived, r.OriginOf("planet.surfaceGravityMs2"));
            Assert.AreEqual(ValueOrigin.Assumed, r.OriginOf("flags.tidallyLocked"));
            Assert.AreEqual(ValueOrigin.Unknown, r.OriginOf("star.massKg"));
            Assert.AreEqual("st_lum", r.NoteOf("solar.luminosityW"));
            Assert.AreEqual("", r.NoteOf("star.massKg"));
        }

        public static PlanetRecord ProximaForOtherTests() => Proxima();
    }
}
