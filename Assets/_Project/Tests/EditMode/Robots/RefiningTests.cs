using System;
using System.Linq;
using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>
    /// Refining in real steps (director 2026-11-15): alumina, then electrolysis; salt to sodium; refinery modules
    /// touching the Workshop, one batch each, days to weeks.
    /// </summary>
    public class RefiningTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        const int WorkshopSlot = 5;

        /// <summary>A colony with a finished Workshop and enough power that refining never pauses.</summary>
        static PlanetSimulation WithWorkshop()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { ConstructionTimeEnabled = false };
            sim.Workforce.Reset(sim.BaseTileId, 1);
            foreach (var (m, kg) in sim.BuildBill(BuildingType.Workshop).Items()) sim.Materials.Add(m, kg);
            Assert.IsTrue(sim.PlaceWorkshop(sim.BaseTileId, WorkshopSlot));
            for (int i = 0; i < 400 && sim.WorkshopCount == 0; i++) sim.Tick(0.25);
            Assert.AreEqual(1, sim.WorkshopCount);
            sim.RefineryPowerForTests = true;   // steady power for these tests
            sim.Materials.Clear();
            return sim;
        }

        /// <summary>Slots touching the Workshop, and one far from it.</summary>
        static (int[] Touching, int Far) Slots(PlanetSimulation sim, BuildingType type)
        {
            int t = sim.BaseTileId;
            var touching = Enumerable.Range(0, sim.SlotCount(t)).Where(s => sim.GetBuilding(t, s) == BuildingType.None && sim.RefineryBlocker(type, t, s) == null).ToArray();
            int far = Enumerable.Range(0, sim.SlotCount(t)).First(s => sim.GetBuilding(t, s) == BuildingType.None && sim.RefineryBlocker(type, t, s) != null);
            return (touching, far);
        }

        static void Build(PlanetSimulation sim, BuildingType type, int slot)
        {
            foreach (var (m, kg) in sim.BuildBill(type).Items()) sim.Materials.Add(m, kg);
            Assert.IsTrue(sim.PlaceRefinery(type, sim.BaseTileId, slot));
            for (int i = 0; i < 400 && sim.IsUnderConstruction(sim.BaseTileId, slot); i++) sim.Tick(0.25);
            Assert.IsFalse(sim.IsUnderConstruction(sim.BaseTileId, slot));
            sim.Materials.Clear();
        }

        static void Run(PlanetSimulation sim, double hours)
        {
            for (double h = 0.0; h < hours; h += 1.0) sim.Tick(1.0);
        }

        [Test]
        public void TheChemistry_IsReal()
        {
            // Al₂O₃ is 52.9 % aluminium: 1.889 kg alumina per kg at best; pot lines use about 1.93.
            Assert.AreEqual(1.889, 101.96 / (2.0 * 26.98), 0.001);
            Assert.GreaterOrEqual(Refining.AluminaPerAluminium, 101.96 / (2.0 * 26.98));
            Assert.AreEqual(2.54, Refining.SaltPerSodium, 0.01, "NaCl / Na = 58.44 / 22.99");
            Assert.That(Refining.SmeltWhPerKg, Is.InRange(13_000.0, 15_000.0), "Hall–Héroult 13–15 kWh/kg");
            Assert.That(Refining.SodiumWhPerKg, Is.InRange(9_000.0, 11_000.0), "Downs cell about 10 kWh/kg");
            foreach (RefineRecipe r in Refining.All)
            {
                Assert.AreEqual(r.InputKg, r.OutputKg + r.SlagKg + r.VentedKg, 1e-6, $"{r.Name}: mass is kept");
                Assert.That(r.Hours, Is.InRange(48.0, 24.0 * 21.0), $"{r.Name}: days to weeks");
            }
            // Anorthosite holds about twice the alumina of ordinary rock, so it takes about half as much.
            Assert.Less(Refining.Of(RefineProcess.AluminaFromAnorthosite).InputKg, Refining.Of(RefineProcess.AluminaFromRock).InputKg * 0.6);
        }

        [Test]
        public void Modules_MustTouchTheWorkshop()
        {
            var sim = WithWorkshop();
            var (touching, far) = Slots(sim, BuildingType.AluminaPlant);
            Assert.IsNotEmpty(touching);
            foreach (var (m, kg) in sim.BuildBill(BuildingType.AluminaPlant).Items()) sim.Materials.Add(m, kg);
            Assert.IsFalse(sim.PlaceRefinery(BuildingType.AluminaPlant, sim.BaseTileId, far));
            StringAssert.Contains("Workshop", sim.RefineryBlocker(BuildingType.AluminaPlant, sim.BaseTileId, far));
            Assert.IsTrue(sim.PlaceRefinery(BuildingType.AluminaPlant, sim.BaseTileId, touching[0]));
            Assert.AreEqual(BuildingType.AluminaPlant, sim.GetBuilding(sim.BaseTileId, touching[0]));
        }

        [Test]
        public void Aluminium_TakesTwoSteps_AndAboutTenDays()
        {
            var sim = WithWorkshop();
            var (touching, _) = Slots(sim, BuildingType.AluminaPlant);
            Build(sim, BuildingType.AluminaPlant, touching[0]);
            Build(sim, BuildingType.Electrolyser, touching[1]);

            Assert.IsNotNull(sim.RefineBlocker(RefineProcess.Aluminium), "no alumina yet");
            RefineRecipe s1 = Refining.Of(RefineProcess.AluminaFromAnorthosite), s2 = Refining.Of(RefineProcess.Aluminium);
            sim.Materials.Add(Material.Anorthosite, s1.InputKg * 2);
            Assert.IsNull(sim.OrderRefine(RefineProcess.AluminaFromAnorthosite));
            Assert.IsNull(sim.OrderRefine(RefineProcess.Aluminium));
            Assert.AreEqual(s1.InputKg, sim.Materials[Material.Anorthosite], 1e-6, "the first batch took its input when it started");
            Run(sim, s1.Hours - 2.0);
            Assert.AreEqual(0.0, sim.Materials[Material.Alumina], 1e-9, "not before its time");
            Run(sim, 3.0);
            Assert.AreEqual(s1.SlagKg, sim.Materials[Material.Slag], 1e-6, "the rest of the rock is slag");
            // The queued electrolysis starts as soon as there is alumina, and takes what a batch needs.
            Assert.AreEqual(s1.OutputKg - s2.InputKg, sim.Materials[Material.Alumina], 1e-6);
            Assert.IsTrue(sim.RefineryLines().Any(l => l.Process == RefineProcess.Aluminium));
            Run(sim, s2.Hours + 2.0);
            Assert.AreEqual(s2.OutputKg, sim.Materials[Material.Aluminium], 1e-6);
            Assert.AreEqual(s1.OutputKg - s2.InputKg, sim.Materials[Material.Alumina], 1e-6);
            Assert.IsTrue(sim.IndustrialReports.Any(r => r.Message.Contains("oxygen")));
        }

        [Test]
        public void MoreModules_MakeMoreAtOnce_AndAutoKeepsStock()
        {
            var sim = WithWorkshop();
            var (touching, _) = Slots(sim, BuildingType.Electrolyser);
            Build(sim, BuildingType.Electrolyser, touching[0]);
            Build(sim, BuildingType.Electrolyser, touching[1]);
            RefineRecipe r = Refining.Of(RefineProcess.SodiumFromSalt);
            sim.Materials.Add(Material.Salt, r.InputKg * 10);
            sim.SetAutoRefine(RefineProcess.SodiumFromSalt, r.OutputKg * 2);
            Run(sim, 1.0);
            Assert.AreEqual(2, sim.RefineryLines().Count(l => l.Process == RefineProcess.SodiumFromSalt), "both cells run");
            Assert.Greater(sim.RefineryPowerW, 0.0);
            Run(sim, r.Hours + 2.0);
            Assert.AreEqual(r.OutputKg * 2, sim.Materials[Material.Sodium], 1e-6);
            Assert.AreEqual(0, sim.RefineryLines().Count(l => l.Process.HasValue), "the stock is reached: auto stops");
            sim.SetAutoRefine(RefineProcess.SodiumFromSalt, 0.0);
            Assert.AreEqual(0.0, sim.AutoRefineTarget(RefineProcess.SodiumFromSalt));
        }

        [Test]
        public void Batches_PauseInABrownout()
        {
            var sim = WithWorkshop();
            var (touching, _) = Slots(sim, BuildingType.Electrolyser);
            Build(sim, BuildingType.Electrolyser, touching[0]);
            RefineRecipe r = Refining.Of(RefineProcess.SodiumFromSalt);
            sim.Materials.Add(Material.Salt, r.InputKg);
            sim.OrderRefine(RefineProcess.SodiumFromSalt);
            Run(sim, 10.0);
            double before = sim.RefineryLines().First(l => l.Process.HasValue).Progress;
            sim.RefineryPowerForTests = false;   // a brownout
            Run(sim, 10.0);
            Assert.AreEqual(before, sim.RefineryLines().First(l => l.Process.HasValue).Progress, 1e-12, "no progress in a brownout");
            Assert.AreEqual(0.0, sim.RefineryPowerW);
        }

        [Test]
        public void Refining_IsSaved_AndOldWorkshopRefineJobsGiveTheirRockBack()
        {
            var a = WithWorkshop();
            var (touching, _) = Slots(a, BuildingType.Electrolyser);
            Build(a, BuildingType.Electrolyser, touching[0]);
            RefineRecipe r = Refining.Of(RefineProcess.SodiumFromSalt);
            a.Materials.Add(Material.Salt, r.InputKg * 2);
            a.OrderRefine(RefineProcess.SodiumFromSalt);
            a.OrderRefine(RefineProcess.SodiumFromSalt);
            a.SetAutoRefine(RefineProcess.PureAluminium, 5.0);
            Run(a, 10.0);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(34, state.Version);
            var b = WithWorkshop();
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(BuildingType.Electrolyser, b.GetBuilding(b.BaseTileId, touching[0]));
            var line = b.RefineryLines().First();
            Assert.AreEqual(RefineProcess.SodiumFromSalt, line.Process);
            Assert.AreEqual(10.0 / r.Hours, line.Progress, 0.02);
            Assert.AreEqual(1, b.RefineQueue.Count);
            Assert.AreEqual(5.0, b.AutoRefineTarget(RefineProcess.PureAluminium));

            SimulationState old = a.CaptureState();
            old.Version = 33;
            old.Jobs.Add(new KardashevAscent.Simulation.Robots.JobRecord { Kind = KardashevAscent.Simulation.Robots.JobKind.Refine, Output = Material.Aluminium, OutputKg = 10.0 });
            double rock = old.MaterialsKg.Where(m => m.Material == Material.Rock).Sum(m => m.Kg);
            var c = WithWorkshop();
            Assert.IsTrue(c.RestoreState(old));
            Assert.AreEqual(0, c.Jobs.Count);
            Assert.Greater(c.Materials[Material.Rock], rock, "the old job's rock comes back");
        }

        [Test]
        public void SaltAndAnorthosite_AreWhereGeologySaysAndSeaSaltNeverRunsOut()
        {
            var wet = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(7, 25.0, false));
            var map = new DepositMap(Sphere, wet, PlanetChemistry.EarthLike, 7);
            int anor = 0, shore = 0;
            for (int t = 0; t < map.TileCount; t++)
            {
                if (map.GradeOf(t, Material.Anorthosite) > 0.0)
                {
                    anor++;
                    Assert.IsTrue(wet.Terrain[t] == TerrainType.Highland || wet.Terrain[t] == TerrainType.Mountain);
                }
                if (map.GradeOf(t, Material.Salt) > 0.0 && map.IsEndless(t, Material.Salt)) shore++;
            }
            Assert.Greater(anor, 0);
            Assert.Greater(shore, 0, "sea salt on shores");
            var dry = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(3, -60.0, false, liquidWater: false));
            var noWater = new DepositMap(Sphere, dry, new PlanetChemistry(0.0, 0.32, false, false), 3);
            for (int t = 0; t < noWater.TileCount; t++)
                Assert.AreEqual(0.0, noWater.GradeOf(t, Material.Salt), "no seas ever: no salt (sodium from rock instead)");
        }
    }
}
