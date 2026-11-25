using System;

namespace KardashevAscent.Simulation.Knowledge
{
    /// <summary>What a record is about (docs/architecture/information-architecture.md, section 4). Saved as an integer: append only.</summary>
    public enum SubjectKind { Planet, Tile, Building, Robot, Colony }

    /// <summary>A typed id: a kind and up to two integers (a tile; a tile and a slot; a robot id; zero for the planet).</summary>
    [Serializable]
    public struct Subject : IEquatable<Subject>, IComparable<Subject>
    {
        public SubjectKind Kind;
        public int A, B;
        public Subject(SubjectKind kind, int a = 0, int b = 0) { Kind = kind; A = a; B = b; }
        public static Subject Planet => new Subject(SubjectKind.Planet);
        public static Subject Tile(int tile) => new Subject(SubjectKind.Tile, tile);
        public static Subject Robot(int id) => new Subject(SubjectKind.Robot, id);
        public static Subject Building(int tile, int slot) => new Subject(SubjectKind.Building, tile, slot);
        public bool Equals(Subject o) => Kind == o.Kind && A == o.A && B == o.B;
        public static bool operator ==(Subject x, Subject y) => x.Equals(y);
        public static bool operator !=(Subject x, Subject y) => !x.Equals(y);
        public override bool Equals(object obj) => obj is Subject s && Equals(s);
        public override int GetHashCode() => ((int)Kind * 73856093) ^ (A * 19349663) ^ (B * 83492791);
        public int CompareTo(Subject o) { int c = Kind.CompareTo(o.Kind); if (c != 0) return c; c = A.CompareTo(o.A); return c != 0 ? c : B.CompareTo(o.B); }
        public override string ToString() => Kind == SubjectKind.Planet ? "planet" : Kind == SubjectKind.Tile ? $"tile {A}" : Kind == SubjectKind.Building ? $"building {A}.{B}" : Kind == SubjectKind.Robot ? $"robot {A}" : "colony";
    }

    /// <summary>Which property a record is about: a fixed list. Saved as an integer: append only.</summary>
    public enum Quantity
    {
        AirPressure, SurfaceGravity, WaterBoilingPoint, SurfaceTemperature, AirTemperature, RelativeHumidity, WindSpeed, HubWindSpeed, WaveHeight, TidalRange,
        BasinDepth, ReliefMax, OreGrade, SurveyCompleted, LatitudeFix, OpticalDepth, DiffuseShare, CloudFactor, Albedo, LineFlow, DustOpticalDepth, SpectrumFit,
    }

    /// <summary>How far to trust a record: a small ordered scale, not a fake percentage.</summary>
    public enum Confidence { Low, Medium, High }

    /// <summary>Who or what observed it.</summary>
    public enum SourceKind { Robot, Instrument, Dataset, Assumed, Derived, MeasuredByColony }

    [Serializable]
    public struct Source : IEquatable<Source>
    {
        public SourceKind Kind;
        /// <summary>A robot id for <see cref="SourceKind.Robot"/>, an experiment or instrument code otherwise (0 when none).</summary>
        public int Id;
        public Source(SourceKind kind, int id = 0) { Kind = kind; Id = id; }
        public bool Equals(Source o) => Kind == o.Kind && Id == o.Id;
        public override bool Equals(object obj) => obj is Source s && Equals(s);
        public override int GetHashCode() => (int)Kind * 31 + Id;
        public override string ToString() => Kind == SourceKind.Robot ? $"robot {Id}" : Kind == SourceKind.Instrument ? "instrument" : Kind == SourceKind.Dataset ? "dataset" : Kind == SourceKind.Assumed ? "assumed" : Kind == SourceKind.Derived ? "derived" : "measured by the colony";
    }

    /// <summary>
    /// One thing the colony (or a robot of it) learned (K1, decided 2026-11-25). Immutable: a changed value is a new record. Value is SI; the unit is fixed by the quantity.
    /// Uncertainty is the half-width of the range the true value lies in. KnownAt is negative while the record is held by a robot that has not synchronised.
    /// </summary>
    [Serializable]
    public struct Observation
    {
        public Subject Subject;
        public Quantity Quantity;
        public double Value, Uncertainty;
        public Confidence Confidence;
        public Source Source;
        public double ObservedAt, KnownAt;

        public Observation(Subject subject, Quantity quantity, double value, double uncertainty, Confidence confidence, Source source, double observedAt, double knownAt)
        { Subject = subject; Quantity = quantity; Value = value; Uncertainty = Math.Max(0.0, uncertainty); Confidence = confidence; Source = source; ObservedAt = observedAt; KnownAt = knownAt; }

