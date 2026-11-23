using System;
using System.Linq;
using KardashevAscent.Simulation.Electrical;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    public class CableThermalTests
    {
        static CableSpec Cu500 => CableSpec.FromSquareMillimetres("Cu 500", ConductorMaterial.Copper, 500, 1, 600);

        [Test]
        public void SteadyTemperatureIsAirPlusHeatTimesThermalResistance()
        {
            double heat = CableThermal.HeatPerM(Cu500, 1000.0, 20.0);   // 1000 A through 500 mm2 copper
            Assert.AreEqual(1000.0 * 1000.0 * 1.7241e-8 / 500e-6, heat, 1e-6);
            Assert.AreEqual(25.0 + heat * CableThermal.ThermalResistanceKmPerW, CableThermal.SteadyCelsius(25.0, heat), 1e-9);
        }

        [Test]
        public void TheTemperatureApproachesTheSteadyValueWithTheTimeConstant()
        {
            double heat = 100.0, air = 20.0;
            double tau = CableThermal.TimeConstantHours(Cu500);
            double steady = CableThermal.SteadyCelsius(air, heat);
            double afterTau = CableThermal.Step(air, air, heat, Cu500, tau);
            Assert.AreEqual(air + (steady - air) * (1.0 - Math.Exp(-1.0)), afterTau, 1e-9, "63 % of the way after one time constant");
            Assert.AreEqual(steady, CableThermal.Step(air, air, heat, Cu500, 50.0 * tau), 1e-6);
            Assert.AreEqual(air, CableThermal.Step(steady, air, 0.0, Cu500, 50.0 * tau), 1e-6, "it cools back to the air");
            Assert.AreEqual(CableThermal.Step(air, air, heat, Cu500, 2 * tau), CableThermal.Step(CableThermal.Step(air, air, heat, Cu500, tau), air, heat, Cu500, tau), 1e-9, "steps compose");
        }

        [Test]
        public void TheTimeConstantIsAboutFourteenMinutesForCopper500()
        {
            Assert.AreEqual(0.5 * 8890.0 * 500e-6 * 385.0 / 3600.0, CableThermal.TimeConstantHours(Cu500), 1e-9);
            Assert.AreEqual(0.24, CableThermal.TimeConstantHours(Cu500), 0.01);
        }

        [Test]
        public void AgeingDoublesPerTenKelvinAboveTheLimitAndHalvesBelow()
        {
            Assert.AreEqual(1.0, CableThermal.AgeingFactor(90.0), 1e-12);
            Assert.AreEqual(2.0, CableThermal.AgeingFactor(100.0), 1e-12);
            Assert.AreEqual(8.0, CableThermal.AgeingFactor(120.0), 1e-12);
            Assert.AreEqual(0.5, CableThermal.AgeingFactor(80.0), 1e-12);
            Assert.IsTrue(CableThermal.Overheated(91.0));
            Assert.IsFalse(CableThermal.Overheated(90.0));
        }

        [Test]
        public void ACableLastsItsDesignLifeAtTheLimit()
        {
            double hours = CableThermal.DesignLifeHours;
            Assert.AreEqual(1.0, CableThermal.ConditionLoss(90.0, hours), 1e-12);
            Assert.AreEqual(0.5, CableThermal.ConditionLoss(100.0, hours / 4.0), 1e-12, "twice as fast, a quarter of the time: half used");
        }

        [Test]
        public void ThermalResistanceIsHalfAKelvinMetrePerWattInEarthAirAndRisesAsTheAirThins()
        {
            Assert.AreEqual(0.5, CableThermal.ThermalResistance(1.0), 1e-12);
            Assert.AreEqual(0.5, CableThermal.ThermalResistance(3.0), 1e-12, "dense air does not cool better than the Earth value");
            Assert.Greater(CableThermal.ThermalResistance(0.1), CableThermal.ThermalResistance(1.0));
            // no air: radiation only, 1 / 0.2 = 5 K m/W, ten times the Earth value
            Assert.AreEqual(1.0 / CableThermal.RadiationConductanceWPerMK, CableThermal.ThermalResistance(0.0), 1e-12);
            Assert.AreEqual(10.0, CableThermal.ThermalResistance(0.0) / CableThermal.ThermalResistance(1.0), 1e-9);
        }

        [Test]
        public void ACableInVacuumRunsMuchHotterAtTheSameCurrentAndRatingDrops()
        {
            double heat = CableThermal.HeatPerM(Cu500, 500.0, 20.0);
            double earth = CableThermal.SteadyCelsius(20.0, heat, 1.0);
            double vacuum = CableThermal.SteadyCelsius(20.0, heat, 0.0);
            Assert.AreEqual(heat * 0.5, earth - 20.0, 1e-9);
            Assert.AreEqual(heat * 5.0, vacuum - 20.0, 1e-9);
            // the current that reaches the 90 C limit is lower in vacuum by the square root of the resistance ratio
            double limitEarth = Math.Sqrt((90.0 - 20.0) / 0.5 / (1.7241e-8 * (1 + 0.00393 * 0.0) / 500e-6));
            double limitVacuum = Math.Sqrt((90.0 - 20.0) / 5.0 / (1.7241e-8 / 500e-6));
            Assert.AreEqual(Math.Sqrt(10.0), limitEarth / limitVacuum, 1e-9);
        }

        [Test]
        public void TheTimeConstantGrowsWithTheThermalResistance()
        {
            Assert.AreEqual(10.0, CableThermal.TimeConstantHours(Cu500, 0.0) / CableThermal.TimeConstantHours(Cu500, 1.0), 1e-9);
        }

        // ---- in the colony ----
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation ChainWithPanels(out int a, out int b)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            sim.Debug.FreeBuilding = true;
            a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            b = Sphere.Neighbors[a].First(n => n != sim.BaseTileId && !Sphere.Neighbors[sim.BaseTileId].Contains(n) && !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            Assert.IsTrue(sim.PlacePowerLine(b, 0, a));
            for (int slot = 1; slot < 8; slot++) sim.PlacePanel(b, slot, 0.35, Math.PI);
            sim.WireAllBuildings();
            return sim;
        }

        [Test]
        public void AFailedLineSplitsTheNetworkAndTakesItsGeneratorsOffTheGrid()
        {
            var sim = ChainWithPanels(out int a, out int b);
            sim.Tick(12.0);
            Assert.IsTrue(sim.IsPowered(b));
            Assert.AreEqual(0, sim.GeneratorsOffGrid);
            Assert.IsTrue(sim.SetCableState(b, 0, 0.0, false));   // the line b to a fails
            sim.Tick(0.1);
            Assert.IsFalse(sim.IsPowered(b), "the network is split at the failed line");
            Assert.IsTrue(sim.IsPowered(a));
            Assert.Greater(sim.GeneratorsOffGrid, 0);
            Assert.AreEqual(1, sim.FailedLines().Count());
            Assert.AreEqual(2, sim.ElectricalNetworkCount, "the electrical graph agrees: two networks now");
        }

        [Test]
        public void RepairTakesMaterialsAndRestoresTheLine()
        {
            var sim = ChainWithPanels(out int a, out int b);
            sim.SetCableState(b, 0, 0.0, false);
            sim.Tick(0.1);
            Assert.IsFalse(sim.IsPowered(b));
            var bill = sim.RepairBill(b, 0);
            Assert.AreEqual(sim.BuildBill(BuildingType.PowerLine)[Material.Copper], bill[Material.Copper], 1e-9, "a failed line costs its whole bill");
            double copperBefore = sim.Materials[Material.Copper];
            sim.Materials.Add(Material.Copper, 1000.0);
            sim.Materials.Add(Material.Iron, 1000.0);
            sim.Materials.Add(Material.Rock, 1000.0);
            Assert.IsNull(sim.RepairLine(b, 0));
            sim.Tick(0.1);
            Assert.IsTrue(sim.IsPowered(b));
            Assert.AreEqual(0, sim.FailedLines().Count());
            Assert.AreEqual(copperBefore + 1000.0 - bill[Material.Copper], sim.Materials[Material.Copper], 1e-6);
        }

        [Test]
        public void RepairWithoutMaterialsSaysWhatIsMissing()
        {
            var sim = ChainWithPanels(out int a, out int b);
            sim.SetCableState(b, 0, 0.0, false);
            sim.Materials.TryTake(Material.Copper, sim.Materials[Material.Copper]);   // the stock is empty of copper
            string why = sim.RepairLine(b, 0);
            StringAssert.Contains("Missing", why);
            Assert.AreEqual(1, sim.FailedLines().Count());
            StringAssert.StartsWith("That line needs no repair", sim.RepairLine(a, 0));
        }

        [Test]
        public void ACarryingLineWarmsAboveTheAirAndAnIdleOneStaysAtIt()
        {
            var sim = ChainWithPanels(out int a, out int b);
            for (int i = 0; i < 12; i++) sim.Tick(1.0);
            double warm = sim.LineTemperatureC(a, sim.BaseTileId);
            double idle = sim.LineTemperatureC(b, a);
            Assert.IsFalse(double.IsNaN(warm));
            Assert.GreaterOrEqual(warm, idle - 1e-9, "the line nearer the lander carries more (everything behind it)");
        }

        [Test]
        public void ConditionSurvivesSaveAndLoadAndAFailedLineStaysFailed()
        {
            var sim = ChainWithPanels(out int a, out int b);
            sim.SetCableState(b, 0, 0.0, false);
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(loaded.RestoreState(sim.CaptureState()));
            loaded.Tick(0.1);
            Assert.IsFalse(loaded.IsPowered(b));
            Assert.AreEqual(1, loaded.FailedLines().Count());
        }
    }
}
