using System;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// What the colony believes about a quantity (research tied to the physics, decided 2026-11-23): a value with a half range. Once the
    /// quantity is measured the half range is 0 and the value is exact. Before that the true value lies inside the range but not at its
    /// centre, so the estimate never gives the answer away. The ranges are game rules, TBD.
    /// </summary>
    public readonly struct KnowledgeEstimate
    {
        public readonly double Value, HalfRange;
        public KnowledgeEstimate(double value, double halfRange) { Value = value; HalfRange = Math.Max(0.0, halfRange); }
        public bool IsExact => HalfRange <= 0.0;
        public double Low => Value - HalfRange;
        public double High => Value + HalfRange;

        /// <summary>An estimate of a true value: exact when known, else a range around a centre offset from the truth by a fixed amount of the range.</summary>
        public static KnowledgeEstimate Of(double truth, double halfRange, bool known, int key)
        {
            if (known || halfRange <= 0.0) return new KnowledgeEstimate(truth, 0.0);
            // a fixed skew in -0.6..+0.6 of the half range, from a stable key (never string.GetHashCode: it changes between runs)
            uint h = unchecked((uint)key * 2654435761u);
            double skew = ((h >> 8) & 0xFF) / 255.0 * 1.2 - 0.6;
            return new KnowledgeEstimate(truth + skew * halfRange, halfRange);
        }

        /// <summary>"5.2 kPa" when exact, "about 5.2 kPa, plus or minus 2.6" otherwise.</summary>
        public string Say(string format, string unit, double scale = 1.0)
        {
            string u = unit.Length > 0 ? " " + unit : "";
            return IsExact ? (Value * scale).ToString(format) + u
                           : "about " + (Value * scale).ToString(format) + u + ", plus or minus " + (HalfRange * scale).ToString(format) + u;
        }
    }
}
