using System;

namespace KardashevAscent.Simulation.Climate
{
    /// <summary>What the weather adds to the climatology at one place and time (Weather, PL-07, decided 2026-11-24, director delegated).</summary>
    public readonly struct WeatherAnomaly
    {
        /// <summary>Added to the cloud factor (the share of clear-sky light that gets through).</summary>
        public readonly double CloudFactor;
        /// <summary>Added to the air temperature, K.</summary>
        public readonly double TemperatureK;
        /// <summary>Added to the surface pressure, Pa.</summary>
        public readonly double PressurePa;
        /// <summary>The wind speed at 10 m, m/s (not an anomaly: wind has no climatology in the game).</summary>
        public readonly double WindMs;
        /// <summary>The wind is above the local 98th percentile for this latitude and month: a storm day.</summary>
        public readonly bool Storm;

        public WeatherAnomaly(double cloud, double temperature, double pressure, double wind, bool storm)
        { CloudFactor = cloud; TemperatureK = temperature; PressurePa = pressure; WindMs = wind; Storm = storm; }

        public static readonly WeatherAnomaly None = new WeatherAnomaly(0.0, 0.0, 0.0, 0.0, false);
    }

    /// <summary>
    /// Deterministic weather: smoothed noise around the climatology the game already has. Nothing is saved: the weather is a pure function of
    /// (seed, place, time). The noise is a sum of independent unit normals on coarse regions (about 1 500 km across, the scale of weather systems:
    /// synoptic scale, 1 000 to 2 000 km, Holton, An Introduction to Dynamic Meteorology) and time knots, blended by a triangular (linear) kernel
    /// in time and smoothstep bilinear weights in space, then rescaled so its standard deviation is exactly the table's. The knot spacing
    /// is solved so that the lag-1 (one day) autocorrelation matches the table. Mean of the anomaly is 0, so the long-run mean stays the climatology.
    /// Game rules, TBD: the region size, and that planets other than Earth use Earth's table (scaled by pressure for pressure).
    /// </summary>
    public sealed class WeatherModel
    {
        public const double RegionKm = 1500.0;
        public const double MinCellDeg = 8.0, MaxCellDeg = 45.0;
        /// <summary>The Gaussian z-score of the 98th percentile.</summary>
        public const double Z98 = 2.0537;
        public enum Channel { Cloud = 0, Temperature = 1, Pressure = 2, Wind = 3, Humidity = 4 }

        readonly int seed;
        readonly double cellRad;
        readonly double pressureScale;
        readonly bool cloudsKnown;

        /// <param name="radiusM">Planet radius, m.</param>
        /// <param name="surfacePressureEarth">Surface pressure, atmospheres; below <see cref="MinPressureEarth"/> there is no weather.</param>
        /// <param name="cloudsKnown">Earth-like planets have a cloud climatology; elsewhere the sky is clear and stays clear.</param>
        public WeatherModel(int seed, double radiusM, double surfacePressureEarth, bool cloudsKnown)
        {
            this.seed = seed;
            double cellDeg = Math.Max(MinCellDeg, Math.Min(MaxCellDeg, RegionKm * 1000.0 / Math.Max(1e5, radiusM) * 180.0 / Math.PI));
            cellRad = cellDeg * Math.PI / 180.0;
            pressureScale = Math.Min(1.0, Math.Max(0.0, surfacePressureEarth));
            HasWeather = surfacePressureEarth >= MinPressureEarth;
            this.cloudsKnown = cloudsKnown;
        }

        /// <summary>Below this a world has no air to speak of (a thousandth of Earth's): no weather. Game rule, TBD.</summary>
        public const double MinPressureEarth = 0.001;

        public bool HasWeather { get; }

        // ---- the noise ----

        static uint Mix(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
                return x;
            }
        }

