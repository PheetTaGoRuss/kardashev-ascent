using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Solar
{
    /// <summary>The sky view's textbook chain (Cooper δ, sin h, cos θ, latitude from the noon sun) against the game's model.</summary>
    public class SkyMathTests
    {
        const double Deg = Math.PI / 180.0;

        [Test]
        public void Cooper_VernalEquinoxExample()
        {
            // The worked example in the design notes: n = 80 (21 March) → 0° 24′ 12.53″, just south of the equator
            // (sin(360° · 364 / 365) is slightly negative, so δ is −0° 24′ 12.5″).
            double d = SkyMath.CooperDeclinationDeg(80);
            Assert.AreEqual(-(24.0 / 60.0 + 12.53 / 3600.0), d, 2e-4);
        }

        [TestCase(172, 23.45, 0.05)]    // 21 June
        [TestCase(355, -23.43, 0.05)]   // 21 December
        [TestCase(264, -0.40, 0.3)]     // 21 September
        public void Cooper_SolsticesAndEquinox(int n, double expected, double tolerance)
        {
            Assert.AreEqual(expected, SkyMath.CooperDeclinationDeg(n), tolerance);
        }

        [TestCase(0.0, 0.0)]
        [TestCase(90.0, 23.44)]
        [TestCase(270.0, -23.44)]
        public void ExactDeclination_FromTiltAndSeason(double lambda, double expected)
        {
            Assert.AreEqual(expected, SkyMath.DeclinationDeg(23.44, lambda), 1e-9);
        }

        [TestCase(0.0, 80)]
        [TestCase(90.0, 171)]
        [TestCase(180.0, 263)]
        [TestCase(270.0, 354)]
        [TestCase(-10.0, 70)]
        public void EarthDayOfYear_FromSeason(double lambda, int n)
        {
            Assert.AreEqual(n, SkyMath.EarthDayOfYear(lambda));
        }

        [Test]
        public void Cooper_AgreesWithTheExactModelWithinADegree()
        {
            for (double lambda = 0.0; lambda < 360.0; lambda += 15.0)
                Assert.AreEqual(SkyMath.DeclinationDeg(23.44, lambda),
                                SkyMath.CooperDeclinationDeg(SkyMath.EarthDayOfYear(lambda)), 1.0, $"λ = {lambda}");
        }

        [TestCase(20.0, 0.0, 0.0, 70.0)]
        [TestCase(13.75, 23.44, 0.0, 80.31)]
        [TestCase(0.0, 0.0, 90.0, 0.0)]
        public void SinH_MatchesTheGameAndHandValues(double lat, double dec, double hour, double altitude)
        {
            Assert.AreEqual(altitude, SkyMath.SunAltitudeDeg(lat, dec, hour), 0.01);
            double up = SunPosition.Direction(lat * Deg, dec * Deg, hour * Deg).Up;
            Assert.AreEqual(Math.Asin(up) / Deg, SkyMath.SunAltitudeDeg(lat, dec, hour), 1e-9);
        }

        [Test]
        public void CosTheta_MatchesPanelModelEverywhere()
        {
            var rng = new Random(11);
            for (int i = 0; i < 200; i++)
            {
                double lat = rng.NextDouble() * 160 - 80, dec = rng.NextDouble() * 47 - 23.5, hour = rng.NextDouble() * 360 - 180;
                double tilt = rng.NextDouble() * 90, az = rng.NextDouble() * 360;
                Double3 sun = SunPosition.Direction(lat * Deg, dec * Deg, hour * Deg);
                double h = Math.Asin(sun.Up) / Deg, a = SunPosition.AzimuthDegrees(sun);
                Assert.AreEqual(PanelModel.Normal(tilt * Deg, az * Deg).Dot(sun),
                                SkyMath.CosIncidence(h, a, tilt, az), 1e-9);
            }
        }

        [TestCase(20.0, 5.0)]
        [TestCase(45.0, -23.0)]
        [TestCase(-30.0, 10.0)]
        [TestCase(-60.0, -20.0)]
        public void LatitudeFromNoon_RoundTrips(double lat, double dec)
        {
            double noon = SkyMath.NoonAltitudeDeg(lat, dec);
            Assert.AreEqual(noon, SkyMath.SunAltitudeDeg(lat, dec, 0.0), 1e-9);
            bool south = lat > dec;
            Assert.AreEqual(lat, SkyMath.LatitudeFromNoonDeg(noon, dec, south), 1e-9);
        }

        [Test]
        public void Sunrise_EquinoxIsSixHours_PolesHaveNoNight()
        {
            Assert.AreEqual(90.0, SkyMath.SunriseHourAngleDeg(35.0, 0.0), 1e-9);
            Assert.AreEqual(180.0, SkyMath.SunriseHourAngleDeg(80.0, 23.0));
            Assert.AreEqual(0.0, SkyMath.SunriseHourAngleDeg(80.0, -23.0));
        }

        [Test]
        public void RuleOfThumb_TiltIsLatitude()
        {
            Assert.AreEqual(20.0, SkyMath.RuleOfThumbTiltDeg(-20.0));
        }

        // ---- DescribeSky on the stub ----

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSimulation Earthlike()
        {
            var r = new PlanetRecord { id = "sky" };
            r.solar.eccentricity = 0.0167;
            r.solar.obliquityDeg = 23.44;
            r.solar.perihelionLongitudeDeg = 282.94;
            r.solar.airTemperatureC = 25.0;
            return new PlanetSimulation(Sphere, Surface, r);
        }

        [Test]
        public void DescribeSky_IsTheTextbookChain()
        {
            var sim = Earthlike();
            sim.Tick(5.0);
            int t = sim.BaseTileId;
            double tilt = 20.0 * Deg, az = Math.PI;
            SkyReport s = sim.DescribeSky(t, tilt, az);
            Assert.IsTrue(s.Valid);
            Assert.AreEqual(sim.GetTile(t).Latitude / Deg, s.LatitudeDeg, 1e-9);
            Assert.AreEqual(SkyMath.DeclinationDeg(23.44, s.EclipticLongitudeDeg), s.DeclinationDeg, 1e-3);
            Assert.AreEqual(SkyMath.SunAltitudeDeg(s.LatitudeDeg, s.DeclinationDeg, s.HourAngleDeg), s.SunAltitudeDeg, 1e-3);
            Assert.AreEqual(SkyMath.SunAzimuthDeg(s.LatitudeDeg, s.DeclinationDeg, s.HourAngleDeg), s.SunAzimuthDeg, 1e-2);
            Assert.AreEqual(SkyMath.CosIncidence(s.SunAltitudeDeg, s.SunAzimuthDeg, 20.0, 180.0), s.CosIncidence, 1e-6);
            Assert.AreEqual(12.0 + s.HourAngleDeg / 15.0, s.LocalSolarHours, 1e-6);
        }

        [Test]
        public void Notebook_IsSavedWithTheColony()
        {
            var a = Earthlike();
            a.Notebook.WorkOutDeclination(a.DescribeSky(a.BaseTileId, 0.3, Math.PI));
            a.Tick(14.0);   // night at the base
            SkyReport night = a.DescribeSky(a.BaseTileId, 0.3, Math.PI);
            Assume.That(night.SunUp, Is.False);
            a.Notebook.MeasurePole(a.BaseTileId, night);
            SimulationState state = a.CaptureState();

            var b = Earthlike();
            Assert.IsFalse(b.Notebook.Surveyed);
            Assert.IsTrue(b.RestoreState(state));
            Assert.IsTrue(b.Notebook.KnowsDeclination);
            Assert.IsTrue(b.Notebook.CanUseBestAngle(b.BaseTileId + 1), "the survey came back too");
            Assert.AreEqual(a.Notebook.LatitudeDeg(a.BaseTileId, 0), b.Notebook.LatitudeDeg(b.BaseTileId, 0), 1e-12);
        }

        [Test]
        public void DescribeSky_PowerMatchesALivePanel()
        {
            var sim = Earthlike();
            int t = sim.BaseTileId;
            double tilt = Math.Abs(sim.GetTile(t).Latitude), az = Math.PI;
            Assert.IsTrue(sim.PlacePanel(t, 0, tilt, az));
            sim.Tick(1.0);   // mid-morning, sun up
            SkyReport s = sim.DescribeSky(t, tilt, az);
            Assert.IsTrue(s.SunUp);
            Assert.Greater(s.PlaneOfArrayWm2, 0.0);
            Assert.AreEqual(sim.GenerationW - sim.RtgW, s.PanelPowerW, Math.Max(1.0, s.PanelPowerW * 1e-9));
        }
    }
}
