namespace KardashevAscent.Simulation.Solar
{
    /// <summary>Step 9 of the solar model: is a panel worth building.</summary>
    public static class Economics
    {
        /// <summary>EROI = energy over the panel's life / energy used to build it.</summary>
        public static double Eroi(double lifetimeEnergy, double embodiedEnergy) => lifetimeEnergy / embodiedEnergy;

        /// <summary>Payback time = energy used to build / energy per year, in years.</summary>
        public static double PaybackYears(double embodiedEnergy, double energyPerYear) => embodiedEnergy / energyPerYear;
    }
}
