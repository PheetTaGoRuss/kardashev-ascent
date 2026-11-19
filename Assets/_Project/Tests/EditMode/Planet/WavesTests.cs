using KardashevAscent.Simulation.Planet;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Planet
{
    public class WavesTests
    {
        const double G = 9.80665;

        [Test]
        public void FullyDevelopedSea_AtGaleWind_IsAboutEightMetres()
        {
            Assert.AreEqual(8.1, Waves.SignificantHeightM(19.5, G), 0.2, "Pierson-Moskowitz at 19.5 m/s");
            Assert.AreEqual(14.2, Waves.PeakPeriodS(19.5, G), 0.3, "peak period, s");
        }

        [Test]
        public void EnergyFlux_ThreeMetreEightSecondSea_IsAbout36kWPerMetre()
        {
            Assert.AreEqual(36e3, Waves.EnergyFluxWPerM(3.0, 8.0, G), 1.0e3);
            Assert.AreEqual(15.7e3, Waves.EnergyFluxWPerM(2.0, 8.0, G), 0.3e3, "a second published worked example");
        }

        [Test]
        public void Height_GoesAsWindSquared_AndAsOneOverGravity()
        {
            Assert.AreEqual(4.0, Waves.SignificantHeightM(20.0, G) / Waves.SignificantHeightM(10.0, G), 1e-9);
            Assert.AreEqual(2.0, Waves.SignificantHeightM(10.0, G) / Waves.SignificantHeightM(10.0, 2.0 * G), 1e-9);
        }

        [Test]
        public void NoWindOrNoGravity_NoWaves()
        {
            Assert.AreEqual(0.0, Waves.SignificantHeightM(0.0, G));
            Assert.AreEqual(0.0, Waves.SignificantHeightM(10.0, 0.0));
            Assert.AreEqual(0.0, Waves.EnergyFluxWPerM(0.0, 8.0, G));
        }

        [Test]
        public void Wind_IsStrongestInTheMidLatitudes()
        {
            double eq = Waves.WindMs(0.0), mid = Waves.WindMs(System.Math.PI / 4.0), pole = Waves.WindMs(System.Math.PI / 2.0);
            Assert.Greater(mid, eq);
            Assert.Greater(mid, pole);
            Assert.AreEqual(10.0, mid, 1e-9);
        }

        [Test]
        public void Converter_NeedsACut_ThenHoldsItsRating_ThenShutsDownInAStorm()
        {
            Assert.AreEqual(0.0, Waves.LinePowerW(0.5, 8.0, G), "a flat calm");
            double mid = Waves.LinePowerW(2.0, 8.0, G);
            Assert.Greater(mid, 0.0);
            Assert.Less(mid, Waves.RatedW);
            Assert.AreEqual(Waves.RatedW, Waves.LinePowerW(5.0, 9.0, G), 1e-6, "capped at the rating");
            Assert.AreEqual(0.0, Waves.LinePowerW(9.0, 12.0, G), "shut down to survive");
        }

        [Test]
        public void Converter_PowerIsFluxTimesWidthTimesCapture()
        {
            Assert.AreEqual(Waves.EnergyFluxWPerM(2.0, 8.0, G) * Waves.LineWidthM * Waves.CaptureShare, Waves.LinePowerW(2.0, 8.0, G), 1e-6);
        }
    }
}
