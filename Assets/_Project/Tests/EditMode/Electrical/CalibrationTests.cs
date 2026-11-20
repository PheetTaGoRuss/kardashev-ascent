using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Electrical;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    /// <summary>
    /// Gate G1 calibration: today's flat 2 % per hop and 20 MW / 1 GW clips against the physical model, for a generator at the end
    /// of a chain of tiles. The cable products and voltages here are CANDIDATES from the workflow file (33 kV and 500 kV, one
    /// 500 mm2 copper conductor, 300 km per tile), not approved values (decisions D2 and D4 are open).
    /// </summary>
    public class CalibrationTests
    {
        const double TileM = 300_000.0;

        static (double deliveredW, double lossW, double maxCurrentA, bool collapsed) Chain(int hops, double generatedW, double volts, CableSpec spec)
        {
            var branches = new List<FlowBranch>();
            double r = spec.ResistanceOhm(TileM);
            for (int i = 0; i < hops; i++) branches.Add(new FlowBranch(i, i + 1, r, spec.RatingA));
            var inj = new double[hops + 1];
            inj[hops] = generatedW;
            var res = DcFlow.Solve(hops + 1, branches, inj, 0, volts);
            if (!res.Converged) return (0, 0, 0, true);
            double max = 0;
            foreach (double c in res.BranchCurrentA) max = Math.Max(max, Math.Abs(c));
            return (-res.SlackPowerW, res.TotalLossW, max, false);
        }

        static double OldDelivered(int hops, double generatedW, double capacityW)
        {
            double p = generatedW;
            for (int i = 0; i < hops; i++) p = Math.Min(p, capacityW) * 0.98;
            return p;
        }

        [Test]
        public void PrintTodayVersusPhysicalTable()
        {
            var plain = CableSpec.FromSquareMillimetres("Cu 500 @33 kV", ConductorMaterial.Copper, 500, 1, 1000.0);
            var high = CableSpec.FromSquareMillimetres("Cu 500 @500 kV", ConductorMaterial.Copper, 500, 1, 3000.0);
            foreach (var (name, spec, volts, cap) in new[] { ("plain 33 kV", plain, 33e3, 20e6), ("high 500 kV", high, 500e3, 1e9) })
                foreach (double p in new[] { 1e6, 5e6, 20e6, 100e6, 1e9 })
                    foreach (int hops in new[] { 1, 3 })
                    {
                        var n = Chain(hops, p, volts, spec);
                        double old = OldDelivered(hops, p, cap);
                        TestContext.Out.WriteLine($"{name} | {p / 1e6,6:0.#} MW | {hops} hop | old {old / 1e6,8:0.00} MW | new {(n.collapsed ? "COLLAPSE" : (n.deliveredW / 1e6).ToString("0.00")),8} MW | loss {n.lossW / 1e6:0.000} MW | I {n.maxCurrentA:0} A");
                    }
            Assert.Pass();
        }

        [Test]
        public void AtTypicalLoadTheModelIsCloseToToday()
        {
            var plain = CableSpec.FromSquareMillimetres("Cu 500", ConductorMaterial.Copper, 500, 1, 1000.0);
            var n = Chain(1, 1e6, 33e3, plain);   // 1 MW over one tile at 33 kV: about 1 % loss, today 2 %
            double old = OldDelivered(1, 1e6, 20e6);
            Assert.AreEqual(old, n.deliveredW, 0.02 * 1e6);
        }

        [Test]
        public void PlainLineLosesNearlyAFifthAtTwentyMegawattsOverOneTile()
        {
            var plain = CableSpec.FromSquareMillimetres("Cu 500", ConductorMaterial.Copper, 500, 1, 1000.0);
            var n = Chain(1, 20e6, 33e3, plain);
            Assert.AreEqual(0.14, n.lossW / 20e6, 0.02);   // today's model says 2 %
            Assert.AreEqual(521.0, n.maxCurrentA, 5.0);
        }
    }
}
