using KardashevAscent.Simulation.Energy;
using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    public class ConstructionTests
    {
        [Test]
        public void Costs_FollowTheOnSiteShareOfEmbodiedEnergy()
        {
            // 10 000 m² at 500 kWh/m² is 5 GWh embodied; 1 % of it is paid on site.
            Assert.AreEqual(50e6, BuildCosts.Panel(500_000.0, 10_000.0), 1e-3);
            // A 20 MWh yard at 100 Wh per Wh is 2 GWh embodied; 1 % is 20 MWh.
            Assert.AreEqual(20e6, BuildCosts.Battery(20e6), 1e-3);
        }

        [Test]
        public void Start_PaysFromStock_ElseQueues()
        {
            var c = new Construction(100.0);
            Assert.IsTrue(c.Start(1, 0, 60.0), "enough parts: finished at once");
            Assert.AreEqual(40.0, c.StockWh, 1e-9);
            Assert.IsFalse(c.Start(1, 1, 60.0), "not enough: a site");
            Assert.AreEqual(0.0, c.StockWh, 1e-9, "the site takes what there is");
            Assert.IsTrue(c.IsQueued(1, 1));
            Assert.AreEqual(40.0 / 60.0, c.Progress(1, 1), 1e-9);
            Assert.AreEqual(20.0, c.RemainingWh, 1e-9);
            Assert.AreEqual(1.0, c.Progress(1, 0), 1e-9, "finished building");
        }

        [Test]
        public void Step_TurnsSparePowerIntoParts_AndFinishesSitesInOrder()
        {
            var c = new Construction(0.0);
            c.Start(1, 0, 100.0);
            c.Start(1, 1, 100.0);
            Assert.AreEqual(0, c.Step(30.0, 2.0).Count, "60 Wh: not enough for the first");
            Assert.AreEqual(0.6, c.Progress(1, 0), 1e-9);
            Assert.AreEqual(0.0, c.Progress(1, 1), 1e-9, "the second waits its turn");

            var done = c.Step(50.0, 3.0);   // 150 Wh: 40 finishes the first, 100 the second, 10 left over
            Assert.AreEqual(2, done.Count);
            Assert.AreEqual((1, 0), done[0]);
            Assert.AreEqual((1, 1), done[1]);
            Assert.AreEqual(10.0, c.StockWh, 1e-9);
            Assert.AreEqual(0, c.QueueCount);
        }

        [Test]
        public void Step_WithNoSites_StocksParts()
        {
            var c = new Construction(0.0);
            c.Step(1000.0, 0.5);
            Assert.AreEqual(500.0, c.StockWh, 1e-9);
            c.Step(-5.0, 1.0);
            Assert.AreEqual(500.0, c.StockWh, 1e-9, "no spare power, no parts");
        }

        [Test]
        public void Cancel_RefundsPaidParts_Salvage_GivesBackHalf()
        {
            var c = new Construction(30.0);
            c.Start(2, 5, 100.0);
            Assert.AreEqual(30.0, c.Cancel(2, 5), 1e-9);
            Assert.AreEqual(30.0, c.StockWh, 1e-9);
            Assert.IsFalse(c.IsQueued(2, 5));
            Assert.AreEqual(0.0, c.Cancel(2, 5), 1e-9, "nothing to cancel twice");
            Assert.AreEqual(50.0, c.Salvage(100.0), 1e-9);
            Assert.AreEqual(80.0, c.StockWh, 1e-9);
        }

        [Test]
        public void SameSlotTwice_Throws()
        {
            var c = new Construction(0.0);
            c.Start(0, 0, 10.0);
            Assert.Throws<InvalidOperationException>(() => c.Start(0, 0, 10.0));
        }
    }

    public class StubConstructionTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSimulation Create() => new PlanetSimulation(Sphere, Surface, Earthlike);

        static double Tilt(PlanetSimulation sim) => Math.Abs(sim.GetTile(sim.BaseTileId).Latitude);

        /// <summary>Place panels until one becomes a site; returns its slot.</summary>
        static int FillStock(PlanetSimulation sim)
        {
            int t = sim.BaseTileId;
            for (int s = 0; s < sim.SlotCount(t); s++)
            {
                Assert.IsTrue(sim.PlacePanel(t, s, Tilt(sim), Math.PI));
                if (sim.IsUnderConstruction(t, s))
                    return s;
            }
            throw new InvalidOperationException("The lander's parts never ran out.");
        }

        [Test]
        public void LanderParts_BuildTheFirstPanelsAtOnce()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.AreEqual(BuildCosts.LanderStockWh, sim.BuildStockWh, 1e-3);
            double cost = sim.BuildCostWh(BuildingType.SolarPanel);
            Assert.Greater(cost, 0.0);
            Assert.IsTrue(sim.PlacePanel(t, 0, Tilt(sim), Math.PI));
            Assert.IsFalse(sim.IsUnderConstruction(t, 0));
            Assert.AreEqual(BuildCosts.LanderStockWh - cost, sim.BuildStockWh, 1e-3);
        }

        [Test]
        public void Site_MakesNoPower_UntilFinished()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            int site = FillStock(sim);
            double withSite = sim.GenerationW;
            Assert.IsTrue(sim.RemovePanel(t, site));
            Assert.AreEqual(withSite, sim.GenerationW, 1e-6, "the site added nothing");
            Assert.AreEqual(BuildingType.None, sim.GetBuilding(t, site));
        }

        [Test]
        public void SparePower_FinishesSites_AndRaisesTheEvent()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            int site = FillStock(sim);
            int finishedTile = -1, finishedSlot = -1;
            sim.BuildingCompleted += (tile, slot) => { finishedTile = tile; finishedSlot = slot; };

            for (int day = 0; day < 60 && sim.IsUnderConstruction(t, site); day++)
                sim.Tick(24.0);

            Assert.IsFalse(sim.IsUnderConstruction(t, site), "sunny days of spare power finish the site");
            Assert.AreEqual(t, finishedTile);
            Assert.AreEqual(site, finishedSlot);
            Assert.AreEqual(0, sim.ConstructionQueueCount);
        }

        [Test]
        public void BatterySite_AddsStorageOnlyWhenFinished()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            FillStock(sim);
            double capacity = sim.BatteryCapacityWh;
            int slot = sim.SlotCount(t) - 1;
            Assert.IsTrue(sim.PlaceBattery(t, slot));
            Assert.IsTrue(sim.IsUnderConstruction(t, slot));
            Assert.AreEqual(capacity, sim.BatteryCapacityWh, 1e-6);

            for (int day = 0; day < 120 && sim.IsUnderConstruction(t, slot); day++)
                sim.Tick(24.0);
            Assert.IsFalse(sim.IsUnderConstruction(t, slot));
            Assert.AreEqual(capacity + sim.BatteryUnitWh, sim.BatteryCapacityWh, 1e-6);

            Assert.IsTrue(sim.RemoveBuilding(t, slot));
            Assert.AreEqual(capacity, sim.BatteryCapacityWh, 1e-6);
        }

        [Test]
        public void Removing_RefundsASite_AndSalvagesHalfAFinishedBuilding()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            double cost = sim.BuildCostWh(BuildingType.SolarPanel);
            Assert.IsTrue(sim.PlacePanel(t, 0, Tilt(sim), Math.PI));
            double before = sim.BuildStockWh;
            Assert.IsTrue(sim.RemovePanel(t, 0));
            Assert.AreEqual(before + BuildCosts.SalvageShare * cost, sim.BuildStockWh, 1e-3);

            int site = FillStock(sim);
            double stock = sim.BuildStockWh;
            double paid = sim.ConstructionProgress(t, site) * cost;
            Assert.IsTrue(sim.RemoveBuilding(t, site));
            Assert.AreEqual(stock + paid, sim.BuildStockWh, 1e-3);
            Assert.AreEqual(0, sim.ConstructionQueueCount);
        }

        [Test]
        public void SetPanelAngles_IsFree()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.IsTrue(sim.PlacePanel(t, 0, 0.0, Math.PI));
            double stock = sim.BuildStockWh;
            double flat = sim.PreviewPanel(t, 0.0, Math.PI).EnergyPerYearWh;
            Assert.IsTrue(sim.SetPanelAngles(t, 0, Tilt(sim), Math.PI));
            Assert.AreEqual(stock, sim.BuildStockWh, 1e-9);
            Assert.IsFalse(sim.IsUnderConstruction(t, 0));
            Assert.Greater(sim.PreviewPanel(t, Tilt(sim), Math.PI).EnergyPerYearWh, flat);
            Assert.IsFalse(sim.SetPanelAngles(t, 1, 0.0, Math.PI), "no panel there");
        }

        [Test]
        public void SaveAndLoad_KeepsPartsAndSitesInOrder()
        {
            var a = Create();
            int t = a.BaseTileId;
            int first = FillStock(a);
            Assert.IsTrue(a.PlacePanel(t, first + 1, Tilt(a), Math.PI));
            int yard = first + 20;   // a battery yard covers two slots: away from the panels
            Assert.IsTrue(a.PlaceBattery(t, yard));
            // Run until the first site is part paid (a fixed time breaks whenever panel cost or output changes).
            for (int i = 0; i < 96 && a.ConstructionProgress(t, first) <= 0.0; i++)
                a.Tick(0.25);
            Assert.Greater(a.ConstructionProgress(t, first), 0.0, "spare power within a day");
            Assert.Greater(a.ConstructionQueueCount, 1, "later sites still waiting");

            var b = Create();
            Assert.IsTrue(b.RestoreState(a.CaptureState()));
            Assert.AreEqual(a.BuildStockWh, b.BuildStockWh, 1e-3);
            Assert.AreEqual(a.ConstructionQueueCount, b.ConstructionQueueCount);
            Assert.AreEqual(a.ConstructionRemainingWh, b.ConstructionRemainingWh, 1e-3);
            foreach (int s in new[] { first, first + 1, yard })
            {
                Assert.AreEqual(a.IsUnderConstruction(t, s), b.IsUnderConstruction(t, s));
                Assert.AreEqual(a.ConstructionProgress(t, s), b.ConstructionProgress(t, s), 1e-9);
            }
            Assert.AreEqual(a.BatteryCapacityWh, b.BatteryCapacityWh, 1e-3);

            for (int i = 0; i < 40; i++)
            {
                a.Tick(24.0);
                b.Tick(24.0);
                Assert.AreEqual(a.ConstructionQueueCount, b.ConstructionQueueCount, $"day {i}");
            }
            Assert.AreEqual(a.BuildStockWh, b.BuildStockWh, Math.Max(1.0, a.BuildStockWh * 1e-6));
        }

        [Test]
        public void VersionOneSave_LoadsEverythingFinished_WithTheLanderParts()
        {
            var a = Create();
            int t = a.BaseTileId;
            Assert.IsTrue(a.PlacePanel(t, 0, Tilt(a), Math.PI));
            var state = a.CaptureState();
            state.Version = 1;
            state.BuildStockWh = 0.0;

            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.IsFalse(b.IsUnderConstruction(t, 0));
            Assert.AreEqual(BuildCosts.LanderStockWh, b.BuildStockWh, 1e-3);
        }
    }
}
