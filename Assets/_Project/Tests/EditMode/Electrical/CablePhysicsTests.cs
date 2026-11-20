using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Electrical;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    public class CablePhysicsTests
    {
        const double TileM = 300_000.0;

        [Test]
        public void ConstantsMatchTheirSources()
        {
            Assert.AreEqual(1.7241e-8, ConductorMaterial.Copper.Rho20, 1e-12);
            Assert.AreEqual(0.00393, ConductorMaterial.Copper.Alpha20, 1e-9);
            Assert.AreEqual(8890.0, ConductorMaterial.Copper.Density, 1e-9);
            Assert.AreEqual(2.8264e-8, ConductorMaterial.Aluminium.Rho20, 1e-12);
            Assert.AreEqual(2703.0, ConductorMaterial.Aluminium.Density, 1e-9);
        }

        [Test]
        public void ResistanceIsRhoLOverAWithUnitConversion()
        {
            var spec = CableSpec.FromSquareMillimetres("Cu 500", ConductorMaterial.Copper, 500, 1, 1000);
            Assert.AreEqual(5e-4, spec.AreaM2, 1e-15);
            Assert.AreEqual(1.7241e-8 * TileM / 5e-4, spec.ResistanceOhm(TileM), 1e-9);
            Assert.AreEqual(10.3446, spec.ResistanceOhm(TileM), 1e-3);
            var loop = CableSpec.FromSquareMillimetres("Cu 500 loop", ConductorMaterial.Copper, 500, 2, 1000);
            Assert.AreEqual(2 * spec.ResistanceOhm(TileM), loop.ResistanceOhm(TileM), 1e-9);
        }

        [Test]
        public void ResistanceRisesWithTemperatureByAlpha()
        {
            var spec = CableSpec.FromSquareMillimetres("Cu", ConductorMaterial.Copper, 100, 1, 500);
            double r20 = spec.ResistanceOhm(1000.0, 20.0), r70 = spec.ResistanceOhm(1000.0, 70.0);
            Assert.AreEqual(1.0 + 0.00393 * 50.0, r70 / r20, 1e-12);
        }

        [Test]
        public void ConductorMassIsDensityTimesLengthTimesArea()
        {
            var spec = CableSpec.FromSquareMillimetres("Cu 500", ConductorMaterial.Copper, 500, 1, 1000);
            Assert.AreEqual(8890.0 * TileM * 5e-4, spec.ConductorMassKg(TileM), 1e-3);
            Assert.AreEqual(1.3335e6, spec.ConductorMassKg(TileM), 1e2);
        }

        // ---- the tile-span sanity table from the workflow (B3) ----
        static double LossFraction(double powerW, double volts, double ohm)
        {
            double i = powerW / volts;
            return i * i * ohm / powerW;
        }

        [Test]
        public void ATileOfCopperBehavesAsTheSanityTableSays()
        {
            double r = CableSpec.FromSquareMillimetres("Cu 500", ConductorMaterial.Copper, 500, 1, 1).ResistanceOhm(TileM);
            Assert.AreEqual(0.0095, LossFraction(1e6, 33e3, r), 0.0005);
            Assert.AreEqual(0.19, LossFraction(20e6, 33e3, r), 0.01);
            Assert.AreEqual(0.041, LossFraction(1e9, 500e3, r), 0.002);
            Assert.AreEqual(24e3, 1e3 * 1e3 / (4 * r), 1e3);   // 1 kV cannot carry more than V^2/4R
        }

        // ---- the nodal solver ----
        static FlowBranch B(int a, int b, double r, double rating = 0) => new FlowBranch(a, b, r, rating);

        [Test]
        public void TwoNodeLineMatchesTheAnalyticAnswer()
        {
            // 1 kV source, 10 ohm line, 20 kW load drawn at node 1: V1 solves V1 (1000 - V1) / 10 = 20000.
            var res = DcFlow.Solve(2, new[] { B(0, 1, 10.0) }, new[] { 0.0, -20e3 }, 0, 1000.0);
            Assert.IsTrue(res.Converged);
            double v1 = res.NodeVoltageV[1];
            Assert.AreEqual(20e3, v1 * (1000.0 - v1) / 10.0, 1e-4);
            Assert.AreEqual(res.BranchCurrentA[0] * res.BranchCurrentA[0] * 10.0, res.TotalLossW, 1e-6);
        }

        [Test]
        public void EnergyBalanceInjectedEqualsDeliveredPlusLoss()
        {
            var branches = new[] { B(0, 1, 2.0), B(1, 2, 3.0), B(2, 0, 4.0), B(2, 3, 1.5) };
            var inj = new[] { 0.0, -3000.0, 2000.0, -4000.0 };
            var res = DcFlow.Solve(4, branches, inj, 0, 400.0);
            Assert.IsTrue(res.Converged);
            double injected = res.SlackPowerW + 2000.0;
            double delivered = 3000.0 + 4000.0;
            Assert.AreEqual(injected, delivered + res.TotalLossW, 1e-6);
        }

        [Test]
        public void KclHoldsAtEveryFreeNodeAndLoopSharesByConductance()
        {
            // Two parallel paths from the source (0) to the load (3): 0-1-3 (1+1 ohm) and 0-2-3 (2+2 ohm).
            var branches = new[] { B(0, 1, 1.0), B(1, 3, 1.0), B(0, 2, 2.0), B(2, 3, 2.0) };
            var res = DcFlow.Solve(4, branches, new[] { 0.0, 0.0, 0.0, -10_000.0 }, 0, 500.0);
            Assert.IsTrue(res.Converged);
            Assert.AreEqual(res.BranchCurrentA[0], res.BranchCurrentA[1], 1e-9 * Math.Abs(res.BranchCurrentA[0]));
            Assert.AreEqual(2.0, res.BranchCurrentA[0] / res.BranchCurrentA[2], 1e-9);   // half the resistance, twice the current
            double load = 10_000.0 / res.NodeVoltageV[3];
            Assert.AreEqual(load, res.BranchCurrentA[1] + res.BranchCurrentA[3], 1e-8);
        }

        [Test]
        public void SeriesAndParallelResistanceReduceAsExpected()
        {
            var series = DcFlow.Solve(3, new[] { B(0, 1, 2.0), B(1, 2, 3.0) }, new[] { 0.0, 0.0, -1000.0 }, 0, 100.0);
            Assert.AreEqual(5.0 * series.BranchCurrentA[0] * series.BranchCurrentA[0], series.TotalLossW, 1e-9);
            var parallel = DcFlow.Solve(2, new[] { B(0, 1, 6.0), B(0, 1, 3.0) }, new[] { 0.0, -1000.0 }, 0, 100.0);
            double i = parallel.BranchCurrentA[0] + parallel.BranchCurrentA[1];
            Assert.AreEqual(2.0 * i * i, parallel.TotalLossW, 1e-8);   // 6 || 3 = 2 ohm
        }

        [Test]
        public void ALoadBeyondTheTransferLimitIsReportedNotNaN()
        {
            // V^2/4R = 1000^2 / 40 = 25 kW; ask for 40 kW.
            var res = DcFlow.Solve(2, new[] { B(0, 1, 10.0) }, new[] { 0.0, -40e3 }, 0, 1000.0);
            Assert.IsFalse(res.Converged);
            Assert.IsNotEmpty(res.Diagnostics);
            foreach (double v in res.NodeVoltageV) Assert.IsFalse(double.IsNaN(v));
        }

        [Test]
        public void AnUnreachableNodeIsDiagnosed()
        {
            var res = DcFlow.Solve(3, new[] { B(0, 1, 1.0) }, new[] { 0.0, -10.0, -10.0 }, 0, 100.0);
            Assert.IsFalse(res.Converged);
            StringAssert.Contains("not connected", res.Diagnostics[0]);
        }

        [Test]
        public void OverloadComesFromCurrentNotWatts()
        {
            var res = DcFlow.Solve(2, new[] { B(0, 1, 1.0, 5.0) }, new[] { 0.0, -10_000.0 }, 0, 1000.0);
            Assert.IsTrue(res.Converged);
            Assert.Greater(Math.Abs(res.BranchCurrentA[0]), 5.0);
            Assert.IsTrue(res.BranchOverloaded[0]);
            var ok = DcFlow.Solve(2, new[] { B(0, 1, 1.0, 500.0) }, new[] { 0.0, -10_000.0 }, 0, 1000.0);
            Assert.IsFalse(ok.BranchOverloaded[0]);
        }

        [Test]
        public void ResultsAreIdenticalAcrossRunsAndBranchOrder()
        {
            var a = new List<FlowBranch> { B(0, 1, 2.0), B(1, 2, 3.0), B(2, 0, 4.0), B(2, 3, 1.5) };
            var b = new List<FlowBranch> { a[3], a[1], a[0], a[2] };
            var inj = new[] { 0.0, -3000.0, 2000.0, -4000.0 };
            var r1 = DcFlow.Solve(4, a, inj, 0, 400.0);
            for (int k = 0; k < 100; k++)
                Assert.AreEqual(r1.TotalLossW, DcFlow.Solve(4, a, inj, 0, 400.0).TotalLossW);
            Assert.AreEqual(r1.TotalLossW, DcFlow.Solve(4, b, inj, 0, 400.0).TotalLossW, 1e-9);
        }

        [Test]
        public void NoSlackIsDiagnosed()
        {
            var res = DcFlow.Solve(2, new[] { B(0, 1, 1.0) }, new[] { 0.0, 0.0 }, -1, 100.0);
            Assert.IsFalse(res.Converged);
            StringAssert.Contains("slack", res.Diagnostics[0]);
        }
    }
}
