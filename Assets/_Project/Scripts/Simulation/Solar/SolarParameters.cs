using System.Collections.Generic;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>Everything that changes what a solar panel makes, apart from its own angles and size (docs/design/solar-measurements.md).</summary>
    public enum SolarParameter { Latitude, OpticalDepth, DiffuseShare, Clouds, Albedo, AirTemperature, Dust, Spectrum }

    /// <summary>
    /// How unsure the colony is about each parameter before a research robot measures it (game rule; every range is a
    /// TBD placeholder until tuned in play). The panel model runs at both ends of a range to show what it can change.
    /// </summary>
    public static class SolarUnknowns
    {
        /// <summary>Latitude before a fix, ± degrees.</summary>
        public const double LatitudeDeg = 5.0;
        /// <summary>Atmosphere optical depth τ, ± share of the estimate.</summary>
        public const double OpticalDepthShare = 0.3;
        /// <summary>Diffuse share k, ± share of the estimate.</summary>
        public const double DiffuseShare = 0.3;
        /// <summary>Cloud factor (yearly share of clear-sky output), ± share of the estimate.</summary>
        public const double CloudShare = 0.15;
        /// <summary>Ground albedo ρ_g, ± absolute.</summary>
        public const double Albedo = 0.1;
        /// <summary>Air temperature, ± °C.</summary>
        public const double AirTemperatureC = 10.0;
        /// <summary>Light dust may block, 0 to this share.</summary>
        public const double Dust = 0.05;
        /// <summary>Spectral match f_spec, ± share of the estimate.</summary>
        public const double Spectrum = 0.1;

        /// <summary>Hours a robot needs on site to take one measurement. TBD.</summary>
        public const double MeasureHours = 6.0;

        public static string Name(SolarParameter p)
        {
            switch (p)
            {
                case SolarParameter.Latitude: return "Latitude φ";
                case SolarParameter.OpticalDepth: return "Air haze (optical depth τ)";
                case SolarParameter.DiffuseShare: return "Diffuse light share k";
                case SolarParameter.Clouds: return "Clouds";
                case SolarParameter.Albedo: return "Ground reflectance ρ_g";
                case SolarParameter.AirTemperature: return "Air temperature";
                case SolarParameter.Dust: return "Dust on panels";
                default: return "Star spectrum (f_spec)";
            }
        }

        /// <summary>The instrument a robot sets up, and the data the puzzle will give.</summary>
        public static string Instrument(SolarParameter p)
        {
            switch (p)
            {
                case SolarParameter.Latitude: return "a rod's noon shadow (or the sky view, V)";
                case SolarParameter.OpticalDepth: return "a sun photometer through the day (Langley plot)";
                case SolarParameter.DiffuseShare: return "a shaded and an unshaded light sensor";
                case SolarParameter.Clouds: return "a light meter logging for days";
                case SolarParameter.Albedo: return "light sensors facing up and down";
                case SolarParameter.AirTemperature: return "a weather station, day and night";
                case SolarParameter.Dust: return "a clean and a dusty reference cell";
                default: return "a spectrometer pointed at the star";
            }
        }

        /// <summary>Latitude is per tile (and comes from the sky notebook); the rest hold for the whole planet for now.</summary>
        public static bool PerTile(SolarParameter p) => p == SolarParameter.Latitude;
    }

    /// <summary>A parameter's effect on one panel's yearly energy: the output at the two ends of its range, Wh.</summary>
    public readonly struct ParameterEffect
    {
        public readonly SolarParameter Parameter;
        public readonly double LowWh, HighWh;
        public readonly bool Known;
        /// <summary>Measured roughly: half the range is left.</summary>
        public readonly bool Rough;

        public ParameterEffect(SolarParameter parameter, double lowWh, double highWh, bool known, bool rough = false)
        {
            Parameter = parameter;
            LowWh = lowWh;
            HighWh = highWh;
            Known = known;
            Rough = rough;
        }

        /// <summary>Half the spread, Wh.</summary>
        public double HalfRangeWh => System.Math.Abs(HighWh - LowWh) / 2.0;
    }

    /// <summary>Which planet-wide parameters have been measured (latitude lives in <see cref="SkyNotebook"/>).</summary>
    public sealed class ParameterBook
    {
        /// <summary>Share of the range left after a rough measurement. TBD.</summary>
        public const double RoughRangeLeft = 0.5;

        readonly HashSet<SolarParameter> measured = new HashSet<SolarParameter>();
        readonly HashSet<SolarParameter> rough = new HashSet<SolarParameter>();

        /// <summary>Measured precisely (no range left).</summary>
        public bool IsMeasured(SolarParameter p) => measured.Contains(p);
        /// <summary>Measured roughly (part of the range left; can be measured again).</summary>
        public bool IsRough(SolarParameter p) => rough.Contains(p);

        public void MarkMeasured(SolarParameter p)
        {
            measured.Add(p);
            rough.Remove(p);
        }

        public void MarkRough(SolarParameter p)
        {
            if (!measured.Contains(p))
                rough.Add(p);
        }

        public List<int> CaptureRough()
        {
            var list = new List<int>();
            foreach (SolarParameter p in rough)
                list.Add((int)p);
            list.Sort();
            return list;
        }

        public void RestoreRough(IEnumerable<int> values)
        {
            rough.Clear();
            if (values == null)
                return;
            foreach (int v in values)
                if (v >= 0 && v <= (int)SolarParameter.Spectrum && !measured.Contains((SolarParameter)v))
                    rough.Add((SolarParameter)v);
        }

        public List<int> Capture()
        {
            var list = new List<int>();
            foreach (SolarParameter p in measured)
                list.Add((int)p);
            list.Sort();
            return list;
        }

        public void Restore(IEnumerable<int> values)
        {
            measured.Clear();
            rough.Clear();
            if (values == null)
                return;
            foreach (int v in values)
                if (v >= 0 && v <= (int)SolarParameter.Spectrum)
                    measured.Add((SolarParameter)v);
        }
    }
}
