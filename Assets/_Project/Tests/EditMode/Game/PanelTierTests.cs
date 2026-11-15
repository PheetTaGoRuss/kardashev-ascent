using KardashevAscent.Simulation.Energy;
using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Bigger solar installations per slot as milestones unlock them.</summary>
    public class PanelTierTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSimulation Create(double luminositySun = 1.0)
        {
            var r = new PlanetRecord { id = "tiers" };
            r.solar.luminosityW = luminositySun * SolarConstants.SolarLuminosity;
            r.solar.obliquityDeg = 23.44;
            r.solar.airTemperatureC = 25.0;
            return new PlanetSimulation(Sphere, Surface, r);
        }

        /// <summary>A star 10⁵× dimmer: the lander's ~100 kW is planet K 0.4, so a day unlocks solar farms (milestone 4).</summary>
        static PlanetSimulation WithFarms()
        {
            var sim = Create(1e-5);
            sim.Tick(24.0);
            Assume.That(sim.MilestonesReached, Is.GreaterThanOrEqualTo(3));
            return sim;
        }

        static double Tilt(PlanetSimulation sim) => Math.Abs(sim.GetTile(sim.BaseTileId).Latitude);

        [TestCase(0, 0)]
        [TestCase(2, 0)]
        [TestCase(3, 1)]
        [TestCase(4, 1)]
        [TestCase(5, 2)]
        [TestCase(7, 3)]
        [TestCase(10, 3)]
        public void Tier_FollowsMilestones(int milestones, int tier)
        {
            Assert.AreEqual(tier, PanelTiers.For(milestones));
        }

        [TestCase(1.0, 0)]
        [TestCase(100.0, 1)]
        [TestCase(80.0, 1)]
        [TestCase(10_000.0, 2)]
        [TestCase(100_000.0, 3)]
        [TestCase(145_000.0, 3)]
        public void OfScale_FindsABuiltPanelsTier(double scale, int tier)
        {
            Assert.AreEqual(tier, PanelTiers.OfScale(scale));
        }

        [TestCase(10_000.0, "1 ha")]
        [TestCase(1e6, "1 km²")]
        [TestCase(1e8, "100 km²")]
        [TestCase(1e9, "1,000 km²")]
        public void FormatArea_HectaresThenSquareKilometres(double m2, string text)
        {
            Assert.AreEqual(text, PanelTiers.FormatArea(m2));
        }

        [Test]
        public void UnlockedAt_NamesTheNewTier()
        {
            Assert.AreEqual(1, PanelTiers.UnlockedAt(3));
            Assert.AreEqual("Solar farm", PanelTiers.All[PanelTiers.UnlockedAt(3)].Name);
            Assert.AreEqual(-1, PanelTiers.UnlockedAt(4));
            Assert.AreEqual(-1, PanelTiers.UnlockedAt(0));
        }

        [Test]
        public void StartsWithOneHectarePanels()
        {
            var sim = Create();
            Assert.AreEqual(0, sim.PanelTier);
            Assert.AreEqual(10_000.0, sim.NewPanelAreaM2(sim.BaseTileId));
            Assert.IsTrue(sim.PlacePanel(sim.BaseTileId, 0, Tilt(sim), Math.PI));
            Assert.AreEqual(10_000.0, sim.PanelAreaAt(sim.BaseTileId, 0));
            Assert.AreEqual(0.0, sim.PanelAreaAt(sim.BaseTileId, 1));
        }

        [Test]
        public void Farms_CoverAHundredTimesTheGround_AndCostAHundredTimesMore()
        {
            var small = Create();
            var big = WithFarms();
            Assert.AreEqual(1, big.PanelTier);
            int t = big.BaseTileId;
            Assert.AreEqual(1e6, big.NewPanelAreaM2(t), 1e-6);
            Assert.AreEqual(100.0 * small.BuildBill(BuildingType.SolarPanel).TotalKg, big.BuildBill(BuildingType.SolarPanel).TotalKg, 1e-3);
            // Same star: output scales with area; capacity factor, payback and EROI don't depend on size.
            PanelPreview a = Create(1e-5).PreviewPanel(t, Tilt(small), Math.PI);
            PanelPreview b = big.PreviewPanel(t, Tilt(big), Math.PI);
            Assert.Greater(b.EnergyPerYearWh, 0.0);
            Assert.AreEqual(100.0, b.EnergyPerYearWh / a.EnergyPerYearWh, 1e-6);
            Assert.AreEqual(a.CapacityFactor, b.CapacityFactor, 1e-12);
            Assert.AreEqual(a.Eroi, b.Eroi, 1e-9);
            Assert.AreEqual(a.PaybackYears, b.PaybackYears, 1e-9);
        }

        [Test]
        public void BuiltPanels_KeepTheirSize_AndSalvageWhatTheyCost()
        {
            var sim = Create(1e-5);
            sim.ConstructionTimeEnabled = false;   // about sizes, not build time
            int t = sim.BaseTileId;
            Assert.IsTrue(sim.PlacePanel(t, 0, Tilt(sim), Math.PI));   // before any milestone: 1 ha
            sim.Tick(24.0);
            Assert.GreaterOrEqual(sim.PanelTier, 1);
            Assert.AreEqual(10_000.0, sim.PanelAreaAt(t, 0), "still a panel field");
            Assert.IsFalse(sim.IsUnderConstruction(t, 0));

            var rock = KardashevAscent.Simulation.Resources.Material.Rock;
            double stock = sim.Materials[rock];
            Assert.IsTrue(sim.RemovePanel(t, 0));
            double smallCost = KardashevAscent.Simulation.Resources.BuildCosts.Panel(10_000.0)[rock];
            Assert.AreEqual(stock + KardashevAscent.Simulation.Resources.BuildCosts.SalvageShare * smallCost, sim.Materials[rock], 1e-6, "salvage at its own size");
        }

        [Test]
        public void SlotArea_IsCappedAtTheSlotsShareOfTheTile()
        {
            var sim = Create();
            sim.PanelAreaM2 = 1e15;   // absurd: bigger than the tile
            int t = sim.BaseTileId;
            double tileArea = 4.0 * Math.PI * Math.Pow(6.371e6, 2) / Sphere.TileCount;
            Assert.AreEqual(tileArea / sim.SlotCount(t), sim.NewPanelAreaM2(t), tileArea * 1e-9);
        }

        [Test]
        public void Save_KeepsEachPanelsArea()
        {
            var a = WithFarms();
            int t = a.BaseTileId;
            Assert.IsTrue(a.PlacePanel(t, 0, Tilt(a), Math.PI));
            SimulationState state = a.CaptureState();
            Assert.AreEqual(1e6, state.Buildings.Find(r => r.Slot == 0 && r.Tile == t).AreaM2, 1e-6);

            var b = Create(1e-5);   // no milestones yet: new panels would be small
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(1e6, b.PanelAreaAt(t, 0), 1e-6);
            Assert.AreEqual(a.MilestonesReached, b.MilestonesReached);
        }

        [Test]
        public void OldSave_PanelsWithoutAreaAreBaseFields()
        {
            var a = Create();
            int t = a.BaseTileId;
            Assert.IsTrue(a.PlacePanel(t, 0, Tilt(a), Math.PI));
            SimulationState state = a.CaptureState();
            foreach (BuildingRecord r in state.Buildings) r.AreaM2 = 0.0;
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(10_000.0, b.PanelAreaAt(t, 0));
        }
    }
}
