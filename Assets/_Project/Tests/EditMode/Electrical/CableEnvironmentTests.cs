using KardashevAscent.Simulation.Electrical;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    public class CableEnvironmentTests
    {
        const double Area500 = 500e-6;

        [Test]
        public void RelativeAirDensityIsOneAtPeeksReferenceConditions()
        {
            Assert.AreEqual(1.0, CableEnvironment.RelativeAirDensity(101325.0, 25.0), 1e-9);
            Assert.Less(CableEnvironment.RelativeAirDensity(101325.0, 45.0), 1.0, "hot air is thinner");
            Assert.Greater(CableEnvironment.RelativeAirDensity(101325.0, -20.0), 1.0, "cold air is denser");
            Assert.AreEqual(0.0065, CableEnvironment.RelativeAirDensity(660.0, 25.0), 1e-4, "Mars surface pressure");
        }

        [Test]
        public void PeeksFieldMatchesTheFormulaAtEarthAirAndFallsWithThinnerAir()
        {
            double r = 1.0;   // cm
            Assert.AreEqual(30.0 * 0.8 * (1.0 + 0.301 / 1.0), CableEnvironment.CoronaOnsetKvPerCm(1.0, r), 1e-9);
            Assert.Less(CableEnvironment.CoronaOnsetKvPerCm(0.5, r), CableEnvironment.CoronaOnsetKvPerCm(1.0, r));
            Assert.AreEqual(30.0 * 1.301, CableEnvironment.CoronaOnsetKvPerCm(1.0, 1.0, 1.0), 1e-9, "a polished 1 cm wire in Earth air: 39 kV/cm peak");
        }

        [Test]
        public void VoltageDerateIsOneInEarthAirAndFallsInThinAir()
        {
            Assert.AreEqual(1.0, CableEnvironment.VoltageDerate(1.0, Area500, out bool fb), 1e-12);
            Assert.IsFalse(fb);
            double thin = CableEnvironment.VoltageDerate(0.3, Area500, out bool f2);
            Assert.Less(thin, 1.0);
            Assert.Greater(thin, 0.2);
            Assert.IsFalse(f2);
            Assert.AreEqual(1.0, CableEnvironment.VoltageDerate(1.8, Area500, out _), 1e-12, "dense air never raises the rating above 1");
        }

        [Test]
        public void OutsidePeeksRangeTheLabelledFallbackIsUsed()
        {
            double mars = CableEnvironment.VoltageDerate(CableEnvironment.RelativeAirDensity(660.0, -60.0), Area500, out bool fallback);
            Assert.IsTrue(fallback);
            Assert.AreEqual(0.1, mars, 1e-12, "floored at 0.1 (game rule, TBD)");
            Assert.AreEqual(0.1, CableEnvironment.VoltageDerate(0.0, Area500, out bool airless), 1e-12);
            Assert.IsTrue(airless);
        }

        [Test]
        public void AThickerWireStartsCoronaAtALowerField()
        {
            Assert.Less(CableEnvironment.CoronaOnsetKvPerCm(1.0, 2.0), CableEnvironment.CoronaOnsetKvPerCm(1.0, 0.5));
            Assert.AreEqual(1.26, CableEnvironment.RadiusCm(Area500), 0.01);
        }

        [Test]
        public void LongAcSeaLinksAreRefusedAndDcAndLandAreNot()
        {
            Assert.IsNull(SubseaRules.Check(NetworkType.Ac, 80_000, true));
            StringAssert.Contains("direct current", SubseaRules.Check(NetworkType.Ac, 300_000, true));
            Assert.IsNull(SubseaRules.Check(NetworkType.Dc, 300_000, true));
            Assert.IsNull(SubseaRules.Check(NetworkType.Ac, 300_000, false));
        }
    }
}
