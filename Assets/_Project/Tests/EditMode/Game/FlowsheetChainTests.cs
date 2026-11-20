using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>
    /// The P2 exit: a chain of three steps with logistics on. Anorthosite lies in a pile away from the plants, is hauled to
    /// the Alumina plant, the alumina goes to the electrolysis cell, and the aluminium is hauled to another tile where a site
    /// needs it. Mass closes over the whole run.
    /// </summary>
    public class FlowsheetChainTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly Material[] Chain = { Material.Anorthosite, Material.Alumina, Material.Aluminium, Material.Slag, Material.Oxygen };

        static double Mass(PlanetSimulation sim) => Chain.Sum(m => sim.TotalKg(m) + sim.VentedKg(m));

        static PlanetSimulation Plants()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { ConstructionTimeEnabled = false };
            sim.Workforce.Reset(sim.BaseTileId, 2);
            foreach (var (m, kg) in sim.BuildBill(BuildingType.Workshop).Items()) sim.Materials.Add(m, kg);
            Assert.IsTrue(sim.PlaceWorkshop(sim.BaseTileId, 5));
            for (int i = 0; i < 400 && sim.WorkshopCount == 0; i++) sim.Tick(0.25);
            sim.RefineryPowerForTests = true;
            sim.Materials.Clear();
            // a charger, so the robots that wait through the days of a batch are charged for the hauls
            foreach (var (m, kg) in sim.BuildBill(BuildingType.ChargingStation).Items()) sim.Materials.Add(m, kg);
            Assert.IsTrue(sim.PlaceChargingStation(sim.BaseTileId, 20));
            for (int i = 0; i < 400 && sim.IsUnderConstruction(sim.BaseTileId, 20); i++) sim.Tick(0.25);
            sim.Materials.Clear();
            foreach (BuildingType type in new[] { BuildingType.AluminaPlant, BuildingType.Electrolyser })
            {
                int t = sim.BaseTileId;
                int slot = Enumerable.Range(0, sim.SlotCount(t)).First(s => sim.GetBuilding(t, s) == BuildingType.None && sim.RefineryBlocker(type, t, s) == null);
                foreach (var (m, kg) in sim.BuildBill(type).Items()) sim.Materials.Add(m, kg);
                Assert.IsTrue(sim.PlaceRefinery(type, t, slot));
                for (int i = 0; i < 400 && sim.IsUnderConstruction(t, slot); i++) sim.Tick(0.25);
                Assert.IsFalse(sim.IsUnderConstruction(t, slot));
                sim.Materials.Clear();
            }
            return sim;
        }

        [Test]
        public void MinedOre_IsHauledToThePlants_RefinedInTwoSteps_AndTheAluminiumIsHauledToATile_WithMassClosed()
        {
            var sim = Plants();
            int baseTile = sim.BaseTileId;
            var land = Sphere.Neighbors[baseTile].Where(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n)).ToArray();
            int ore = land[0], consumer = land[1];
            sim.LogisticsEnabled = true;
            sim.PutMaterials(ore, Material.Anorthosite, 160.0);   // what mining left in a pile there
            double before = Mass(sim);
            Assert.AreEqual(160.0, before, 1e-9);

            sim.OrderRefine(RefineProcess.AluminaFromAnorthosite);
            sim.OrderRefine(RefineProcess.Aluminium);
            sim.SetStockTarget(consumer, Material.Aluminium, 20.0);   // a site there would use it: hauls bring it

            for (int i = 0; i < 900 && sim.KgAt(consumer, Material.Aluminium) < 20.0 - 1e-6; i++)
            {
                sim.Tick(1.0);
                Assert.AreEqual(before, Mass(sim) + InPot(sim), 1e-6, "mass closes at every step, with what is in the pots");
            }
            Assert.AreEqual(0.0, sim.KgAt(ore, Material.Anorthosite) - (160.0 - 156.862), 0.01, "the pile gave its ore to the plant");
            Assert.AreEqual(20.0, sim.KgAt(consumer, Material.Aluminium), 1e-6, "the aluminium reached the other tile");
            Assert.AreEqual(Refining.Of(RefineProcess.Aluminium).VentedKg, sim.VentedKg(Material.Oxygen), 1e-6, "the oxygen is accounted for");
            Assert.AreEqual(before, Mass(sim), 1e-6, "mass in = mass out over the whole run");
            Assert.Greater(sim.HaulTotals.KgMoved, 150.0, "robots hauled the ore and the metal");
            Assert.Greater(sim.IndustrialWasteHeatKWh, 90.0, "the heat of both steps was counted");
        }

        /// <summary>Mass inside running batches (taken from the buffers, not yet made).</summary>
        static double InPot(PlanetSimulation sim)
        {
            double kg = 0.0;
            foreach (var line in sim.RefineryLines())
            {
                if (!line.Process.HasValue) continue;
                RefineRecipe r = Refining.Of(line.Process.Value);
                if (Array.IndexOf(Chain, r.Input) >= 0) kg += r.InputKg;
            }
            return kg;
        }

        [Test]
        public void TwoRuns_GiveTheSameCapturedState()
        {
            string Run()
            {
                var sim = Plants();
                var land = Sphere.Neighbors[sim.BaseTileId].Where(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n)).ToArray();
                sim.LogisticsEnabled = true;
                sim.PutMaterials(land[0], Material.Anorthosite, 160.0);
                sim.OrderRefine(RefineProcess.AluminaFromAnorthosite);
                sim.OrderRefine(RefineProcess.Aluminium);
                sim.SetStockTarget(land[1], Material.Aluminium, 20.0);
                for (int i = 0; i < 400; i++) sim.Tick(1.0);
                var s = sim.CaptureState();
                return string.Join(";", s.Inventories.Select(r => $"{r.Kind}{r.Id}:" + string.Join(",", r.Stock.Select(m => $"{m.Material}={m.Kg:R}"))))
                       + "|" + string.Join(",", s.VentedKg.Select(m => $"{m.Material}={m.Kg:R}")) + "|" + s.IndustrialWasteHeatKWh.ToString("R")
                       + "|" + string.Join(",", s.MaterialsKg.Select(m => $"{m.Material}={m.Kg:R}"));
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void Slag_CanReplaceRock_InABill_WhenTheColonyHasSlagAndNoRock()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            sim.Debug.UnlockAll = true;
            int t = sim.BaseTileId;
            sim.Materials.Clear();
            foreach (var (m, kg) in sim.BuildBill(BuildingType.StorageYard).Items())
                if (m != Material.Rock) sim.Materials.Add(m, kg);
            sim.Materials.Add(Material.Slag, 400.0);
            Assert.IsTrue(sim.PlaceStorage(BuildingType.StorageYard, t, 0));
            sim.Tick(0.25);
            Assert.AreNotEqual(StandIn.None, sim.StandInsAt(t, 0) & StandIn.SlagForRock, "the site takes slag for rock");
            for (int i = 0; i < 400 && sim.IsUnderConstruction(t, 0); i++) sim.Tick(1.0);
            Assert.IsFalse(sim.IsUnderConstruction(t, 0), "built from slag");
            Assert.AreEqual(100.0, sim.Materials[Material.Slag], 1.0, "300 kg of slag went into the yard in place of 300 kg of rock");
        }
    }
}
