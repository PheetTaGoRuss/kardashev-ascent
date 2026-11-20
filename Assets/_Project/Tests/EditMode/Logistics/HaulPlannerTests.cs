using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Resources;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Logistics
{
    /// <summary>The haul planner (MO-03): nearest holding first, then lowest tile id; payload and availability limit each haul.</summary>
    public class HaulPlannerTests
    {
        static readonly int[] Sources = { 1, 2, 3, 4 };

        static double Hours(int a, int b) => System.Math.Abs(a - b);

        [Test]
        public void TheNearestHoldingIsChosen()
        {
            var plan = HaulPlanner.Plan(new[] { new HaulDemand(5, Material.Iron, 100.0) }, Sources, (t, m) => 1000.0, Hours, 500.0);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(4, plan[0].From, "tile 4 is nearest to tile 5");
            Assert.AreEqual(100.0, plan[0].Kg, 1e-12);
        }

        [Test]
        public void TiesGoToTheLowestTileId()
        {
            var plan = HaulPlanner.Plan(new[] { new HaulDemand(3, Material.Iron, 10.0) }, Sources, (t, m) => 100.0, Hours, 500.0);
            Assert.AreEqual(2, plan[0].From, "tiles 2 and 4 are equally far from 3; the lower id wins");
        }

        [Test]
        public void ADemandBiggerThanThePayload_IsSplitIntoSeveralHauls()
        {
            var plan = HaulPlanner.Plan(new[] { new HaulDemand(9, Material.Rock, 1200.0) }, Sources, (t, m) => 5000.0, Hours, 500.0);
            Assert.AreEqual(3, plan.Count);
            Assert.AreEqual(500.0, plan[0].Kg, 1e-12);
            Assert.AreEqual(1200.0, plan.Sum(p => p.Kg), 1e-9);
        }

        [Test]
        public void WhatAHoldingCannotSpare_IsNotPlannedTwice()
        {
            // Tile 4 can spare 30 kg; two demands want 50 each: the first takes 30 from it and 20 from the next nearest, the second the rest.
            var avail = new Dictionary<int, double> { { 4, 30.0 }, { 3, 100.0 }, { 2, 0.0 }, { 1, 0.0 } };
            var plan = HaulPlanner.Plan(new[] { new HaulDemand(5, Material.Copper, 50.0), new HaulDemand(6, Material.Copper, 50.0) },
                                        Sources, (t, m) => avail[t], Hours, 500.0);
            Assert.AreEqual(100.0, plan.Sum(p => p.Kg), 1e-9);
            Assert.LessOrEqual(plan.Where(p => p.From == 4).Sum(p => p.Kg), 30.0 + 1e-9);
            Assert.LessOrEqual(plan.Where(p => p.From == 3).Sum(p => p.Kg), 100.0 + 1e-9);
        }

        [Test]
        public void NothingToTakeFrom_PlansNothing()
        {
            Assert.AreEqual(0, HaulPlanner.Plan(new[] { new HaulDemand(5, Material.Iron, 10.0) }, Sources, (t, m) => 0.0, Hours, 500.0).Count);
            Assert.AreEqual(0, HaulPlanner.Plan(new[] { new HaulDemand(5, Material.Iron, 10.0) }, Sources, (t, m) => 50.0, (a, b) => double.PositiveInfinity, 500.0).Count, "no way there");
        }

        [Test]
        public void ADepotNeverHaulsToItself()
        {
            var plan = HaulPlanner.Plan(new[] { new HaulDemand(2, Material.Iron, 10.0) }, new[] { 2 }, (t, m) => 100.0, Hours, 500.0);
            Assert.AreEqual(0, plan.Count);
        }

        [Test]
        public void TheSameInputsGiveTheSamePlan()
        {
            var demands = new[] { new HaulDemand(7, Material.Iron, 700.0), new HaulDemand(6, Material.Rock, 90.0) };
            string Run() => string.Join(";", HaulPlanner.Plan(demands, Sources, (t, m) => 400.0, Hours, 300.0).Select(p => $"{p.From}>{p.To}:{p.Material}:{p.Kg}"));
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void TheBook_CountsWhatIsOnItsWay_AndKeepsTargetsInOrder()
        {
            var book = new HaulBook();
            book.Add(1, 5, Material.Iron, 100.0);
            var o = book.Add(2, 5, Material.Iron, 50.0);
            Assert.AreEqual(150.0, book.InFlightTo(5, Material.Iron), 1e-12);
            Assert.AreEqual(100.0, book.ReservedAt(1, Material.Iron), 1e-12);
            o.Stage = 1;
            Assert.AreEqual(0.0, book.ReservedAt(2, Material.Iron), 1e-12, "loaded: no longer promised from the depot");
            book.SetTarget(9, Material.Rock, 10.0);
            book.SetTarget(3, Material.Iron, 20.0);
            book.SetTarget(3, Material.Copper, 5.0);
            var t = book.Targets();
            Assert.AreEqual(3, t[0].Tile);
            Assert.AreEqual(9, t[2].Tile);
            book.SetTarget(9, Material.Rock, 0.0);
            Assert.AreEqual(2, book.Targets().Count);
            Assert.AreEqual(3, book.Add(0, 1, Material.Iron, 1.0).Id, "ids rise and are not reused");
        }
    }
}
