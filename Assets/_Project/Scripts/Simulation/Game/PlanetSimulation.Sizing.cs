using System;
using KardashevAscent.Simulation.Climate;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>What a dust outlook asks of the colony's power (estimated, built only on the outlook): the shortfall and what would cover it.</summary>
    public readonly struct DustSizing
    {
        public readonly bool Applies;
        /// <summary>The share of the usual sunlight the panels keep in the outlook, and the days it is judged over.</summary>
        public readonly double SolarShare, Days;
        /// <summary>Energy short per day at the current colony load, Wh.</summary>
        public readonly double ShortfallWhPerDay;
        /// <summary>Extra battery storage that would carry the whole period, Wh, or the extra panels (of the average size now) that would close the gap.</summary>
        public readonly double ExtraStorageWh;
        public readonly int ExtraPanels;
        public DustSizing(bool applies, double share, double days, double shortfall, double storage, int panels)
        { Applies = applies; SolarShare = share; Days = days; ShortfallWhPerDay = shortfall; ExtraStorageWh = storage; ExtraPanels = panels; }
    }

    public sealed partial class PlanetSimulation
    {
        /// <summary>
        /// The dust sizing advice (game rule, TBD; estimated, built on the outlook): at the current colony load, with the panels keeping <see cref="ExpectedSolarShareInDust"/>
        /// of their sunlight for a regional-storm season (10 sols) or a global storm (the 15-sol rise plus one 40-sol decay), the daily shortfall, the storage that would carry
        /// the whole period, and the panels (of today's average) that would close the gap. Uses the week's average solar output and the current demand.
        /// </summary>
        /// <summary>The advice as one phrase for the log and the Power window, or empty when nothing is short.</summary>
        public string SizingText(bool globalStorm)
        {
            DustSizing s = DustSizingAdvice(globalStorm);
            if (!s.Applies) return "";
            if (s.ShortfallWhPerDay <= 0.0 && s.ExtraStorageWh <= 0.0) return "At the current load the colony rides the " + (globalStorm ? "storm" : "season") + " out.";
            string panels = s.ExtraPanels == int.MaxValue ? "no panels would do" : $"{s.ExtraPanels} panels";
            return $"Short about {s.ShortfallWhPerDay / 1000.0:0} kWh a day for {s.Days:0} days (and the night): add ~{s.ExtraStorageWh / 1000.0:0} kWh storage or {panels}.";
        }

        public DustSizing DustSizingAdvice(bool globalStorm)
        {
            if (!ThinCo2Air) return new DustSizing(false, 1.0, 0.0, 0.0, 0.0, 0);
            double share = ExpectedSolarShareInDust(globalStorm);
            double days = globalStorm ? ThinAirClimate.RiseDays + ThinAirClimate.DecayDays : 10.0;
            double other = TidalPowerW() + WavePowerW() + BarragePowerW() + WindPowerW() + RtgW;
            double mean = 0.0; int n = History != null ? History.Count : 0;
            for (int i = 0; i < n; i++) mean += History[i].GenerationW;
            double meanGen = n > 0 ? mean / n : GenerationW;
            double solar = Math.Max(0.0, meanGen - other);
            double gapW = Math.Max(0.0, DemandW - other - solar * share);
            double perDay = gapW * 24.0;
            int panels = 0; for (int t = 0; t < TileCount; t++) panels += PanelCount(t);
            double perPanelW = panels > 0 ? solar / panels * share : 0.0;
            int extra = gapW <= 0.0 ? 0 : perPanelW > 1e-9 ? (int)Math.Ceiling(gapW / perPanelW) : int.MaxValue;
            // the average shortfall over the period, plus a night's load that the panels never cover, less the storage the colony already has
            double needed = perDay * days * (DayLengthHours / 24.0) + Math.Max(0.0, DemandW - other) * DayLengthHours / 2.0;
            return new DustSizing(true, share, days, perDay, Math.Max(0.0, needed - BatteryCapacityWh), extra);
        }
    }
}
