using System;

namespace KardashevAscent.Simulation.Climate
{
    /// <summary>Energy sources, grouped by whether they add heat the planet would not otherwise have.</summary>
    public enum EnergySourceKind
    {
        SolarPV,
        ConcentratedSolar,
        Wind,
        Hydro,
        Biomass,
        Otec,
        DayNightThermoelectric,
        Tidal,
        Geothermal,
        Fossil,
        Fission,
        Fusion,
        Rtg,
        MoonDynamo,
        OrbitalSolarBeamed,
        Antimatter,
    }

    /// <summary>
    /// Planet temperature with waste heat (docs/physics/physics-verification.md, P1 item 11).
    /// Uses the full T⁴ balance, never the linear dT ≈ T/4 · P_w/P_abs shortcut (it overshoots by ~17 % at 6 × 10¹⁶ W).
    /// </summary>
    public static class WasteHeat
    {
        /// <summary>Stefan–Boltzmann constant, W m⁻² K⁻⁴.</summary>
        public const double Sigma = 5.670374419e-8;

        /// <summary>
        /// Surface warming ÷ no-feedback (Planck) warming. 3 matches Earth's climate sensitivity
        /// λ ≈ 0.8 K per W/m² (λ / λ₀ with λ₀ = 1/(4σT³) ≈ 0.27 at 254 K).
        /// </summary>
        public const double DefaultFeedbackFactor = 3.0;

        /// <summary>Typical albedo of a PV panel (dark glass), used for the albedo change of solar farms.</summary>
        public const double PanelAlbedo = 0.1;

        /// <summary>
        /// T_eq⁴ = L(1 − A) / (16πσd²) + P_waste / (4πR²σ), K. Full heat redistribution, no greenhouse effect.
        /// </summary>
        public static double EquilibriumTemperatureK(double luminosityW, double distanceM, double bondAlbedo,
                                                     double planetRadiusM, double wasteHeatW = 0.0)
        {
            double fromStar = luminosityW * (1.0 - bondAlbedo) / (16.0 * Math.PI * Sigma * distanceM * distanceM);
            double fromWaste = Math.Max(0.0, wasteHeatW) / (4.0 * Math.PI * planetRadiusM * planetRadiusM * Sigma);
            return Math.Pow(fromStar + fromWaste, 0.25);
        }

        /// <summary>Waste heat spread over the whole surface, W/m². 10¹⁶ W on Earth ≈ 19.6 W/m² (≈ 5.3 × a CO₂ doubling).</summary>
        public static double ForcingWm2(double wasteHeatW, double planetRadiusM) =>
            wasteHeatW / (4.0 * Math.PI * planetRadiusM * planetRadiusM);

        /// <summary>No-feedback rise of the equilibrium temperature, K. Earth + 10¹⁶ W ≈ 5.1 K.</summary>
        public static double EquilibriumWarmingK(double luminosityW, double distanceM, double bondAlbedo,
                                                 double planetRadiusM, double wasteHeatW) =>
            EquilibriumTemperatureK(luminosityW, distanceM, bondAlbedo, planetRadiusM, wasteHeatW)
            - EquilibriumTemperatureK(luminosityW, distanceM, bondAlbedo, planetRadiusM);

        /// <summary>
        /// Surface warming with climate feedbacks (water vapour, ice), K: feedback factor × the no-feedback rise.
        /// Earth + 10¹⁶ W ≈ 15 K. A planet with no atmosphere should pass feedbackFactor = 1.
        /// </summary>
        public static double SurfaceWarmingK(double luminosityW, double distanceM, double bondAlbedo,
                                             double planetRadiusM, double wasteHeatW,
                                             double feedbackFactor = DefaultFeedbackFactor) =>
            feedbackFactor * EquilibriumWarmingK(luminosityW, distanceM, bondAlbedo, planetRadiusM, wasteHeatW);

        /// <summary>
        /// True when the source adds heat the planet would not otherwise have. Sunlight-driven sources
        /// (PV, CSP, wind, hydro, biomass, OTEC, day–night thermoelectric) only move heat that was already
        /// arriving; ground solar changes the planet's albedo instead (<see cref="BondAlbedoWithPanels"/>).
        /// Tidal power diverts dissipation that happens anyway. Geothermal is counted as added because
        /// a colony mines stored heat faster than it would flow out on its own.
        /// </summary>
        public static bool AddsWasteHeat(EnergySourceKind source)
        {
            switch (source)
            {
                case EnergySourceKind.SolarPV:
                case EnergySourceKind.ConcentratedSolar:
                case EnergySourceKind.Wind:
                case EnergySourceKind.Hydro:
                case EnergySourceKind.Biomass:
                case EnergySourceKind.Otec:
                case EnergySourceKind.DayNightThermoelectric:
                case EnergySourceKind.Tidal:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Planet Bond albedo after covering <paramref name="panelAreaM2"/> of ground with panels:
        /// A' = A − (area / 4πR²)·(ρ_ground − ρ_panel). Panels are darker, so the planet absorbs a little more sunlight.
        /// Simplification: ignores clouds and the atmosphere between the panels and space.
        /// </summary>
        public static double BondAlbedoWithPanels(double bondAlbedo, double panelAreaM2, double planetRadiusM,
                                                  double groundAlbedo, double panelAlbedo = PanelAlbedo)
        {
            double covered = Math.Min(1.0, Math.Max(0.0, panelAreaM2 / (4.0 * Math.PI * planetRadiusM * planetRadiusM)));
            return Math.Max(0.0, Math.Min(1.0, bondAlbedo - covered * (groundAlbedo - panelAlbedo)));
        }
    }
}
