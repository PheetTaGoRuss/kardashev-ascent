using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Plants with buffers, byproducts and waste heat (MO-06, MO-07, MO-08).</summary>
    public class PlantTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        const int WorkshopSlot = 5;

        /// <summary>A colony with a finished Workshop and an Electrolysis cell touching it, and steady power.</summary>
        static (PlanetSimulation Sim, int Cell) WithCell()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { ConstructionTimeEnabled = false };
            sim.Workforce.Reset(sim.BaseTileId, 1);
            foreach (var (m, kg) in sim.BuildBill(BuildingType.Workshop).Items()) sim.Materials.Add(m, kg);
            Assert.IsTrue(sim.PlaceWorkshop(sim.BaseTileId, WorkshopSlot));
            for (int i = 0; i < 400 && sim.WorkshopCount == 0; i++) sim.Tick(0.25);
            sim.RefineryPowerForTests = true;
            sim.Materials.Clear();
            int t = sim.BaseTileId;
            int slot = Enumerable.Range(0, sim.SlotCount(t)).First(s => sim.GetBuilding(t, s) == BuildingType.None && sim.RefineryBlocker(BuildingType.Electrolyser, t, s) == null);
            foreach (var (m, kg) in sim.BuildBill(BuildingType.Electrolyser).Items()) sim.Materials.Add(m, kg);
            Assert.IsTrue(sim.PlaceRefinery(BuildingType.Electrolyser, t, slot));
            for (int i = 0; i < 400 && sim.IsUnderConstruction(t, slot); i++) sim.Tick(0.25);
            Assert.IsFalse(sim.IsUnderConstruction(t, slot));
            sim.Materials.Clear();
            return (sim, slot);
        }

        static void Run(PlanetSimulation sim, double hours)
        {
            for (double h = 0.0; h < hours; h += 1.0) sim.Tick(1.0);
        }

        static double Mass(PlanetSimulation sim, params Material[] materials) => materials.Sum(m => sim.TotalKg(m) + sim.VentedKg(m));

        [Test]
        public void ADownsBatch_ConservesMass_AndVentsTheChlorineWithAWarning_ByDefault()
        {
            var (sim, cell) = WithCell();
            sim.Materials.Add(Material.Salt, 100.0);
            Assert.AreEqual(ByproductPolicy.Vent, sim.ByproductPolicyOf(sim.BaseTileId, cell));
            double before = Mass(sim, Material.Salt, Material.Sodium, Material.Chlorine);
            Assert.IsNull(sim.OrderRefine(RefineProcess.SodiumFromSalt));
            Run(sim, 100.0);
            Assert.AreEqual(10.0, sim.Materials[Material.Sodium], 1e-6);
            Assert.AreEqual(Refining.Of(RefineProcess.SodiumFromSalt).VentedKg, sim.VentedKg(Material.Chlorine), 1e-6, "the chlorine is accounted for");
            Assert.AreEqual(before, Mass(sim, Material.Salt, Material.Sodium, Material.Chlorine), 1e-6, "salt in = sodium + chlorine vented + salt left");
            Assert.IsTrue(sim.IndustrialReports.Any(r => r.Message.Contains("WARNING") && r.Message.Contains("chlorine")), "venting chlorine warns");
        }

        [Test]
        public void TheBuffers_HoldTheBatchInputWhileItRuns_AndTheOutputGoesToTheDepot()
        {
            var (sim, cell) = WithCell();
            sim.Materials.Add(Material.Salt, 100.0);
            sim.OrderRefine(RefineProcess.SodiumFromSalt);
            sim.Tick(1.0);
            PlantReport mid = sim.PlantReportAt(sim.BaseTileId, cell);
            Assert.AreEqual(0.0, mid.InputBufferKg, 1e-9, "the batch took its input from the buffer when it started");
            Assert.Greater(sim.Materials[Material.Salt], 0.0, "only one batch's worth left the depot");
            Run(sim, 100.0);
            PlantReport done = sim.PlantReportAt(sim.BaseTileId, cell);
            Assert.AreEqual(0.0, done.OutputBufferKg, 1e-9, "the conveyor emptied the output buffer");
        }

        [Test]
        public void StoringGas_WithNoTank_FillsTheBuffer_ThenStopsThePlant_AndVentingRestartsIt()
        {
            var (sim, cell) = WithCell();
            int t = sim.BaseTileId;
            sim.LogisticsEnabled = true;   // the lander holds no gas: it needs a tank
            Assert.IsTrue(sim.SetByproductPolicy(t, cell, ByproductPolicy.Store));
            sim.Materials.Add(Material.Salt, 5000.0);
            for (int i = 0; i < 20; i++) sim.OrderRefine(RefineProcess.SodiumFromSalt);
            PlantReport r = null;
            for (int i = 0; i < 2000; i++)
            {
                sim.Tick(1.0);
                r = sim.PlantReportAt(t, cell);
                if (r.Blocker == IndustrialBlocker.OutputStorageUnavailable) break;
            }
            Assert.AreEqual(IndustrialBlocker.OutputStorageUnavailable, r.Blocker, "a full gas store stops the cell");
            Assert.AreEqual(0.0, sim.VentedKg(Material.Chlorine), 1e-9, "nothing was vented while it stored");
            double stored = r.OutputBufferKg;
            Assert.Greater(stored, 150.0);
            Assert.LessOrEqual(stored, PlanetSimulation.PlantOutputGasKg + 1e-6);
            Assert.IsTrue(sim.SetByproductPolicy(t, cell, ByproductPolicy.Vent));
            Run(sim, 120.0);
            Assert.Greater(sim.VentedKg(Material.Chlorine), 0.0, "vented, and the plant runs again");
            Assert.IsTrue(sim.IndustrialReports.Any(x => x.Message.Contains("vented")), "the release is logged");
        }

        [Test]
        public void AGasTank_TakesTheGasFromTheBuffer_UntilItIsFull()
        {
            var (sim, cell) = WithCell();
            int t = sim.BaseTileId;
            sim.LogisticsEnabled = true;
            sim.Debug.FreeBuilding = true;
            int slot = Enumerable.Range(0, sim.SlotCount(t)).First(s => sim.GetBuilding(t, s) == BuildingType.None);
            Assert.IsTrue(sim.PlaceStorage(BuildingType.GasTank, t, slot));
            sim.Debug.FreeBuilding = false;
            sim.Tick(1.0);
            Assert.AreEqual(1, sim.StorageCount(t, BuildingType.GasTank));
            Assert.IsTrue(sim.SetByproductPolicy(t, cell, ByproductPolicy.Store));
            // fill the tank to within one batch of its capacity
            double cap = sim.StorageCapacityKg(t, StorageClass.PressurizedGas);
            sim.PutMaterials(t, Material.Chlorine, cap - 20.0);
            sim.Materials.Add(Material.Salt, 400.0);
            for (int i = 0; i < 6; i++) sim.OrderRefine(RefineProcess.SodiumFromSalt);
            Run(sim, 700.0);
            Assert.AreEqual(cap, sim.Materials[Material.Chlorine], 1e-6, "the tank filled to its capacity and no further");
            Assert.AreEqual(0.0, sim.VentedKg(Material.Chlorine), 1e-9);
            double made = 6 * Refining.Of(RefineProcess.SodiumFromSalt).VentedKg;
            PlantReport r = sim.PlantReportAt(t, cell);
            Assert.AreEqual(made - 20.0, r.OutputBufferKg, 1e-6, "what the tank could not take waits in the plant's buffer: no gas is lost");
        }

        [Test]
        public void EachBatchCountsItsWasteHeat_ElectricityMinusTheChemicalEnergyKept()
        {
            var (sim, cell) = WithCell();
            sim.Materials.Add(Material.Alumina, 100.0);
            sim.OrderRefine(RefineProcess.Aluminium);
            sim.Tick(1.0);
            Assert.Greater(sim.IndustrialWasteHeatW, 0.0, "heat while it runs");
            Assert.Greater(sim.IndustrialWasteForcingWm2, 0.0);
            Run(sim, 180.0);
            RefineRecipe al = Refining.Of(RefineProcess.Aluminium);
            Assert.AreEqual((al.EnergyWh - al.ReactionWhPerKgOutput * al.OutputKg) / 1000.0, sim.IndustrialWasteHeatKWh, 0.5, "97.4 kWh for one 20 kg batch");
            Assert.AreEqual(0.0, sim.IndustrialWasteHeatW, 1e-9, "none once it is idle");
            Assert.AreEqual(al.OutputKg, sim.Materials[Material.Aluminium], 1e-6);
            Assert.AreEqual(al.VentedKg, sim.VentedKg(Material.Oxygen), 1e-6);
        }

        [Test]
        public void AModuleTakenDown_ReturnsWhatIsInItsBuffers()
        {
            var (sim, cell) = WithCell();
            int t = sim.BaseTileId;
            sim.LogisticsEnabled = true;   // no tank: stored gas stays in the output buffer
            sim.SetByproductPolicy(t, cell, ByproductPolicy.Store);
            sim.Materials.Add(Material.Salt, 100.0);
            sim.OrderRefine(RefineProcess.SodiumFromSalt);
            Run(sim, 100.0);
            Assert.Greater(sim.PlantReportAt(t, cell).OutputBufferKg, 10.0, "the chlorine waits in the buffer");
            double before = Mass(sim, Material.Salt, Material.Sodium, Material.Chlorine);
            Assert.IsTrue(sim.RemoveBuilding(t, cell));
            Assert.AreEqual(before, Mass(sim, Material.Salt, Material.Sodium, Material.Chlorine), 1e-6, "nothing is lost with the buffers");
            Assert.AreEqual(Refining.Of(RefineProcess.SodiumFromSalt).VentedKg, sim.Materials[Material.Chlorine], 1e-6, "the gas went back to the depot");
        }

        [Test]
        public void ThePolicyAndTheBuffers_SurviveSaveAndLoad()
        {
            var (sim, cell) = WithCell();
            int t = sim.BaseTileId;
            sim.LogisticsEnabled = true;
            sim.SetByproductPolicy(t, cell, ByproductPolicy.Store);
            sim.Materials.Add(Material.Salt, 200.0);
            for (int i = 0; i < 3; i++) sim.OrderRefine(RefineProcess.SodiumFromSalt);
            Run(sim, 200.0);
            SimulationState state = sim.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var other = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(other.RestoreState(state));
            Assert.AreEqual(ByproductPolicy.Store, other.ByproductPolicyOf(t, cell));
            Assert.AreEqual(sim.PlantReportAt(t, cell).OutputBufferKg, other.PlantReportAt(t, cell).OutputBufferKg, 1e-9);
            Assert.AreEqual(sim.IndustrialWasteHeatKWh, other.IndustrialWasteHeatKWh, 1e-9);
            Assert.AreEqual(sim.VentedKg(Material.Chlorine), other.VentedKg(Material.Chlorine), 1e-9);
        }

        [Test]
        public void AnOldSave_LoadsWithVentAsThePolicy()
        {
            var (sim, cell) = WithCell();
            SimulationState state = sim.CaptureState();
            state.Version = 39;
            state.PlantPolicies.Add(new PlantPolicyRecord { Tile = sim.BaseTileId, Slot = cell, Policy = ByproductPolicy.Store });
            var other = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(other.RestoreState(state));
            Assert.AreEqual(ByproductPolicy.Vent, other.ByproductPolicyOf(other.BaseTileId, cell), "refining always vented before");
        }

        [Test]
        public void AutoRefine_IsAlsoAStockTargetAtTheLanderDepot()
        {
            var (sim, cell) = WithCell();
            sim.SetAutoRefine(RefineProcess.Aluminium, 60.0);
            Assert.AreEqual(60.0, sim.AutoRefineTarget(RefineProcess.Aluminium), 1e-12);
            Assert.AreEqual(60.0, sim.StockTarget(sim.BaseTileId, Material.Aluminium), 1e-12);
            sim.SetAutoRefine(RefineProcess.Aluminium, 0.0);
            Assert.AreEqual(0.0, sim.StockTarget(sim.BaseTileId, Material.Aluminium), 1e-12);
        }
    }
}
