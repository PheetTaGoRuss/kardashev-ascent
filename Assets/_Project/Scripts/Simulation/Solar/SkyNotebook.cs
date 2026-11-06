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
    /// </summary>
    public sealed class SkyNotebook
    {
        /// <summary>A sun sighting counts as "at noon" within this hour angle (±15 minutes of a 24 h day; the latitude is then within about half a degree).</summary>
        public const double NoonWindowDeg = 3.75;

        readonly Dictionary<int, (double LatitudeDeg, string Method)> latitudes = new Dictionary<int, (double, string)>();
        readonly List<string> log = new List<string>();

        public bool KnowsDeclination { get; private set; }
        public bool TracksSun { get; private set; }
        public IReadOnlyList<string> Log => log;

        public bool KnowsLatitude(int tile) => latitudes.ContainsKey(tile);

        /// <summary>The latitude found for a tile (NaN if not found yet).</summary>
        public double LatitudeDeg(int tile) => latitudes.TryGetValue(tile, out var l) ? l.LatitudeDeg : double.NaN;

        /// <summary>How the latitude of a tile was found, or "".</summary>
        public string LatitudeMethod(int tile) => latitudes.TryGetValue(tile, out var l) ? l.Method : "";

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
                latitudes[tile] = (phi, "noon sun: φ = 90° − h + δ");
                return Note(line + $" Noon sighting: φ = {(south ? "90° − h + δ" : "h − 90° + δ")} = {phi:+0.0;−0.0}°.");
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
            latitudes[tile] = (sky.LatitudeDeg, "celestial pole altitude");
            return Note($"The {(sky.LatitudeDeg >= 0 ? "north" : "south")} celestial pole is {Math.Abs(sky.LatitudeDeg):0.0}° above the horizon: φ = {sky.LatitudeDeg:+0.0;−0.0}°.");
        }

        string Note(string text)
        {
            log.Add(text);
            if (log.Count > 30)
                log.RemoveAt(0);
            return text;
        }
    }
}
