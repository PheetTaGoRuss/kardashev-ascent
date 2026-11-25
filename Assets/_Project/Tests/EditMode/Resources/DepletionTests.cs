using System;
using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Resources
{
    /// <summary>Deposits run out (director 2026-11-15; known issue KI-12): what is mined comes out of the ground.</summary>
    public class DepletionTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Wet = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static PlanetSimulation Create()
        {
            var sim = new PlanetSimulation(Sphere, Wet, Earthlike) { ConstructionTimeEnabled = false };
            sim.Workforce.Reset(sim.BaseTileId, 1);
            sim.Materials.Clear();
            return sim;
        }

        static DepositMap Map() => new DepositMap(Sphere, Wet, PlanetChemistry.EarthLike, 7);

        [Test]
        public void AReserve_IsTheGradeTimesTheGrade1Size_RareMaterialsHoldLess()
        {
            DepositMap map = Map();
            for (int t = 0; t < map.TileCount; t++)
                foreach (Deposit d in map.At(t))
                    if (!map.IsEndless(t, d.Material))
                    {
                        // Ore deep in a mountain or canyon holds more (director 2026-11-15).
                        double deep = DepositMap.IsDeepGround(Wet.Terrain[t]) && d.Material != Material.Rock ? DepositMap.DeepReserveFactor : 1.0;
                        Assert.AreEqual(d.Grade * DepositMap.ReserveKgAtGrade1(d.Material) * deep, map.ReserveKg(t, d.Material), 1e-6);
                    }
            Assert.Greater(DepositMap.ReserveKgAtGrade1(Material.Rock), DepositMap.ReserveKgAtGrade1(Material.Iron));
            Assert.Greater(DepositMap.ReserveKgAtGrade1(Material.Iron), DepositMap.ReserveKgAtGrade1(Material.Copper));
            Assert.Greater(DepositMap.ReserveKgAtGrade1(Material.Copper), DepositMap.ReserveKgAtGrade1(Material.Lithium));
            Assert.AreEqual(0.0, DepositMap.ReserveKgAtGrade1(Material.Aluminium), "refined, not mined");
        }

        [Test]
        public void Extract_TakesFromTheGround_NeverMoreThanIsLeft()
        {
            DepositMap map = Map();
            int t = Enumerable.Range(0, map.TileCount).First(i => map.GradeOf(i, Material.Iron) > 0.0);
            double reserve = map.ReserveKg(t, Material.Iron);
            Assert.AreEqual(100.0, map.Extract(t, Material.Iron, 100.0), 1e-9);
            Assert.AreEqual(reserve - 100.0, map.RemainingKg(t, Material.Iron), 1e-6);
            Assert.AreEqual(reserve - 100.0, map.Extract(t, Material.Iron, 1e12), 1e-6, "only what is left");
            Assert.IsTrue(map.IsExhausted(t, Material.Iron));
            Assert.AreEqual(0.0, map.Extract(t, Material.Iron, 10.0));
            Assert.AreEqual(0.0, map.RemainingShare(t, Material.Iron), 1e-12);
            int noLithium = Enumerable.Range(0, map.TileCount).First(i => map.GradeOf(i, Material.Lithium) <= 0.0);
            Assert.AreEqual(0.0, map.Extract(noLithium, Material.Lithium, 10.0), "nothing from a deposit that is not there");
        }

        [Test]
        public void SeaWater_NeverRunsOut_IceDoes()
        {
            DepositMap map = Map();
            int shore = -1, ice = -1;
            for (int t = 0; t < map.TileCount; t++)
            {
                if (map.GradeOf(t, Material.Water) <= 0.0) continue;
                if (map.IsEndless(t, Material.Water)) { if (shore < 0) shore = t; }
                else if (ice < 0) ice = t;
            }
            Assert.GreaterOrEqual(shore, 0, "a shore tile");
            Assert.GreaterOrEqual(ice, 0, "an ice tile");
            Assert.AreEqual(1e9, map.Extract(shore, Material.Water, 1e9), 1e-3);
            Assert.AreEqual(1.0, map.RemainingShare(shore, Material.Water));
            Assert.IsFalse(map.IsExhausted(shore, Material.Water));
            map.Extract(ice, Material.Water, 1e12);
            Assert.IsTrue(map.IsExhausted(ice, Material.Water));
        }

        [Test]
        public void Mining_StopsWhenTheDepositIsUsedUp_AndMassIsKept()
        {
            var sim = Create();
            int t = sim.BaseTileId;   // every land tile has iron; the base needs no drive
            sim.Deposits.MarkSurveyed(t);
            double reserve = sim.Deposits.ReserveKg(t, Material.Iron);
            sim.Deposits.Extract(t, Material.Iron, reserve - 10.0);   // ten kilograms left, less than a batch

            int exhausted = 0;
            sim.DepositExhausted += (tile, m) => { if (tile == t && m == Material.Iron) exhausted++; };
            Assert.IsTrue(sim.OrderMining(t, Material.Iron));
            sim.Tick(1.0);
            Assert.AreEqual(10.0, sim.Materials[Material.Iron], 1e-9, "the last batch only brings what was left");
            Assert.AreEqual(1, exhausted);
            Assert.IsFalse(sim.IsMiningOrdered(t, Material.Iron), "the order ends");
            Assert.IsFalse(sim.OrderMining(t, Material.Iron), "nothing left to mine");
            sim.Tick(2.0);
            Assert.AreEqual(10.0, sim.Materials[Material.Iron], 1e-9);
            // Mass is kept: what is on hand plus what is left in the ground is what the ground held.
            Assert.AreEqual(reserve, sim.Deposits.MinedKg(t, Material.Iron), 1e-6);
            Assert.IsTrue(sim.IndustrialReports.Any(r => r.Message.Contains("used up")));
        }

        [Test]
        public void WhatWasMined_IsSaved_AndOldSavesStartFull()
        {
            var a = Create();
            int t = a.BaseTileId;
            Assert.IsTrue(a.OrderMining(t, Material.Rock));
            a.Tick(3.0);
            double mined = a.Deposits.MinedKg(t, Material.Rock);
            Assert.Greater(mined, 0.0);
            SimulationState state = a.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);

            var b = Create();
            Assert.IsTrue(b.RestoreState(state));
            Assert.AreEqual(mined, b.Deposits.MinedKg(t, Material.Rock), 1e-9);

            SimulationState old = a.CaptureState();
            old.Version = 31;
            old.Mined = null;
            var c = Create();
            Assert.IsTrue(c.RestoreState(old));
            Assert.AreEqual(0.0, c.Deposits.MinedKg(t, Material.Rock));

            // An exhausted deposit's saved mining order is dropped on load.
            var d = Create();
            d.Deposits.MarkSurveyed(t);
            Assert.IsTrue(d.OrderMining(t, Material.Iron));
            d.Deposits.Extract(t, Material.Iron, 1e12);
            var e = Create();
            Assert.IsTrue(e.RestoreState(d.CaptureState()));
            Assert.IsFalse(e.IsMiningOrdered(t, Material.Iron));
        }
    }
}
