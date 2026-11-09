using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Building footprints: several slots of one tile (decided 2026-11-09).</summary>
    public class FootprintTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static StubPlanetSimulation Create() => new StubPlanetSimulation(Sphere, Surface, Earthlike);

        [Test]
        public void NearestSlots_AnchorFirst_ThenItsNeighbours()
        {
            TileSite site = TileSite.Create(Sphere, 5);
            List<int> f = site.NearestSlots(0, 7);
            Assert.AreEqual(0, f[0]);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5, 6 }, f, "the centre and its first ring");
            Assert.AreEqual(3, site.NearestSlots(20, 3).Count);
        }

        [Test]
        public void Sizes_ByTypeAndTier()
        {
            var sim = Create();
            Assert.AreEqual(BuildingFootprints.BatteryYard, sim.FootprintSize(BuildingType.Battery));
            Assert.AreEqual(BuildingFootprints.Workshop, sim.FootprintSize(BuildingType.Workshop));
            Assert.AreEqual(1, sim.FootprintSize(BuildingType.SolarPanel), "a panel field at tier 0");
            Assert.AreEqual(7, BuildingFootprints.SlotsFor(BuildingType.SolarPanel, 3), "a solar array");
        }

        [Test]
        public void Workshop_CoversThreeSlots_AnyOfThemNamesIt()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            List<int> f = sim.FootprintFor(BuildingType.Workshop, t, 0);
            Assert.IsTrue(sim.PlaceWorkshop(t, 0));
            Assert.AreEqual(3, sim.FootprintAt(t, 0).Count);
            foreach (int s in f)
            {
                Assert.AreEqual(BuildingType.Workshop, sim.GetBuilding(t, s));
                Assert.AreEqual(0, sim.AnchorOf(t, s));
            }
            Assert.AreEqual(-1, sim.AnchorOf(t, 40));
            Assert.IsFalse(sim.PlaceBattery(t, f[1]), "covered");
            Assert.IsFalse(sim.CanPlace(BuildingType.Battery, t, f[2]));
        }

        [Test]
        public void Overlapping_FootprintsAreRefused()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.IsTrue(sim.PlaceBattery(t, 0));
            List<int> yard = new List<int>(sim.FootprintAt(t, 0));
            // A Workshop whose footprint would reach into the yard can't go next to it.
            int touching = -1;
            for (int s = 0; s < sim.SlotCount(t) && touching < 0; s++)
                if (sim.GetBuilding(t, s) == BuildingType.None)
                    foreach (int c in sim.FootprintFor(BuildingType.Workshop, t, s))
                        if (yard.Contains(c)) { touching = s; break; }
            Assert.GreaterOrEqual(touching, 0);
            Assert.IsFalse(sim.PlaceWorkshop(t, touching));
        }

        [Test]
        public void RemovingByAnyCoveredSlot_FreesTheWholeFootprint()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.PlaceWorkshop(t, 0);
            List<int> f = new List<int>(sim.FootprintAt(t, 0));
            Assert.IsTrue(sim.RemoveBuilding(t, f[2]));
            foreach (int s in f)
                Assert.AreEqual(BuildingType.None, sim.GetBuilding(t, s));
            Assert.IsTrue(sim.PlaceBattery(t, f[1]), "free again");
        }

        [Test]
        public void SalvageAndSiteQueries_WorkFromAnyCoveredSlot()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Workforce.Pause(sim.Workforce.Robots[0].GroupId);
            sim.PlaceBattery(t, 0);
            int other = sim.FootprintAt(t, 0)[1];
            Assert.IsTrue(sim.IsUnderConstruction(t, other));
            Assert.AreEqual(0.0, sim.ConstructionProgress(t, other));
            Assert.IsTrue(sim.OrderSalvage(t, other), "a site is cancelled");
            Assert.AreEqual(BuildingType.None, sim.GetBuilding(t, 0));
        }

        [Test]
        public void Save_KeepsFootprints_OldSavesUseOneSlot()
        {
            var a = Create();
            int t = a.BaseTileId;
            a.PlaceWorkshop(t, 0);
            a.PlaceBattery(t, 30);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(20, state.Version);
            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            CollectionAssert.AreEqual(a.FootprintAt(t, 0), b.FootprintAt(t, 0));
            CollectionAssert.AreEqual(a.FootprintAt(t, 30), b.FootprintAt(t, 30));

            foreach (BuildingRecord r in state.Buildings) r.Covered.Clear();   // as written by version 15
            var c = Create();
            Assert.IsTrue(c.RestoreState(state));
            Assert.AreEqual(1, c.FootprintAt(t, 0).Count);
        }
    }
}
