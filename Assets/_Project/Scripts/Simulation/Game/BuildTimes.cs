using System;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>How long buildings take to put up (director 2026-11-15: as in real life, days to weeks).</summary>
    public static class BuildTimes
    {
        /// <summary>
        /// Robot work hours to put up a building, for one General robot working round the clock (director 2026-11-15:
        /// construction times as in real life, days to weeks; time can be sped up, and a crew is faster). Calibrated to
        /// real installations, rounded, TBD: a utility solar farm takes about 1–2 person-hours per kW installed (NREL
        /// cost benchmarks), so a 1 ha field (about 0.6 MW) about 1 000 h; a containerised battery of 20 MWh a few
        /// weeks for a small crew; a steel-frame workshop hall about a month for a crew; a lab somewhat less; a charger
        /// or mast a few days. Robots assemble parts made ready on the lander's design: about four times a human worker's
        /// pace (director 2026-11-15: building faster; game rule, TBD). A charger goes up in half a day, so the first robots
        /// can build one before their batteries run low.
        /// </summary>
        public static double Hours(BuildingType type, double areaM2 = 0.0)
        {
            switch (type)
            {
                case BuildingType.SolarPanel: return PanelHoursPerHa * Math.Pow(Math.Max(areaM2, 1.0) / 10_000.0, 0.9);
                case BuildingType.FloatingSolar: return FloatingHoursFactor * PanelHoursPerHa * Math.Pow(Math.Max(areaM2, 1.0) / 10_000.0, 0.9);
                case BuildingType.Battery: return 100.0;
                case BuildingType.Workshop: return 300.0;
                case BuildingType.ResearchStation: return 150.0;
                case BuildingType.AluminaPlant:
                case BuildingType.Electrolyser: return 120.0;
                case BuildingType.ChargingStation: return 12.0;
                case BuildingType.Relay: return 12.0;
                default: return 0.0;
            }
        }

        /// <summary>A 1 ha base solar field, robot hours (about 0.6 MW at about 1.6 person-hours per kW, at four times a worker's pace; TBD).</summary>
        public const double PanelHoursPerHa = 240.0;
        /// <summary>Floating panels take longer (pontoons, anchoring; TBD).</summary>
        public const double FloatingHoursFactor = 1.3;

    }
}
