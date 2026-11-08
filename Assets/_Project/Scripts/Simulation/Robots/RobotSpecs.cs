namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// Numbers for robots. Sourced values say where they come from; values marked TBD are PLACEHOLDERS pending
    /// decision D4 in docs/design/robotic-workforce.md: they make the mechanics run and carry no balance claim.
    /// </summary>
    public static class RobotSpecs
    {
        // ---- Battery (docs/physics/physics-verification.md, robot battery section) ----

        /// <summary>
        /// A General robot's battery, Wh: Perseverance's two 43 Ah Li-ion batteries at 28 V
        /// (JPL Mars 2020 press kit; EaglePicher) = 2 × 43 × 28.
        /// </summary>
        public const double BatteryCapacityWh = 2.0 * 43.0 * 28.0;

        /// <summary>Go to charge below this state of charge (spec §6).</summary>
        public const double ReturnToChargeFraction = 0.20;

        /// <summary>Charge until this state of charge, then go back to work (spec §6).</summary>
        public const double ResumeWorkFraction = 0.80;

        /// <summary>Equivalent full cycles to end of life: low end of 3000–8000 for LFP/NMC (energy_sources.md §13).</summary>
        public const double BatteryCycleLife = 3000.0;

        /// <summary>Health at end of life: cycle life is counted to 80 % of the first capacity.</summary>
        public const double BatteryEndOfLifeHealth = 0.80;

        // ---- Energy use (TBD placeholders) ----

        /// <summary>Draw while idle or waiting, W. TBD (D4).</summary>
        public const double IdlePowerW = 20.0;

        /// <summary>Draw while building, W. TBD (D4).</summary>
        public const double BuildPowerW = 200.0;

        /// <summary>Draw while taking a building down, W. TBD (D4).</summary>
        public const double SalvagePowerW = 200.0;

        /// <summary>Draw while surveying (sensors, drilling cores), W. TBD (D4).</summary>
        public const double SurveyPowerW = 100.0;

        /// <summary>Draw while mining, W. TBD (D4).</summary>
        public const double MinePowerW = 300.0;

        public static double WorkingPowerW(TaskKind kind)
        {
            switch (kind)
            {
                case TaskKind.Salvage: return SalvagePowerW;
                case TaskKind.Survey: return SurveyPowerW;
                case TaskKind.Mine: return MinePowerW;
                default: return BuildPowerW;
            }
        }

        // ---- Survey and mining (phase 3a; TBD placeholders) ----

        /// <summary>Hours of work to survey one tile's ground. TBD (D4).</summary>
        public const double SurveyWorkHours = 2.0;

        /// <summary>Hours of work in one mining batch; the robot re-evaluates its work after each. TBD (D4).</summary>
        public const double MiningBatchHours = 1.0;

        /// <summary>Material mined per hour of work from a grade-1 deposit, kg. TBD (D4).</summary>
        public const double MiningKgPerHour = 50.0;

        /// <summary>A specialised robot's work speed at its own job, relative to a General robot. TBD (D4).</summary>
        public const double SpecialistSpeed = 2.0;

        // ---- Condition and maintenance (TBD placeholders) ----

        /// <summary>Condition lost per hour of work (1 = new). TBD (D4).</summary>
        public const double WearPerWorkingHour = 0.001;

        /// <summary>Extra condition lost per hour on a planet of a flaring red dwarf (design D13). TBD (D4).</summary>
        public const double FlareWearPerHour = 0.0005;

        /// <summary>
        /// Stars cooler than this are M dwarfs, which flare often (Proxima Cen, TRAPPIST-1: stellar_exoplanet.md §2).
        /// The ~3,900 K boundary between K and M dwarfs is background knowledge, not re-sourced.
        /// </summary>
        public const double FlaringStarBelowK = 3900.0;

        /// <summary>Go for maintenance below this condition. TBD (D4).</summary>
        public const double MaintenanceBelowCondition = 0.30;

        /// <summary>Condition restored per hour of maintenance. TBD (D4).</summary>
        public const double RepairPerHour = 0.10;

        /// <summary>Build parts for a full repair (condition 0 → 1), Wh. TBD (D4); manufacture (phase 3) must cost more.</summary>
        public const double RepairPartsWh = 10_000.0;
    }

    /// <summary>
    /// What a facility offers robots (spec §6): charging power per robot, how many robots at once, and whether it
    /// repairs (design D10, D12). Slot counts and powers are TBD placeholders (D4).
    /// </summary>
    public readonly struct ChargingCapability
    {
        public readonly int Slots;
        public readonly double PowerW;
        public readonly bool Repairs;

        public ChargingCapability(int slots, double powerW, bool repairs)
        {
            Slots = slots;
            PowerW = powerW;
            Repairs = repairs;
        }

        /// <summary>The lander: the robots' home, charges and repairs (D10). TBD values.</summary>
        public static ChargingCapability Lander => new ChargingCapability(2, 1_000.0, true);

        /// <summary>A battery yard charges robots (spec §6 example). TBD values.</summary>
        public static ChargingCapability BatteryYard => new ChargingCapability(4, 2_000.0, false);
    }

    /// <summary>One place robots can charge or be repaired. Slot −1 is the lander on its tile.</summary>
    public readonly struct RobotFacility
    {
        public readonly int Tile;
        public readonly int Slot;
        public readonly ChargingCapability Capability;

        public RobotFacility(int tile, int slot, ChargingCapability capability)
        {
            Tile = tile;
            Slot = slot;
            Capability = capability;
        }
    }
}
