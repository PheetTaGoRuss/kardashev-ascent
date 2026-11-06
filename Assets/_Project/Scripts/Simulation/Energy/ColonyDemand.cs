using System;

namespace KardashevAscent.Simulation.Energy
{
    /// <summary>
    /// How much power the colony asks for, and how it grows (gameplay rule, not physics).
    /// The colony grows toward what its power supply can carry: <see cref="UsableShare"/> of the mean generation over the
    /// last full planet day (so day and night are both counted). It only grows after a full day with no brownout, approaching that target with a time constant of
    /// <see cref="GrowthDays"/> days. A brownout makes it cut back toward the power it actually got (time constant
    /// <see cref="CutBackHours"/> hours). It never falls below the base's life-support load.
    /// So panels raise the target, and batteries (to get through the night without a brownout) let the colony reach it.
    /// </summary>
    public sealed class ColonyDemand
    {
        /// <summary>Share of average generation the colony can rely on (leaves room for battery losses and bad days).</summary>
        public const double UsableShare = 0.85;
        public const double GrowthDays = 2.0;
        public const double CutBackHours = 6.0;

        public ColonyDemand(double baseDemandW, double dayLengthHours)
        {
            if (baseDemandW < 0.0) throw new ArgumentOutOfRangeException(nameof(baseDemandW));
            BaseW = baseDemandW;
            DemandW = baseDemandW;
            TargetW = baseDemandW;
            DayHours = dayLengthHours > 0.0 ? dayLengthHours : 24.0;
            HoursSinceBrownout = 0.0;        // a new colony settles in for a day before it grows
        }

        /// <summary>Life-support load the colony never goes below, W.</summary>
        public double BaseW { get; set; }
        public double DemandW { get; private set; }
        /// <summary>What the colony is growing toward, W.</summary>
        public double TargetW { get; private set; }
        /// <summary>Mean generation over the last full planet day, W (0 before the first day ends).</summary>
        public double AverageGenerationW { get; private set; }

        double dayEnergyWh, dayClockHours;

        public double DayEnergyWh => dayEnergyWh;
        public double DayClockHours => dayClockHours;

        public void Restore(double demandW, double hoursSinceBrownout, double averageGenerationW, double dayEnergyWh, double dayClockHours)
        {
            DemandW = demandW;
            HoursSinceBrownout = hoursSinceBrownout;
            AverageGenerationW = averageGenerationW;
            this.dayEnergyWh = dayEnergyWh;
            this.dayClockHours = dayClockHours;
            TargetW = Math.Max(BaseW, UsableShare * AverageGenerationW);
            DemandW = Math.Max(DemandW, BaseW);
        }
        public double HoursSinceBrownout { get; private set; }
        /// <summary>Length of the averaging and no-brownout window: one planet day (24 h if tidally locked).</summary>
        public double DayHours { get; }

        /// <summary>True while the colony is expanding toward a higher target.</summary>
        public bool IsGrowing => HoursSinceBrownout >= DayHours && DemandW < TargetW * 0.999;

        /// <summary>Advance by <paramref name="hours"/> with what the grid did in that time.</summary>
        public void Step(double hours, double generationW, double servedW, bool brownout)
        {
            if (hours <= 0.0)
                return;
            dayEnergyWh += generationW * hours;
            dayClockHours += hours;
            if (dayClockHours >= DayHours - 1e-9)
            {
                AverageGenerationW = dayEnergyWh / dayClockHours;
                dayEnergyWh = 0.0;
                dayClockHours = 0.0;
            }
            TargetW = Math.Max(BaseW, UsableShare * AverageGenerationW);

            if (brownout)
            {
                HoursSinceBrownout = 0.0;
                double floor = Math.Max(BaseW, servedW);
                if (DemandW > floor)
                    DemandW -= (DemandW - floor) * (1.0 - Math.Exp(-hours / CutBackHours));
            }
            else
            {
                HoursSinceBrownout += hours;
                if (HoursSinceBrownout >= DayHours && DemandW < TargetW)
                    DemandW += (TargetW - DemandW) * (1.0 - Math.Exp(-hours / (GrowthDays * DayHours)));
            }
            DemandW = Math.Max(DemandW, BaseW);
        }
    }
}
