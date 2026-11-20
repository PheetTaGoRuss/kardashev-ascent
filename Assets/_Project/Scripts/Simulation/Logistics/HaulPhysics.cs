using System;

namespace KardashevAscent.Simulation.Logistics
{
    /// <summary>
    /// The energy of hauling cargo (mission operations spec, MO-04). Rolling resistance is F = C_rr m g, so the drive
    /// power grows with the gravity and with the total mass (robot + cargo). The existing drive power (a robot with no
    /// cargo) is the calibration, so an empty robot costs exactly what it did before. Climbing is potential energy
    /// m g dh over the drive efficiency, with no regeneration downhill (an assumption). Sources and the open
    /// parameters are in docs/physics/physics-verification.md ("Mission operations: sources").
    /// </summary>
    public static class HaulPhysics
    {
        /// <summary>Mass of a robot, kg: Perseverance, 1 025 kg (NASA). To verify; a game default.</summary>
        public const double DefaultRobotMassKg = 1025.0;

        /// <summary>Standard gravity, m/s², the calibration gravity of the drive power.</summary>
        public const double StandardGravityMs2 = 9.80665;

        /// <summary>
        /// Metres of height per unit of <c>PlanetSurface.Elevation</c> (dimensionless, about -1 to 1). An assumed scale:
        /// game rule, TBD (open question Q5). Climbing is off by default (<see cref="ClimbEnabled"/>).
        /// </summary>
        public const double MetresPerElevationUnit = 1000.0;

        /// <summary>Whether the climb term counts. Off until the director approves the scale (Q5).</summary>
        public static bool ClimbEnabled { get; set; }

        /// <summary>
        /// Drive power, W, with cargo: P = P_base (g / g_earth) (m_r + m_c) / m_r. With no cargo this is the plain
        /// drive power for that gravity.
        /// </summary>
        public static double DrivePowerW(double baseDrivePowerW, double gravityMs2, double robotMassKg, double cargoKg)
        {
            if (robotMassKg <= 0.0) throw new ArgumentOutOfRangeException(nameof(robotMassKg));
            double g = gravityMs2 / StandardGravityMs2;
            return baseDrivePowerW * g * (robotMassKg + Math.Max(0.0, cargoKg)) / robotMassKg;
        }

        /// <summary>The multiplier a cargo puts on the empty drive power: (m_r + m_c) / m_r.</summary>
        public static double LoadFactor(double robotMassKg, double cargoKg) =>
            (robotMassKg + Math.Max(0.0, cargoKg)) / robotMassKg;

        /// <summary>Energy to climb <paramref name="heightM"/> metres, Wh, with drive efficiency <paramref name="efficiency"/>; zero downhill.</summary>
        public static double ClimbEnergyWh(double robotMassKg, double cargoKg, double gravityMs2, double heightM, double efficiency)
        {
            if (efficiency <= 0.0) throw new ArgumentOutOfRangeException(nameof(efficiency));
            double up = Math.Max(0.0, heightM);
            return (robotMassKg + Math.Max(0.0, cargoKg)) * gravityMs2 * up / efficiency / 3600.0;
        }

        /// <summary>Light-years from parsecs: x 3.26156 (IAU). One-way light time to Earth in years equals this.</summary>
        public static double LightYearsFromParsecs(double parsecs) => parsecs * 3.26156;
    }
}
