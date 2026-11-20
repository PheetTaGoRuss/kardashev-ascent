using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Electrical
{
    /// <summary>
    /// A conductor metal (workflow W-12). Data, not code: add a row to add a metal. Values at 20 C.
    /// Copper: the International Annealed Copper Standard, 1.7241e-8 ohm m, alpha 0.00393 /K, 8890 kg/m3 [verified: ASTM B193 table, copper.org].
    /// Aluminium: 61 % IACS, so 1.7241e-8 / 0.61 = 2.8264e-8 ohm m [estimated from the IACS ratio, IEC 60889 text not read], density 2703 kg/m3
    /// [verified: NBS handbook tables], alpha 0.00403 /K [recalled, TBD].
    /// </summary>
    public sealed class ConductorMaterial
    {
        public readonly string Name;
        /// <summary>Resistivity at 20 C, ohm m.</summary>
        public readonly double Rho20;
        /// <summary>Temperature coefficient of resistivity at 20 C, 1/K.</summary>
        public readonly double Alpha20;
        /// <summary>Density, kg/m3.</summary>
        public readonly double Density;

        public ConductorMaterial(string name, double rho20, double alpha20, double density)
        {
            Name = name; Rho20 = rho20; Alpha20 = alpha20; Density = density;
        }

        public static readonly ConductorMaterial Copper = new ConductorMaterial("Copper", 1.7241e-8, 0.00393, 8890.0);
        public static readonly ConductorMaterial Aluminium = new ConductorMaterial("Aluminium", 1.7241e-8 / 0.61, 0.00403, 2703.0);

        /// <summary>Resistivity at a temperature: rho20 (1 + alpha (T - 20)).</summary>
        public double Rho(double celsius) => Rho20 * (1.0 + Alpha20 * (celsius - 20.0));
    }

    /// <summary>
    /// A cable product (W-13): metal, cross-section and how many conductors carry the loop current. The player picks a spec, not a
    /// number. Resistance follows R = rho L / A in SI units; a DC link with a return conductor has two in the loop. Current rating is
    /// data (game rule, TBD) until thermal limits are modelled.
    /// </summary>
    public sealed class CableSpec
    {
        public readonly string Name;
        public readonly ConductorMaterial Material;
        /// <summary>Cross-section of one conductor, m2 (use <see cref="FromSquareMillimetres"/> for mm2).</summary>
        public readonly double AreaM2;
        /// <summary>Conductors in the current loop (2 for DC out and back; the one-way model uses 1).</summary>
        public readonly int ConductorsInLoop;
        /// <summary>Highest continuous current, A.</summary>
        public readonly double RatingA;

        public CableSpec(string name, ConductorMaterial material, double areaM2, int conductorsInLoop, double ratingA)
        {
            Name = name; Material = material; AreaM2 = areaM2; ConductorsInLoop = conductorsInLoop; RatingA = ratingA;
        }

        public static CableSpec FromSquareMillimetres(string name, ConductorMaterial m, double mm2, int conductors, double ratingA) =>
            new CableSpec(name, m, mm2 * 1e-6, conductors, ratingA);

        /// <summary>Loop resistance of a length of this cable, ohm, at a conductor temperature.</summary>
        public double ResistanceOhm(double lengthM, double celsius = 20.0) => Material.Rho(celsius) * lengthM / AreaM2 * ConductorsInLoop;

        /// <summary>Metal in a length of this cable, kg (all conductors): density x length x area x conductors.</summary>
        public double ConductorMassKg(double lengthM) => Material.Density * lengthM * AreaM2 * ConductorsInLoop;
    }

    /// <summary>One cable as the solver sees it.</summary>
    public readonly struct FlowBranch
    {
        public readonly int A, B;
        public readonly double ResistanceOhm;
        public readonly double RatingA;
        public FlowBranch(int a, int b, double resistanceOhm, double ratingA) { A = a; B = b; ResistanceOhm = resistanceOhm; RatingA = ratingA; }
    }

    /// <summary>Result of a DC nodal solve (W-14).</summary>
    public sealed class DcFlowResult
    {
        public bool Converged;
        /// <summary>Why the solve failed or what looks wrong, in plain words; empty when all is well.</summary>
        public readonly List<string> Diagnostics = new List<string>();
        public double[] NodeVoltageV;
        /// <summary>Current in each branch from A to B, A.</summary>
        public double[] BranchCurrentA;
        public bool[] BranchOverloaded;
        public double[] BranchLossW;
        public double TotalLossW;
        /// <summary>Power the slack node supplies (negative when it absorbs), W.</summary>
        public double SlackPowerW;
        public int Iterations;
    }

    /// <summary>
    /// DC nodal power flow for one network (W-14, director decision D-W1: real flow, no dispatch). Generators and loads are fixed
    /// power injections (positive generates); one slack node holds its voltage and balances the rest. Solves G V = I with I = P / V
    /// by fixed-point iteration, each step a dense Cholesky solve (n is a few hundred at most; sparse only if profiled). Nodes are
    /// ordered by index, so results do not depend on hashing. Beyond the line's transfer limit (about V^2 / 4R) it reports a
    /// collapse instead of returning NaN.
    /// </summary>
    public static class DcFlow
    {
        public const double MinResistanceOhm = 1e-9;

        public static DcFlowResult Solve(int nodeCount, IReadOnlyList<FlowBranch> branches, double[] injectionW, int slack, double slackVoltageV,
                                         int maxIterations = 100, double tolerance = 1e-10)
        {
            var r = new DcFlowResult
            {
                NodeVoltageV = new double[nodeCount],
                BranchCurrentA = new double[branches.Count],
                BranchOverloaded = new bool[branches.Count],
                BranchLossW = new double[branches.Count]
            };
            if (slack < 0 || slack >= nodeCount || slackVoltageV <= 0.0)
            {
                r.Diagnostics.Add("No slack node: a network needs one grid-forming node to hold the voltage.");
                return r;
            }
            // Conductance matrix over non-slack nodes.
            var index = new int[nodeCount];
            int m = 0;
            for (int i = 0; i < nodeCount; i++) index[i] = i == slack ? -1 : m++;
            var g = new double[m, m];
            var gs = new double[m];   // conductance to the slack, per free node
            for (int k = 0; k < branches.Count; k++)
            {
                var br = branches[k];
                double res = br.ResistanceOhm;
                if (res < MinResistanceOhm) { res = MinResistanceOhm; r.Diagnostics.Add("A cable has about zero resistance."); }
                double c = 1.0 / res;
                int a = index[br.A], b = index[br.B];
                if (a >= 0) g[a, a] += c;
                if (b >= 0) g[b, b] += c;
                if (a >= 0 && b >= 0) { g[a, b] -= c; g[b, a] -= c; }
                if (a >= 0 && br.B == slack) gs[a] += c;
                if (b >= 0 && br.A == slack) gs[b] += c;
            }
            double[,] chol = Cholesky(g, m);
            if (chol == null)
            {
                r.Diagnostics.Add("Part of the network is not connected to the slack node, so its voltage is undefined.");
                return r;
            }
            var v = new double[m];
            for (int i = 0; i < m; i++) v[i] = slackVoltageV;
            var rhs = new double[m];
            for (int it = 1; it <= maxIterations; it++)
            {
                r.Iterations = it;
                for (int i = 0; i < nodeCount; i++)
                {
                    int f = index[i];
                    if (f < 0) continue;
                    if (v[f] <= 0.0) { r.Diagnostics.Add("Voltage collapse: the load is beyond what the line can carry."); return r; }
                    rhs[f] = injectionW[i] / v[f] + gs[f] * slackVoltageV;
                }
                var next = CholeskySolve(chol, rhs, m);
                double change = 0.0;
                for (int i = 0; i < m; i++) change = Math.Max(change, Math.Abs(next[i] - v[i]) / slackVoltageV);
                v = next;
                if (change < tolerance) { r.Converged = true; break; }
                bool bad = false;
                for (int i = 0; i < m; i++) if (double.IsNaN(v[i]) || v[i] <= 0.0 || v[i] > 1e3 * slackVoltageV) bad = true;
                if (bad) { r.Diagnostics.Add("Voltage collapse: the load is beyond what the line can carry (near V^2/4R)."); return r; }
            }
            if (!r.Converged) { r.Diagnostics.Add("The flow did not settle: the load is at or beyond the line's transfer limit."); return r; }

            for (int i = 0; i < nodeCount; i++) r.NodeVoltageV[i] = index[i] < 0 ? slackVoltageV : v[index[i]];
            double slackCurrent = 0.0;
            for (int k = 0; k < branches.Count; k++)
            {
                var br = branches[k];
                double res = Math.Max(br.ResistanceOhm, MinResistanceOhm);
                double cur = (r.NodeVoltageV[br.A] - r.NodeVoltageV[br.B]) / res;
                r.BranchCurrentA[k] = cur;
                r.BranchLossW[k] = cur * cur * res;
                r.TotalLossW += r.BranchLossW[k];
                if (br.RatingA > 0.0 && Math.Abs(cur) > br.RatingA) r.BranchOverloaded[k] = true;
                if (br.A == slack) slackCurrent += cur;
                if (br.B == slack) slackCurrent -= cur;
            }
            r.SlackPowerW = slackCurrent * slackVoltageV;
            return r;
        }

        static double[,] Cholesky(double[,] a, int n)
        {
            var l = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j <= i; j++)
                {
                    double s = a[i, j];
                    for (int k = 0; k < j; k++) s -= l[i, k] * l[j, k];
                    if (i == j)
                    {
                        if (s <= 1e-30) return null;
                        l[i, i] = Math.Sqrt(s);
                    }
                    else l[i, j] = s / l[j, j];
                }
            return l;
        }

        static double[] CholeskySolve(double[,] l, double[] b, int n)
        {
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = b[i];
                for (int k = 0; k < i; k++) s -= l[i, k] * y[k];
                y[i] = s / l[i, i];
            }
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double s = y[i];
                for (int k = i + 1; k < n; k++) s -= l[k, i] * x[k];
                x[i] = s / l[i, i];
            }
            return x;
        }
    }
}
