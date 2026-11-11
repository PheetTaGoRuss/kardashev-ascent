using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>
    /// The light puzzles (docs/design/solar-measurements.md, stage D). The readings come from the game's own model
    /// (<see cref="Atmosphere"/>), so working them out gives back the planet's value:
    /// - Haze, Langley method: DNI = G₀·e^(−τ·m), so τ = ln(G₀ / DNI) / m (or the slope of ln DNI against m).
    /// - Diffuse share: DHI = k·G₀·sin h·(1 − e^(−τ·m)) and e^(−τ·m) = DNI / G₀, so k = DHI / (G₀·sin h·(1 − DNI / G₀)).
    /// - Clouds: the share of clear-sky light that got through, f = Σ measured / Σ clear-sky over the logged days.
    /// Stage E (ground and heat):
    /// - Ground reflectance: ρ = Σ reflected / Σ incoming, from light sensors facing down and up.
    /// - Air temperature: the daily mean of eight weather-station readings, three hours apart.
    /// - Dust: the share of light a dusty reference cell loses next to a clean one, 1 − dusty / clean.
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
        public double[] Incoming = new double[0];
        public double[] Reflected = new double[0];
        public double[] HourOfDay = new double[0];
        public double[] TemperatureC = new double[0];
        public double[] CleanW = new double[0];
        public double[] DustyW = new double[0];
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

        /// <summary>Air temperature: day–night swing around the mean, ± °C (placeholder until climate is modelled).</summary>
        public const double DaySwingC = 6.0;

        public static ParameterPuzzle Albedo(int tile, double g0, double albedo)
        {
            var p = new ParameterPuzzle { Parameter = SolarParameter.Albedo, Tile = tile, G0 = Math.Round(g0) };
            double[] share = { 0.62, 0.45, 0.22 };   // morning to afternoon: incoming light on flat ground, × G₀
            p.Incoming = new double[share.Length];
            p.Reflected = new double[share.Length];
            for (int i = 0; i < share.Length; i++)
            {
                p.Incoming[i] = Math.Round(g0 * share[i]);
                p.Reflected[i] = Math.Round(p.Incoming[i] * albedo, 1);
            }
            return p;
        }

        public static ParameterPuzzle AirTemperature(int tile, double meanC)
        {
            var p = new ParameterPuzzle { Parameter = SolarParameter.AirTemperature, Tile = tile };
            p.HourOfDay = new double[8];
            p.TemperatureC = new double[8];
            for (int i = 0; i < 8; i++)
            {
                double hour = 3.0 * i;   // warmest mid-afternoon
                p.HourOfDay[i] = hour;
                p.TemperatureC[i] = Math.Round(meanC + DaySwingC * Math.Cos(2.0 * Math.PI * (hour - 15.0) / 24.0), 1);
            }
            return p;
        }

        public static ParameterPuzzle Dust(int tile, double g0, double dustShare)
        {
            var p = new ParameterPuzzle { Parameter = SolarParameter.Dust, Tile = tile, G0 = Math.Round(g0) };
            double[] share = { 0.55, 0.70, 0.40 };
            p.CleanW = new double[share.Length];
            p.DustyW = new double[share.Length];
            for (int i = 0; i < share.Length; i++)
            {
                double clean = g0 * share[i] * 0.2;   // a 1 m² cell at 20 %
                p.CleanW[i] = Math.Round(clean, 1);
                p.DustyW[i] = Math.Round(clean * (1.0 - dustShare), 1);
            }
            return p;
        }

        /// <summary>Temperature and dust are checked in absolute terms (their answers can be near zero).</summary>
        public bool AbsoluteCheck => Parameter == SolarParameter.AirTemperature || Parameter == SolarParameter.Dust;

        /// <summary>Precise and rough limits: a share of the answer, or absolute (°C, or share of light for dust). TBD.</summary>
        /// <summary>Reflectance reads to two decimals, so 0.21 for 0.20 is already 5 % off: it gets a wider precise band. TBD.</summary>
        public const double AlbedoPreciseShare = 0.10;

        public double PreciseLimit => Parameter == SolarParameter.AirTemperature ? 1.0 : Parameter == SolarParameter.Dust ? 0.005
                                    : Parameter == SolarParameter.Albedo ? AlbedoPreciseShare : PreciseShare;
        public double RoughLimit => Parameter == SolarParameter.AirTemperature ? 3.0 : Parameter == SolarParameter.Dust ? 0.015 : RoughShare;

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
                    case SolarParameter.Albedo:
                    {
                        double incoming = 0.0, reflected = 0.0;
                        for (int i = 0; i < Incoming.Length; i++) { incoming += Incoming[i]; reflected += Reflected[i]; }
                        return incoming > 0.0 ? reflected / incoming : 0.0;
                    }
                    case SolarParameter.AirTemperature:
                    {
                        double sum = 0.0;
                        foreach (double t in TemperatureC) sum += t;
                        return TemperatureC.Length > 0 ? sum / TemperatureC.Length : 0.0;
                    }
                    case SolarParameter.Dust:
                    {
                        double clean = 0.0, dusty = 0.0;
                        for (int i = 0; i < CleanW.Length; i++) { clean += CleanW[i]; dusty += DustyW[i]; }
                        return clean > 0.0 ? 1.0 - dusty / clean : 0.0;
                    }
                    default:
                        return double.NaN;
                }
            }
        }

        public LatitudePuzzle.Grade Check(double value)
        {
            double answer = Answer;
            double error = AbsoluteCheck ? Math.Abs(value - answer) : Math.Abs(value - answer) / Math.Max(1e-9, Math.Abs(answer));
            return error <= PreciseLimit ? LatitudePuzzle.Grade.Precise
                 : error <= RoughLimit ? LatitudePuzzle.Grade.Rough : LatitudePuzzle.Grade.Wrong;
        }
    }
}
