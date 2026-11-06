using KardashevAscent.Simulation.Civilization;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Civilization
{
    /// <summary>Values from docs/physics/physics-verification.md (Kardashev table) and notes/kardashev_thermal.md.</summary>
    public class KardashevTests
    {
        const double SunLuminosityW = 3.828e26;

        [TestCase(1e6, 0.0)]
        [TestCase(1e16, 1.0)]
        [TestCase(1e26, 2.0)]
        [TestCase(1e36, 3.0)]
        [TestCase(18.76e12, 0.7273)]
        [TestCase(2e13, 0.7301)]
        [TestCase(6e16, 1.0778)]
        public void K_MatchesSaganInterpolation(double watts, double expected)
        {
            Assert.AreEqual(expected, Kardashev.K(watts), 1e-4);
        }

        [Test]
        public void K_NonPositivePowerIsClampedToOneWatt()
        {
            Assert.AreEqual(-0.6, Kardashev.K(0.0), 1e-12);
            Assert.AreEqual(-0.6, Kardashev.K(-5.0), 1e-12);
        }

        [TestCase(0.0)]
        [TestCase(0.7273)]
        [TestCase(2.0)]
        public void PowerForK_InvertsK(double k)
        {
            Assert.AreEqual(k, Kardashev.K(Kardashev.PowerForK(k)), 1e-12);
        }

        [Test]
        public void Humanity_IsAboutK073()
        {
            Assert.AreEqual(0.727, Kardashev.K(Kardashev.Humanity2024W), 1e-3);
        }

        [TestCase(1361.0, 1.0, 1e16, 1.000)]     // Earth-like
        [TestCase(1361.0, 1.5, 2.25e16, 1.035)]  // super-Earth
        [TestCase(13.61, 0.4, 1.6e13, 0.720)]    // dim cool planet
        public void TypeITarget_ScalesWithInterceptedLight(double insolation, double radiusEarth, double expectedW, double expectedK)
        {
            double target = Kardashev.TypeITargetW(insolation, radiusEarth * Kardashev.EarthRadiusM);
            Assert.AreEqual(expectedW, target, expectedW * 1e-9);
            Assert.AreEqual(expectedK, Kardashev.K(target), 1e-3);
        }

        [Test]
        public void Completion_IsPowerOverTarget()
        {
            Assert.AreEqual(0.5, Kardashev.PlanetCompletion(5e15, 1e16), 1e-12);
            Assert.AreEqual(0.0, Kardashev.PlanetCompletion(-1.0, 1e16));
            Assert.AreEqual(0.0, Kardashev.PlanetCompletion(1e15, 0.0));
        }

        [Test]
        public void TypeI_IsAboutSixPercentOfEarthsSunlight()
        {
            double intercepted = Kardashev.InterceptedStarlightW(Kardashev.EarthIrradiance, Kardashev.EarthRadiusM);
            Assert.AreEqual(1.7355e17, intercepted, 0.001e17);
            Assert.AreEqual(0.0576, Kardashev.TypeIW / intercepted, 5e-4);
            Assert.AreEqual(1.124, Kardashev.K(intercepted), 1e-3);
        }

        [Test]
        public void DysonSwarmAndTypeIII()
        {
            Assert.AreEqual(0.261, 1e26 / SunLuminosityW, 1e-3);
            Assert.AreEqual(2.61e9, 1e36 / SunLuminosityW, 0.01e9);
        }
    }
}
