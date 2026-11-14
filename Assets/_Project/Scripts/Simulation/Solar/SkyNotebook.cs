using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>
    /// What the colony has found out about its sky (game rule). Numbers in the sky view stay hidden until the player
    /// works them out or measures them, the way a navigator would:
    /// the date gives the declination δ; a sun sighting gives its altitude h and azimuth A_s (and the colony keeps
    /// tracking it afterwards); a sighting near noon, with δ known, gives the latitude φ = 90° − h + δ; at night the
    /// celestial pole stands φ above the horizon.
    /// The first latitude fix anchors a survey: from then on every tile's latitude can be read off the map, so later
    /// sites don't each need a noon sighting. Knowing a tile's latitude unlocks the best-angle advice there.
    /// </summary>
    public sealed class SkyNotebook
    {
        /// <summary>A sun sighting counts as "at noon" within this hour angle (±15 minutes of a 24 h day; the latitude is then within about half a degree).</summary>
        public const double NoonWindowDeg = 3.75;

        readonly Dictionary<int, (double LatitudeDeg, string Method)> latitudes = new Dictionary<int, (double, string)>();
        readonly List<string> log = new List<string>();
        int surveyAnchor = -1;

        public bool KnowsDeclination { get; private set; }
        public bool TracksSun { get; private set; }
        public IReadOnlyList<string> Log => log;

        /// <summary>A latitude has been fixed somewhere, so the map gives every tile's latitude.</summary>
        public bool Surveyed => surveyAnchor >= 0;

        /// <summary>The tile of the first latitude fix (−1 before any).</summary>
        public int SurveyAnchor => surveyAnchor;

        /// <summary>This tile's latitude was measured here (not just surveyed).</summary>
        public bool MeasuredLatitude(int tile) => latitudes.ContainsKey(tile);

        /// <summary>The colony knows this tile's latitude: measured here, or surveyed from an earlier fix.</summary>
        public bool KnowsLatitude(int tile) => latitudes.ContainsKey(tile) || Surveyed;

        /// <summary>Best-angle advice (the B key, "best here", the tile's best angle) needs the tile's latitude.</summary>
        public bool CanUseBestAngle(int tile) => KnowsLatitude(tile);

        /// <summary>
        /// The latitude the colony has for a tile: the measured value (with its small error), the map's value
        /// (<paramref name="mapLatitudeDeg"/>) once surveyed, or NaN.
        /// </summary>
        public double LatitudeDeg(int tile, double mapLatitudeDeg) =>
            latitudes.TryGetValue(tile, out var l) ? l.LatitudeDeg : Surveyed ? mapLatitudeDeg : double.NaN;

        /// <summary>How the latitude of a tile was found, or "".</summary>
        public string LatitudeMethod(int tile) =>
            latitudes.TryGetValue(tile, out var l) ? l.Method
            : Surveyed ? $"surveyed from the first fix on tile #{surveyAnchor}"
            : "";

        /// <summary>The date and the planet's tilt give today's declination.</summary>
        public string WorkOutDeclination(in SkyReport sky)
        {
            KnowsDeclination = true;
            return Note($"Worked out δ = {sky.DeclinationDeg:+0.00;−0.00}° from the date (ε = {sky.ObliquityDeg:0.##}°, λ = {sky.EclipticLongitudeDeg:0}°).");
        }

        /// <summary>
        /// Sight the sun from a tile. Fails at night. Near noon, with δ known, it also gives the tile's latitude
        /// (with the small error of not being exactly at noon).
        /// </summary>
        public string MeasureSun(int tile, in SkyReport sky)
        {
            if (!sky.Valid)
                return Note("Nothing to measure here.");
            if (!sky.SunUp)
                return Note("The sun is below the horizon: measure it in daylight, or sight the celestial pole tonight.");
            TracksSun = true;
            string line = $"Sun at h = {sky.SunAltitudeDeg:0.0}°, A_s = {sky.SunAzimuthDeg:0}° (H = {sky.HourAngleDeg:+0;−0}°).";
            if (Math.Abs(sky.HourAngleDeg) <= NoonWindowDeg)
            {
                if (!KnowsDeclination)
                    return Note(line + " It's noon: work out δ from the date, then measure again to find your latitude.");
                bool south = sky.SunAzimuthDeg > 90.0 && sky.SunAzimuthDeg < 270.0;
                double phi = SkyMath.LatitudeFromNoonDeg(sky.SunAltitudeDeg, sky.DeclinationDeg, south);
                bool first = !Surveyed;
                Fix(tile, phi, "noon sun: φ = 90° − h + δ");
                return Note(line + $" Noon sighting: φ = {(south ? "90° − h + δ" : "h − 90° + δ")} = {phi:+0.0;−0.0}°." +
                            (first ? " Best angles unlocked, and the survey now gives every tile's latitude." : ""));
            }
            return Note(line + " Measure again within a quarter hour of noon to find your latitude.");
        }

        /// <summary>At night the celestial pole stands as high above the horizon as your latitude.</summary>
        public string MeasurePole(int tile, in SkyReport sky)
        {
            if (!sky.Valid)
                return Note("Nothing to measure here.");
            if (sky.SunUp)
                return Note("Too bright to see the stars: sight the pole after sunset, or the sun at noon.");
            bool first = !Surveyed;
            Fix(tile, sky.LatitudeDeg, "celestial pole altitude");
            return Note($"The {(sky.LatitudeDeg >= 0 ? "north" : "south")} celestial pole is {Math.Abs(sky.LatitudeDeg):0.0}° above the horizon: φ = {sky.LatitudeDeg:+0.0;−0.0}°." +
                        (first ? " Best angles unlocked, and the survey now gives every tile's latitude." : ""));
        }

        // ---- Puzzles: the player works each value out from real-world readings (director 2026-11-14) ----

        /// <summary>Answers within this many degrees are right (instruments and rounding; game rule).</summary>
        public const double AnswerToleranceDeg = 1.0;

        /// <summary>Length of the shadow stick (gnomon), m.</summary>
        public const double GnomonLengthM = 1.0;

        /// <summary>
        /// What a vertical stick of <see cref="GnomonLengthM"/> shows now: its shadow's length (m, rounded to the
        /// millimetre) and the compass bearing it points to (°, clockwise from north). The shadow points away from the
        /// sun, so tan h = stick / shadow and A_s = bearing ± 180°. No shadow at night (length NaN).
        /// </summary>
        public static (double ShadowM, double BearingDeg) GnomonReading(in SkyReport sky)
        {
            if (!sky.Valid || !sky.SunUp || sky.SunAltitudeDeg <= 0.5)
                return (double.NaN, double.NaN);
            double shadow = GnomonLengthM / Math.Tan(sky.SunAltitudeDeg * Math.PI / 180.0);
            double bearing = (sky.SunAzimuthDeg + 180.0) % 360.0;
            return (Math.Round(shadow, 3), Math.Round(bearing));
        }

        /// <summary>
        /// A quadrant aimed at the celestial pole at night: the plumb line's angle from the sight line's perpendicular,
        /// read on the scale from the zenith (°, 0.1° steps); the pole's altitude is 90° − reading. Which pole is up
        /// (north or south) is part of the reading. NaN in daylight.
        /// </summary>
        public static (double ReadingDeg, bool North) QuadrantReading(in SkyReport sky) =>
            !sky.Valid || sky.SunUp ? (double.NaN, true) : (Math.Round(90.0 - Math.Abs(sky.LatitudeDeg), 1), sky.LatitudeDeg >= 0.0);

        /// <summary>The noon sighting kept for the latitude step: (altitude, sun in the south), or null.</summary>
        public (double AltitudeDeg, bool South)? NoonSighting { get; private set; }

        static double AngleError(double a, double b)
        {
            double d = Math.Abs((a - b) % 360.0);
            return Math.Min(d, 360.0 - d);
        }

        /// <summary>The player's δ from sin δ = sin ε · sin λ. Right within <see cref="AnswerToleranceDeg"/>.</summary>
        public string AnswerDeclination(in SkyReport sky, double declinationDeg, out bool right)
        {
            right = Math.Abs(declinationDeg - sky.DeclinationDeg) <= AnswerToleranceDeg;
            if (!right)
                return $"δ = {declinationDeg:0.##}° doesn't fit: sin δ = sin ε · sin λ, then δ = arcsin of that.";
            return WorkOutDeclination(sky);
        }

        /// <summary>
        /// The player's sun position from the stick's shadow (h = arctan(stick / shadow), A_s = shadow bearing ± 180°).
        /// Right when both are within <see cref="AnswerToleranceDeg"/> (azimuth 2°). Near noon it keeps the sighting for
        /// the latitude step.
        /// </summary>
        public string AnswerSun(int tile, in SkyReport sky, double altitudeDeg, double azimuthDeg, out bool right)
        {
            right = false;
            if (!sky.Valid || !sky.SunUp)
                return "The sun is down: no shadow to measure. Sight the celestial pole tonight, or wait for day.";
            bool hOk = Math.Abs(altitudeDeg - sky.SunAltitudeDeg) <= AnswerToleranceDeg;
            bool aOk = AngleError(azimuthDeg, sky.SunAzimuthDeg) <= 2.0 * AnswerToleranceDeg;
            if (!hOk || !aOk)
                return !hOk && !aOk ? "Neither fits: h = arctan(stick ÷ shadow); the sun is opposite the shadow (bearing ± 180°)."
                     : !hOk ? $"The direction is right; h = {altitudeDeg:0.#}° isn't: h = arctan(stick ÷ shadow)."
                     : $"h is right; A_s = {azimuthDeg:0}° isn't: the sun is on the opposite side of the shadow (bearing ± 180°).";
            right = true;
            TracksSun = true;
            bool noon = Math.Abs(sky.HourAngleDeg) <= NoonWindowDeg;
            bool south = sky.SunAzimuthDeg > 90.0 && sky.SunAzimuthDeg < 270.0;
            NoonSighting = noon ? (sky.SunAltitudeDeg, south) : ((double, bool)?)null;
            return Note($"Shadow sighting: sun at h = {sky.SunAltitudeDeg:0.0}°, A_s = {sky.SunAzimuthDeg:0}°." +
                        (noon ? (KnowsDeclination ? " It's noon: work out your latitude in step 3." : " It's noon: work out δ first, then your latitude.")
                              : " Sight it again within a quarter hour of noon to find your latitude."));
        }

        /// <summary>The player's latitude from the noon sighting: φ = 90° − h + δ (sun in the south) or h − 90° + δ.</summary>
        public string AnswerNoonLatitude(int tile, in SkyReport sky, double latitudeDeg, out bool right)
        {
            right = false;
            if (NoonSighting == null)
                return "Measure the sun within a quarter hour of noon first.";
            if (!KnowsDeclination)
                return "Work out δ first (step 1).";
            var (h, south) = NoonSighting.Value;
            double phi = SkyMath.LatitudeFromNoonDeg(h, sky.DeclinationDeg, south);
            right = Math.Abs(latitudeDeg - phi) <= AnswerToleranceDeg;
            if (!right)
                return $"φ = {latitudeDeg:0.#}° doesn't fit: sun in the {(south ? "south: φ = 90° − h + δ" : "north: φ = h − 90° + δ")} (south of the equator is negative).";
            return RecordLatitude(tile, phi, "noon sun: φ = 90° − h + δ");
        }

        /// <summary>The player's latitude from the quadrant on the celestial pole: φ = ±(90° − reading).</summary>
        public string AnswerPole(int tile, in SkyReport sky, double latitudeDeg, out bool right)
        {
            right = false;
            if (!sky.Valid || sky.SunUp)
                return "Too bright to see the stars: sight the pole after sunset.";
            right = Math.Abs(latitudeDeg - sky.LatitudeDeg) <= AnswerToleranceDeg;
            if (!right)
                return $"φ = {latitudeDeg:0.#}° doesn't fit: the pole's altitude is 90° − the quadrant reading, and φ is that (negative if it is the south pole).";
            return RecordLatitude(tile, sky.LatitudeDeg, "celestial pole with a quadrant");
        }

        /// <summary>A latitude worked out from a robot's measurement (the shadow puzzle).</summary>
        public string RecordLatitude(int tile, double latitudeDeg, string method)
        {
            bool first = !Surveyed;
            Fix(tile, latitudeDeg, method);
            return Note($"Tile #{tile}: φ = {latitudeDeg:+0.0;−0.0}° ({method})." +
                        (first ? " Best angles unlocked, and the survey now gives every tile's latitude." : ""));
        }

        void Fix(int tile, double latitudeDeg, string method)
        {
            latitudes[tile] = (latitudeDeg, method);
            if (surveyAnchor < 0)
                surveyAnchor = tile;
        }

        string Note(string text)
        {
            log.Add(text);
            if (log.Count > 30)
                log.RemoveAt(0);
            return text;
        }

        // ---- Saving ----

        public SkyNotebookState Capture()
        {
            var state = new SkyNotebookState
            {
                KnowsDeclination = KnowsDeclination,
                TracksSun = TracksSun,
                SurveyAnchor = surveyAnchor,
                Log = new List<string>(log),
            };
            foreach (var kv in latitudes)
                state.Fixes.Add(new SkyNotebookState.LatitudeFix { Tile = kv.Key, LatitudeDeg = kv.Value.LatitudeDeg, Method = kv.Value.Method });
            state.Fixes.Sort((a, b) => a.Tile.CompareTo(b.Tile));
            return state;
        }

        /// <summary>Replace what is known with a saved notebook (null: start over).</summary>
        public void Restore(SkyNotebookState state)
        {
            latitudes.Clear();
            log.Clear();
            KnowsDeclination = state != null && state.KnowsDeclination;
            TracksSun = state != null && state.TracksSun;
            surveyAnchor = -1;
            if (state == null)
                return;
            if (state.Fixes != null)
                foreach (var f in state.Fixes)
                    if (f != null)
                        latitudes[f.Tile] = (f.LatitudeDeg, f.Method ?? "");
            surveyAnchor = latitudes.Count > 0 ? (latitudes.ContainsKey(state.SurveyAnchor) ? state.SurveyAnchor : state.Fixes[0].Tile) : -1;
            if (state.Log != null)
                log.AddRange(state.Log);
        }
    }

    /// <summary>A saved <see cref="SkyNotebook"/>.</summary>
    [Serializable]
    public sealed class SkyNotebookState
    {
        public bool KnowsDeclination;
        public bool TracksSun;
        public int SurveyAnchor = -1;
        public List<LatitudeFix> Fixes = new List<LatitudeFix>();
        public List<string> Log = new List<string>();

        [Serializable]
        public sealed class LatitudeFix
        {
            public int Tile;
            public double LatitudeDeg;
            public string Method = "";
        }
    }
}
