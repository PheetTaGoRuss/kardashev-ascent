using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// The puzzle every experiment ends in (RS-13, decided 2026-11-23, director delegated): "fit the data". The instrument returns a noisy
    /// series that is physically correct for the planet; the player moves one to three handles of a simple model and sees the residuals.
    /// There is no pass or fail: the quality of the fit sets how wide the result's range stays (a good fit narrows it to the instrument's
    /// precision). "Fit for me" gives the least-squares answer, with a wider range than a good hand fit. All numbers are game rules, TBD.
    /// </summary>
    public sealed class FitPuzzle
    {
        /// <summary>The narrowest share of the original range a result can keep: the instrument's precision (game rule, TBD).</summary>
        public const double PrecisionShare = 0.10;
        /// <summary>The share "fit for me" leaves (game rule, TBD): worse than a good hand fit, better than a poor one.</summary>
        public const double AutoFitShare = 0.30;

        public ExperimentKind Kind { get; private set; }
        public string Physics { get; private set; }
        public string XLabel { get; private set; }
        public string YLabel { get; private set; }
        public double[] X { get; private set; }
        public double[] Y { get; private set; }
        public string[] ParamNames { get; private set; }
        public double[] Min { get; private set; }
        public double[] Max { get; private set; }
        public double[] Start { get; private set; }
        /// <summary>The curve a set of handles draws at x (null for puzzles with no curve: the relief threshold and the ore samples).</summary>
        public Func<double[], double, double> Curve { get; private set; }
        /// <summary>The least-squares handles (or the best choice for a puzzle with no curve).</summary>
        public double[] Best { get; private set; }
        /// <summary>Share of the original range kept for a set of handles: 0.1 (a perfect fit) to 1 (a poor one).</summary>
        public Func<double[], double> ShareOf { get; private set; }
        /// <summary>Robot hours a set of handles costs on top of the experiment (only the ore assay: each sample takes time).</summary>
        public Func<double[], double> ExtraHours { get; private set; }

        public double Share(double[] p) => Math.Max(PrecisionShare, Math.Min(1.0, ShareOf(p)));

        /// <summary>A fit's share from its residual: 1 for a poor fit, the precision for the least-squares one.</summary>
        static double ShareFromResidual(double rms, double bestRms)
        {
            double r = bestRms > 1e-12 ? rms / bestRms : 1.0 + rms * 1e6;
            return PrecisionShare + (1.0 - PrecisionShare) * (1.0 - Math.Exp(-2.0 * Math.Max(0.0, r - 1.0)));
        }

        public double Rms(double[] p)
        {
            if (Curve == null) return 0.0;
            double s = 0.0;
            for (int i = 0; i < X.Length; i++) { double d = Y[i] - Curve(p, X[i]); s += d * d; }
            return Math.Sqrt(s / X.Length);
        }

        // ---- makers ----

        /// <summary>Everything a puzzle needs to draw its data from the planet: the true values, as the simulation knows them.</summary>
        public struct Truth
        {
            public double PressurePa, ScaleHeightM, TemperatureMeanC, TemperatureAmpK, DayHours;
            public double ReliefMaxM, TideRangeM, TidePeriodHours, GravityMs2, WaveHeightM, WavePeriodS, BasinDepthM, OreGrade;
        }

        static FitPuzzle Curve1(ExperimentKind kind, string physics, string xl, string yl, double[] x, double[] truth, string[] names, double[] min, double[] max, double[] start,
                                Func<double[], double, double> curve, double noise, int seed)
        {
            var rng = new Rng(seed);
            var y = new double[x.Length];
            for (int i = 0; i < x.Length; i++) y[i] = curve(truth, x[i]) + rng.Gauss() * noise;
            var puzzle = new FitPuzzle { Kind = kind, Physics = physics, XLabel = xl, YLabel = yl, X = x, Y = y, ParamNames = names, Min = min, Max = max, Start = start, Curve = curve };
            puzzle.Best = LeastSquares(curve, x, y, (double[])start.Clone(), min, max);
            double best = puzzle.Rms(puzzle.Best);
            puzzle.ShareOf = p => ShareFromResidual(puzzle.Rms(p), best);
            return puzzle;
        }

        static double[] Range(double from, double to, int n)
        {
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = from + (to - from) * i / (n - 1);
            return a;
        }

        public static FitPuzzle Make(ExperimentKind kind, Truth t, int seed)
        {
            switch (kind)
            {
                case ExperimentKind.AirPressure:
                {
                    // barometer readings at several heights: p = p0 exp(-h / H), H = R T / (M g) (the isothermal barometric formula)
                    double p0 = Math.Max(1.0, t.PressurePa) / 1000.0, H = Math.Max(100.0, t.ScaleHeightM);
                    return Curve1(kind, "p = p0 exp(-h / H), with the scale height H = R T / (M g): the air thins by a factor e every H metres.",
                        "height above the datum, m", "pressure, kPa", Range(0.0, 3.0 * H, 9), new[] { p0, H }, new[] { "datum pressure p0, kPa", "scale height H, m" },
                        new[] { p0 * 0.1, H * 0.2 }, new[] { p0 * 3.0, H * 3.0 }, new[] { p0 * 0.6, H * 0.6 },
                        (p, x) => p[0] * Math.Exp(-x / p[1]), 0.02 * p0, seed);
                }
                case ExperimentKind.SurfaceTemperature:
                {
                    double day = Math.Max(1.0, t.DayHours);
                    return Curve1(kind, "T = mean + amplitude sin(2 pi (t - phase) / day): the ground warms by day and cools by night; the amplitude is the night margin.",
                        "hours into the day", "temperature, C", Range(0.0, day, 25), new[] { t.TemperatureMeanC, t.TemperatureAmpK, day * 0.25 },
                        new[] { "mean, C", "amplitude, K", "phase, h" }, new[] { t.TemperatureMeanC - 80.0, 0.0, 0.0 }, new[] { t.TemperatureMeanC + 80.0, 120.0, day },
                        new[] { t.TemperatureMeanC - 20.0, t.TemperatureAmpK * 0.4, day * 0.5 },
                        (p, x) => p[0] + p[1] * Math.Sin(2.0 * Math.PI * (x - p[2]) / day), 1.0, seed);
                }
                case ExperimentKind.TideGauge:
                {
                    double period = Math.Max(1.0, t.TidePeriodHours), a = Math.Max(0.01, t.TideRangeM / 2.0);
                    return Curve1(kind, "h = A cos(2 pi t / T - phase): the main tide is a wave with the moon's own period T; the range is twice the amplitude A.",
                        "hours", "sea level, m", Range(0.0, 2.0 * period, 40), new[] { a, period * 0.3 }, new[] { "amplitude A, m", "phase, h" },
                        new[] { 0.0, 0.0 }, new[] { a * 3.0, period }, new[] { a * 0.5, period * 0.6 },
                        (p, x) => p[0] * Math.Cos(2.0 * Math.PI * (x - p[1]) / period), 0.05 * a, seed);
                }
                case ExperimentKind.Gravity:
                {
                    double g = Math.Max(0.1, t.GravityMs2);
                    return Curve1(kind, "T = 2 pi sqrt(L / g): a longer pendulum swings slower; g follows from the slope.",
                        "pendulum length, m", "period, s", Range(0.2, 1.6, 8), new[] { g }, new[] { "g, m/s2" }, new[] { g * 0.2 }, new[] { g * 3.0 }, new[] { g * 0.6 },
                        (p, x) => 2.0 * Math.PI * Math.Sqrt(x / Math.Max(0.01, p[0])), 0.01 * 2.0 * Math.PI * Math.Sqrt(1.0 / g), seed);
                }
                case ExperimentKind.BoilingPoint:
                {
                    // vapour pressure by the Antoine form ln P = a - b / (T + 233.4), fitted to heated-water readings
                    const double B = 3984.0;   // ln-form constant: 1730.63 ln10
                    double aTrue = Math.Log(101.325) + B / (100.0 + 233.426);
                    return Curve1(kind, "ln P = a - b / (T + 233 C): water boils where its vapour pressure equals the air's; read the temperature at the air pressure.",
                        "water temperature, C", "vapour pressure, kPa", Range(20.0, 120.0, 11), new[] { aTrue, B }, new[] { "a", "b" },
                        new[] { aTrue - 3.0, B * 0.5 }, new[] { aTrue + 3.0, B * 1.6 }, new[] { aTrue - 1.0, B * 0.8 },
                        (p, x) => Math.Exp(p[0] - p[1] / (x + 233.426)), 0.5, seed);
                }
                case ExperimentKind.SeismicSurvey:
                {
                    const double v = 3000.0;   // rock P-wave speed, m/s (game rule)
                    return Curve1(kind, "t = 2 d / v: an echo from a layer at depth d returns after twice the travel time; the slope gives the rock's wave speed v.",
                        "layer depth, m", "echo time, s", Range(100.0, 2000.0, 9), new[] { v }, new[] { "wave speed v, m/s" }, new[] { 500.0 }, new[] { 8000.0 }, new[] { 2000.0 },
                        (p, x) => 2.0 * x / Math.Max(1.0, p[0]), 0.01, seed);
                }
                case ExperimentKind.WaveBuoy:
                {
                    double h = Math.Max(0.05, t.WaveHeightM), period = Math.Max(2.0, t.WavePeriodS);
                    return Curve1(kind, "heave = (H / 2) sin(2 pi t / T - phase): the buoy rises and falls with the swell; H is the wave height.",
                        "seconds", "heave, m", Range(0.0, 3.0 * period, 40), new[] { h, period * 0.2 }, new[] { "wave height H, m", "phase, s" },
                        new[] { 0.0, 0.0 }, new[] { h * 3.0, period }, new[] { h * 0.5, period * 0.5 },
                        (p, x) => p[0] / 2.0 * Math.Sin(2.0 * Math.PI * (x - p[1]) / period), 0.05 * h, seed);
                }
                case ExperimentKind.BasinSurvey:
                {
                    double d = Math.Max(1.0, t.BasinDepthM);
                    return Curve1(kind, "depth = D (1 - (x / R)^2): a bay is shallow at its shores and deepest in the middle; D and the half-width R set its volume.",
                        "across the bay, km", "depth, m", Range(-4.0, 4.0, 15), new[] { d, 4.5 }, new[] { "depth D, m", "half-width R, km" },
                        new[] { d * 0.2, 2.0 }, new[] { d * 3.0, 9.0 }, new[] { d * 0.6, 3.0 },
                        (p, x) => Math.Max(0.0, p[0] * (1.0 - x * x / (p[1] * p[1]))), 0.03 * d, seed);
                }
                case ExperimentKind.ReliefSurvey:
                    return MakeRelief(t, seed);
                default:
                    return MakeAssay(t, seed);
            }
        }

        /// <summary>The altimeter profile: a smooth land surface, noise, and a few false spikes. The handle is the spike rejection threshold, in standard deviations above the mean.</summary>
        static FitPuzzle MakeRelief(Truth t, int seed)
        {
            var rng = new Rng(seed);
            const int N = 60;
            double peak = Math.Max(100.0, t.ReliefMaxM);
            var x = Range(0.0, 59.0, N);
            var y = new double[N];
            double trueMax = 0.0;
            for (int i = 0; i < N; i++)
            {
                double s = 0.5 + 0.5 * Math.Sin(i * 0.21 + 1.0) * Math.Cos(i * 0.07);
                y[i] = 0.55 * peak * s;
                trueMax = Math.Max(trueMax, y[i]);
            }
            double highest = trueMax;
            for (int i = 0; i < N; i++) y[i] += rng.Gauss() * 0.02 * peak;
            for (int k = 0; k < 4; k++) y[3 + rng.Next(N - 6)] += peak * (1.5 + rng.Uniform());   // spikes
            double mean = 0.0, sd = 0.0;
            foreach (double v in y) mean += v;
            mean /= N;
            foreach (double v in y) sd += (v - mean) * (v - mean);
            sd = Math.Sqrt(sd / N);
            double ReadMax(double k) { double m = double.MinValue; foreach (double v in y) if (v <= mean + k * sd) m = Math.Max(m, v); return m; }
            var puzzle = new FitPuzzle
            {
                Kind = ExperimentKind.ReliefSurvey, Physics = "A laser altimeter sometimes returns a false spike (a cloud, a glint). Reject readings above mean + k standard deviations, then read the highest point; too high a threshold keeps spikes, too low cuts real peaks.",
                XLabel = "distance along the track", YLabel = "height, m", X = x, Y = y,
                ParamNames = new[] { "threshold k, standard deviations" }, Min = new[] { 0.2 }, Max = new[] { 6.0 }, Start = new[] { 6.0 }, Best = new[] { 1.8 },
            };
            puzzle.ShareOf = p => { double err = Math.Abs(ReadMax(p[0]) - highest) / Math.Max(1.0, highest); return PrecisionShare + (1.0 - PrecisionShare) * (1.0 - Math.Exp(-8.0 * err)); };
            double bestErr = double.MaxValue;
            for (double k = 0.5; k <= 5.0; k += 0.05) { double e = Math.Abs(ReadMax(k) - highest); if (e < bestErr) { bestErr = e; puzzle.Best = new[] { k }; } }
            puzzle.Thresholds = (mean, sd);
            return puzzle;
        }

        /// <summary>Mean and standard deviation of a relief profile (for drawing the threshold line).</summary>
        public (double Mean, double Sd) Thresholds { get; private set; }

        /// <summary>The ore assay: noisy samples of a grade. The handle is how many samples to take; each costs robot time; the range is t s / sqrt(n).</summary>
        static FitPuzzle MakeAssay(Truth t, int seed)
        {
            var rng = new Rng(seed);
            const int Max = 40;
            double grade = Math.Max(0.01, t.OreGrade);
            var x = Range(1.0, Max, Max);
            var y = new double[Max];
            for (int i = 0; i < Max; i++) y[i] = Math.Max(0.0, grade * (1.0 + 0.35 * rng.Gauss()));
            var puzzle = new FitPuzzle
            {
                Kind = ExperimentKind.OreAssay, Physics = "mean grade = average of n samples, plus or minus t s / sqrt(n): more samples narrow the range, but each takes robot time (0.5 h).",
                XLabel = "sample number", YLabel = "grade", X = x, Y = y, ParamNames = new[] { "samples n" }, Min = new[] { 1.0 }, Max = new[] { (double)Max }, Start = new[] { 4.0 }, Best = new[] { 20.0 },
            };
            puzzle.ShareOf = p =>
            {
                int n = Math.Max(2, Math.Min(Max, (int)Math.Round(p[0])));
                double mean = 0.0; for (int i = 0; i < n; i++) mean += y[i]; mean /= n;
                double s = 0.0; for (int i = 0; i < n; i++) s += (y[i] - mean) * (y[i] - mean);
                s = Math.Sqrt(s / (n - 1));
                double half = StudentT95(n - 1) * s / Math.Sqrt(n);
                return half / (0.25 * Math.Max(1e-9, mean));   // against the 25 % range an unassayed grade has
            };
            puzzle.ExtraHours = p => 0.5 * Math.Round(p[0]);
            return puzzle;
        }

        /// <summary>Two-sided 95 % Student t for a few degrees of freedom (table values), 1.96 for many.</summary>
        public static double StudentT95(int df)
        {
            double[] tbl = { 0, 12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228 };
            if (df < 1) return 12.706;
            if (df <= 10) return tbl[df];
            if (df <= 20) return 2.228 - (df - 10) * 0.014;
            if (df <= 30) return 2.086 - (df - 20) * 0.004;
            return 2.0;
        }

        // ---- the least-squares solver ----

        /// <summary>Levenberg-Marquardt with a numeric Jacobian, clamped to the handle ranges. Up to three handles.</summary>
        public static double[] LeastSquares(Func<double[], double, double> f, double[] x, double[] y, double[] p, double[] min, double[] max)
        {
            int n = p.Length;
            double lambda = 1e-2;
            double Cost(double[] q) { double s = 0.0; for (int i = 0; i < x.Length; i++) { double d = y[i] - f(q, x[i]); s += d * d; } return s; }
            double cost = Cost(p);
            for (int iter = 0; iter < 200; iter++)
            {
                var jtj = new double[n, n];
                var jtr = new double[n];
                for (int i = 0; i < x.Length; i++)
                {
                    double r = y[i] - f(p, x[i]);
                    var J = new double[n];
                    for (int k = 0; k < n; k++)
                    {
                        double h = Math.Max(1e-6, Math.Abs(p[k]) * 1e-6);
                        var q = (double[])p.Clone(); q[k] += h;
                        J[k] = (f(q, x[i]) - f(p, x[i])) / h;
                    }
                    for (int a = 0; a < n; a++) { jtr[a] += J[a] * r; for (int b = 0; b < n; b++) jtj[a, b] += J[a] * J[b]; }
                }
                bool improved = false;
                for (int tries = 0; tries < 12 && !improved; tries++)
                {
                    var m = (double[,])jtj.Clone();
                    for (int a = 0; a < n; a++) m[a, a] += lambda * (jtj[a, a] + 1e-12);
                    double[] step = Solve(m, jtr);
                    if (step == null) { lambda *= 10.0; continue; }
                    var cand = new double[n];
                    for (int a = 0; a < n; a++) cand[a] = Math.Max(min[a], Math.Min(max[a], p[a] + step[a]));
                    double c = Cost(cand);
                    if (c < cost) { cost = c; p = cand; lambda = Math.Max(1e-9, lambda / 5.0); improved = true; }
                    else lambda *= 10.0;
                }
                if (!improved) break;
            }
            return p;
        }

        static double[] Solve(double[,] a, double[] b)
        {
            int n = b.Length;
            var m = (double[,])a.Clone();
            var v = (double[])b.Clone();
            for (int c = 0; c < n; c++)
            {
                int piv = c;
                for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[piv, c])) piv = r;
                if (Math.Abs(m[piv, c]) < 1e-300) return null;
                if (piv != c) { for (int k = 0; k < n; k++) { double tmp = m[c, k]; m[c, k] = m[piv, k]; m[piv, k] = tmp; } double tv = v[c]; v[c] = v[piv]; v[piv] = tv; }
                for (int r = c + 1; r < n; r++)
                {
                    double fct = m[r, c] / m[c, c];
                    for (int k = c; k < n; k++) m[r, k] -= fct * m[c, k];
                    v[r] -= fct * v[c];
                }
            }
            var x = new double[n];
            for (int r = n - 1; r >= 0; r--)
            {
                double s = v[r];
                for (int k = r + 1; k < n; k++) s -= m[r, k] * x[k];
                x[r] = s / m[r, r];
            }
            return x;
        }

        /// <summary>A small deterministic random source (the same data every run).</summary>
        sealed class Rng
        {
            uint state;
            public Rng(int seed) { state = unchecked((uint)seed * 2654435761u + 12345u); if (state == 0) state = 1; }
            public uint NextU() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
            public double Uniform() => (NextU() & 0xFFFFFF) / (double)0x1000000;
            public int Next(int n) => (int)(NextU() % (uint)n);
            public double Gauss() { double u = Math.Max(1e-12, Uniform()), v = Uniform(); return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v); }
        }
    }
}
