using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Solar
{
    /// <summary>Finding the sky's numbers by measuring (sky view).</summary>
    public class SkyNotebookTests
    {
        static SkyReport Sky(double lat, double dec, double hour)
        {
            double h = SkyMath.SunAltitudeDeg(lat, dec, hour);
            double a = SkyMath.SunAzimuthDeg(lat, dec, hour);
            return new SkyReport(false, lat, 23.44, 0.0, dec, hour, 12.0 + hour / 15.0, h, a, 20.0, 180.0,
                                 SkyMath.CosIncidence(h, a, 20.0, 180.0), 1361, 900, 100, 800, 40, 1e6, 1.0, 1.0);
        }

        // ---- Puzzles from real-world readings (director 2026-11-14) ----

        [Test]
        public void Gnomon_ShadowGivesTheSunsHeight_AndPointsAwayFromIt()
        {
            SkyReport sky = Sky(20, 10, -30);
            var (shadow, bearing) = SkyNotebook.GnomonReading(sky);
            // tan h = stick / shadow; the shadow points opposite the sun.
            Assert.AreEqual(sky.SunAltitudeDeg, System.Math.Atan(SkyNotebook.GnomonLengthM / shadow) * 180.0 / System.Math.PI, 0.05);
            Assert.AreEqual((sky.SunAzimuthDeg + 180.0) % 360.0, bearing, 0.51);
            Assert.IsNaN(SkyNotebook.GnomonReading(Sky(20, 0, 150)).ShadowM, "no shadow at night");
        }

        [Test]
        public void SunPuzzle_WrongIsRefused_RightTracksTheSun()
        {
            var n = new SkyNotebook();
            SkyReport sky = Sky(20, 10, -30);
            n.AnswerSun(1, sky, sky.SunAltitudeDeg + 5.0, sky.SunAzimuthDeg, out bool wrong);
            Assert.IsFalse(wrong);
            Assert.IsFalse(n.TracksSun);
            n.AnswerSun(1, sky, sky.SunAltitudeDeg + 0.4, sky.SunAzimuthDeg - 1.0, out bool right);
            Assert.IsTrue(right);
            Assert.IsTrue(n.TracksSun);
            Assert.IsNull(n.NoonSighting, "not noon: no latitude from this one");
        }

        [Test]
        public void DeclinationThenNoonShadow_GivesTheLatitude()
        {
            var n = new SkyNotebook();
            SkyReport noon = Sky(35, -12, 0.5);
            n.AnswerDeclination(noon, -20.0, out bool wrongDec);
            Assert.IsFalse(wrongDec);
            n.AnswerDeclination(noon, -12.3, out bool dec);
            Assert.IsTrue(dec);
            n.AnswerSun(7, noon, noon.SunAltitudeDeg, noon.SunAzimuthDeg, out bool sun);
            Assert.IsTrue(sun);
            Assert.IsNotNull(n.NoonSighting);
            double phi = 90.0 - noon.SunAltitudeDeg + noon.DeclinationDeg;   // sun in the south
            n.AnswerNoonLatitude(7, noon, phi + 3.0, out bool wrongPhi);
            Assert.IsFalse(wrongPhi);
            n.AnswerNoonLatitude(7, noon, phi + 0.5, out bool rightPhi);
            Assert.IsTrue(rightPhi);
            Assert.IsTrue(n.KnowsLatitude(7));
            Assert.AreEqual(35.0, n.LatitudeDeg(7, 35.0), 0.5);
        }

        [Test]
        public void Quadrant_OnThePole_GivesTheLatitudeAtNight()
        {
            var n = new SkyNotebook();
            SkyReport night = Sky(-28, 0, 170);
            var (reading, north) = SkyNotebook.QuadrantReading(night);
            Assert.IsFalse(north, "south of the equator: the south celestial pole");
            Assert.AreEqual(62.0, reading, 0.05, "90° − 28°");
            n.AnswerPole(3, night, 28.0, out bool wrongSign);
            Assert.IsFalse(wrongSign);
            n.AnswerPole(3, night, -(90.0 - reading), out bool right);
            Assert.IsTrue(right);
            Assert.IsTrue(n.KnowsLatitude(3));
        }

        [Test]
        public void StartsKnowingNothing()
        {
            var n = new SkyNotebook();
            Assert.IsFalse(n.KnowsDeclination);
            Assert.IsFalse(n.TracksSun);
            Assert.IsFalse(n.KnowsLatitude(5));
            Assert.IsFalse(n.CanUseBestAngle(5));
            Assert.IsFalse(n.Surveyed);
            Assert.IsNaN(n.LatitudeDeg(5, 20.0));
        }

        [Test]
        public void SunAtNight_CannotBeMeasured()
        {
            var n = new SkyNotebook();
            StringAssert.Contains("below the horizon", n.MeasureSun(1, Sky(20, 0, 150)));
            Assert.IsFalse(n.TracksSun);
        }

        [Test]
        public void NoonSun_WithoutDeclination_GivesNoLatitudeYet()
        {
            var n = new SkyNotebook();
            n.MeasureSun(1, Sky(20, 5, 2));
            Assert.IsTrue(n.TracksSun);
            Assert.IsFalse(n.KnowsLatitude(1));
        }

        [TestCase(20.0, 5.0, 0.0)]
        [TestCase(20.0, 5.0, 3.5)]
        [TestCase(-35.0, -20.0, -3.5)]
        [TestCase(-10.0, 15.0, 3.0)]
        public void NoonSun_WithDeclination_FindsLatitude(double lat, double dec, double hour)
        {
            var n = new SkyNotebook();
            SkyReport sky = Sky(lat, dec, hour);
            n.WorkOutDeclination(sky);
            n.MeasureSun(3, sky);
            Assert.IsTrue(n.KnowsLatitude(3));
            Assert.IsTrue(n.MeasuredLatitude(3));
            Assert.AreEqual(lat, n.LatitudeDeg(3, double.NaN), 0.6, "a sighting within a quarter hour of noon is close");
            Assert.IsFalse(n.MeasuredLatitude(4), "the measurement belongs to the tile it was taken on");
        }

        [Test]
        public void AfternoonSun_IsNotNoon()
        {
            var n = new SkyNotebook();
            SkyReport sky = Sky(20, 5, 40);
            n.WorkOutDeclination(sky);
            StringAssert.Contains("within a quarter hour of noon", n.MeasureSun(1, sky));
            Assert.IsFalse(n.KnowsLatitude(1));
        }

        [Test]
        public void Pole_AtNightGivesLatitude_InDaylightItIsTooBright()
        {
            var n = new SkyNotebook();
            StringAssert.Contains("Too bright", n.MeasurePole(1, Sky(-33, 0, 0)));
            Assert.IsFalse(n.KnowsLatitude(1));
            n.MeasurePole(1, Sky(-33, 0, 170));
            Assert.AreEqual(-33.0, n.LatitudeDeg(1, double.NaN), 1e-9);
            StringAssert.Contains("pole", n.LatitudeMethod(1));
        }

        [Test]
        public void FirstFix_SurveysEveryTile_AndUnlocksBestAngles()
        {
            var n = new SkyNotebook();
            n.MeasurePole(7, Sky(20, 0, 170));
            Assert.IsTrue(n.Surveyed);
            Assert.AreEqual(7, n.SurveyAnchor);
            Assert.IsTrue(n.KnowsLatitude(99), "other tiles come from the survey map");
            Assert.IsTrue(n.CanUseBestAngle(99));
            Assert.IsFalse(n.MeasuredLatitude(99));
            Assert.AreEqual(-41.5, n.LatitudeDeg(99, -41.5), "a surveyed tile reads the map");
            StringAssert.Contains("surveyed", n.LatitudeMethod(99));
            StringAssert.Contains("pole", n.LatitudeMethod(7));
        }

        [Test]
        public void CaptureRestore_KeepsEverything()
        {
            var a = new SkyNotebook();
            SkyReport noon = Sky(20, 5, 1);
            a.WorkOutDeclination(noon);
            a.MeasureSun(4, noon);
            a.MeasurePole(9, Sky(20, 5, 170));
            SkyNotebookState state = a.Capture();

            var b = new SkyNotebook();
            b.Restore(state);
            Assert.IsTrue(b.KnowsDeclination);
            Assert.IsTrue(b.TracksSun);
            Assert.AreEqual(4, b.SurveyAnchor);
            Assert.AreEqual(a.LatitudeDeg(4, 0), b.LatitudeDeg(4, 0), 1e-12);
            Assert.AreEqual(a.LatitudeMethod(9), b.LatitudeMethod(9));
            CollectionAssert.AreEqual(a.Log, b.Log);

            b.Restore(null);
            Assert.IsFalse(b.KnowsDeclination);
            Assert.IsFalse(b.Surveyed);
            Assert.AreEqual(0, b.Log.Count);
        }

        [Test]
        public void Log_KeepsTheLastThirtyLines()
        {
            var n = new SkyNotebook();
            for (int i = 0; i < 40; i++)
                n.WorkOutDeclination(Sky(0, 0, 0));
            Assert.AreEqual(30, n.Log.Count);
        }
    }
}
