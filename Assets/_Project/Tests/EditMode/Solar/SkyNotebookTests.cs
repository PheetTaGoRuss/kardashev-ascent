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

        [Test]
        public void StartsKnowingNothing()
        {
            var n = new SkyNotebook();
            Assert.IsFalse(n.KnowsDeclination);
            Assert.IsFalse(n.TracksSun);
            Assert.IsFalse(n.KnowsLatitude(5));
            Assert.IsNaN(n.LatitudeDeg(5));
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
            Assert.AreEqual(lat, n.LatitudeDeg(3), 0.6, "a sighting within a quarter hour of noon is close");
            Assert.IsFalse(n.KnowsLatitude(4), "latitude belongs to the tile it was measured on");
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
            Assert.AreEqual(-33.0, n.LatitudeDeg(1), 1e-9);
            StringAssert.Contains("pole", n.LatitudeMethod(1));
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