        /// <summary>A repeatable standard normal for an integer cell of (channel, region lat, region lon, time knot).</summary>
        double Normal(int channel, int la, int lo, long knot)
        {
            unchecked
            {
                uint h = Mix((uint)seed ^ 0x9e3779b9u);
                h = Mix(h + (uint)channel * 0x85ebca6bu);
                h = Mix(h + (uint)la * 0xc2b2ae35u);
                h = Mix(h + (uint)lo * 0x27d4eb2fu);
                h = Mix(h + (uint)(knot & 0xffffffff) * 0x165667b1u);
                h = Mix(h + (uint)(knot >> 32));
                uint h2 = Mix(h ^ 0xa511e9b3u);
                double u1 = (h + 0.5) / 4294967296.0, u2 = (h2 + 0.5) / 4294967296.0;
                return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            }
        }

        /// <summary>Autocorrelation of the linear-interpolation noise with knot spacing T at lag tau (s = tau / T), phase-averaged.</summary>
        static double Rho(double s) => s <= 1.0 ? 1.0 - 1.5 * s * s + 0.75 * s * s * s : s <= 2.0 ? (2.0 - s) * (2.0 - s) * (2.0 - s) / 4.0 : 0.0;

        /// <summary>The knot spacing, hours, whose noise has this lag-1 (24 h) autocorrelation.</summary>
        public static double KnotHoursFor(double rho)
        {
            rho = Math.Max(0.02, Math.Min(0.995, rho));
            double lo = 0.0, hi = 2.0;
            for (int i = 0; i < 50; i++) { double mid = 0.5 * (lo + hi); if (Rho(mid) > rho) lo = mid; else hi = mid; }
            return 24.0 / (0.5 * (lo + hi));
        }

        static double Smooth(double u) => u * u * (3.0 - 2.0 * u);

        /// <summary>Unit-variance noise at a place and time with the given lag-1 autocorrelation.</summary>
        public double Noise(Channel channel, double latRad, double lonRad, double timeHours, double rho)
        {
            double knotH = KnotHoursFor(rho);
            double phase = (Mix((uint)seed + (uint)channel * 7919u) & 0xffff) / 65536.0 * knotH;
            double tk = (timeHours + phase) / knotH;
            long k0 = (long)Math.Floor(tk);
            double ft = tk - k0;
            // the two time knots with linear weights; each is a spatial blend of the four region corners
            double fa = latRad / cellRad + 1000.0, fo = lonRad / cellRad + 1000.0;
            int a0 = (int)Math.Floor(fa), o0 = (int)Math.Floor(fo);
            double ua = Smooth(fa - a0), uo = Smooth(fo - o0);
            int lonCells = Math.Max(1, (int)Math.Round(2.0 * Math.PI / cellRad));
            double sum = 0.0, sq = 0.0;
            for (int dk = 0; dk < 2; dk++)
            {
                double wt = dk == 0 ? 1.0 - ft : ft;
                for (int da = 0; da < 2; da++)
                    for (int dO = 0; dO < 2; dO++)
                    {
                        double w = wt * (da == 0 ? 1.0 - ua : ua) * (dO == 0 ? 1.0 - uo : uo);
                        int lo = ((o0 + dO) % lonCells + lonCells) % lonCells;   // longitude wraps around the planet
                        sum += w * Normal((int)channel, a0 + da, lo, k0 + dk);
                        sq += w * w;
                    }
            }
            // time weights (1-ft, ft) have mean-square 2/3 averaged over phase; space weights are renormalised exactly
            double spaceNorm = 0.0;
            for (int da = 0; da < 2; da++) for (int dO = 0; dO < 2; dO++)
                spaceNorm += Math.Pow((da == 0 ? 1.0 - ua : ua) * (dO == 0 ? 1.0 - uo : uo), 2);
            return sum / Math.Sqrt(spaceNorm * (2.0 / 3.0));
        }

        // ---- the anomaly ----

        static double Row(double[][] table, double latRad, int month)
        {
            double y = (latRad * 180.0 / Math.PI - WeatherVariabilityTable.FirstLatitudeDeg) / WeatherVariabilityTable.StepDeg;
            y = Math.Max(0.0, Math.Min(table.Length - 1, y));
            int i0 = Math.Min((int)y, table.Length - 2);
            double f = y - i0;
            return table[i0][month] + f * (table[i0 + 1][month] - table[i0][month]);
        }

