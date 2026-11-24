using System;
using KardashevAscent.Simulation.Electrical;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Research tied to the physics (decided 2026-11-23, director delegated; docs/design/research-station.md): until a quantity is
    /// measured the game shows an estimate with a range, and buildings run on the cautious end. The simulation always uses the true values.
    /// All ranges are game rules, TBD.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>The share of relief the assumed scale can be off by: Earth twins and Mars are well mapped, other worlds scale Earth by 1/g (game rule, TBD).</summary>
        public const double ReliefSpreadWell = 0.10, ReliefSpreadAssumed = 0.30;
        /// <summary>The share a tide range seen only from orbit can be off by (game rule, TBD).</summary>
        public const double TideSpreadShare = 0.50;
        /// <summary>The share an ore grade can be off by before an assay (game rule, TBD).</summary>
        public const double GradeSpreadShare = 0.25;
        /// <summary>The cable rating never drops below this share while the air is unmeasured (game rule, TBD).</summary>
        public const double MinimumCableShare = 0.5;

        int EstimateKey(int salt)
        {
            int h = salt;
            foreach (char c in PlanetId ?? "") h = unchecked(h * 31 + c);
            return h;
        }

        /// <summary>Air pressure at the base, Pa: exact once measured, else the orbit brief's range.</summary>
        public KnowledgeEstimate PressureEstimate =>
            KnowledgeEstimate.Of(PressurePaAt(KnowledgeTile), PressurePaAt(KnowledgeTile) * OrbitalBrief.PressureSpreadShare * ExperimentRangeShare(ExperimentKind.AirPressure), false, EstimateKey(1));

        /// <summary>Mean air temperature at the base over a year, C: exact once measured, else the orbit brief's range.</summary>
        public KnowledgeEstimate TemperatureEstimate
        {
            get
            {
                var (lo, hi) = SurfaceTemperatureRange(KnowledgeTile);
                return KnowledgeEstimate.Of((lo + hi) / 2.0, OrbitalBrief.TemperatureSpreadC * ExperimentRangeShare(ExperimentKind.SurfaceTemperature), false, EstimateKey(2));
            }
        }

        /// <summary>The highest point of the planet, m: exact after the terrain relief survey, else the assumed scale with its range.</summary>
        public KnowledgeEstimate HighestPointEstimate
        {
            get
            {
                string id = (PlanetId ?? "").ToLowerInvariant();
                double spread = id == "mars" || id == "earth" || id == "viridia" ? ReliefSpreadWell : ReliefSpreadAssumed;
                double max = Relief.MaxM;
                return KnowledgeEstimate.Of(max, max * spread * ExperimentRangeShare(ExperimentKind.ReliefSurvey), false, EstimateKey(3));
            }
        }

        /// <summary>The open-sea tide range, m: exact after the tide gauge, else a range (0 and exact with no moon).</summary>
        public KnowledgeEstimate TideRangeEstimate
        {
            get
            {
                double open = Tides.EquilibriumRangeM(Moon, planetRadiusM);
                return KnowledgeEstimate.Of(open, open * TideSpreadShare * ExperimentRangeShare(ExperimentKind.TideGauge), false, EstimateKey(4));
            }
        }

        /// <summary>The grade of an ore at a surveyed tile, 0 to 1: exact after the ore assay, else a range.</summary>
        public KnowledgeEstimate GradeEstimate(int tileId, Material material)
        {
            double g = Deposits.GradeOf(tileId, material);
            if (material == Material.Rock) return new KnowledgeEstimate(g, 0.0);   // rock is everywhere on land: nothing to assay
            return KnowledgeEstimate.Of(g, g * GradeSpreadShare * ExperimentRangeShare(ExperimentKind.OreAssay), false, unchecked(EstimateKey(5) + tileId * 7 + (int)material));
        }

        /// <summary>What one mining robot digs out of a tile, kg per hour: the nominal rate times the grade, as a range until the assay.</summary>
        public KnowledgeEstimate MiningRateEstimate(int tileId, Material material)
        {
            KnowledgeEstimate g = GradeEstimate(tileId, material);
            return new KnowledgeEstimate(Robots.RobotSpecs.MiningKgPerHour * g.Value, Robots.RobotSpecs.MiningKgPerHour * g.HalfRange);
        }

        /// <summary>True where Peek's law is not valid (the Paschen region, with a labelled fallback): the cable rating there is a game rule, not the law's result (UI marks, outside model).</summary>
        public bool CableOutsideModel
        {
            get
            {
                double p = planet.SurfacePressureEarth * CableEnvironment.StandardPressurePa;
                return CableEnvironment.RegimeOf(CableEnvironment.RelativeAirDensity(p, 25.0)) == CableEnvironment.Regime.Paschen;
            }
        }

        /// <summary>
        /// The share of its rating a cable is given while the air is unmeasured: the voltage derate at the cautious (thin) end of the
        /// pressure range over the derate at the true pressure, never below <see cref="MinimumCableShare"/>. 1 once the air pressure is measured.
        /// </summary>
        public double CableDesignShare
        {
            get
            {
                double rangeShare = ExperimentRangeShare(ExperimentKind.AirPressure);
                if (!SafetyMarginsEnabled || rangeShare <= 0.0) return 1.0;
                double p = planet.SurfacePressureEarth * CableEnvironment.StandardPressurePa;
                double delta = CableEnvironment.RelativeAirDensity(p, 25.0);
                double low = CableEnvironment.RelativeAirDensity(p * (1.0 - OrbitalBrief.PressureSpreadShare * rangeShare), 25.0);
                double area = 500e-6;
                double now = CableEnvironment.VoltageDerate(delta, area, out _), cautious = CableEnvironment.VoltageDerate(low, area, out _);
                return now <= 0.0 ? 1.0 : Math.Max(MinimumCableShare, Math.Min(1.0, cautious / now));
            }
        }
    }
}
