using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>
    /// The latitude puzzle (docs/design/solar-measurements.md, stage C). A robot stands a rod upright and marks the
    /// shortest shadow of the day (local noon). The player gets the rod's height H, the shadow's length L, which way
    /// the shadow points, and the day's declination δ, and works out:
    ///   noon sun altitude  h = atan(H / L)
    ///   latitude           φ = 90° − h + δ   (sun to the south at noon, shadow pointing north)
    ///                      φ = h − 90° + δ   (sun to the north, shadow pointing south)
    /// (the same rule as the sky view's noon sighting, <see cref="SkyMath.LatitudeFromNoonDeg"/>).
    /// </summary>
    [Serializable]
    public sealed class LatitudePuzzle
    {
        /// <summary>Rod height, m. TBD.</summary>
        public const double RodM = 2.0;
        /// <summary>Within this many degrees the answer is a precise fix. TBD.</summary>
        public const double PreciseDeg = 0.5;
        /// <summary>Within this many degrees the answer is a rough fix (the colony keeps it, marked rough). TBD.</summary>
        public const double RoughDeg = 2.0;
        /// <summary>The sun's lowest noon altitude for a readable shadow, degrees (longer shadows can't be measured).</summary>
        public const double MinAltitudeDeg = 5.0;

        public int Tile;
        public double RodHeightM;
        public double ShadowM;
        public double DeclinationDeg;
        /// <summary>The noon sun stands to the south (shadow points north).</summary>
        public bool SunSouth;
        public double TrueLatitudeDeg;
        public int Attempts;

        /// <summary>The readings a robot takes on a tile with this latitude on a day with this declination.</summary>
        public static LatitudePuzzle Make(int tile, double latitudeDeg, double declinationDeg)
        {
            double h = 90.0 - Math.Abs(latitudeDeg - declinationDeg);
            h = Math.Max(MinAltitudeDeg, h);
            double shadow = RodM / Math.Tan(h * Math.PI / 180.0);
            return new LatitudePuzzle
            {
                Tile = tile,
                RodHeightM = RodM,
                ShadowM = Math.Round(shadow, 2),   // a tape measure: centimetres
                DeclinationDeg = Math.Round(declinationDeg, 2),
                SunSouth = latitudeDeg > declinationDeg,
                TrueLatitudeDeg = latitudeDeg,
            };
        }

        /// <summary>The noon altitude the readings give, degrees.</summary>
        public double AltitudeDeg => Math.Atan2(RodHeightM, ShadowM) * 180.0 / Math.PI;

        /// <summary>The latitude the readings give (what a perfect answer is), degrees.</summary>
        public double AnswerDeg => SkyMath.LatitudeFromNoonDeg(AltitudeDeg, DeclinationDeg, SunSouth);

        public enum Grade { Wrong, Rough, Precise }

        /// <summary>How good an answer is, against the readings (not the hidden true value: rounding is not the player's fault).</summary>
        public Grade Check(double answerDeg)
        {
            double error = Math.Abs(answerDeg - AnswerDeg);
            return error <= PreciseDeg ? Grade.Precise : error <= RoughDeg ? Grade.Rough : Grade.Wrong;
        }
    }
}
