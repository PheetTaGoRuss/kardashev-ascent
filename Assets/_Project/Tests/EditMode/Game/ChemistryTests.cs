using System;
using System.Linq;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Storage chemistry (M2, M3): real reactions, mass conserved, the shelter decides.</summary>
    public class ChemistryTests
    {
        static readonly ChemEnvironment Humid = new ChemEnvironment(15.0, 70.0, 101_325.0, 0.2095, false);

        [TestCase(Material.Iron)] [TestCase(Material.Sodium)] [TestCase(Material.Lithium)] [TestCase(Material.Water)] [TestCase(Material.Salt)] [TestCase(Material.Oxygen)]
        public void EveryReactionConservesMass(Material m)
        {
            foreach (var env in new[] { Humid, new ChemEnvironment(25.0, 95.0, 101_325.0, 0.2095, true), new ChemEnvironment(-20.0, 30.0, 600.0, 0.0, false), new ChemEnvironment(30.0, 50.0, 0.0, 0.0, false) })
                foreach (var shelter in new[] { Shelter.OpenPile, Shelter.Yard, Shelter.Sealed })
                {
                    var r = Chemistry.Step(m, 100.0, shelter, env, 0.5, 30.0);
                    Assert.AreEqual(0.0, r.Imbalance(), 2e-3, $"{m} {shelter}");
                }
        }

        [Test]
        public void IronRustsAtTheIsoRate_AndGainsTheMassOfTheAir()
        {
            double perYear = Chemistry.IronLossPerYear(2, Chemistry.IronAreaM2PerKg);   // C3
            Assert.AreEqual(0.0148, perYear, 0.001, "37.5 um/yr x 7874 kg/m3 x 0.05 m2/kg");
            var r = Chemistry.Step(Material.Iron, 1000.0, Shelter.OpenPile, Humid, 0.1, 365.25);
            double fe = -r.Delta[Material.Iron];
            Assert.Greater(fe, 0.0);
            Assert.AreEqual(fe * 88.851 / 55.845, r.Delta[Material.Rust], 1e-3, "1 kg Fe gives 1.591 kg FeOOH");
            Assert.AreEqual(Chemistry.OxygenPerFe * fe, r.OxygenFromAir, 1e-9);
            Assert.AreEqual(0, Chemistry.CorrosivityCategory(0.0, false));
            Assert.AreEqual(2, Chemistry.CorrosivityCategory(0.1, false));
            Assert.AreEqual(3, Chemistry.CorrosivityCategory(0.1, true), "chloride raises it a class");
        }

        [Test]
        public void SodiumDoesNotSurviveOpenHumidAir_ASealedStoreKeepsMostOfIt_AndDryAirLeavesItAlone()
        {
            double left = 100.0 + Chemistry.Step(Material.Sodium, 100.0, Shelter.OpenPile, Humid, 0, 30.0).Delta[Material.Sodium];
            Assert.Less(left, 1.0, "under 1 % of it left after a month at 70 % RH");
            double kept = 100.0 + Chemistry.Step(Material.Sodium, 100.0, Shelter.Sealed, Humid, 0, 30.0).Delta[Material.Sodium];
            Assert.Greater(kept, 90.0);
            var dry = new ChemEnvironment(15.0, 0.0, 101_325.0, 0.2, false);
            Assert.AreEqual(0, Chemistry.Step(Material.Sodium, 100.0, Shelter.OpenPile, dry, 0, 30.0).Delta.Count, "no moisture, no reaction");
        }

        [Test]
        public void AluminiumRockAndTheRestAreStable()
        {
            foreach (Material m in new[] { Material.Aluminium, Material.PureAluminium, Material.Rock, Material.Slag, Material.Anorthosite, Material.Alumina, Material.RareEarth, Material.Copper })
                Assert.AreEqual(0, Chemistry.Step(m, 100.0, Shelter.OpenPile, Humid, 1.0, 365.0).Delta.Count, m.ToString());
        }

        [Test]
        public void IceSublimatesOnMarsWhenWarm_MeltsAndRunsOffAnOpenPile_AndAnAirlessWorldDriesItFaster()
        {
            var marsWarm = new ChemEnvironment(-5.0, 2.0, 600.0, 0.0, false);
            Assert.Greater(Chemistry.Step(Material.Water, 1000.0, Shelter.OpenPile, marsWarm, 0, 30.0).WaterSublimated, 0.0);
            var warmEarth = new ChemEnvironment(10.0, 60.0, 101_325.0, 0.2, false);
            Assert.Greater(Chemistry.Step(Material.Water, 1000.0, Shelter.OpenPile, warmEarth, 0, 5.0).Drained[Material.Water], 100.0, "meltwater runs off an open pile");
            Assert.AreEqual(0, Chemistry.Step(Material.Water, 1000.0, Shelter.Sealed, warmEarth, 0, 5.0).Delta.Count);
            double airless = Chemistry.Step(Material.Water, 1000.0, Shelter.OpenPile, new ChemEnvironment(-50.0, 0.0, 0.0, 0.0, false), 0, 30.0).WaterSublimated;
            double air = Chemistry.Step(Material.Water, 1000.0, Shelter.OpenPile, new ChemEnvironment(-50.0, 0.0, 101_325.0, 0.2, false), 0, 30.0).WaterSublimated;
            Assert.Greater(airless, air);
            Assert.AreEqual(611.2, Chemistry.IceVapourPressurePa(273.15), 15.0, "ice near the triple point: 611 Pa");
        }

        [Test]
        public void SaltTakesUpWaterAboveSeventyFivePercent_OnlyInAnOpenPile()
        {
            Assert.AreEqual(0, Chemistry.Step(Material.Salt, 100.0, Shelter.OpenPile, new ChemEnvironment(20, 70.0, 101325, 0.2, false), 0, 30).Delta.Count);
            Assert.Greater(-Chemistry.Step(Material.Salt, 100.0, Shelter.OpenPile, new ChemEnvironment(20, 95.0, 101325, 0.2, false), 0, 10).Delta[Material.Salt], 1.0);
            Assert.AreEqual(0, Chemistry.Step(Material.Salt, 100.0, Shelter.Yard, new ChemEnvironment(20, 95.0, 101325, 0.2, false), 0, 10).Delta.Count);
        }

        [Test]
        public void TheHumidityTableIsEarthLike()
        {
            var (tropics, _) = HumidityClimate.At(0.0, 0.5);
            Assert.Greater(tropics, 55.0); Assert.Less(tropics, 95.0);
        }

        [Test]
        public void InTheSimulation_AnOpenIronPileRustsSlowly_SodiumDoesNot_TheLedgerCloses_AndTheLanderIsSealed()
        {
            var earth = new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0, 282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 15.0, false, 1.0);
            var sphere = HexSphere.Create(12);
            var sim = new PlanetSimulation(sphere, PlanetSurface.Generate(sphere, new SurfaceSettings(7)), earth, Kardashev.EarthRadiusM) { Clouds = CloudModel.EarthClimatology };
            int tile = Enumerable.Range(0, sim.TileCount).First(t => t != sim.BaseTileId && !sim.GetTile(t).IsWater);
            sim.InventoriesForTests.Add(PlaceId.Depot(tile), Material.Iron, 1000.0);
            // a depot with no sealed store cannot even hold sodium (hazardous), so the exposed pile is a wreck: it is an open pile with no capacity limit
            var dead = sim.Workforce.AddRobot(KardashevAscent.Simulation.Robots.RobotKind.General, tile);
            var wreck = sim.DestroyForTests(dead.Id, 10.0);
            sim.InventoriesForTests.Add(PlaceId.Wreck(wreck.Id), Material.Sodium, 100.0);
            Assert.AreEqual(Shelter.OpenPile, sim.ShelterOf(tile, Material.Iron));
            Assert.AreEqual(Shelter.Sealed, sim.ShelterOf(sim.BaseTileId, Material.Iron));
            for (int d = 0; d < 5; d++) sim.Tick(24.0);
            Assert.IsTrue(sim.OperationsLog.Any(o => o.Quantity == "Reactive metal exposed"), "an ACT NOW entry for sodium in open air");
            for (int d = 0; d < 365 * 2; d++) sim.Tick(24.0);
            double iron = sim.InventoriesForTests.StockOf(PlaceId.Depot(tile))[Material.Iron];
            TestContext.WriteLine($"iron after 2 years in an open pile: {iron:0.0} of 1000 kg, rust {sim.InventoriesForTests.StockOf(PlaceId.Depot(tile))[Material.Rust]:0.0}");
            Assert.Less(iron, 1000.0);
            Assert.Greater(iron, 800.0, "a coast-or-damp tile loses about a tenth in 2 years; the balance table has the inland figure");
            Assert.Greater(sim.InventoriesForTests.StockOf(PlaceId.Depot(tile))[Material.Rust], 0.0);
            Assert.Less(sim.InventoriesForTests.StockOf(PlaceId.Wreck(wreck.Id))[Material.Sodium], 1.0, "sodium does not survive open humid air");
            Assert.AreEqual(0.0, sim.Chemistry.ImbalanceKg, 1e-6, "the environment ledger closes");
        }

        [Test]
        public void PhoenixSummer_IceSublimatesMillimetresToCentimetresPerSol_AndAColdAirlessWorldKeepsItsIce()
        {
            // Phoenix landing site, northern summer: about -35 C, 800 Pa of CO2, g = 3.71; exposed ice chunks vanished within about 4 sols (Smith et al. 2009, Science 325)
            foreach (double c in new[] { -15.0, -5.0, 0.0 })   // the daytime temperature of sunlit ice (the night, at -45 C, gives 0.06 mm a sol)
            {
                double j = Chemistry.SublimationFlux(c, 0.1, 800.0, 3.71, 0.04401);
                double mmPerSol = j * 88775.0 / 917.0 * 1000.0;
                TestContext.WriteLine($"Mars at {c:0} C: {j:0.0e+0} kg/m2/s, {mmPerSol:0.0} mm of ice per sol");
                Assert.Greater(mmPerSol, 1.0); Assert.Less(mmPerSol, 40.0);
            }
            double cold = Chemistry.SublimationFlux(-153.15, 0.0, 0.0);   // 120 K, no air
            double warm = Chemistry.SublimationFlux(-50.0, 0.0, 0.0);
            Assert.Less(cold * 86400.0 * 365.0 * 2.0 * 0.005, 0.001, "ice at 120 K stays: under 0.1 % in two years");
            Assert.Greater(warm, cold * 1e6);
        }
    }
}
