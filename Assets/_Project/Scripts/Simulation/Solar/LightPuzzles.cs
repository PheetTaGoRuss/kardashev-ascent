using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>
    /// The light puzzles (docs/design/solar-measurements.md, stage D). The readings come from the game's own model
    /// (<see cref="Atmosphere"/>), so working them out gives back the planet's value:
    /// - Haze, Langley method: DNI = G₀·e^(−τ·m), so τ = ln(G₀ / DNI) / m (or the slope of ln DNI against m).
    /// - Diffuse share: DHI = k·G₀·sin h·(1 − e^(−τ·m)) and e^(−τ·m) = DNI / G₀, so k = DHI / (G₀·sin h·(1 − DNI / G₀)).
    /// - Clouds: the share of clear-sky light that got through, f = Σ measured / Σ clear-sky over the logged days.
    /// Answers are checked against what the readings give (rounding is not the player's fault).
    /// </summary>
    [Serializable]
    public sealed class ParameterPuzzle
    {
        /// <summary>Within this share of the answer: precise; within the rough share: rough (half the range kept). TBD.</summary>
        public const double PreciseShare = 0.05, RoughShare = 0.15;

        public SolarParameter Parameter;
        public int Tile;
        /// <summary>Starlight above the air, W/m².</summary>
        public double G0;
        public double[] AltitudeDeg = new double[0];
        public double[] AirMass = new double[0];
        public double[] Dni = new double[0];
        public double[] Dhi = new double[0];
        public double[] ClearKwh = new double[0];
        public double[] MeasuredKwh = new double[0];
        public int Attempts;

        /// <summary>Sun altitudes the robot reads at, degrees.</summary>
        static readonly double[] Altitudes = { 60.0, 40.0, 25.0, 15.0 };
        /// <summary>Day-to-day weather around the season's mean (they average to exactly 1).</summary>
        static readonly double[] Weather = { 0.80, 1.15, 0.95, 1.20, 0.90 };

        public static ParameterPuzzle Haze(int tile, double g0, double opticalDepth, double pressureRatio)
        {
            var p = new ParameterPuzzle { Parameter = SolarParameter.OpticalDepth, Tile = tile, G0 = Math.Round(g0) };
            p.AltitudeDeg = (double[])Altitudes.Clone();
            p.AirMass = new double[Altitudes.Length];
            p.Dni = new double[Altitudes.Length];
            for (int i = 0; i < Altitudes.Length; i++)
            {
                p.AirMass[i] = Math.Round(Atmosphere.AirMassKastenYoung(90.0 - Altitudes[i], pressureRatio), 3);
                p.Dni[i] = Math.Round(Atmosphere.DirectNormal(p.G0, opticalDepth, p.AirMass[i]));
            }
            return p;
        }

        public static ParameterPuzzle Diffuse(int tile, double g0, double opticalDepth, double diffuseK, double pressureRatio)
        {
            var p = new ParameterPuzzle { Parameter = SolarParameter.DiffuseShare, Tile = tile, G0 = Math.Round(g0) };
            int n = 3;
            p.AltitudeDeg = new double[n];
            p.AirMass = new double[n];
            p.Dni = new double[n];
            p.Dhi = new double[n];
            for (int i = 0; i < n; i++)
            {
                double h = Altitudes[i];
                p.AltitudeDeg[i] = h;
                p.AirMass[i] = Math.Round(Atmosphere.AirMassKastenYoung(90.0 - h, pressureRatio), 3);
                p.Dni[i] = Math.Round(Atmosphere.DirectNormal(p.G0, opticalDepth, p.AirMass[i]));
                p.Dhi[i] = Math.Round(Atmosphere.DiffuseHorizontal(p.G0, Math.Sin(h * Math.PI / 180.0), opticalDepth, p.AirMass[i], diffuseK), 1);
            }
            return p;
        }

        /// <param name="clearKwhPerDay">Clear-sky light on the ground per day, kWh/m².</param>
        /// <param name="cloudFactor">Share of it that gets through this season.</param>
        public static ParameterPuzzle Clouds(int tile, double clearKwhPerDay, double cloudFactor)
        {
            var p = new ParameterPuzzle { Parameter = SolarParameter.Clouds, Tile = tile };
            p.ClearKwh = new double[Weather.Length];
            p.MeasuredKwh = new double[Weather.Length];
            for (int d = 0; d < Weather.Length; d++)
            {
                double clear = clearKwhPerDay * (1.0 + 0.02 * (d - 2));
                p.ClearKwh[d] = Math.Round(clear, 2);
                p.MeasuredKwh[d] = Math.Round(clear * cloudFactor * Weather[d], 2);
            }
            return p;
        }

        /// <summary>The value the readings give.</summary>
        public double Answer
        {
            get
            {
                switch (Parameter)
                {
                    case SolarParameter.OpticalDepth:
                    {
                        // Least-squares slope of ln DNI against m (the Langley plot): τ = −slope.
                        int n = AirMass.Length;
                        double sx = 0, sy = 0, sxx = 0, sxy = 0;
                        for (int i = 0; i < n; i++)
                        {
                            double x = AirMass[i], y = Math.Log(Dni[i]);
                            sx += x; sy += y; sxx += x * x; sxy += x * y;
                        }
                        return -(n * sxy - sx * sy) / (n * sxx - sx * sx);
                    }
                    case SolarParameter.DiffuseShare:
                    {
                        double sum = 0.0;
                        for (int i = 0; i < Dhi.Length; i++)
                            sum += Dhi[i] / (G0 * Math.Sin(AltitudeDeg[i] * Math.PI / 180.0) * (1.0 - Dni[i] / G0));
                        return sum / Dhi.Length;
                    }
                    case SolarParameter.Clouds:
                    {
                        double clear = 0.0, measured = 0.0;
                        for (int d = 0; d < ClearKwh.Length; d++) { clear += ClearKwh[d]; measured += MeasuredKwh[d]; }
                        return clear > 0.0 ? measured / clear : 1.0;
                    }
                    default:
                        return double.NaN;
                }
            }
        }

        public LatitudePuzzle.Grade Check(double value)
        {
            double answer = Answer;
            double error = Math.Abs(value - answer) / Math.Max(1e-9, Math.Abs(answer));
            return error <= PreciseShare ? LatitudePuzzle.Grade.Precise
                 : error <= RoughShare ? LatitudePuzzle.Grade.Rough : LatitudePuzzle.Grade.Wrong;
        }
    }
}
