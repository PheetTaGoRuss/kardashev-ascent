using System;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    public class ColdEarthTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSimulation Make(double meanC) => new PlanetSimulation(Sphere, Surface, new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, meanC, false, 1.0), Kardashev.EarthRadiusM) { Clouds = CloudModel.EarthClimatology };

        [Test]
        public void AColdEarthLikeWorldIsColderByItsOwnMean_AndEarthAtItsMeanIsUnchanged()
        {
            var earth = Make(15.0); var cold = Make(-25.0);
            double e = 0, c = 0, max = 0;
            for (int t = 0; t < Sphere.TileCount; t += 7) { double d = cold.AnnualAirTemperature(t) - earth.AnnualAirTemperature(t); max = Math.Max(max, Math.Abs(d + 40.0)); e += earth.AnnualAirTemperature(t); c += cold.AnnualAirTemperature(t); }
            Assert.AreEqual(-40.0, (c - e) / (Sphere.TileCount / 7 + 1), 1.0, "40 K colder everywhere");
            Assert.Less(max, 1.0);
        }

        [Test]
        public void EarthAtItsOwnMean_ShiftsByUnderHalfAKelvin()
        {
            var sim = Make(15.0);
            for (int t = 0; t < Sphere.TileCount; t += 11)
                Assert.AreEqual(KardashevAscent.Simulation.Climate.EarthAirTemperature.AnnualMean(Sphere.Latitude(t)), sim.AnnualAirTemperature(t), 0.5);
        }

        [Test]
        public void TheOffsetDoesNotTouchTheClouds()
        {
            var a = Make(15.0); var b = Make(-25.0);
            for (int t = 0; t < Sphere.TileCount; t += 13) Assert.AreEqual(a.CloudFactor(t, 1.0), b.CloudFactor(t, 1.0), 1e-12);
        }
    }
}