        public bool IsKnown => KnownAt >= 0.0;
        public Observation WithKnownAt(double t) { var o = this; o.KnownAt = t; return o; }
        public string Unit => Quantities.Unit(Quantity);
        public double Low => Value - Uncertainty;
        public double High => Value + Uncertainty;
    }

    /// <summary>Facts about each quantity: its unit, its name and when it goes stale (game rule, TBD).</summary>
    public static class Quantities
    {
        public static string Unit(Quantity q)
        {
            switch (q)
            {
                case Quantity.AirPressure: return "Pa";
                case Quantity.SurfaceGravity: return "m/s2";
                case Quantity.WaterBoilingPoint:
                case Quantity.SurfaceTemperature:
                case Quantity.AirTemperature: return "C";
                case Quantity.RelativeHumidity: return "%";
                case Quantity.WindSpeed:
                case Quantity.HubWindSpeed: return "m/s";
                case Quantity.WaveHeight:
                case Quantity.TidalRange:
                case Quantity.BasinDepth:
                case Quantity.ReliefMax: return "m";
                case Quantity.LatitudeFix: return "deg";
                case Quantity.LineFlow: return "W";
                case Quantity.OreGrade:
                case Quantity.DiffuseShare:
                case Quantity.CloudFactor:
                case Quantity.Albedo: return "";
                default: return "";
            }
        }

        public static string Name(Quantity q)
        {
            switch (q)
            {
                case Quantity.AirPressure: return "Air pressure";
                case Quantity.SurfaceGravity: return "Surface gravity";
                case Quantity.WaterBoilingPoint: return "Water boiling point";
                case Quantity.SurfaceTemperature: return "Surface temperature";
                case Quantity.AirTemperature: return "Air temperature";
                case Quantity.RelativeHumidity: return "Relative humidity";
                case Quantity.WindSpeed: return "Wind speed";
                case Quantity.HubWindSpeed: return "Wind at hub height";
                case Quantity.WaveHeight: return "Wave height";
                case Quantity.TidalRange: return "Tidal range";
                case Quantity.BasinDepth: return "Basin depth";
                case Quantity.ReliefMax: return "Highest relief";
                case Quantity.OreGrade: return "Ore grade";
                case Quantity.SurveyCompleted: return "Survey";
                case Quantity.LatitudeFix: return "Latitude";
                case Quantity.OpticalDepth: return "Optical depth";
                case Quantity.DiffuseShare: return "Diffuse light share";
                case Quantity.CloudFactor: return "Cloud factor";
                case Quantity.Albedo: return "Albedo";
                case Quantity.LineFlow: return "Line flow";
                case Quantity.DustOpticalDepth: return "Dust optical depth";
                case Quantity.SpectrumFit: return "Light spectrum";
                default: return q.ToString();
            }
        }

        /// <summary>Game hours after which a value is shown as stale (game rule, TBD): weather in a day or two, the planet's slow properties in a year or never.</summary>
        public static double StaleAfterHours(Quantity q)
        {
            switch (q)
            {
                case Quantity.AirTemperature:
                case Quantity.RelativeHumidity:
                case Quantity.WindSpeed:
                case Quantity.CloudFactor:
                case Quantity.LineFlow:
                case Quantity.DustOpticalDepth: return 48.0;
                case Quantity.HubWindSpeed:
                case Quantity.WaveHeight: return 24.0 * 30.0;
                case Quantity.OreGrade:
                case Quantity.SurveyCompleted: return 24.0 * 365.0 * 10.0;
                default: return 24.0 * 365.0 * 5.0;   // the planet's own properties change over years, if at all
            }
        }

        /// <summary>Quantities that change from day to day: a new record is the latest value, not a disagreement (no discrepancy reports).</summary>
        public static bool IsTimeVarying(Quantity q) => q == Quantity.AirTemperature || q == Quantity.RelativeHumidity || q == Quantity.WindSpeed || q == Quantity.CloudFactor || q == Quantity.LineFlow || q == Quantity.DustOpticalDepth;

        /// <summary>Quantities whose staleness the colony is told about (game rule).</summary>
        public static bool ReportsStale(Quantity q) => q == Quantity.HubWindSpeed || q == Quantity.AirPressure || q == Quantity.SurfaceTemperature;

        public static string Age(double hours)
        {
            if (hours < 1.0) return "now";
            if (hours < 48.0) return $"{hours:0} h ago";
            if (hours < 24.0 * 60.0) return $"{hours / 24.0:0} d ago";
            return $"{hours / 24.0 / 365.256:0.#} yr ago";
        }
    }
}
