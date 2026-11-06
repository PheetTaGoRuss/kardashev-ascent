using System;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Game
{
    public enum ClockSpeed
    {
        Paused,
        /// <summary>One game second per real second.</summary>
        RealTime,
        /// <summary>One game hour per real second (a day in 24 s on Earth): easy to watch the sun move.</summary>
        HourPerSecond,
        /// <summary>One solar day per real second.</summary>
        DayPerSecond,
        /// <summary>One orbit per real minute.</summary>
        YearPerMinute,
    }

    /// <summary>Northern-hemisphere season; the southern one is the opposite.</summary>
    public enum Season
    {
        Spring,
        Summer,
        Autumn,
        Winter,
    }

    /// <summary>
    /// Game time, kept apart from the frame rate: Presentation passes real seconds, the clock answers
    /// how many game hours to simulate. Also turns hours into calendar facts (day, year, local time, season).
    /// </summary>
    public sealed class SimClock
    {
        /// <summary>Longest stretch of game time one call may return, so a slow frame cannot stall the game.</summary>
        public double MaxHoursPerAdvance { get; set; } = 24.0 * 30.0;

        public ClockSpeed Speed { get; set; } = ClockSpeed.HourPerSecond;

        /// <summary>Solar day used for "1 day per second" and calendar days; 24 h if the planet has none (tidally locked).</summary>
        public double DayLengthHours { get; }

        public double YearLengthHours { get; }

        public SimClock(double dayLengthHours, double yearLengthHours)
        {
            if (yearLengthHours <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(yearLengthHours), "Year length must be positive.");
            DayLengthHours = dayLengthHours > 0.0 ? dayLengthHours : 24.0;
            YearLengthHours = yearLengthHours;
        }

        /// <summary>Game hours per real second at the current speed.</summary>
        public double HoursPerSecond
        {
            get
            {
                switch (Speed)
                {
                    case ClockSpeed.RealTime: return 1.0 / 3600.0;
                    case ClockSpeed.HourPerSecond: return 1.0;
                    case ClockSpeed.DayPerSecond: return DayLengthHours;
                    case ClockSpeed.YearPerMinute: return YearLengthHours / 60.0;
                    default: return 0.0;
                }
            }
        }

        /// <summary>Game hours to simulate for <paramref name="realSeconds"/> of real time.</summary>
        public double Advance(double realSeconds)
        {
            if (realSeconds <= 0.0)
                return 0.0;
            return Math.Min(realSeconds * HoursPerSecond, MaxHoursPerAdvance);
        }

        /// <summary>Whole orbits completed (0 in the first year).</summary>
        public int YearIndex(double timeHours) => (int)Math.Floor(timeHours / YearLengthHours);

        /// <summary>Day within the current year, starting at 1.</summary>
        public int DayOfYear(double timeHours)
        {
            double intoYear = timeHours - YearIndex(timeHours) * YearLengthHours;
            return (int)Math.Floor(intoYear / DayLengthHours) + 1;
        }

        /// <summary>Hours since local midnight on a 24-hour dial, for a tile at <paramref name="hourAngle"/> (radians, 0 = noon).</summary>
        public static double LocalTime24(double hourAngle)
        {
            double t = (hourAngle / (2.0 * Math.PI) + 0.5) % 1.0;
            if (t < 0.0)
                t += 1.0;
            return t * 24.0;
        }

        /// <summary>Northern season from the star's ecliptic longitude λ (0 = northern spring equinox).</summary>
        public static Season SeasonFromEclipticLongitude(double eclipticLongitude)
        {
            double deg = eclipticLongitude * 180.0 / Math.PI % 360.0;
            if (deg < 0.0)
                deg += 360.0;
            return deg < 90.0 ? Season.Spring : deg < 180.0 ? Season.Summer : deg < 270.0 ? Season.Autumn : Season.Winter;
        }

        /// <summary>
        /// Star's ecliptic longitude λ = ν + λₚ at a time, counting time 0 as periapsis
        /// (the convention <see cref="StubPlanetSimulation"/> uses).
        /// </summary>
        public static double EclipticLongitude(in PlanetSolarParams planet, double timeHours)
        {
            double meanAnomaly = 2.0 * Math.PI * timeHours / planet.YearLengthHours;
            double nu = Orbit.TrueAnomaly(Orbit.SolveKepler(meanAnomaly, planet.Eccentricity), planet.Eccentricity);
            return nu + planet.PerihelionLongitude;
        }
    }
}
