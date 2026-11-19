using System;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>A moon that raises tides on a planet. Generated per world from its seed (director 2026-11-19: "generate the moon"), so it is an assumption, not NASA data.</summary>
    public readonly struct Moon
    {
        /// <summary>Moon mass over planet mass.</summary>
        public readonly double MassRatio;
        /// <summary>Orbital distance in planet radii.</summary>
        public readonly double DistanceRadii;

        public Moon(double massRatio, double distanceRadii)
        {
            MassRatio = massRatio;
            DistanceRadii = distanceRadii;
        }

        /// <summary>The Earth-Moon pair, for the Earth-relative strength: 0.0123 planet masses at 60.3 planet radii.</summary>
        public static readonly Moon EarthMoon = new Moon(0.012300, 60.27);

        public bool Exists => MassRatio > 0.0 && DistanceRadii > 1.0;
    }

    /// <summary>
    /// Tides and free-flow tidal power. Physics first (equilibrium tide, orbital period, Betz-limited turbine power),
    /// then a few game rules marked TBD. Pure functions; the simulation applies them per coast tile.
    /// </summary>
    public static class Tides
    {
        public const double RhoSeaWater = 1025.0;   // kg/m3, mean ocean value
        public const double EarthTidalDissipationW = 3.7e12;   // Egbert and Ray 2000
        public const double BetzLimit = 16.0 / 27.0;   // 0.593, the most any open rotor can take from a stream

        /// <summary>Practical turbine power coefficient (game rule; real tidal rotors reach about 0.35 to 0.45). TBD.</summary>
        public const double TurbineCp = 0.35;

        /// <summary>Cut-in current speed, m/s (below it the rotor does not turn). TBD.</summary>
        public const double CutInMs = 0.7;

        /// <summary>Rotor swept area of one free-flow turbine, m2 (a 22 m rotor). TBD.</summary>
        public const double RotorAreaM2 = 380.0;

        /// <summary>Peak stream speed per metre of local tidal range, m/s per m (game rule: strong channels run about 1 m/s per metre, e.g. a 4 m range with a 5 m/s stream in the Pentland Firth). TBD.</summary>
        public const double PeakMsPerMetreOfRange = 1.0;

        public const double MaxPeakMs = 5.0;

        /// <summary>The highest tide a coast can reach, m: the Bay of Fundy record (about 16 m). A coast cannot funnel past it.</summary>
        public const double MaxCoastRangeM = 16.0;

        /// <summary>Open-sea range times how much a coast funnels it, capped at the Fundy record.</summary>
        public static double CoastRangeM(double openRangeM, double funnel) => Math.Min(MaxCoastRangeM, openRangeM * funnel);

        /// <summary>Rated stream speed, m/s: above it the rotor sheds power and holds its rating (real rotors rate at about 3 m/s, e.g. a 1.2 MW SeaGen at 2.4 m/s). TBD.</summary>
        public const double RatedMs = 3.0;

        /// <summary>
        /// A generated moon from the world's seed: mass ratio 0.002 to 0.015 (Earth's Moon is 0.0123) and distance 35 to 90
        /// planet radii (Earth's Moon is 60). Deterministic. TBD ranges: a plausible spread, not a catalogue.
        /// </summary>
        public static Moon Generate(int seed)
        {
            var rng = new Random(unchecked(seed * 7919 + 104729));
            double mass = 0.002 * Math.Pow(7.5, rng.NextDouble());   // 0.002 .. 0.015, log-uniform
            double distance = 35.0 + 55.0 * rng.NextDouble();
            return new Moon(mass, distance);
        }

        /// <summary>
        /// Open-ocean equilibrium tide range (high minus low) from one moon: (3/2) * (m/M) * R * (R/d)^3.
        /// For the Earth and its Moon this is 0.54 m, the known lunar equilibrium range.
        /// </summary>
        public static double EquilibriumRangeM(Moon moon, double planetRadiusM)
        {
            if (!moon.Exists) return 0.0;
            double r = 1.0 / moon.DistanceRadii;
            return 1.5 * moon.MassRatio * planetRadiusM * r * r * r;
        }

        /// <summary>Tidal strength against the Earth-Moon pair (m/M times (R/d) cubed). Scales the 3.7 TW planet ceiling.</summary>
        public static double StrengthRelativeToEarth(Moon moon)
        {
            if (!moon.Exists) return 0.0;
            double earth = Moon.EarthMoon.MassRatio / Math.Pow(Moon.EarthMoon.DistanceRadii, 3.0);
            return moon.MassRatio / Math.Pow(moon.DistanceRadii, 3.0) / earth;
        }

        /// <summary>The most tidal power the whole planet can give, W: 3.7 TW scaled by tidal strength (physical ceiling).</summary>
        public static double PlanetCeilingW(Moon moon) => EarthTidalDissipationW * StrengthRelativeToEarth(moon);

        /// <summary>Moon orbital period in hours: Kepler's third law, T = 2 pi sqrt(a^3 / (G (M + m))).</summary>
        public static double MoonPeriodHours(Moon moon, double planetMassKg, double planetRadiusM)
        {
            const double G = 6.67430e-11;
            double a = moon.DistanceRadii * planetRadiusM;
            double mu = G * planetMassKg * (1.0 + moon.MassRatio);
            return 2.0 * Math.PI * Math.Sqrt(a * a * a / mu) / 3600.0;
        }

        /// <summary>
        /// Time between two high tides (the semidiurnal tide), hours. The tide bulge follows the moon, so it meets a spot
        /// on the planet every half of one lunar day: 1 / (1/rotation - 1/moon period), halved. A tidally locked planet's
        /// rotation period is its year.
        /// </summary>
        public static double TidePeriodHours(double rotationPeriodHours, double moonPeriodHours)
        {
            double inverse = 1.0 / rotationPeriodHours - 1.0 / moonPeriodHours;
            if (Math.Abs(inverse) < 1e-12) return double.PositiveInfinity;   // the moon is stationary in the sky: no cycle
            return 0.5 / Math.Abs(inverse);
        }

        /// <summary>Peak tidal stream speed at a coast with this local range, m/s (game rule, capped).</summary>
        public static double PeakStreamMs(double localRangeM) =>
            Math.Min(MaxPeakMs, Math.Max(0.0, PeakMsPerMetreOfRange * localRangeM));

        /// <summary>Stream speed at a moment in the tide cycle: the peak times |sin|, so it slacks twice per cycle.</summary>
        public static double StreamMs(double peakMs, double timeHours, double tidePeriodHours)
        {
            if (double.IsInfinity(tidePeriodHours) || tidePeriodHours <= 0.0) return 0.0;
            return peakMs * Math.Abs(Math.Sin(Math.PI * timeHours / tidePeriodHours));
        }

        // ---- tidal barrage (director 2026-11-19) ----

        /// <summary>Basin behind one barrage, m2 (La Rance's is about 22 km2). A tile is far bigger than a real basin, so this is a fixed game rule. TBD.</summary>
        public const double BarrageBasinM2 = 22e6;

        /// <summary>Share of one emptying's energy the plant turns into electricity. About 0.3 puts La Rance's 8 m range at about 420 GWh a year (real: about 500, two-way). TBD.</summary>
        public const double BarragePlantShare = 0.3;

        /// <summary>The milestone level at which a barrage opens (a late source). TBD.</summary>
        public const int BarrageMilestone = 3;

        /// <summary>The smallest local tidal range a barrage is worth building on, m (La Rance works at about 8). TBD.</summary>
        public const double BarrageMinRangeM = 4.0;

        /// <summary>How much of the tile's tidal range a built barrage takes away (a basin's tide weakens behind a dam). TBD.</summary>
        public const double BarrageRangeDrop = 0.25;

        /// <summary>The share of land around a tile that makes it a bay (2 of 6 neighbours). TBD.</summary>
        public const double BarrageMinLandShare = 0.33;

        /// <summary>One-way (ebb) generation: it starts this far into the tide cycle after high water and runs for the next share of the cycle (La Rance generates about 5 of 12.4 hours). TBD.</summary>
        public const double BarrageGenStart = 0.10, BarrageGenLength = 0.40;

        /// <summary>Energy of one emptying of a barrage basin, J: 1/2 rho g A h^2 times the plant share.</summary>
        public static double BarrageEnergyPerTideJ(double basinM2, double rangeM, double gravityMs2, double plantShare = BarragePlantShare) =>
            basinM2 <= 0.0 || rangeM <= 0.0 || gravityMs2 <= 0.0 ? 0.0 : 0.5 * RhoSeaWater * gravityMs2 * basinM2 * rangeM * rangeM * plantShare;

        /// <summary>
        /// Barrage power at a point in the tide cycle, W: a half-sine pulse over the generation window holding the whole
        /// emptying's energy (peak = pi E / (2 window)), zero outside it. <paramref name="cycleFraction"/> is time since high water over the cycle.
        /// </summary>
        public static double BarragePowerW(double energyPerTideJ, double tidePeriodHours, double cycleFraction)
        {
            if (energyPerTideJ <= 0.0 || tidePeriodHours <= 0.0 || double.IsInfinity(tidePeriodHours)) return 0.0;
            double f = cycleFraction - Math.Floor(cycleFraction);
            double x = (f - BarrageGenStart) / BarrageGenLength;
            if (x < 0.0 || x > 1.0) return 0.0;
            double windowS = BarrageGenLength * tidePeriodHours * 3600.0;
            return Math.PI * energyPerTideJ / (2.0 * windowS) * Math.Sin(Math.PI * x);
        }

        /// <summary>Electric power of free-flow rotors in a stream, W: 1/2 * rho * A * v^3 * Cp each, zero below the cut-in speed.</summary>
        public static double TurbinePowerW(double streamMs, int turbines = 1)
        {
            if (streamMs < CutInMs || turbines <= 0) return 0.0;
            double v = Math.Min(streamMs, RatedMs);
            return 0.5 * RhoSeaWater * RotorAreaM2 * v * v * v * TurbineCp * turbines;
        }
    }
}
