using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Electrical
{
    /// <summary>
    /// What a cable is made of and what it costs to carry power over it (electrical redesign stage 5, W-23 to W-25). Pure functions:
    /// nothing here changes a building bill by itself. Switching the power line's bill from the flat 15 Cu to the real conductor mass
    /// waits for the director's answer on gate G0(d).
    /// </summary>
    public static class CableEconomics
    {
        /// <summary>CO-03a: a building takes the real mass of the installation times this share on site (game rule, TBD, as for panels and batteries).</summary>
        public const double OnSiteShare = 0.01;

        /// <summary>Metal in a length of cable, kg (density x length x area x conductors).</summary>
        public static double ConductorMassKg(CableSpec spec, double lengthM) => spec.ConductorMassKg(lengthM);

        /// <summary>The conductor part of a build bill, kg: the real mass times the on-site share (CO-03a).</summary>
        public static double ConductorBillKg(CableSpec spec, double lengthM, double onSiteShare = OnSiteShare) => spec.ConductorMassKg(lengthM) * onSiteShare;

        /// <summary>
        /// Metal handed back when a line is taken down, kg: the share of the bill that is recovered (game rule, TBD: the caller
        /// chooses it). Never more than was spent.
        /// </summary>
        public static double SalvageKg(double billKg, double recoveredShare) => billKg * Math.Max(0.0, Math.Min(1.0, recoveredShare));

        /// <summary>One cable choice for a route, with what it costs in metal and in lost energy.</summary>
        public sealed class Choice
        {
            public CableSpec Spec;
            public double MetalKg;
            public double ResistanceOhm;
            public double CurrentA;
            public double LossW;
            public double LossShare;
            public double LossKWhPerYear;
            /// <summary>The current is above the spec's rating: it cannot carry this load.</summary>
            public bool OverRated;
            /// <summary>Metal cost plus the cost of the energy lost over the years, in the units of the two prices given.</summary>
            public double TotalCost;
        }

        /// <summary>
        /// Compares cables for one route and one steady load (Kelvin's law): a thicker cable costs more metal and loses less energy.
        /// The economic size is where the two balance. Advice only: the player chooses. The two prices are inputs because the game has
        /// no metal price or energy value yet (game rule, TBD): <paramref name="metalPerKg"/> and <paramref name="energyPerKWh"/> may be
        /// any consistent unit, and only their ratio matters.
        /// </summary>
        public static List<Choice> Compare(IEnumerable<CableSpec> specs, double lengthM, double powerW, double volts, double loadHoursPerYear,
                                           double years, double metalPerKg, double energyPerKWh, double celsius = 20.0)
        {
            var list = new List<Choice>();
            foreach (var spec in specs)
            {
                double r = spec.ResistanceOhm(lengthM, celsius);
                double i = powerW / volts;
                var c = new Choice
                {
                    Spec = spec,
                    MetalKg = spec.ConductorMassKg(lengthM),
                    ResistanceOhm = r,
                    CurrentA = i,
                    LossW = i * i * r,
                    OverRated = i > spec.RatingA,
                };
                c.LossShare = powerW > 0.0 ? c.LossW / powerW : 0.0;
                c.LossKWhPerYear = c.LossW * loadHoursPerYear / 1000.0;
                c.TotalCost = c.MetalKg * metalPerKg + c.LossKWhPerYear * years * energyPerKWh;
                list.Add(c);
            }
            return list;
        }

        /// <summary>The cheapest choice that can carry the current, or null when none can.</summary>
        public static Choice Economic(List<Choice> choices)
        {
            Choice best = null;
            foreach (var c in choices)
                if (!c.OverRated && (best == null || c.TotalCost < best.TotalCost)) best = c;
            return best;
        }
    }
}
