using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>Panel technology tiers for energy payback (docs/physics/physics-verification.md, P0 item 9).</summary>
    public enum PanelTechnology
    {
        /// <summary>~2010 mono-Si at 13 % (Bhandari et al. 2015 harmonized mean: EPBT 4.1 years).</summary>
        EarlyMonoSi,
        /// <summary>Mono-Si PERC/TOPCon at 20.9 % (IEA PVPS Task 12, 2024: EPBT ≈ 1 year).</summary>
        ModernMonoSi,
    }

    /// <summary>
    /// Step 9 of the solar model: is a panel worth building.
    /// Convention: both sides counted in electricity, which equals the literature's primary-energy-equivalent EPBT.
    /// The literature's reference site gets 1700 kWh/m² per year at performance ratio 0.75.
    /// </summary>
    public static class Economics
    {
        /// <summary>Yearly light on the panel at the literature's reference site, kWh/m².</summary>
        public const double ReferenceIrradiationKWhPerM2 = 1700.0;

        /// <summary>Performance ratio used by Bhandari et al. 2015 (wiring, inverter, soiling, temperature).</summary>
        public const double ReferencePerformanceRatio = 0.75;

        /// <summary>Harmonized panel lifetime, years (Bhandari et al. 2015).</summary>
        public const double LifetimeYears = 30.0;

        /// <summary>EROI = energy over the panel's life / energy used to build it.</summary>
        public static double Eroi(double lifetimeEnergy, double embodiedEnergy) => lifetimeEnergy / embodiedEnergy;

        /// <summary>Payback time = energy used to build / energy per year, in years.</summary>
        public static double PaybackYears(double embodiedEnergy, double energyPerYear) => embodiedEnergy / energyPerYear;

        /// <summary>Energy payback at the reference site, years.</summary>
        public static double ReferencePaybackYears(PanelTechnology tech)
        {
            switch (tech)
            {
                case PanelTechnology.EarlyMonoSi: return 4.1;
                case PanelTechnology.ModernMonoSi: return 1.0;
                default: throw new ArgumentOutOfRangeException(nameof(tech));
            }
        }

        /// <summary>Module efficiency the reference payback was measured with.</summary>
        public static double ReferenceEfficiency(PanelTechnology tech)
        {
            switch (tech)
            {
                case PanelTechnology.EarlyMonoSi: return 0.13;
                case PanelTechnology.ModernMonoSi: return 0.209;
                default: throw new ArgumentOutOfRangeException(nameof(tech));
            }
        }

        /// <summary>
        /// Energy needed to make 1 m² of panel, Wh (electricity-equivalent):
        /// EPBT_ref × 1700 kWh/m² × η_ref × PR. Modern ≈ 266 kWh/m², early ≈ 680 kWh/m².
        /// Use with <see cref="PaybackYears"/> and the simulated yield, so clouds, angle and heat all count.
        /// </summary>
        public static double EmbodiedWhPerM2(PanelTechnology tech) =>
            ReferencePaybackYears(tech) * ReferenceIrradiationKWhPerM2 * 1000.0
            * ReferenceEfficiency(tech) * ReferencePerformanceRatio;

        /// <summary>
        /// Quick estimate without a yield simulation: EPBT ≈ EPBT_ref × 1700 / (yearly light on the panel).
        /// A Mars panel at 40 % of the reference light pays back in 2.5 years (modern tech).
        /// </summary>
        public static double PaybackYearsFromIrradiation(PanelTechnology tech, double irradiationKWhPerM2Year) =>
            ReferencePaybackYears(tech) * ReferenceIrradiationKWhPerM2 / irradiationKWhPerM2Year;

        /// <summary>EROI ≈ lifetime / EPBT [inference]. Modern at the reference site ≈ 30.</summary>
        public static double EroiFromPayback(double paybackYears, double lifetimeYears = LifetimeYears) =>
            lifetimeYears / paybackYears;
    }
}
