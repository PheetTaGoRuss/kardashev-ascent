using KardashevAscent.Simulation.Energy;
using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    public class ConstructionTests
    {
        static MaterialBill Bill(double iron, double copper = 0.0) =>
            new MaterialBill().Add(Material.Iron, iron).Add(Material.Copper, copper);

        static MaterialStock Stock(double iron, double copper = 0.0)
        {
            var s = new MaterialStock();
            s.Add(Material.Iron, iron);
            s.Add(Material.Copper, copper);
            return s;
        }

        [Test]
        public void PanelBill_FollowsTheModuleComposition_TimesTheOnSiteShare()
        {
            // 10 000 m² × 11.5 kg/m² = 115 t of modules; 1 % on site = 1 150 kg:
            // glass 75 % + polymers 12 % + silicon 3 % as rock, frame 9 % as iron, copper 1 %.
            MaterialBill b = BuildCosts.Panel(10_000.0);
            Assert.AreEqual(1150.0, b.TotalKg, 1e-6);
            Assert.AreEqual(1035.0, b[Material.Rock], 1e-6);
            Assert.AreEqual(103.5, b[Material.Iron], 1e-6);
            Assert.AreEqual(11.5, b[Material.Copper], 1e-6);
        }

        [Test]
        public void BatteryBill_IsTheLfpCathode_TimesTheOnSiteShare()
        {
            // 20 MWh × 2.05 kg/kWh = 41 t of LiFePO4; 1 % = 410 kg, of which Li 6.94/157.76 and Fe 55.845/157.76.
            MaterialBill b = BuildCosts.Battery(20e6);
            Assert.AreEqual(410.0, b.TotalKg, 1e-6);
            Assert.AreEqual(410.0 * 6.94 / 157.76, b[Material.Lithium], 1e-9);
            Assert.AreEqual(410.0 * 55.845 / 157.76, b[Material.Iron], 1e-9);
        }

        [Test]
        public void LanderCargo_CoversOneOfEachStarterBuilding()
        {
            MaterialBill all = BuildCosts.Panel(10_000.0).Plus(BuildCosts.Battery(20e6))
                .Plus(BuildCosts.ChargingStation).Plus(BuildCosts.Workshop);
            var stock = new MaterialStock();
            foreach (var (m, kg) in BuildCosts.LanderCargo.Items()) stock.Add(m, kg);
            Assert.IsTrue(all.CoveredBy(stock));
        }

        [Test]
        public void Start_QueuesASite_AnEmptyBillIsFinishedAtOnce()
        {
            var c = new Construction { TimeEnabled = false };
            Assert.IsTrue(c.Start(1, 0, new MaterialBill()));
            Assert.IsFalse(c.IsQueued(1, 0));
            Assert.IsFalse(c.Start(1, 1, Bill(60.0)));
            Assert.IsTrue(c.IsQueued(1, 1));
            Assert.AreEqual(0.0, c.Progress(1, 1), 1e-9);
            Assert.AreEqual(60.0, c.Remaining[Material.Iron], 1e-9);
            Assert.AreEqual(1.0, c.Progress(1, 0), 1e-9, "no site there");
        }

        [Test]
        public void Pay_TakesTheSameShareOfEveryMaterial_AsTheScarcestAllows()
        {
            var c = new Construction { TimeEnabled = false };
            c.Start(1, 0, Bill(100.0, 10.0));
            MaterialStock stock = Stock(1000.0, 4.0);   // copper for 40 % only
            Assert.AreEqual(0, c.Pay(stock, null).Count);
            Assert.AreEqual(0.4, c.Progress(1, 0), 1e-9);
            Assert.AreEqual(960.0, stock[Material.Iron], 1e-9, "40 % of the iron");
            Assert.AreEqual(0.0, stock[Material.Copper], 1e-9);
            Assert.AreEqual(60.0, c.RemainingAt(1, 0)[Material.Iron], 1e-9);

            stock.Add(Material.Copper, 100.0);
            var done = c.Pay(stock, null);
            Assert.AreEqual(1, done.Count);
            Assert.AreEqual(900.0, stock[Material.Iron], 1e-9);
            Assert.AreEqual(94.0, stock[Material.Copper], 1e-9);
            Assert.AreEqual(0, c.QueueCount);
        }

        [Test]
        public void Pay_OnlySitesARobotWorksOn_InQueueOrder()
        {
            var c = new Construction { TimeEnabled = false };
            c.Start(1, 0, Bill(100.0));
            c.Start(1, 1, Bill(100.0));
            MaterialStock stock = Stock(150.0);
            var done = c.Pay(stock, key => key.Slot == 1);
            Assert.AreEqual(1, done.Count);
            Assert.AreEqual((1, 1), done[0]);
            Assert.AreEqual(0.0, c.Progress(1, 0), 1e-9, "no robot there");
            done = c.Pay(stock, null);
            Assert.AreEqual(0, done.Count);
            Assert.AreEqual(0.5, c.Progress(1, 0), 1e-9, "the 50 kg left");
        }

        [Test]
        public void Cancel_RefundsWhatWasPutIn_Salvage_GivesBackHalf()
        {
            var c = new Construction { TimeEnabled = false };
            c.Start(2, 5, Bill(100.0));
            MaterialStock stock = Stock(30.0);
            c.Pay(stock, null);
            Assert.AreEqual(0.0, stock[Material.Iron], 1e-9);
            Assert.AreEqual(30.0, c.Cancel(2, 5, stock)[Material.Iron], 1e-9);
            Assert.AreEqual(30.0, stock[Material.Iron], 1e-9);
            Assert.IsFalse(c.IsQueued(2, 5));
            Assert.IsTrue(c.Cancel(2, 5, stock).IsEmpty, "nothing to cancel twice");
            Assert.AreEqual(50.0, c.Salvage(Bill(100.0), stock)[Material.Iron], 1e-9);
            Assert.AreEqual(80.0, stock[Material.Iron], 1e-9);
        }

        [Test]
        public void SameSlotTwice_Throws()
        {
            var c = new Construction { TimeEnabled = false };
            c.Start(0, 0, Bill(10.0));
            Assert.Throws<InvalidOperationException>(() => c.Start(0, 0, Bill(10.0)));
        }
    }

    public class ConstructionTimeTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        [Test]
        public void BuildHours_AreRealWorldDaysToWeeks()
        {
            // Director 2026-11-15: as in real life. A 1 ha field about 1 000 robot hours (~1.6 person-hours per kW);
            // bigger fields take less per hectare; a mast a couple of days, a workshop hall weeks.
            Assert.AreEqual(BuildCosts.PanelHoursPerHa, BuildCosts.BuildHours(BuildingType.SolarPanel, 10_000.0), 1e-9);
            double hundred = BuildCosts.BuildHours(BuildingType.SolarPanel, 1_000_000.0);
            Assert.Less(hundred, 100.0 * BuildCosts.PanelHoursPerHa);
            Assert.Greater(hundred, 50.0 * BuildCosts.PanelHoursPerHa);
            Assert.Greater(BuildCosts.BuildHours(BuildingType.FloatingSolar, 10_000.0), BuildCosts.PanelHoursPerHa);
            foreach (BuildingType b in new[] { BuildingType.Battery, BuildingType.Workshop, BuildingType.ChargingStation, BuildingType.Relay,
                                               BuildingType.ResearchStation, BuildingType.AluminaPlant, BuildingType.Electrolyser })
                Assert.That(BuildCosts.BuildHours(b), Is.InRange(24.0, 24.0 * 60.0), b + ": a day to two months for one robot");
            Assert.Less(BuildCosts.BuildHours(BuildingType.Relay), BuildCosts.BuildHours(BuildingType.Workshop));
        }

        [Test]
        public void ACrew_BuildsFasterThanOneRobot()
        {
            Assert.Greater(KardashevAscent.Simulation.Robots.FacilityStaffing.CapacityFor(KardashevAscent.Simulation.Robots.TaskKind.Build), 1);
            double one = KardashevAscent.Simulation.Robots.FacilityStaffing.Rate(new[] { 1.0 });
            double three = KardashevAscent.Simulation.Robots.FacilityStaffing.Rate(new[] { 1.0, 1.0, 1.0 });
            Assert.Greater(three, 1.5 * one);
        }

        [Test]
        public void Work_CannotRunAheadOfTheMaterials_AndBothFinishTheSite()
        {
            var c = new Construction();
            MaterialBill bill = new MaterialBill().Add(Material.Iron, 1000.0);
            c.Start(0, 0, bill, 6.0);   // 6 h of work
            var stock = new MaterialStock();
            stock.Add(Material.Iron, 500.0);
            c.Pay(stock, null);
            Assert.AreEqual(3.0, c.Work(0, 0, 10.0), 1e-9, "half the materials: half the work");
            Assert.AreEqual(0.5, c.Progress(0, 0), 1e-9);
            Assert.AreEqual(0, c.Pay(stock, null).Count);
            stock.Add(Material.Iron, 500.0);
            Assert.AreEqual(0, c.Pay(stock, null).Count, "materials in, but work left");
            Assert.AreEqual(1.0, c.MaterialShare(0, 0), 1e-9);
            Assert.AreEqual(3.0, c.Work(0, 0, 10.0), 1e-9);
            Assert.AreEqual(1, c.Pay(stock, null).Count);
        }

        [Test]
        public void ARobotTakesHours_ToPutUpAPanel()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.Workforce.Reset(sim.BaseTileId, 1);
            int t = sim.BaseTileId;
            Assert.IsTrue(sim.PlacePanel(t, 0, 0.3, Math.PI));
            Assert.IsTrue(sim.IsUnderConstruction(t, 0), "not at once any more");
            double hours = sim.ConstructionWork(t, 0).Total;
            Assert.AreEqual(BuildCosts.BuildHours(BuildingType.SolarPanel, sim.PanelAreaAt(t, 0)), hours, 1e-9);
            sim.Tick(1.0);
            Assert.IsTrue(sim.IsBeingBuilt(t, 0));
            Assert.Greater(sim.ConstructionProgress(t, 0), 0.0);
            double elapsed = 1.0;
            while (sim.IsUnderConstruction(t, 0) && elapsed < 4.0 * hours) { sim.Tick(1.0); elapsed += 1.0; }
            Assert.IsFalse(sim.IsUnderConstruction(t, 0));
            Assert.GreaterOrEqual(elapsed, hours - 1.0, "about its work hours (a robot may stop to charge)");
            Assert.Greater(elapsed, 24.0 * 7.0, "a 1 ha field takes one robot weeks (director 2026-11-15)");
        }

        [Test]
        public void Save_KeepsTheWorkDone_AnOlderSiteWorksAsFarAsItsMaterials()
        {
            var a = new PlanetSimulation(Sphere, Surface, Earthlike);
            a.Workforce.Reset(a.BaseTileId, 1);
            int t = a.BaseTileId;
            Assert.IsTrue(a.PlacePanel(t, 0, 0.3, Math.PI));
            a.Tick(2.0);
            double done = a.ConstructionWork(t, 0).Done;
            Assert.Greater(done, 0.0);
            SimulationState state = a.CaptureState();
            var b = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(done, b.ConstructionWork(t, 0).Done, 1e-9);

            state.Version = 26;
            var c = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(c.RestoreState(state));
            Assert.AreEqual(c.ConstructionWork(t, 0).Total * c.ConstructionMaterialShare(t, 0), c.ConstructionWork(t, 0).Done, 1e-9);
        }
    }

    public class StubConstructionTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSimulation Create() => new PlanetSimulation(Sphere, Surface, Earthlike) { ConstructionTimeEnabled = false };

        static double Tilt(PlanetSimulation sim) => Math.Abs(sim.GetTile(sim.BaseTileId).Latitude);

        static double Kg(PlanetSimulation sim) => sim.Materials[Material.Rock] + sim.Materials[Material.Iron] + sim.Materials[Material.Copper];

        [Test]
        public void NewColony_HasTheLanderCargo()
        {
            var sim = Create();
            foreach (var (m, kg) in BuildCosts.LanderCargo.Items())
                Assert.AreEqual(kg, sim.Materials[m], 1e-9, m.ToString());
        }

        [Test]
        public void Robots_BuildFromTheCargo_TakingTheBill()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            MaterialBill bill = sim.BuildBill(BuildingType.SolarPanel);
            double iron = sim.Materials[Material.Iron];
            int finishedSlot = -1;
            sim.BuildingCompleted += (tile, slot) => finishedSlot = slot;
            Assert.IsTrue(sim.PlacePanel(t, 0, Tilt(sim), Math.PI));
            for (int i = 0; i < 96 && sim.IsUnderConstruction(t, 0); i++)
                sim.Tick(0.25);
            Assert.IsFalse(sim.IsUnderConstruction(t, 0), "a starter robot builds it");
            Assert.AreEqual(iron - bill[Material.Iron], sim.Materials[Material.Iron], 1e-6);
        }

        [Test]
        public void WithoutMaterials_ASiteWaits_AndMakesNoPower()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Materials.Clear();
            double before = sim.GenerationW;
            Assert.IsTrue(sim.PlacePanel(t, 0, Tilt(sim), Math.PI));
            sim.Tick(24.0);
            Assert.IsTrue(sim.IsUnderConstruction(t, 0));
            Assert.AreEqual(0.0, sim.ConstructionProgress(t, 0), 1e-12);
            Assert.IsFalse(sim.ConstructionShortfall.IsEmpty);

            foreach (var (m, kg) in sim.BuildBill(BuildingType.SolarPanel).Items())
                sim.Materials.Add(m, kg);
            for (int i = 0; i < 96 && sim.IsUnderConstruction(t, 0); i++)
                sim.Tick(0.25);
            Assert.IsFalse(sim.IsUnderConstruction(t, 0), "finished once the materials are there");
            Assert.AreEqual(0.0, Kg(sim), 1e-6, "it took exactly its bill");
        }

        [Test]
        public void SparePower_NoLongerBuildsAnything()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Materials.Clear();
            Assert.IsTrue(sim.PlacePanel(t, 0, Tilt(sim), Math.PI));
            for (int day = 0; day < 10; day++)
                sim.Tick(24.0);
            Assert.IsTrue(sim.IsUnderConstruction(t, 0));
        }

        [Test]
        public void BatterySite_AddsStorageOnlyWhenFinished()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            double capacity = sim.BatteryCapacityWh;
            int slot = sim.SlotCount(t) - 1;
            MaterialBill bill = sim.BuildBill(BuildingType.Battery);
            sim.Materials.Clear();
            Assert.IsTrue(sim.PlaceBattery(t, slot));
            Assert.IsTrue(sim.IsUnderConstruction(t, slot));
            Assert.AreEqual(capacity, sim.BatteryCapacityWh, 1e-6);

            foreach (var (m, kg) in bill.Items()) sim.Materials.Add(m, kg);
            for (int i = 0; i < 96 && sim.IsUnderConstruction(t, slot); i++)
                sim.Tick(0.25);
            Assert.IsFalse(sim.IsUnderConstruction(t, slot));
            Assert.AreEqual(capacity + sim.BatteryUnitWh, sim.BatteryCapacityWh, 1e-6);
            Assert.Greater(bill[Material.Lithium], 0.0, "LFP cells take lithium");

            Assert.IsTrue(sim.RemoveBuilding(t, slot));
            Assert.AreEqual(capacity, sim.BatteryCapacityWh, 1e-6);
        }

        [Test]
        public void Removing_RefundsASite_AndSalvagesHalfAFinishedBuilding()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            MaterialBill bill = sim.BuildBill(BuildingType.SolarPanel);
            Assert.IsTrue(sim.PlacePanel(t, 0, Tilt(sim), Math.PI));
            for (int i = 0; i < 96 && sim.IsUnderConstruction(t, 0); i++)
                sim.Tick(0.25);
            Assert.IsFalse(sim.IsUnderConstruction(t, 0));
            double iron = sim.Materials[Material.Iron];
            Assert.IsTrue(sim.RemovePanel(t, 0));
            Assert.AreEqual(iron + BuildCosts.SalvageShare * bill[Material.Iron], sim.Materials[Material.Iron], 1e-6);

            // A half-built site gives back what went in.
            sim.Materials.Clear();
            foreach (var (m, kg) in bill.Times(0.5).Items()) sim.Materials.Add(m, kg);
            Assert.IsTrue(sim.PlacePanel(t, 1, Tilt(sim), Math.PI));
            for (int i = 0; i < 96 && sim.ConstructionProgress(t, 1) < 0.5 - 1e-9; i++)
                sim.Tick(0.25);
            Assert.AreEqual(0.5, sim.ConstructionProgress(t, 1), 1e-9);
            Assert.IsTrue(sim.RemoveBuilding(t, 1));
            Assert.AreEqual(0.5 * bill[Material.Iron], sim.Materials[Material.Iron], 1e-6);
            Assert.AreEqual(0, sim.ConstructionQueueCount);
        }

        [Test]
        public void SetPanelAngles_IsFree()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.IsTrue(sim.PlacePanel(t, 0, 0.0, Math.PI));
            for (int i = 0; i < 96 && sim.IsUnderConstruction(t, 0); i++)
                sim.Tick(0.25);
            double kg = Kg(sim);
            double flat = sim.PreviewPanel(t, 0.0, Math.PI).EnergyPerYearWh;
            Assert.IsTrue(sim.SetPanelAngles(t, 0, Tilt(sim), Math.PI));
            Assert.AreEqual(kg, Kg(sim), 1e-9);
            Assert.IsFalse(sim.IsUnderConstruction(t, 0));
            Assert.Greater(sim.PreviewPanel(t, Tilt(sim), Math.PI).EnergyPerYearWh, flat);
            Assert.IsFalse(sim.SetPanelAngles(t, 1, 0.0, Math.PI), "no panel there");
        }

        [Test]
        public void SaveAndLoad_KeepsSitesTheirShareAndTheStock()
        {
            var a = Create();
            int t = a.BaseTileId;
            MaterialBill bill = a.BuildBill(BuildingType.SolarPanel);
            a.Materials.Clear();
            foreach (var (m, kg) in bill.Times(0.3).Items()) a.Materials.Add(m, kg);
            Assert.IsTrue(a.PlacePanel(t, 0, Tilt(a), Math.PI));
            Assert.IsTrue(a.PlacePanel(t, 1, Tilt(a), Math.PI));
            for (int i = 0; i < 96 && a.ConstructionProgress(t, 0) < 0.3 - 1e-9; i++)
                a.Tick(0.25);
            Assert.AreEqual(0.3, a.ConstructionProgress(t, 0), 1e-9);

            var b = Create();
            SimulationState state = a.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(a.ConstructionQueueCount, b.ConstructionQueueCount);
            foreach (int s in new[] { 0, 1 })
                Assert.AreEqual(a.ConstructionProgress(t, s), b.ConstructionProgress(t, s), 1e-9);
            Assert.AreEqual(a.ConstructionRemaining.TotalKg, b.ConstructionRemaining.TotalKg, 1e-6);
            Assert.AreEqual(Kg(a), Kg(b), 1e-9, "no cargo added to a new save");
        }

        [Test]
        public void Version25Save_KeepsTheShareOfPartsPaid_AndGetsTheCargo()
        {
            var a = Create();
            int t = a.BaseTileId;
            a.Materials.Clear();
            Assert.IsTrue(a.PlacePanel(t, 0, Tilt(a), Math.PI));
            SimulationState state = a.CaptureState();
            state.Version = 25;
            BuildingRecord site = state.Buildings.Find(r => r.Tile == t && r.Slot == 0);
            Assert.IsTrue(site.UnderConstruction);
            // A version-25 site held parts in Wh: a quarter of the old panel cost.
            site.PaidWh = 0.25 * BuildCosts.LegacyPanelWh(a.EmbodiedWhPerM2, a.PanelAreaAt(t, 0));
            site.PaidShare = 0.0;
            state.MaterialsKg = new List<MaterialRecord>();

            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.IsTrue(b.IsUnderConstruction(t, 0));
            Assert.AreEqual(0.25, b.ConstructionProgress(t, 0), 1e-9);
            foreach (var (m, kg) in BuildCosts.LanderCargo.Items())
                Assert.AreEqual(kg, b.Materials[m], 1e-9, "the old parts became the lander's cargo");
        }
    }
}
