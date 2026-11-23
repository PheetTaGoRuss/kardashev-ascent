using System;
using System.Linq;
using KardashevAscent.Simulation.Electrical;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    /// <summary>AC on the planet grid (KI-44): AC lines, converters, the media reach, the conversion loss, the milestone, the save.</summary>
    public class AcGridTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        const int Ac33 = VoltageLadder.Ac33k, Ac500 = VoltageLadder.Ac500k;

        static PlanetSimulation Colony(out int t1, out int t2, out int t3)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            sim.Debug.FreeBuilding = true;
            sim.Debug.UnlockAll = true;
            int B = sim.BaseTileId;
            int one = Sphere.Neighbors[B].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            int two = Sphere.Neighbors[one].First(n => n != B && !Sphere.Neighbors[B].Contains(n) && !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            int three = Sphere.Neighbors[two].First(n => n != one && !Sphere.Neighbors[one].Contains(n) && n != B && !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            t1 = one; t2 = two; t3 = three;
            return sim;
        }

        static void Panels(PlanetSimulation sim, int tile, int count = 6)
        {
            for (int slot = 1; slot <= count; slot++) sim.PlacePanel(tile, slot, 0.35, Math.PI);
            sim.WireAllBuildings();
        }

        static void Run(PlanetSimulation sim, double hours = 12.0) { for (double h = 0; h < hours; h += 1.0) sim.Tick(1.0); }

        [Test]
        public void TheAcBuildingsOpenAtTheAcMilestone()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            StringAssert.Contains("milestone 2", sim.KnowledgeBlocker(BuildingType.AcLine));
            StringAssert.Contains("milestone 2", sim.KnowledgeBlocker(BuildingType.Inverter));
            sim.Debug.UnlockAll = true;
            Assert.IsNull(sim.KnowledgeBlocker(BuildingType.Transformer));
            Assert.AreEqual(2, PlanetSimulation.AcMilestone);
        }

        [Test]
        public void AnInverterAndARectifierCarryGenerationOverAnAcLine()
        {
            var sim = Colony(out int t1, out int t2, out int t3);
            Assert.IsTrue(sim.PlaceWire(sim.BaseTileId, t1, false));
            Assert.IsTrue(sim.PlaceConverter(BuildingType.Rectifier, t1, 40));
            Assert.IsTrue(sim.PlaceAcWire(t1, t2, Ac500));
            Assert.IsTrue(sim.PlaceAcWire(t2, t3, Ac500));
            Assert.IsTrue(sim.PlaceConverter(BuildingType.Inverter, t3, 40));
            Panels(sim, t3);
            Run(sim, 5.0);
            Assert.IsTrue(sim.IsPowered(t3), "DC at the far end, through the AC line");
            Assert.IsFalse(sim.IsPowered(t2), "the middle tile is AC only: no DC bus there");
            Assert.Greater(sim.ConverterLossW, 0.0, "two conversions lose power as heat");
            Assert.AreEqual(0, sim.GeneratorsOffGrid);
        }

        [Test]
        public void WithoutTheInverterTheGeneratorsAreOffTheGrid()
        {
            var sim = Colony(out int t1, out int t2, out int t3);
            sim.PlaceWire(sim.BaseTileId, t1, false);
            sim.PlaceConverter(BuildingType.Rectifier, t1, 40);
            sim.PlaceAcWire(t1, t2, Ac500); sim.PlaceAcWire(t2, t3, Ac500);
            Panels(sim, t3);
            Run(sim, 2.0);
            Assert.IsFalse(sim.IsPowered(t3));
            Assert.Greater(sim.GeneratorsOffGrid, 0);
        }

        [Test]
        public void WithoutTheRectifierTheAcLineDoesNotJoinTheGrid()
        {
            var sim = Colony(out int t1, out int t2, out int t3);
            sim.PlaceWire(sim.BaseTileId, t1, false);
            sim.PlaceAcWire(t1, t2, Ac500); sim.PlaceAcWire(t2, t3, Ac500);
            sim.PlaceConverter(BuildingType.Inverter, t3, 40);
            Panels(sim, t3);
            Run(sim, 2.0);
            Assert.IsFalse(sim.IsPowered(t3));
        }

        [Test]
        public void AnInverterAtTheLanderEndDoesNotDoTheRectifiersJob()
        {
            // moving away from the lander the first DC-to-AC crossing needs a Rectifier; an Inverter there does not bridge
            var sim = Colony(out int t1, out int t2, out int t3);
            sim.PlaceWire(sim.BaseTileId, t1, false);
            sim.PlaceConverter(BuildingType.Inverter, t1, 40);
            sim.PlaceAcWire(t1, t2, Ac500); sim.PlaceAcWire(t2, t3, Ac500);
            sim.PlaceConverter(BuildingType.Inverter, t3, 40);
            Panels(sim, t3);
            Run(sim, 2.0);
            Assert.IsFalse(sim.IsPowered(t3));
        }

        [Test]
        public void ATransformerJoinsTheTwoAcClasses_AndWithoutOneTheyAreSeparate()
        {
            var sim = Colony(out int t1, out int t2, out int t3);
            sim.PlaceWire(sim.BaseTileId, t1, false);
            sim.PlaceConverter(BuildingType.Rectifier, t1, 40);
            sim.PlaceAcWire(t1, t2, Ac33);
            sim.PlaceAcWire(t2, t3, Ac500);
            sim.PlaceConverter(BuildingType.Inverter, t3, 40);
            Panels(sim, t3);
            Run(sim, 2.0);
            Assert.IsFalse(sim.IsPowered(t3), "the 33 kV and 500 kV lines meet at t2 with no transformer");
            Assert.IsTrue(sim.PlaceConverter(BuildingType.Transformer, t2, 40));
            Run(sim, 2.0);
            Assert.IsTrue(sim.IsPowered(t3));
        }

        [Test]
        public void TheConversionLossIsTheTwoConvertersEfficiencyAgainstAPlainDcLine()
        {
            double Delivered(bool ac)
            {
                var sim = Colony(out int t1, out int t2, out int t3);
                Assert.IsTrue(sim.PlaceWire(sim.BaseTileId, t1, false), sim.WireProblem);
                if (ac)
                {
                    Assert.IsTrue(sim.PlaceConverter(BuildingType.Rectifier, t1, 40));
                    Assert.IsTrue(sim.PlaceAcWire(t1, t2, Ac33), sim.WireProblem);
                    Assert.IsTrue(sim.PlaceAcWire(t2, t3, Ac33), sim.WireProblem);
                    Assert.IsTrue(sim.PlaceConverter(BuildingType.Inverter, t3, 40));
                }
                else
                {
                    Assert.IsTrue(sim.PlaceWire(t1, t2, false), sim.WireProblem);
                    Assert.IsTrue(sim.PlaceWire(t2, t3, false), sim.WireProblem);
                }
                // a panel here makes gigawatts and would saturate the line, so measure with the 1 MW probe of an empty tile
                Run(sim, 1.0);
                Assert.IsTrue(sim.IsPowered(t3), ac ? "ac" : "dc");
                return sim.GridEfficiency(t3);
            }
            double dc = Delivered(false), ac = Delivered(true);
            // the same 33 kV conductors, so the line loss is the same: the difference is the two converters, 0.985 x 0.985 = 0.9702
            Assert.AreEqual(0.985 * 0.985, ac / dc, 0.003);
        }

        [Test]
        public void TheFullChain_DcInverterAc33TransformerAc500TransformerAc33RectifierDc_DeliversTheProductOfTheEfficiencies()
        {
            // lander - plain DC - [rectifier] - AC33 - [transformer] - AC500 - [transformer] - AC33 - [inverter] - generator tile
            double Probe(bool ac)
            {
                var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
                sim.Debug.FreeBuilding = true; sim.Debug.UnlockAll = true;
                var path = new System.Collections.Generic.List<int> { sim.BaseTileId };
                while (path.Count < 6)
                {
                    int last = path[path.Count - 1];
                    int next = Sphere.Neighbors[last].First(n => !path.Contains(n) && !sim.GetTile(n).IsWater && sim.IsOpenGround(n)
                        && path.All(p => p == last || !Sphere.Neighbors[p].Contains(n)));
                    path.Add(next);
                }
                int b = path[0], t1 = path[1], t2 = path[2], t3 = path[3], t4 = path[4], gen = path[5];
                Assert.IsTrue(sim.PlaceWire(b, t1, false), sim.WireProblem);
                if (ac)
                {
                    Assert.IsTrue(sim.PlaceConverter(BuildingType.Rectifier, t1, 40));
                    Assert.IsTrue(sim.PlaceAcWire(t1, t2, Ac33), sim.WireProblem);
                    Assert.IsTrue(sim.PlaceConverter(BuildingType.Transformer, t2, 40));
                    Assert.IsTrue(sim.PlaceAcWire(t2, t3, Ac500), sim.WireProblem);
                    Assert.IsTrue(sim.PlaceConverter(BuildingType.Transformer, t3, 40));
                    Assert.IsTrue(sim.PlaceAcWire(t3, t4, Ac33), sim.WireProblem);
                    Assert.IsTrue(sim.PlaceAcWire(t4, gen, Ac33), sim.WireProblem);
                    Assert.IsTrue(sim.PlaceConverter(BuildingType.Inverter, gen, 40));
                }
                else
                {
                    Assert.IsTrue(sim.PlaceWire(t1, t2, false), sim.WireProblem);
                    Assert.IsTrue(sim.PlaceWire(t2, t3, false), sim.WireProblem);
                    Assert.IsTrue(sim.PlaceWire(t3, t4, false), sim.WireProblem);
                    Assert.IsTrue(sim.PlaceWire(t4, gen, false), sim.WireProblem);
                }
                Run(sim, 1.0);
                Assert.IsTrue(sim.IsPowered(gen), ac ? "the AC chain stands" : "the DC chain stands");
                if (ac) Assert.AreEqual(0.985 * 0.995 * 0.995 * 0.985, sim.ConversionEfficiencyFrom(gen), 1e-12, "rectifier 33 kV x two transformers x inverter 33 kV");
                else Assert.AreEqual(1.0, sim.ConversionEfficiencyFrom(gen), 1e-12);
                return sim.GridEfficiency(gen);
            }
            // the whole delivered share: the converters' product times the line loss; the 500 kV leg is a better line than 33 kV DC, so the
            // AC chain delivers a little more than the product of the efficiencies against the all-33-kV DC chain, and always less than the DC chain
            double ratio = Probe(true) / Probe(false);
            double product = 0.985 * 0.995 * 0.995 * 0.985;
            Assert.Greater(ratio, product);
            Assert.Less(ratio, 1.0);
        }

        [Test]
        public void AnAcSeaLinkLongerThanTheLimitIsRefusedAndTheReasonIsGiven()
        {
            var sim = Colony(out _, out _, out _);
            int sea = Enumerable.Range(0, sim.TileCount).FirstOrDefault(t => sim.GetTile(t).Terrain == TerrainType.ShallowOcean
                && Sphere.Neighbors[t].Any(n => !sim.GetTile(n).IsWater));
            if (sea == 0 && sim.GetTile(0).Terrain != TerrainType.ShallowOcean) Assert.Pass("no shallow sea next to land on this planet");
            int land = Sphere.Neighbors[sea].First(n => !sim.GetTile(n).IsWater);
            // a tile is hundreds of kilometres here, far over the 100 km limit
            Assert.IsFalse(sim.PlaceAcWire(land, sea, Ac500));
            StringAssert.Contains("direct current", sim.WireProblem);
        }

        [Test]
        public void AcLinesAndConvertersSurviveSaveAndLoad()
        {
            var sim = Colony(out int t1, out int t2, out int t3);
            sim.PlaceWire(sim.BaseTileId, t1, false);
            sim.PlaceConverter(BuildingType.Rectifier, t1, 40);
            sim.PlaceAcWire(t1, t2, Ac33); sim.PlaceAcWire(t2, t3, Ac500);
            sim.PlaceConverter(BuildingType.Transformer, t2, 40);
            sim.PlaceConverter(BuildingType.Inverter, t3, 40);
            Panels(sim, t3);
            Run(sim, 4.0);
            var state = sim.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            Assert.AreEqual(2, state.Buildings.Count(b => b.Type == BuildingType.AcLine));
            Assert.AreEqual(Ac33, state.Buildings.First(b => b.Type == BuildingType.AcLine && b.Tile == t1 || b.Type == BuildingType.AcLine && b.LinkTo == t1).AcClass == Ac33 ? Ac33 : 0);
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            Assert.IsTrue(loaded.RestoreState(state));
            loaded.Tick(1.0);
            Assert.IsTrue(loaded.IsPowered(t3), "the AC chain stands after loading");
            Assert.AreEqual(3, loaded.ConvertersOn(t1).Count() + loaded.ConvertersOn(t2).Count() + loaded.ConvertersOn(t3).Count());
            Assert.AreEqual(2, loaded.AcLinks().Count());
        }

        [Test]
        public void AcLinesHaveTheSameMeterInspectProtectionAndRepairAsDcLines()
        {
            var sim = Colony(out int t1, out int t2, out int t3);
            sim.PlaceWire(sim.BaseTileId, t1, false);
            sim.PlaceConverter(BuildingType.Rectifier, t1, 40);
            sim.PlaceAcWire(t1, t2, Ac500); sim.PlaceAcWire(t2, t3, Ac500);
            sim.PlaceConverter(BuildingType.Inverter, t3, 40);
            Run(sim, 2.0);
            CollectionAssert.Contains(sim.LineTiles().ToList(), t2, "the AC-only middle tile is in the lines list");
            Assert.IsFalse(sim.IsPowered(t2));
            var read = sim.LineReadout(t2);
            Assert.IsTrue(read.HasLine);
            Assert.AreEqual(PlanetSimulation.KindCapacityW(LineKind.Ac500), read.CapacityW, 1e-6, "the AC 500 kV rating: sqrt(3) x 500 kV x 3000 A");
            Assert.IsFalse(read.Measured);
            Assert.IsTrue(sim.InstallLineMeter(t2));
            Assert.IsTrue(sim.LineReadout(t2).Measured);
            Assert.IsTrue(sim.InstallLineProtection(t2, ProtectionKind.Breaker));
            Assert.AreEqual(ProtectionKind.Breaker, sim.LineProtectionOf(t2));
        }

        [Test]
        public void AFailedAcLineSplitsTheChainAndRepairRestoresIt()
        {
            var sim = Colony(out int t1, out int t2, out int t3);
            sim.PlaceWire(sim.BaseTileId, t1, false);
            sim.PlaceConverter(BuildingType.Rectifier, t1, 40);
            sim.PlaceAcWire(t1, t2, Ac33); sim.PlaceAcWire(t2, t3, Ac33);
            sim.PlaceConverter(BuildingType.Inverter, t3, 40);
            Run(sim, 2.0);
            Assert.IsTrue(sim.IsPowered(t3));
            var link = sim.AcLinks().First(l => l.From == t2 || l.To == t2);
            int hostTile = link.From, hostSlot = sim.CaptureState().Buildings.First(b => b.Type == BuildingType.AcLine && b.Tile == hostTile && b.LinkTo == link.To).Slot;
            Assert.IsTrue(sim.SetCableState(hostTile, hostSlot, 0.0, false));
            Run(sim, 0.2);
            Assert.IsFalse(sim.IsPowered(t3), "the AC chain is broken at the failed line");
            Assert.AreEqual(1, sim.FailedLines().Count());
            Assert.IsNull(sim.RepairLine(hostTile, hostSlot));
            Run(sim, 0.2);
            Assert.IsTrue(sim.IsPowered(t3));
        }

        [Test]
        public void ASaveFromVersion47LoadsWithNoAc()
        {
            var sim = Colony(out int t1, out _, out _);
            sim.PlaceWire(sim.BaseTileId, t1, false);
            var state = sim.CaptureState();
            state.Version = 47;
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            Assert.IsTrue(loaded.RestoreState(state));
            Assert.AreEqual(0, loaded.AcLinks().Count());
            Assert.IsTrue(loaded.IsPowered(t1));
        }

        [Test]
        public void AColonyWithoutAcRunsExactlyAsBefore()
        {
            var sim = Colony(out int t1, out int t2, out _);
            sim.PlaceWire(sim.BaseTileId, t1, false); sim.PlaceWire(t1, t2, false);
            Run(sim, 2.0);
            Assert.IsTrue(sim.IsPowered(t2));
            Assert.AreEqual(0.0, sim.ConverterLossW);
        }
    }
}
