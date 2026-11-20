using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>The wired grid (director 2026-11-20): generators and stations must be connected to the lander tile by power lines.</summary>
    public class WiredGridTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Wired()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            sim.Debug.FreeBuilding = true;
            return sim;
        }

        [Test]
        public void WhenNotWired_EverythingWorksAsBefore()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsFalse(sim.WiredGrid);
            Assert.IsTrue(sim.IsPowered(0));
            Assert.AreEqual(0, sim.GeneratorsOffGrid);
        }

        [Test]
        public void TheLanderTileIsOnTheGrid_AndTheRestIsNot()
        {
            var sim = Wired();
            Assert.IsTrue(sim.IsPowered(sim.BaseTileId));
            int other = Enumerable.Range(0, sim.TileCount).First(t => t != sim.BaseTileId && !sim.GetTile(t).IsWater);
            Assert.IsFalse(sim.IsPowered(other));
            Assert.AreEqual(1, sim.PoweredTileCount);
        }

        [Test]
        public void ALineNextToTheGrid_JoinsIt_AndALineApartDoesNot()
        {
            var sim = Wired();
            int near = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(near, 0, sim.BaseTileId));
            sim.Tick(0.1);
            Assert.IsTrue(sim.IsPowered(near));
            int apart = Enumerable.Range(0, sim.TileCount).First(t => t != sim.BaseTileId && t != near && !Sphere.Neighbors[sim.BaseTileId].Contains(t)
                                                                      && !Sphere.Neighbors[near].Contains(t) && !sim.GetTile(t).IsWater && sim.IsOpenGround(t));
            Assert.IsTrue(sim.PlacePowerLine(apart, 0, Sphere.Neighbors[apart].First(n => n != sim.BaseTileId && n != near && !sim.GetTile(n).IsWater)));
            sim.Tick(0.1);
            Assert.IsFalse(sim.IsPowered(apart), "a line with a gap to the grid is not connected");
        }

        [Test]
        public void ALineUnderConstruction_CarriesNothing()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            int near = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(near, 0, sim.BaseTileId));
            Assert.IsTrue(sim.IsUnderConstruction(near, 0));
            Assert.IsFalse(sim.IsPowered(near), "a site is not a wire yet");
        }

        [Test]
        public void ChainsConnectThroughManyTiles_AndCutsDisconnect()
        {
            var sim = Wired();
            int a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            int b = Sphere.Neighbors[a].First(n => n != sim.BaseTileId && !Sphere.Neighbors[sim.BaseTileId].Contains(n) && !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            Assert.IsTrue(sim.PlacePowerLine(b, 0, a));
            sim.Tick(0.1);
            Assert.IsTrue(sim.IsPowered(b));
            Assert.IsTrue(sim.RemoveBuilding(a, 0));
            sim.Tick(0.1);
            Assert.IsFalse(sim.IsPowered(a));
            Assert.IsFalse(sim.IsPowered(b), "cutting the middle of the chain cuts everything beyond it");
        }

        [Test]
        public void AGeneratorOffTheGrid_MakesNothing_UntilItIsWiredIn()
        {
            var sim = Wired();
            int far = Enumerable.Range(0, sim.TileCount).First(t => !sim.GetTile(t).IsWater && sim.IsOpenGround(t) && t != sim.BaseTileId
                                                                     && !Sphere.Neighbors[sim.BaseTileId].Contains(t));
            Assert.IsTrue(sim.PlacePanel(far, 0, 0.35, Math.PI));
            Assert.AreEqual(1, sim.GeneratorsOffGrid);
            double offGrid = 0.0;
            for (int i = 0; i < 48; i++) { sim.Tick(1.0); offGrid = Math.Max(offGrid, sim.GenerationW - sim.RtgW); }
            Assert.AreEqual(0.0, offGrid, 1e-6, "an unwired panel makes nothing the colony can use");

            var wired = Wired();
            Assert.IsTrue(wired.PlacePanel(wired.BaseTileId, 0, 0.35, Math.PI));
            double unwiredMax = 0.0;
            for (int i = 0; i < 24; i++) { wired.Tick(1.0); unwiredMax = Math.Max(unwiredMax, wired.GenerationW - wired.RtgW); }
            Assert.AreEqual(0.0, unwiredMax, 1e-6, "a panel nobody wired makes nothing, even on the lander tile");
            Assert.IsTrue(wired.PlaceBuildingWire(wired.BaseTileId, 0));
            double onGrid = 0.0;
            for (int i = 0; i < 48; i++) { wired.Tick(1.0); onGrid = Math.Max(onGrid, wired.GenerationW - wired.RtgW); }
            Assert.Greater(onGrid, 1000.0, "a panel on the lander tile is on the grid");
        }

        [Test]
        public void StationsAndStorage_OnlyGoWhereTheGridReaches()
        {
            var sim = Wired();
            int far = Enumerable.Range(0, sim.TileCount).First(t => !sim.GetTile(t).IsWater && sim.IsOpenGround(t) && t != sim.BaseTileId
                                                                     && !Sphere.Neighbors[sim.BaseTileId].Contains(t));
            Assert.IsFalse(sim.CanPlace(BuildingType.ChargingStation, far, 0), "no grid there");
            Assert.IsFalse(sim.CanPlace(BuildingType.Battery, far, 0));
            Assert.IsTrue(sim.CanPlace(BuildingType.ChargingStation, sim.BaseTileId, 0));
            var plain = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(plain.CanPlace(BuildingType.ChargingStation, far, 0), "not wired: anywhere, as before");
        }

        [Test]
        public void SaveAndLoad_KeepsTheWiringAndTheFlag()
        {
            var sim = Wired();
            int near = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(near, 0, sim.BaseTileId));
            sim.Tick(0.1);
            SimulationState state = sim.CaptureState();
            Assert.IsTrue(state.WiredGrid);
            var other = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(other.RestoreState(state));
            Assert.IsTrue(other.WiredGrid);
            Assert.AreEqual(BuildingType.PowerLine, other.GetBuilding(near, 0));
            other.Tick(0.1);
            Assert.IsTrue(other.IsPowered(near));
        }

        [Test]
        public void EachTileOfLine_LosesAShareOfThePower()
        {
            var sim = Wired();
            int a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            int b = Sphere.Neighbors[a].First(n => n != sim.BaseTileId && !Sphere.Neighbors[sim.BaseTileId].Contains(n) && !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            Assert.IsTrue(sim.PlacePowerLine(b, 0, a));
            sim.Tick(0.1);
            Assert.AreEqual(1.0, sim.GridEfficiency(sim.BaseTileId), 1e-12);
            Assert.AreEqual(1.0 - PlanetSimulation.LineLossPerHop, sim.GridEfficiency(a), 1e-12);
            double two = (1.0 - PlanetSimulation.LineLossPerHop) * (1.0 - PlanetSimulation.LineLossPerHop);
            Assert.AreEqual(two, sim.GridEfficiency(b), 1e-12);
            Assert.AreEqual(0.0, sim.GridEfficiency(Enumerable.Range(0, sim.TileCount).First(t => !sim.IsPowered(t))), "off the grid: nothing arrives");
            Assert.AreEqual(1.0, new PlanetSimulation(Sphere, Surface, Earthlike).GridEfficiency(a), "not wired: no losses");
        }

        [Test]
        public void AFarPanel_ReportsItsLineLoss_AndMakesLessThanAnIdenticalOneAtHome()
        {
            var sim = Wired();
            int a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            Assert.IsTrue(sim.PlacePanel(a, 1, 0.35, Math.PI));
            sim.WireAllBuildings();
            double lossSeen = 0.0;
            for (int i = 0; i < 48; i++) { sim.Tick(1.0); lossSeen = Math.Max(lossSeen, sim.TransmissionLossW); }
            Assert.Greater(lossSeen, 0.0, "the lines lose something while the panel makes power");
            var home = Wired();
            Assert.IsTrue(home.PlacePanel(home.BaseTileId, 1, 0.35, Math.PI));
            home.WireAllBuildings();
            for (int i = 0; i < 48; i++) home.Tick(1.0);
            Assert.AreEqual(0.0, home.TransmissionLossW, 1e-9, "a panel on the lander tile loses nothing");
        }

        /// <summary>Wire a tile next to the lander and cover its slots with panels, so the line out of it has more to carry than a plain line can.</summary>
        static int OverloadedLineTile(PlanetSimulation sim)
        {
            int a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            int placed = 0;
            for (int slot = 1; slot < sim.SlotCount(a) && placed < 45; slot++)
                if (sim.PlacePanel(a, slot, 0.35, Math.PI)) placed++;
            Assert.GreaterOrEqual(placed, 30);
            sim.WireAllBuildings();
            return a;
        }

        static void RunToNoon(PlanetSimulation sim, Func<bool> done)
        {
            for (int i = 0; i < 48 && !done(); i++) sim.Tick(1.0);
        }

        [Test]
        public void ATooThinLine_OverloadsAndCurtailsWhatIsBeyondIt()
        {
            var sim = Wired();
            int a = OverloadedLineTile(sim);
            RunToNoon(sim, () => sim.IsLineOverloaded(a));
            Assert.IsTrue(sim.IsLineOverloaded(a), "forty panels are more than a plain line carries");
            Assert.AreEqual(PlanetSimulation.LineCapacityPlainW, sim.LineCapacityW(a), 1e-6);
            Assert.LessOrEqual(sim.LineFlowW(a), PlanetSimulation.LineCapacityPlainW * 1.0001, "the line carries no more than its rating");
            Assert.Greater(sim.OverloadLossW, 1e6, "the rest is lost");
            Assert.IsTrue(double.IsPositiveInfinity(sim.LineCapacityW(sim.BaseTileId)), "the lander tile has no line to overload");
        }

        [Test]
        public void AHighVoltageLine_CarriesWhatThePlainLineCannot()
        {
            var sim = Wired();
            sim.SetMilestonesForTests(PlanetSimulation.HighVoltageMilestone);
            int a = OverloadedLineTile(sim);
            RunToNoon(sim, () => sim.IsLineOverloaded(a));
            Assert.IsTrue(sim.IsLineOverloaded(a));
            double plainDelivered = sim.GenerationW;
            Assert.IsTrue(sim.PlaceHighVoltageLine(a, 50, sim.BaseTileId));
            sim.Tick(0.1);
            Assert.AreEqual(PlanetSimulation.LineCapacityHighW, sim.LineCapacityW(a), 1e-6);
            Assert.IsFalse(sim.IsLineOverloaded(a), "the high-voltage line carries it");
            Assert.AreEqual(0.0, sim.OverloadLossW, 1e-6);
            Assert.Greater(sim.GenerationW, plainDelivered, "more power reaches the lander tile");
        }

        [Test]
        public void TheHighVoltageLine_OpensAtAMilestone_AndTheDebugUnlockSkipsIt()
        {
            var sim = Wired();
            StringAssert.Contains("milestone", sim.KnowledgeBlocker(BuildingType.HighVoltageLine));
            sim.SetMilestonesForTests(PlanetSimulation.HighVoltageMilestone);
            Assert.IsNull(sim.KnowledgeBlocker(BuildingType.HighVoltageLine));
            var other = Wired();
            other.Debug.UnlockAll = true;
            Assert.IsNull(other.KnowledgeBlocker(BuildingType.HighVoltageLine));
        }

        [Test]
        public void SaveAndLoad_KeepsAHighVoltageLine()
        {
            var sim = Wired();
            sim.SetMilestonesForTests(PlanetSimulation.HighVoltageMilestone);
            int a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(a, 0, sim.BaseTileId));
            Assert.IsTrue(sim.PlaceHighVoltageLine(a, 1, sim.BaseTileId));
            sim.Tick(0.1);
            var other = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(other.RestoreState(sim.CaptureState()));
            other.Tick(0.1);
            Assert.AreEqual(BuildingType.HighVoltageLine, other.GetBuilding(a, 1));
            Assert.AreEqual(PlanetSimulation.LineCapacityHighW, other.LineCapacityW(a), 1e-6);
            Assert.Contains(a, other.LineTiles().ToList());
        }

        [Test]
        public void TheWireTool_RunsWiresTileToTile_OnlyBetweenNeighbours()
        {
            var sim = Wired();
            int a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            int b = Sphere.Neighbors[a].First(n => n != sim.BaseTileId && !Sphere.Neighbors[sim.BaseTileId].Contains(n) && !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlaceWire(sim.BaseTileId, a, false));
            Assert.IsTrue(sim.PlaceWire(a, b, false), "the path continues tile after tile");
            sim.Tick(0.1);
            Assert.IsTrue(sim.IsPowered(a));
            Assert.IsTrue(sim.IsPowered(b));
            var links = sim.WireLinks().ToList();
            Assert.AreEqual(2, links.Count);
            Assert.IsTrue(links.All(l => l.Built));
            Assert.IsFalse(sim.PlaceWire(sim.BaseTileId, b, false), "not neighbours");
            StringAssert.Contains("neighbouring", sim.WireProblem);
            Assert.IsFalse(sim.PlaceWire(a, sim.BaseTileId, false), "already wired");
            StringAssert.Contains("already", sim.WireProblem);
        }

        [Test]
        public void AWire_IsBuiltOnTheEndThatIsOnTheGrid()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            int a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlaceWire(a, sim.BaseTileId, false), "drawn from the far end toward the lander");
            Assert.IsTrue(sim.IsUnderConstruction(sim.BaseTileId, 0) || Enumerable.Range(0, sim.SlotCount(sim.BaseTileId)).Any(sl => sim.GetBuilding(sim.BaseTileId, sl) == BuildingType.PowerLine),
                "a robot builds it where the grid is, so it can reach it");
        }

        [Test]
        public void AHighVoltageWire_CanBeRunOverAPlainOne_AndLiftsItsCapacity()
        {
            var sim = Wired();
            sim.SetMilestonesForTests(PlanetSimulation.HighVoltageMilestone);
            int a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlaceWire(sim.BaseTileId, a, false));
            sim.Tick(0.1);
            Assert.AreEqual(PlanetSimulation.LineCapacityPlainW, sim.LineCapacityW(a), 1e-6);
            Assert.IsTrue(sim.PlaceWire(sim.BaseTileId, a, true), "the same two tiles, the better wire");
            sim.Tick(0.1);
            Assert.AreEqual(PlanetSimulation.LineCapacityHighW, sim.LineCapacityW(a), 1e-6);
            Assert.IsFalse(sim.PlaceWire(sim.BaseTileId, a, true), "one wire of each kind between two tiles");
        }

        [Test]
        public void AnOldSave_IsNotWired()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            SimulationState state = sim.CaptureState();
            state.WiredGrid = false;
            state.Version = 34;
            var other = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            Assert.IsTrue(other.RestoreState(state));
            Assert.IsFalse(other.WiredGrid, "a save from before the wired grid keeps working as it did");
        }

        [Test]
        public void ABuildingNeedsItsOwnWire_AndTheListShowsWhatIsStillUnwired()
        {
            var sim = Wired();
            Assert.IsTrue(sim.PlaceBattery(sim.BaseTileId, 0));
            Assert.IsTrue(sim.PlacePanel(sim.BaseTileId, 3, 0.35, Math.PI));
            var open = sim.UnwiredBuildings().ToList();
            Assert.AreEqual(2, open.Count);
            Assert.IsTrue(open.Any(b => b.Type == BuildingType.Battery && PlanetSimulation.IsWireInput(b.Type)), "storage takes power");
            Assert.IsTrue(open.Any(b => b.Type == BuildingType.SolarPanel && PlanetSimulation.IsWireOutput(b.Type)), "a panel gives power");
            Assert.IsFalse(sim.IsBuildingWired(sim.BaseTileId, 0));
            Assert.IsTrue(sim.PlaceBuildingWire(sim.BaseTileId, 0));
            Assert.IsFalse(sim.PlaceBuildingWire(sim.BaseTileId, 0), "only once");
            StringAssert.Contains("Already", sim.WireProblem);
            Assert.IsFalse(sim.PlaceBuildingWire(sim.BaseTileId, 10), "an empty slot has nothing to wire");
            Assert.AreEqual(1, sim.UnwiredBuildings().Count());
            sim.RemoveBuilding(sim.BaseTileId, 0);
            Assert.IsTrue(sim.PlaceBattery(sim.BaseTileId, 0));
            Assert.IsFalse(sim.IsBuildingWired(sim.BaseTileId, 0), "a new building on the same slot is not wired");
        }

        [Test]
        public void AnUnwiredBatteryYard_IsNoRobotChargingFacility()
        {
            var sim = Wired();
            Assert.IsTrue(sim.PlaceBattery(sim.BaseTileId, 0));
            sim.Tick(48.0);
            int before = sim.RobotFacilities().Count;
            Assert.IsTrue(sim.PlaceBuildingWire(sim.BaseTileId, 0));
            Assert.AreEqual(before + 1, sim.RobotFacilities().Count);
        }

        [Test]
        public void AnUnwiredWorkshop_DoesNotCount_UntilWired()
        {
            var sim = Wired();
            sim.Debug.UnlockAll = true;
            Assert.IsTrue(sim.PlaceWorkshop(sim.BaseTileId, 0));
            sim.Tick(200.0);
            Assert.AreEqual(0, sim.WorkshopCount, "no power to it yet");
            Assert.IsTrue(sim.PlaceBuildingWire(sim.BaseTileId, 0));
            Assert.AreEqual(1, sim.WorkshopCount);
        }

        [Test]
        public void AnUnwiredResearchStation_IsNotThereForExperiments_UntilWired()
        {
            var sim = Wired();
            sim.Debug.UnlockAll = true;
            Assert.IsTrue(sim.PlaceResearchStation(sim.BaseTileId, 0));
            sim.Tick(200.0);
            Assert.IsFalse(sim.HasResearchStation, "no power to it yet");
            Assert.IsTrue(sim.PlaceBuildingWire(sim.BaseTileId, 0));
            Assert.IsTrue(sim.HasResearchStation);
        }

        [Test]
        public void BuildingWires_SurviveSaveAndLoad()
        {
            var sim = Wired();
            Assert.IsTrue(sim.PlacePanel(sim.BaseTileId, 0, 0.35, Math.PI));
            Assert.IsTrue(sim.PlaceBuildingWire(sim.BaseTileId, 0));
            Assert.IsTrue(sim.PlacePanel(sim.BaseTileId, 4, 0.35, Math.PI));
            var loaded = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(loaded.RestoreState(sim.CaptureState()));
            Assert.IsTrue(loaded.IsBuildingWired(loaded.BaseTileId, 0));
            Assert.IsFalse(loaded.IsBuildingWired(loaded.BaseTileId, 4));
        }
    }
}
