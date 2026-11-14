using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Resources
{
    /// <summary>Deposits per planet (geology_resources.md claims 1, 2, 10), surveys and mining (phase 3a).</summary>
    public class ResourceTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Wet = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSurface Dry = PlanetSurface.Generate(Sphere, new SurfaceSettings(7, liquidWater: false));

        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static int Count(DepositMap map, Material m)
        {
            int n = 0;
            for (int t = 0; t < map.TileCount; t++)
                if (map.GradeOf(t, m) > 0.0) n++;
            return n;
        }

        // ---- Chemistry ----

        [Test]
        public void MetalFactor_Is10ToTheMetallicity()
        {
            Assert.AreEqual(1.0, PlanetChemistry.EarthLike.MetalFactor, 1e-12);
            Assert.AreEqual(Math.Pow(10.0, 0.21), new PlanetChemistry(0.21, 0.32, false, false).MetalFactor, 1e-12);
            Assert.AreEqual(1.6218, new PlanetChemistry(0.21, 0.32, false, false).MetalFactor, 1e-4, "Kepler-452: [Fe/H] +0.21");
        }

        [Test]
        public void IronFactor_UsesTheMeasuredFraction_ElseTheStar()
        {
            Assert.AreEqual(0.21 / 0.32, new PlanetChemistry(0.052, 0.21, true, true).IronFactor, 1e-12, "TRAPPIST-1: Agol 2021");
            Assert.AreEqual(Math.Pow(10.0, 0.21), new PlanetChemistry(0.21, 0.32, false, false).IronFactor, 1e-12);
        }

        [Test]
        public void PlanetRecord_ChemistryFromJson()
        {
            var r = new PlanetRecord();
            r.chemistry.metallicityDex = 0.052;
            r.chemistry.ironMassFraction = 0.21;
            r.provenance.Add(new PlanetRecord.ProvenanceRecord { field = "chemistry.ironMassFraction", origin = "derived" });
            PlanetChemistry c = r.ToChemistry();
            Assert.IsTrue(c.IronMeasured);
            Assert.AreEqual(0.21 / 0.32, c.IronFactor, 1e-12);
            Assert.IsTrue(c.WaterHistory);
            r.look.water = "none";
            Assert.IsFalse(r.ToChemistry().WaterHistory);
        }

        // ---- Deposit rules ----

        [Test]
        public void SeaTiles_HoldNothing_LandHasRockAndIron()
        {
            var map = new DepositMap(Sphere, Wet, PlanetChemistry.EarthLike, 7);
            for (int t = 0; t < map.TileCount; t++)
            {
                bool water = Terrains.IsWater(Wet.Terrain[t]);
                Assert.AreEqual(water ? 0.0 : 1.0, map.GradeOf(t, Material.Rock), "rock on land only");
                Assert.AreEqual(!water, map.GradeOf(t, Material.Iron) > 0.0);
            }
        }

        [Test]
        public void Grades_StayWithinTheSpread_AndScaleWithTheStar()
        {
            var earth = new DepositMap(Sphere, Wet, PlanetChemistry.EarthLike, 7);
            var rich = new DepositMap(Sphere, Wet, new PlanetChemistry(0.21, 0.32, false, true), 7);
            for (int t = 0; t < earth.TileCount; t++)
            {
                double g = earth.GradeOf(t, Material.Iron);
                if (g <= 0.0) continue;
                Assert.GreaterOrEqual(g, 1.0 - DepositMap.Spread - 1e-12);
                Assert.Less(g, 1.0 + DepositMap.Spread);
                Assert.AreEqual(g * Math.Pow(10.0, 0.21), rich.GradeOf(t, Material.Iron), 1e-9);
            }
        }

        [Test]
        public void WetPlanet_HasCopperAndLithium_DryPlanetHasNeither()
        {
            var wet = new DepositMap(Sphere, Wet, PlanetChemistry.EarthLike, 7);
            Assert.Greater(Count(wet, Material.Copper), 0);
            Assert.Greater(Count(wet, Material.Lithium), 0);
            Assert.Greater(Count(wet, Material.Water), 0);
            var dry = new DepositMap(Sphere, Dry, new PlanetChemistry(0.0, 0.32, false, false), 7);
            for (int t = 0; t < dry.TileCount; t++)
                Assert.LessOrEqual(dry.GradeOf(t, Material.Copper), DepositMap.SulfideCopperGrade * (1.0 + DepositMap.Spread),
                                   "dry planets get only the poorer magmatic copper (no arcs without water, claim 1)");
            Assert.AreEqual(0, Count(dry, Material.Lithium), "lithium brines need a water history (claim 2)");
        }

        [Test]
        public void Copper_OnlyInHighGround_RicherNearTheSea()
        {
            var map = new DepositMap(Sphere, Wet, PlanetChemistry.EarthLike, 7);
            int rich = 0;
            for (int t = 0; t < map.TileCount; t++)
            {
                double g = map.GradeOf(t, Material.Copper);
                if (g <= 0.0) continue;
                TerrainType here = Wet.Terrain[t];
                Assert.IsTrue(here == TerrainType.Highland || here == TerrainType.Mountain);
                if (g > DepositMap.SulfideCopperGrade * (1.0 + DepositMap.Spread)) rich++;
            }
            Assert.Greater(rich, 0, "arc copper near the sea");
        }

        [Test]
        public void EveryPlanet_HasSomeCopper_OnTheRealMapSize()
        {
            var sphere = HexSphere.Create(24);
            var wet = PlanetSurface.Generate(sphere, SurfaceSettings.ForClimate(7, 25.0, false, true, 0.62));
            var dry = PlanetSurface.Generate(sphere, SurfaceSettings.ForClimate(7, -8.0, false, false, 0.45));
            int Cu(PlanetSurface s, bool water)
            {
                var m = new DepositMap(sphere, s, new PlanetChemistry(0.0, 0.32, false, water), 7);
                int n = 0;
                for (int t = 0; t < m.TileCount; t++) if (m.GradeOf(t, Material.Copper) > 0.0) n++;
                return n;
            }
            Assert.Greater(Cu(wet, true), 20, "an Earth-like planet has plenty of copper sites");
            Assert.Greater(Cu(dry, false), 0, "a dry planet has a few poorer ones");
        }

        [Test]
        public void SameSeed_SameDeposits()
        {
            var a = new DepositMap(Sphere, Wet, PlanetChemistry.EarthLike, 7);
            var b = new DepositMap(Sphere, Wet, PlanetChemistry.EarthLike, 7);
            for (int t = 0; t < a.TileCount; t++)
                Assert.AreEqual(a.GradeOf(t, Material.Copper), b.GradeOf(t, Material.Copper));
        }

        [Test]
        public void OnlyRockIsKnownBeforeASurvey()
        {
            var map = new DepositMap(Sphere, Wet, PlanetChemistry.EarthLike, 7);
            Assert.IsTrue(map.IsKnown(3, Material.Rock));
            Assert.IsFalse(map.IsKnown(3, Material.Iron));
            map.MarkSurveyed(3);
            Assert.IsTrue(map.IsKnown(3, Material.Iron));
        }

        [Test]
        public void Stock_TakesOnlyWhatItHas()
        {
            var s = new MaterialStock();
            s.Add(Material.Iron, 10.0);
            Assert.IsFalse(s.TryTake(Material.Iron, 11.0));
            Assert.AreEqual(10.0, s[Material.Iron]);
            Assert.IsTrue(s.TryTake(Material.Iron, 4.0));
            Assert.AreEqual(6.0, s[Material.Iron], 1e-12);
        }

        // ---- Survey and mining in the colony ----

        /// <summary>One robot and no lander cargo, so amounts mined are easy to follow.</summary>
        static PlanetSimulation Create()
        {
            var sim = new PlanetSimulation(Sphere, Wet, Earthlike);
            sim.Workforce.Reset(sim.BaseTileId, 1);
            sim.Materials.Clear();
            return sim;
        }

        static int LandTileWith(PlanetSimulation sim, Material m)
        {
            for (int t = 0; t < sim.TileCount; t++)
                if (sim.Deposits.GradeOf(t, m) > 0.0) return t;
            return -1;
        }

        [Test]
        public void Survey_TakesTheRobotTwoHours_ThenRevealsTheTile()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            int seen = -1;
            sim.TileSurveyed += x => seen = x;
            Assert.IsTrue(sim.OrderSurvey(t));
            Assert.IsFalse(sim.OrderSurvey(t), "already ordered");
            Assert.AreEqual(TaskKind.Survey, sim.Workforce.Robots[0].Task.Value.Kind);
            sim.Tick(1.0);
            Assert.AreEqual(0.5, sim.SurveyProgress(t), 1e-9);
            Assert.IsFalse(sim.Deposits.IsSurveyed(t));
            sim.Tick(1.0);
            Assert.IsFalse(sim.IsSurveyOrdered(t));
            Assert.IsNull(sim.Workforce.Robots[0].Task, "free again");
            Assert.AreEqual(1, sim.SurveyPuzzles.Count, "the cores are in; the player estimates the grades (stage F)");
            Assert.IsFalse(sim.Deposits.IsSurveyed(t), "not until the estimate is in");
            Assert.IsFalse(sim.OrderSurvey(t), "cores already waiting");
            PuzzleSolving.SolveSurvey(sim, t);
            Assert.IsTrue(sim.Deposits.IsSurveyed(t));
            Assert.AreEqual(t, seen);
            Assert.IsFalse(sim.OrderSurvey(t), "already surveyed");
        }

        [Test]
        public void Survey_RefusesTheSea()
        {
            var sim = Create();
            for (int t = 0; t < sim.TileCount; t++)
                if (sim.GetTile(t).IsWater) { Assert.IsFalse(sim.OrderSurvey(t)); return; }
        }

        [Test]
        public void Mining_NeedsAKnownDeposit_AddsMaterialEachBatch()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.IsFalse(sim.OrderMining(t, Material.Iron), "not surveyed yet");
            Assert.IsTrue(sim.OrderMining(t, Material.Rock), "rock is always known");
            sim.Tick(1.0);
            Assert.AreEqual(RobotSpecs.MiningKgPerHour * RobotSpecs.MiningBatchHours, sim.Materials[Material.Rock], 1e-9);
            sim.Tick(2.0);
            Assert.AreEqual(3.0 * RobotSpecs.MiningKgPerHour, sim.Materials[Material.Rock], 1e-9, "it keeps going");
            Assert.IsTrue(sim.CancelMining(t, Material.Rock));
            sim.Tick(1.0);
            Assert.AreEqual(3.0 * RobotSpecs.MiningKgPerHour, sim.Materials[Material.Rock], 1e-9);
        }

        [Test]
        public void Mining_YieldFollowsTheGrade()
        {
            var sim = Create();
            int t = sim.BaseTileId;   // every land tile has iron; the base needs no drive
            sim.Deposits.MarkSurveyed(t);
            Assert.IsTrue(sim.OrderMining(t, Material.Iron));
            sim.Tick(1.0);
            Assert.AreEqual(RobotSpecs.MiningKgPerHour * sim.Deposits.GradeOf(t, Material.Iron), sim.Materials[Material.Iron], 1e-9);
        }

        [Test]
        public void MiningBatches_LetConstructionGoFirst()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.OrderMining(t, Material.Rock);
            sim.Tick(0.5);
            sim.Workforce.Pause(sim.Workforce.Robots[0].GroupId);
            foreach (var (m, kg) in sim.BuildBill(BuildingType.Battery).Items()) sim.Materials.Add(m, kg);
            sim.PlaceBattery(t, 0);
            sim.Workforce.Resume(sim.Workforce.Robots[0].GroupId);
            sim.Tick(0.5);   // the batch ends; the robot re-evaluates and builds (Build ranks before Mine)
            Assert.IsFalse(sim.IsUnderConstruction(t, 0));
        }

        [Test]
        public void Specialists_WorkTwiceAsFast()
        {
            Assert.AreEqual(1.0, RobotAbilities.Speed(RobotKind.General, TaskKind.Mine));
            Assert.AreEqual(RobotSpecs.SpecialistSpeed, RobotAbilities.Speed(RobotKind.Mining, TaskKind.Mine));
            Assert.AreEqual(0.0, RobotAbilities.Speed(RobotKind.Mining, TaskKind.Survey));
            Assert.IsTrue(RobotAbilities.CanDo(RobotKind.Research, TaskKind.Survey));
        }

        [Test]
        public void Save_KeepsSurveysOrdersMaterialsAndWork()
        {
            var a = Create();
            int t = a.BaseTileId;
            a.OrderSurvey(t);
            a.Tick(2.0);
            PuzzleSolving.SolveSurvey(a, t);
            int other = LandTileWith(a, Material.Iron) == t ? t + 1 : LandTileWith(a, Material.Iron);
            a.OrderMining(t, Material.Iron);
            a.Tick(1.5);
            if (!a.GetTile(other).IsWater) a.OrderSurvey(other);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);

            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.IsTrue(b.Deposits.IsSurveyed(t));
            Assert.IsTrue(b.IsMiningOrdered(t, Material.Iron));
            Assert.AreEqual(a.Materials[Material.Iron], b.Materials[Material.Iron], 1e-9);
            Assert.AreEqual(a.IsSurveyOrdered(other), b.IsSurveyOrdered(other));
            b.Tick(0.5);
            a.Tick(0.5);
            Assert.AreEqual(a.Materials[Material.Iron], b.Materials[Material.Iron], 1e-9, "the half-done batch carried over");
        }

        [Test]
        public void SaveTypesAreSerializable()
        {
            Assert.IsTrue(typeof(MiningOrderRecord).IsSerializable);
            Assert.IsTrue(typeof(MaterialRecord).IsSerializable);
            Assert.IsTrue(typeof(FieldWorkRecord).IsSerializable);
            Assert.IsTrue(typeof(PlanetRecord.ChemistryRecord).IsSerializable);
        }
    }
}
