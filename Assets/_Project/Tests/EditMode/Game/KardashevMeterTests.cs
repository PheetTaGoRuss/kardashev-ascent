using System;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>The K meter numbers the stub simulation gives the UI (issue #10).</summary>
    public class KardashevMeterTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSolarParams Star(double luminositySun) => new PlanetSolarParams(
            luminositySun * SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        [Test]
        public void KAndPlanetK_FollowTheUsedPower()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Star(1.0));
            sim.Tick(36.0);
            Assert.Greater(sim.KardashevPowerW, 0.0);
            Assert.AreEqual(Kardashev.K(sim.KardashevPowerW), sim.KardashevK, 1e-12);
            Assert.AreEqual(Kardashev.PlanetK(sim.KardashevPowerW, sim.TypeITargetW), sim.PlanetK, 1e-12);
        }

        [Test]
        public void DimStar_PlanetKIsAheadOfAbsoluteK()
        {
            // A star 100× dimmer at 1 AU: the target is 100× lower, so planet K runs 0.2 ahead of absolute K.
            var sim = new PlanetSimulation(Sphere, Surface, Star(0.01));
            sim.Tick(36.0);
            Assert.AreEqual(1e14, sim.TypeITargetW, 1e14 * 0.01);
            Assert.AreEqual(sim.KardashevK + 1.0 - Kardashev.K(sim.TypeITargetW), sim.PlanetK, 1e-9);
            Assert.AreEqual(0.2, sim.PlanetK - sim.KardashevK, 2e-3);
        }
    }
}
