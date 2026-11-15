using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>
    /// Barriers (director 2026-11-15): canyons and mountains block driving, building and mining until a tunnel, a blast
    /// or a bridge opens them; deep ore (rare earths) shows after a seismic survey.
    /// </summary>
    public class BarrierTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Rich = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(7, 25.0, false));
        static readonly PlanetSurface Dry = PlanetSurface.Generate(Sphere, SurfaceSettings.ForClimate(3, -60.0, false, liquidWater: false));
        static readonly PlanetSurface Plain = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        /// <summary>A colony landed next to a barrier of this ground (one robot), so trips are short.</summary>
        static PlanetSimulation Create(PlanetSurface surface, TerrainType nextTo = TerrainType.Mountain)
        {
            int landing = -1;
            for (int t = 0; t < Sphere.TileCount && landing < 0; t++)
                if (Terrains.LandingProblem(surface.Terrain[t]) == null && Sphere.Neighbors[t].Any(n => surface.Terrain[n] == nextTo))
                    landing = t;
            var sim = new PlanetSimulation(Sphere, surface, Earthlike, baseTileId: landing) { ConstructionTimeEnabled = false, PuzzleChance = 0.0 };
            sim.Workforce.Reset(sim.BaseTileId, 1);
            return sim;
        }

        /// <summary>The nearest barrier of this ground the base robots can reach the edge of.</summary>
        static int ReachableBarrier(PlanetSimulation sim, TerrainType ground)
        {
            double[] hours = sim.Routes.HoursFrom(sim.BaseTileId);
            int best = -1;
            for (int t = 0; t < sim.TileCount; t++)
                if (sim.GetTile(t).Terrain == ground && !double.IsInfinity(hours[t]) && (best < 0 || hours[t] < hours[best]))
                    best = t;
            return best;
        }

        [Test]
        public void RichPlanets_HaveCanyons_InLongRifts_OnLandOnly()
        {
            int canyons = 0, linked = 0;
            for (int t = 0; t < Sphere.TileCount; t++)
            {
                if (Rich.Terrain[t] != TerrainType.Canyon) continue;
                canyons++;
                if (Sphere.Neighbors[t].Any(n => Rich.Terrain[n] == TerrainType.Canyon)) linked++;
                Assert.GreaterOrEqual(Rich.Elevation[t], 0.0f, "a canyon floor is still land");
            }
            Assert.Greater(canyons, 0);
            Assert.Greater(linked, canyons * 0.7, "canyons run in rifts, not single holes");
            Assert.IsFalse(Plain.Terrain.Contains(TerrainType.Canyon), "plain test worlds have none");
            Assert.IsTrue(Terrains.IsBarrier(TerrainType.Canyon) && Terrains.IsBarrier(TerrainType.Mountain));
            Assert.IsFalse(Terrains.IsBarrier(TerrainType.Highland));
            Assert.IsNotNull(Terrains.LandingProblem(TerrainType.Canyon));
        }

        [Test]
        public void ABarrier_CanBeReachedButNotCrossed()
        {
            var sim = Create(Plain);
            int m = ReachableBarrier(sim, TerrainType.Mountain);
            Assert.GreaterOrEqual(m, 0);
            double[] hours = sim.Routes.HoursFrom(sim.BaseTileId);
            Assert.IsFalse(double.IsInfinity(hours[m]), "robots reach the mountain's edge");
            // No route goes through a closed mountain.
            for (int t = 0; t < sim.TileCount; t++)
            {
                var route = sim.Routes.Route(sim.BaseTileId, t);
                if (route == null) continue;
                for (int i = 0; i < route.Count - 1; i++)
                    Assert.IsTrue(sim.Routes.IsPassable(route[i]), $"route to {t} crosses closed tile {route[i]}");
            }
        }

        [Test]
        public void AClosedMountain_CantBeBuiltOnOrMined_UntilATunnelOpensIt()
        {
            var sim = Create(Plain);
            int m = ReachableBarrier(sim, TerrainType.Mountain);
            sim.Deposits.MarkSurveyed(m);
            Assert.IsFalse(sim.CanPlace(BuildingType.Relay, m, 0));
            Assert.IsFalse(sim.OrderMining(m, Material.Rock));
            Assert.IsFalse(sim.PreviewPanel(m, 0.3, Math.PI).CanPlace);
            StringAssert.Contains("tunnel", sim.PreviewPanel(m, 0.3, Math.PI).Reason);

            // A tunnel: little steel, many robot hours, and the cut rock comes out.
            sim.Materials.Clear();
            StringAssert.Contains("Missing", sim.PassageBlocker(m, PassageKind.Tunnel));
            foreach (var (mat, kg) in sim.PassageBill(m, PassageKind.Tunnel).Items()) sim.Materials.Add(mat, kg);
            Assert.IsNotNull(sim.PassageBlocker(m, PassageKind.Bridge), "a mountain isn't bridged");
            Assert.IsNull(sim.OrderPassage(m, PassageKind.Tunnel));
            Assert.AreEqual(0.0, sim.Materials[Material.Iron], 1e-9, "the steel is taken when ordered");
            Assert.AreEqual(PassageKind.Tunnel, sim.PassageOrdered(m));
            int opened = 0;
            sim.PassageOpened += (t, k) => { if (t == m) opened++; };
            for (int i = 0; i < 2000 && sim.PassageAt(m) == PassageKind.None; i++)
                sim.Tick(1.0);
            Assert.AreEqual(PassageKind.Tunnel, sim.PassageAt(m), $"hours {sim.Routes.HoursFrom(sim.BaseTileId)[m]} progress {sim.PassageProgress(m)} robot {sim.Workforce.Robots[0].Condition} task {sim.Workforce.Robots[0].Task} state {sim.Workforce.Robots[0].State}");
            Assert.AreEqual(1, opened);
            Assert.GreaterOrEqual(sim.Materials[Material.Rock], Passages.MuckKg(PassageKind.Tunnel, sim.Deposits.HasCave(m)) - 1e-6);
            Assert.IsTrue(sim.IsOpenGround(m));
            Assert.IsTrue(sim.Routes.IsPassable(m));
            Assert.IsTrue(sim.OrderMining(m, Material.Rock));
        }

        [Test]
        public void Blasting_IsQuickButCostly_CancellingGivesTheMaterialsBack()
        {
            var sim = Create(Plain);
            int m = ReachableBarrier(sim, TerrainType.Mountain);
            Assert.Less(Passages.WorkHours(PassageKind.Blast, false), Passages.WorkHours(PassageKind.Tunnel, false));
            Assert.Greater(Passages.Bill(PassageKind.Blast, false).TotalKg, Passages.Bill(PassageKind.Tunnel, false).TotalKg);
            Assert.Less(Passages.Bill(PassageKind.Tunnel, true).TotalKg, Passages.Bill(PassageKind.Tunnel, false).TotalKg, "a cave helps");
            sim.Materials.Clear();
            foreach (var (mat, kg) in sim.PassageBill(m, PassageKind.Blast).Items()) sim.Materials.Add(mat, kg);
            Assert.IsNull(sim.OrderPassage(m, PassageKind.Blast));
            Assert.IsTrue(sim.CancelPassage(m));
            Assert.AreEqual(sim.PassageBill(m, PassageKind.Blast)[Material.Iron], sim.Materials[Material.Iron], 1e-9);
            Assert.AreEqual(PassageKind.None, sim.PassageOrdered(m));
        }

        [Test]
        public void ACanyon_IsBridged()
        {
            var sim = Create(Rich, TerrainType.Canyon);
            int c = ReachableBarrier(sim, TerrainType.Canyon);
            Assume.That(c >= 0, "a canyon the base can reach");
            CollectionAssert.AreEqual(new[] { PassageKind.Bridge }, Passages.For(TerrainType.Canyon));
            Assert.IsNotNull(sim.PassageBlocker(c, PassageKind.Tunnel));
            sim.Materials.Clear();
            foreach (var (mat, kg) in sim.PassageBill(c, PassageKind.Bridge).Items()) sim.Materials.Add(mat, kg);
            Assert.IsNull(sim.OrderPassage(c, PassageKind.Bridge));
            for (int i = 0; i < 2000 && sim.PassageAt(c) == PassageKind.None; i++)
                sim.Tick(1.0);
            Assert.AreEqual(PassageKind.Bridge, sim.PassageAt(c));
            Assert.IsTrue(sim.Routes.IsPassable(c));
        }

        [Test]
        public void Passages_AreSaved_AndOldSavesKeepUsedMountainsOpen()
        {
            var a = Create(Plain);
            int m = ReachableBarrier(sim: a, ground: TerrainType.Mountain);
            a.OpenForTests(m, PassageKind.Blast);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var b = Create(Plain);
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(PassageKind.Blast, b.PassageAt(m));
            Assert.IsTrue(b.Routes.IsPassable(m));

            // A version-32 save with a mining order on a mountain: that mountain stays usable.
            SimulationState old = a.CaptureState();
            old.Version = 32;
            old.Passages = null;
            old.MiningOrders.Add(new MiningOrderRecord { Tile = m, Material = Material.Rock });
            var c = Create(Plain);
            Assert.IsTrue(c.RestoreState(old));
            Assert.IsTrue(c.IsOpenGround(m));
            Assert.IsTrue(c.IsMiningOrdered(m, Material.Rock));
        }

        [Test]
        public void RareEarths_AreDeepOre_ShownOnlyAfterASeismicSurvey()
        {
            var map = new DepositMap(Sphere, Rich, PlanetChemistry.EarthLike, 7);
            int ree = Enumerable.Range(0, map.TileCount).FirstOrDefault(t => map.GradeOf(t, Material.RareEarth) > 0.0);
            Assert.Greater(map.GradeOf(ree, Material.RareEarth), 0.0, "some tile has rare earths");
            for (int t = 0; t < map.TileCount; t++)
                if (map.GradeOf(t, Material.RareEarth) > 0.0)
                    Assert.IsTrue(DepositMap.IsDeepGround(Rich.Terrain[t]), $"rare earths on {Rich.Terrain[t]}");
            map.MarkSurveyed(ree);
            Assert.IsFalse(map.IsKnown(ree, Material.RareEarth), "a surface survey doesn't see deep ore");
            Assert.IsFalse(map.Known(ree).Any(d => d.Material == Material.RareEarth));
            Assert.IsFalse(SurveyPuzzle.Make(ree, map.At(ree)).Materials.Contains(Material.RareEarth), "not in the surface cores");
            map.DeepScanned = true;
            Assert.IsTrue(map.IsKnown(ree, Material.RareEarth));
            Assert.Less(DepositMap.ReserveKgAtGrade1(Material.RareEarth), DepositMap.ReserveKgAtGrade1(Material.Lithium), "rarer than lithium");
        }

        [Test]
        public void Experiments_NeedTheStationsTileSurveyed_SeismicNeedsGravity()
        {
            var sim = Create(Plain);
            int t = sim.BaseTileId;
            foreach (var (m, kg) in sim.BuildBill(BuildingType.ResearchStation).Items()) sim.Materials.Add(m, kg);
            int slot = Enumerable.Range(0, sim.SlotCount(t)).First(s => sim.CanPlace(BuildingType.ResearchStation, t, s));
            Assert.IsTrue(sim.PlaceResearchStation(t, slot));
            for (int i = 0; i < 50 && !sim.HasResearchStation; i++) sim.Tick(0.5);
            Assert.IsTrue(sim.HasResearchStation);
            StringAssert.Contains("Survey", sim.ExperimentBlocker(ExperimentKind.Gravity));
            Assert.IsFalse(sim.OrderExperiment(ExperimentKind.Gravity));
            sim.Deposits.MarkSurveyed(t);
            Assert.IsNull(sim.ExperimentBlocker(ExperimentKind.Gravity));
            StringAssert.Contains("gravity", sim.ExperimentBlocker(ExperimentKind.SeismicSurvey));
            sim.LearnExperimentForTests(ExperimentKind.Gravity);
            Assert.IsTrue(sim.OrderExperiment(ExperimentKind.SeismicSurvey));
            for (int i = 0; i < 400 && !sim.IsExperimentKnown(ExperimentKind.SeismicSurvey); i++) sim.Tick(0.5);
            Assert.IsTrue(sim.IsExperimentKnown(ExperimentKind.SeismicSurvey));
            Assert.IsTrue(sim.Deposits.DeepScanned);

            var b = Create(Plain);
            Assert.IsTrue(b.RestoreState(sim.CaptureState()));
            Assert.IsTrue(b.Deposits.DeepScanned, "kept in saves");
        }

        [Test]
        public void FasterDrive_NeedsRareEarthMagnets()
        {
            Assert.Greater(RobotTech.Upgrade(RobotModule.FastDrive, false, false).Inputs.Where(i => i.Material == Material.RareEarth).Sum(i => i.Kg), 0.0);
        }
    }
}
