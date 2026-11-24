using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>Weather (PL-07, W3): the day's weather acts through the existing systems: solar light, robot heater (wind chill), air temperature, cable air density.</summary>
    public class WeatherEffectsTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static PlanetSolarParams Earthlike(double pressure = 1.0) => new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, pressure);

        [Test]
        public void WindChillIsOneAtTheReferenceWind_GrowsWithWind_AndIsCappedAt20()
        {
            Assert.AreEqual(1.0, RobotThermal.WindFactor(RobotThermal.ReferenceWindMs, 1.0), 1e-12);
            Assert.Greater(RobotThermal.WindFactor(15.0, 1.0), RobotThermal.WindFactor(8.0, 1.0));
            Assert.Less(RobotThermal.WindFactor(0.0, 1.0), 1.0, "calm air is gentler than the reference");
            Assert.AreEqual(RobotThermal.WindFactor(20.0, 1.0), RobotThermal.WindFactor(35.0, 1.0), 1e-12, "capped at 20 m/s");
            Assert.AreEqual(1.0, RobotThermal.SiplePassel(0.0), 1e-12, "h(0)/h(0)");
            Assert.AreEqual((10.45 - 10.0 + 10.0 * Math.Sqrt(10.0)) / 10.45, RobotThermal.SiplePassel(10.0), 1e-12);
        }

        [Test]
        public void ThinAirFeelsLessWindThanThickAir()
        {
            Assert.Less(RobotThermal.WindFactor(15.0, 0.006) - 1.0, RobotThermal.WindFactor(15.0, 1.0) - 1.0);
        }

        [Test]
        public void TheHeaterGrowsWithTheWind()
        {
            double calm = RobotThermal.HeaterW(-40.0, false, RobotThermal.WindFactor(1.0, 1.0));
            double gale = RobotThermal.HeaterW(-40.0, false, RobotThermal.WindFactor(18.0, 1.0));
            Assert.Greater(gale, calm * 1.5);
            Assert.AreEqual(RobotThermal.HeaterW(-40.0, false), RobotThermal.HeaterW(-40.0, false, 1.0), 1e-12);
        }

        [Test]
        public void ThePanelsOutputVariesFromDayToDay_AndTheAverageStaysNearTheClimatology()
        {
            double Mean(bool weather, out double spread)
            {
                var sim = new PlanetSimulation(Sphere, Surface, Earthlike()) { WeatherEnabled = weather, WiredGrid = false, Clouds = CloudModel.EarthClimatology };
                sim.Debug.FreeBuilding = true;
                sim.Debug.UnlockAll = true;
                sim.SafetyMarginsEnabled = false;
                sim.PlacePanel(sim.BaseTileId, 1, 0.35, Math.PI);
                var noon = Enumerable.Range(0, 60).Select(d =>
                {
                    sim.Tick(24.0);
                    return sim.DescribeSky(sim.BaseTileId, 0.35, Math.PI).PanelPowerW;
                }).Where(p => p > 0.0).ToArray();
                double m = noon.Average();
                spread = Math.Sqrt(noon.Select(x => (x - m) * (x - m)).Average());
                return m;
            }
            double s1, s2;
            double with = Mean(true, out s1), without = Mean(false, out s2);
            Assert.Greater(s1, s2, $"weather adds day-to-day change: with {with} +- {s1}, without {without} +- {s2}");
            Assert.AreEqual(without, with, without * 0.25, "and the mean stays near the climate");
        }

        [Test]
        public void WithoutWeatherTheSimulationIsUnchanged()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike()) { WeatherEnabled = false };
            var w = sim.WeatherAt(sim.BaseTileId);
            Assert.AreEqual(sim.CloudFactor(sim.BaseTileId, 0.0), w.CloudFactor, 1e-12);
            Assert.AreEqual(sim.AirTemperatureAt(sim.BaseTileId, 0.0), w.AirC, 1e-12);
        }

        [Test]
        public void TheDaysPressureMovesCableAirDensityAroundTheClimatology()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike());
            var p = Enumerable.Range(0, 200).Select(i => { sim.Tick(24.0); return sim.WeatherAt(sim.BaseTileId).PressurePa; }).ToArray();
            Assert.Greater(p.Max() - p.Min(), 500.0, "pressure changes with the weather");
            Assert.AreEqual(sim.PressurePaAt(sim.BaseTileId), p.Average(), 700.0);
        }
    }
}
