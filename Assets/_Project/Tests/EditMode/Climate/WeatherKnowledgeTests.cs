using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>Weather (PL-07, W6): measured where something measures it, climatology with a range elsewhere, and an honest forecast from a measured today.</summary>
    public class WeatherKnowledgeTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Earthlike => new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, 1.0);

        [Test]
        public void TheBaseAndATileWithARobotAreMeasured_AFarTileIsClimatologyWithARange()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.Tick(5.0);
            var atBase = sim.WeatherKnownAt(sim.BaseTileId);
            Assert.IsTrue(atBase.Measured); Assert.IsTrue(atBase.AirC.IsExact);
            Assert.AreEqual(sim.LiveAirTemperatureAt(sim.BaseTileId), atBase.AirC.Value, 1e-9);
            int far = Enumerable.Range(0, sim.TileCount).First(t => !sim.GetTile(t).IsWater && sim.Routes.HoursBetween(sim.BaseTileId, t) > 30.0);
            var unknown = sim.WeatherKnownAt(far);
            Assert.IsFalse(unknown.Measured); Assert.IsFalse(unknown.AirC.IsExact); Assert.Greater(unknown.AirC.HalfRange, 0.5);
            Assert.IsNull(sim.ForecastAt(far, 1), "no forecast without today measured");
            sim.Workforce.AddRobot(RobotKind.General, far);
            Assert.IsTrue(sim.WeatherKnownAt(far).Measured, "a robot there measures it");
        }

        [Test]
        public void AForecastIsHonest_TheTrueValueFallsInsideItsRangeAboutAsOftenAsTheRangeSays()
        {
            foreach (int k in new[] { 1, 3 })
            {
                var now = new PlanetSimulation(Sphere, Surface, Earthlike);
                var later = new PlanetSimulation(Sphere, Surface, Earthlike);
                later.Tick(24.0 * k);
                int inside = 0, n = 300;
                for (int i = 0; i < n; i++)
                {
                    now.Tick(24.0 * 3.0); later.Tick(24.0 * 3.0);
                    var f = now.ForecastAt(now.BaseTileId, k).Value.AirC;
                    double actual = later.LiveAirTemperatureAt(later.BaseTileId);
                    if (Math.Abs(actual - f.Value) <= f.HalfRange) inside++;
                }
                Assert.AreEqual(0.68, inside / (double)n, 0.14, "one standard deviation covers about 68 % at " + k + " day(s)");
            }
        }

        [Test]
        public void TheForecastRangeGrowsWithTheLeadTime_AndNeedsTodayMeasured()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.Tick(100.0);
            double r1 = sim.ForecastAt(sim.BaseTileId, 1).Value.AirC.HalfRange, r5 = sim.ForecastAt(sim.BaseTileId, 5).Value.AirC.HalfRange;
            Assert.Greater(r5, r1);
            Assert.IsNull(sim.ForecastAt(sim.BaseTileId, 6));
            Assert.IsNull(sim.ForecastAt(sim.BaseTileId, 0));
            Assert.IsFalse(sim.ForecastAt(sim.BaseTileId, 2).Value.Measured, "a forecast is never a measurement");
        }

        [Test]
        public void WithoutWeatherNothingIsMeasuredAndThereIsNoForecast()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WeatherEnabled = false };
            Assert.IsFalse(sim.WeatherMeasuredAt(sim.BaseTileId));
            Assert.IsNull(sim.ForecastAt(sim.BaseTileId, 1));
        }
    }
}
