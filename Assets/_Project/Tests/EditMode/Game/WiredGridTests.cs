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
            Assert.IsTrue(sim.PlacePowerLine(near, 0));
            sim.Tick(0.1);
            Assert.IsTrue(sim.IsPowered(near));
            int apart = Enumerable.Range(0, sim.TileCount).First(t => t != sim.BaseTileId && t != near && !Sphere.Neighbors[sim.BaseTileId].Contains(t)
                                                                      && !Sphere.Neighbors[near].Contains(t) && !sim.GetTile(t).IsWater && sim.IsOpenGround(t));
            Assert.IsTrue(sim.PlacePowerLine(apart, 0));
            sim.Tick(0.1);
            Assert.IsFalse(sim.IsPowered(apart), "a line with a gap to the grid is not connected");
        }

        [Test]
        public void ALineUnderConstruction_CarriesNothing()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike) { WiredGrid = true };
            int near = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(near, 0));
            Assert.IsTrue(sim.IsUnderConstruction(near, 0));
            Assert.IsFalse(sim.IsPowered(near), "a site is not a wire yet");
        }

        [Test]
        public void ChainsConnectThroughManyTiles_AndCutsDisconnect()
        {
            var sim = Wired();
            int a = Sphere.Neighbors[sim.BaseTileId].First(n => !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            int b = Sphere.Neighbors[a].First(n => n != sim.BaseTileId && !Sphere.Neighbors[sim.BaseTileId].Contains(n) && !sim.GetTile(n).IsWater && sim.IsOpenGround(n));
            Assert.IsTrue(sim.PlacePowerLine(a, 0));
            Assert.IsTrue(sim.PlacePowerLine(b, 0));
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
            Assert.IsTrue(sim.PlacePowerLine(near, 0));
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
            Assert.IsTrue(sim.PlacePowerLine(a, 0));
            Assert.IsTrue(sim.PlacePowerLine(b, 0));
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
            Assert.IsTrue(sim.PlacePowerLine(a, 0));
            Assert.IsTrue(sim.PlacePanel(a, 1, 0.35, Math.PI));
            double lossSeen = 0.0;
            for (int i = 0; i < 48; i++) { sim.Tick(1.0); lossSeen = Math.Max(lossSeen, sim.TransmissionLossW); }
            Assert.Greater(lossSeen, 0.0, "the lines lose something while the panel makes power");
            var home = Wired();
            Assert.IsTrue(home.PlacePanel(home.BaseTileId, 1, 0.35, Math.PI));
            for (int i = 0; i < 48; i++) home.Tick(1.0);
            Assert.AreEqual(0.0, home.TransmissionLossW, 1e-9, "a panel on the lander tile loses nothing");
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
    }
}
