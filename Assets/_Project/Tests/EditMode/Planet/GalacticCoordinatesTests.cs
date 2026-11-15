using KardashevAscent.Simulation.Planet;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Planet
{
    /// <summary>Equatorial → galactic coordinates for the star map, against SIMBAD's galactic positions.</summary>
    public class GalacticCoordinatesTests
    {
        [Test]
        public void TheNorthGalacticPoleIsLatitude90()
        {
            GalacticCoordinates.FromEquatorial(GalacticCoordinates.PoleRaDeg, GalacticCoordinates.PoleDecDeg, out _, out double b);
            Assert.That(b, Is.EqualTo(90.0).Within(1e-6));
        }

        [Test]
        public void SagittariusAStarSitsAtTheGalacticCentre()
        {
            // Sgr A*: RA 17h45m40.04s, Dec −29°00′28.2″ (J2000); SIMBAD gives l = 359.944°, b = −0.046°.
            GalacticCoordinates.FromEquatorial(266.41683, -29.00781, out double l, out double b);
            Assert.That(l, Is.EqualTo(359.944).Within(0.01));
            Assert.That(b, Is.EqualTo(-0.046).Within(0.01));
        }

        [Test]
        public void ProximaCentauriMatchesSimbad()
        {
            // Archive ra/dec for Proxima Cen; SIMBAD gives l = 313.94°, b = −1.93°.
            GalacticCoordinates.FromEquatorial(217.3934657, -62.6761821, out double l, out double b);
            Assert.That(l, Is.EqualTo(313.94).Within(0.02));
            Assert.That(b, Is.EqualTo(-1.93).Within(0.02));
        }

        [Test]
        public void CartesianKeepsTheDistance()
        {
            GalacticCoordinates.ToCartesian(313.94, -1.93, 1.30119, out double x, out double y, out double z);
            Assert.That(System.Math.Sqrt(x * x + y * y + z * z), Is.EqualTo(1.30119).Within(1e-9));
            Assert.That(x, Is.GreaterThan(0.0), "Proxima lies on the galactic-centre side of the Sun");
            Assert.That(y, Is.LessThan(0.0), "and behind the Sun's motion (l > 180°)");
        }

        [Test]
        public void TowardsTheCentreIsPlusX()
        {
            GalacticCoordinates.ToCartesian(0.0, 0.0, 10.0, out double x, out double y, out double z);
            Assert.That(x, Is.EqualTo(10.0).Within(1e-9));
            Assert.That(y, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(z, Is.EqualTo(0.0).Within(1e-9));
        }
    }
}
