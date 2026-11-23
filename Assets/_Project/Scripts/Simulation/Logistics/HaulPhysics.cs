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
        /// The share of the true climbing energy m g dh the robots pay (game rule, TBD; decided 2026-11-23 with the real relief, see
        /// <c>Planet.ReliefScale</c>). At the full 1.0 a tile hop on land climbs 1.2 km on average, which costs a one-tonne robot about 3.3 kWh,
        /// ten times the starting robot's 350 Wh battery, whose flat drive (50 W, 5 h a hop) costs 260 Wh. The game's robots are on a smaller
        /// scale than the real mass used here, so 0.15 makes the average climb about as costly as the flat drive. Tune with the robot battery.
        /// </summary>
        public const double ClimbEffortShare = 0.15;

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
