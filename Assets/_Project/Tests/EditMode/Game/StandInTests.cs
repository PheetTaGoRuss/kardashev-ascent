using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>
    /// Aluminium for copper and sodium for lithium in building bills (director 2026-11-15: every planet must be
    /// startable, also one with no copper or lithium ore).
    /// </summary>
    public class StandInTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetSimulation Create() => new PlanetSimulation(Sphere, Surface, Earthlike) { ConstructionTimeEnabled = false };

        [Test]
        public void Ratios_FollowConductivityDensityAndMolarMass()
        {
            // Same resistance over the same length: area × 1/0.61, mass × 2.70/8.96 → 0.494 kg Al per kg Cu.
            Assert.AreEqual(0.494, StandIns.AluminiumPerCopperKg, 0.001);
            // One Na⁺ per Li⁺, one charge each: 22.99 / 6.94 = 3.31 kg Na per kg Li.
            Assert.AreEqual(3.313, StandIns.SodiumPerLithiumKg, 0.001);
        }

        [Test]
        public void Apply_SwapsOnlyTheNamedMetal()
        {
            MaterialBill bill = new MaterialBill().Add(Material.Iron, 100.0).Add(Material.Copper, 10.0).Add(Material.Lithium, 2.0);
            MaterialBill al = StandIns.Apply(bill, StandIn.AluminiumForCopper);
            Assert.AreEqual(0.0, al[Material.Copper]);
            Assert.AreEqual(10.0 * StandIns.AluminiumPerCopperKg, al[Material.Aluminium], 1e-9);
            Assert.AreEqual(2.0, al[Material.Lithium]);
            Assert.AreEqual(100.0, al[Material.Iron]);
            MaterialBill both = StandIns.Apply(bill, StandIn.AluminiumForCopper | StandIn.SodiumForLithium);
            Assert.AreEqual(0.0, both[Material.Lithium]);
            Assert.AreEqual(2.0 * StandIns.SodiumPerLithiumKg, both[Material.Sodium], 1e-9);
            Assert.AreEqual(bill.TotalKg, StandIns.Apply(bill, StandIn.None).TotalKg, 1e-12);
        }

        [Test]
        public void RefiningAluminiumAndSodium_IsOpenFromTheStart()
        {
            Assert.AreEqual(0, RobotTech.UnlockMilestone(Material.Aluminium));
            Assert.AreEqual(0, RobotTech.UnlockMilestone(Material.Sodium));
            // Both come from plain rock: no ore deposit needed.
            Assert.AreEqual(Material.Rock, RobotTech.Refine(Material.Aluminium).Inputs[0].Material);
            Assert.AreEqual(Material.Rock, RobotTech.Refine(Material.Sodium).Inputs[0].Material);
        }

        [Test]
        public void WithCopperInStock_BuildingsUseCopper()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            Assert.IsTrue(sim.PlaceRelay(t, 0));
            Assert.AreEqual(StandIn.None, sim.StandInsAt(t, 0));
            Assert.AreEqual(sim.BuildBill(BuildingType.Relay)[Material.Copper], sim.BillToBuild(BuildingType.Relay)[Material.Copper], 1e-9);
        }

        [Test]
        public void WithAluminiumAndNoCopper_BuildingsAreWiredWithAluminium_AndGiveItBack()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Materials.Take(Material.Copper, sim.Materials[Material.Copper]);
            double al = sim.BuildBill(BuildingType.Relay)[Material.Copper] * StandIns.AluminiumPerCopperKg;
            sim.Materials.Add(Material.Aluminium, al);
            MaterialBill bill = sim.BillToBuild(BuildingType.Relay);
            Assert.AreEqual(0.0, bill[Material.Copper]);
            Assert.AreEqual(al, bill[Material.Aluminium], 1e-9);

            Assert.IsTrue(sim.PlaceRelay(t, 0));
            Assert.AreEqual(StandIn.AluminiumForCopper, sim.StandInsAt(t, 0));
            for (int i = 0; i < 96 && sim.IsUnderConstruction(t, 0); i++)
                sim.Tick(0.25);
            Assert.IsFalse(sim.IsUnderConstruction(t, 0));
            Assert.AreEqual(0.0, sim.Materials[Material.Aluminium], 1e-9);

            // Taking it down gives back aluminium, never copper it was not made of.
            Assert.IsTrue(sim.RemoveBuilding(t, 0));
            Assert.AreEqual(BuildCosts.SalvageShare * al, sim.Materials[Material.Aluminium], 1e-9);
            Assert.AreEqual(0.0, sim.Materials[Material.Copper], 1e-9);
            Assert.AreEqual(StandIn.None, sim.StandInsAt(t, 0));
        }

        [Test]
        public void AWaitingSite_TakesWhicheverMetalArrivesFirst()
        {
            // Mars: no copper left, none mined. The site waits with a copper bill; refined aluminium arrives and it switches.
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Materials.Take(Material.Copper, sim.Materials[Material.Copper]);
            Assert.IsTrue(sim.PlaceRelay(t, 0));
            Assert.AreEqual(StandIn.None, sim.StandInsAt(t, 0));
            Assert.IsTrue(sim.IsUnderConstruction(t, 0));
            double al = sim.BuildBill(BuildingType.Relay)[Material.Copper] * StandIns.AluminiumPerCopperKg;
            sim.Materials.Add(Material.Aluminium, al);
            for (int i = 0; i < 96 && sim.IsUnderConstruction(t, 0); i++)
                sim.Tick(0.25);
            Assert.IsFalse(sim.IsUnderConstruction(t, 0));
            Assert.AreEqual(StandIn.AluminiumForCopper, sim.StandInsAt(t, 0));

            // A wet planet: the same site gets mined copper instead and stays copper.
            var wet = Create();
            double cu = wet.Materials[Material.Copper];
            wet.Materials.Take(Material.Copper, cu);
            Assert.IsTrue(wet.PlaceRelay(t, 0));
            wet.Materials.Add(Material.Copper, cu);
            for (int i = 0; i < 96 && wet.IsUnderConstruction(t, 0); i++)
                wet.Tick(0.25);
            Assert.IsFalse(wet.IsUnderConstruction(t, 0));
            Assert.AreEqual(StandIn.None, wet.StandInsAt(t, 0));
        }

        [Test]
        public void WithNoLithium_BatteriesUseSodiumCells()
        {
            var sim = Create();
            int t = sim.BaseTileId;
            sim.Materials.Take(Material.Lithium, sim.Materials[Material.Lithium]);
            double na = sim.BuildBill(BuildingType.Battery)[Material.Lithium] * StandIns.SodiumPerLithiumKg;
            sim.Materials.Add(Material.Sodium, na);
            int slot = sim.SlotCount(t) - 1;
            Assert.AreEqual(0.0, sim.BillToBuild(BuildingType.Battery)[Material.Lithium]);
            Assert.AreEqual(na, sim.BillToBuild(BuildingType.Battery)[Material.Sodium], 1e-9);
            Assert.IsTrue(sim.PlaceBattery(t, slot));
            Assert.AreEqual(StandIn.SodiumForLithium, sim.StandInsAt(t, slot));
        }

        [Test]
        public void StandIns_AreSaved_AndOldSavesHaveNone()
        {
            var a = Create();
            int t = a.BaseTileId;
            a.ConstructionTimeEnabled = true;
            a.Materials.Take(Material.Copper, a.Materials[Material.Copper]);
            a.Materials.Add(Material.Aluminium, 1000.0);
            Assert.IsTrue(a.PlaceRelay(t, 0));
            a.Tick(0.25);   // some aluminium goes in: the choice is kept
            Assert.IsTrue(a.IsUnderConstruction(t, 0));
            SimulationState state = a.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);

            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(StandIn.AluminiumForCopper, b.StandInsAt(t, 0));
            Assert.AreEqual(0.0, b.ConstructionRemainingAt(t, 0)[Material.Copper]);

            // A version-30 save has no stand-ins: its sites keep their copper bills.
            SimulationState old = a.CaptureState();
            old.Version = 30;
            foreach (BuildingRecord r in old.Buildings) r.StandIns = StandIn.None;
            var c = Create();
            Assert.IsTrue(c.RestoreState(old));
            Assert.AreEqual(StandIn.None, c.StandInsAt(t, 0));
            Assert.AreEqual(0.0, c.ConstructionRemainingAt(t, 0)[Material.Aluminium]);
        }
    }
}