        /// <summary>How much and how persistently the weather varies at a latitude and time of year (the NASA table, interpolated).</summary>
        public readonly struct Spread
        {
            public readonly double CloudSd, CloudRho, TemperatureSd, TemperatureRho, WindMean, WindSd, WindRho, PressureSd, PressureRho;
            public Spread(double cs, double cr, double ts, double tr, double wm, double ws, double wr, double ps, double pr)
            { CloudSd = cs; CloudRho = cr; TemperatureSd = ts; TemperatureRho = tr; WindMean = wm; WindSd = ws; WindRho = wr; PressureSd = ps; PressureRho = pr; }
        }

        public Spread SpreadAt(double latRad, double yearFraction)
        {
            int month = (int)Math.Floor((yearFraction - Math.Floor(yearFraction)) * 12.0) % 12;
            double mean = Row(WeatherVariabilityTable.WindMean, latRad, month), p98 = Row(WeatherVariabilityTable.WindP98, latRad, month);
            return new Spread(cloudsKnown ? Row(WeatherVariabilityTable.CloudSd, latRad, month) : 0.0, Row(WeatherVariabilityTable.CloudRho, latRad, month),
                              Row(WeatherVariabilityTable.TemperatureSd, latRad, month), Row(WeatherVariabilityTable.TemperatureRho, latRad, month),
                              mean, Math.Max(0.1, (p98 - mean) / Z98), Row(WeatherVariabilityTable.WindRho, latRad, month),
                              Row(WeatherVariabilityTable.PressureSd, latRad, month) * pressureScale, Row(WeatherVariabilityTable.PressureRho, latRad, month));
        }

        /// <summary>The wind speed above which a day is a storm day here (the local 98th percentile), m/s.</summary>
        public static double StormWindMs(double latRad, double yearFraction) => Row(WeatherVariabilityTable.WindP98, latRad, (int)Math.Floor((yearFraction - Math.Floor(yearFraction)) * 12.0) % 12);

        /// <summary>The day's weather at a place: <paramref name="yearFraction"/> is the time of year (0 = 1 January) for the monthly table row.</summary>
        public WeatherAnomaly At(double latRad, double lonRad, double timeHours, double yearFraction)
        {
            if (!HasWeather) return WeatherAnomaly.None;
            int month = (int)Math.Floor((yearFraction - Math.Floor(yearFraction)) * 12.0) % 12;
            double cloud = 0.0;
            if (cloudsKnown)
            {
                double sd = Row(WeatherVariabilityTable.CloudSd, latRad, month), rho = Row(WeatherVariabilityTable.CloudRho, latRad, month);
                if (sd > 0.0) cloud = sd * Noise(Channel.Cloud, latRad, lonRad, timeHours, rho > 0.0 ? rho : 0.3);
            }
            double t = Row(WeatherVariabilityTable.TemperatureSd, latRad, month) * Noise(Channel.Temperature, latRad, lonRad, timeHours, Row(WeatherVariabilityTable.TemperatureRho, latRad, month));
            double p = Row(WeatherVariabilityTable.PressureSd, latRad, month) * pressureScale * Noise(Channel.Pressure, latRad, lonRad, timeHours, Row(WeatherVariabilityTable.PressureRho, latRad, month));
            double mean = Row(WeatherVariabilityTable.WindMean, latRad, month), p98 = Row(WeatherVariabilityTable.WindP98, latRad, month);
            double sdW = Math.Max(0.1, (p98 - mean) / Z98);
            double w = Math.Max(0.0, mean + sdW * Noise(Channel.Wind, latRad, lonRad, timeHours, Row(WeatherVariabilityTable.WindRho, latRad, month)));
            return new WeatherAnomaly(cloud, t, p, w, w > p98);
        }
    }
}
