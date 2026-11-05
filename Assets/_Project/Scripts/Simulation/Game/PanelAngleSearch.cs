using System;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>The best panel angles found for one tile.</summary>
    public readonly struct PanelAngles
    {
        /// <summary>False when no angle can take a panel (water, unsupported planet).</summary>
        public readonly bool Found;
        /// <summary>Tilt β from horizontal, radians (0 .. π/2).</summary>
        public readonly double Tilt;
        /// <summary>Azimuth γ clockwise from north, radians (0 .. 2π).</summary>
        public readonly double Azimuth;
        public readonly double EnergyPerYearWh;
        /// <summary>How many angles were tried.</summary>
        public readonly int Evaluations;

        public PanelAngles(bool found, double tilt, double azimuth, double energyPerYearWh, int evaluations)
        {
            Found = found;
            Tilt = tilt;
            Azimuth = azimuth;
            EnergyPerYearWh = energyPerYearWh;
            Evaluations = evaluations;
        }
    }

    /// <summary>How close the player's angle is to the best one.</summary>
    public enum AngleRating
    {
        /// <summary>Under 90 % of the best yield.</summary>
        Off,
        /// <summary>90–98 % of the best yield.</summary>
        Good,
        /// <summary>98 % or more: earns the near-optimal bonus.</summary>
        Optimal,
    }

    /// <summary>
    /// Finds the panel tilt and azimuth with the most energy per year on a tile (#7): a coarse grid
    /// (tilt every 10°, azimuth every 30°) followed by a few rounds of local refinement at half the step.
    /// Each try is one <see cref="IPlanetSimulation.PreviewPanel"/> call, so this is meant for a background thread.
    /// </summary>
    public static class PanelAngleSearch
    {
        public const double OptimalShare = 0.98;
        public const double GoodShare = 0.90;

        const double Deg = Math.PI / 180.0;
        const double MaxTilt = Math.PI / 2.0;
        const double FullTurn = 2.0 * Math.PI;

        public static PanelAngles Best(IPlanetSimulation simulation, int tileId, int refinements = 3)
        {
            if (simulation == null)
                throw new ArgumentNullException(nameof(simulation));
            return Best((tilt, azimuth) => simulation.PreviewPanel(tileId, tilt, azimuth), refinements);
        }

        /// <summary>Search over any preview function (tilt, azimuth in radians).</summary>
        public static PanelAngles Best(Func<double, double, PanelPreview> preview, int refinements = 3)
        {
            if (preview == null)
                throw new ArgumentNullException(nameof(preview));

            int evaluations = 0;
            bool found = false;
            double bestTilt = 0.0, bestAzimuth = 0.0, bestEnergy = double.NegativeInfinity;

            void Try(double tilt, double azimuth)
            {
                tilt = Math.Max(0.0, Math.Min(MaxTilt, tilt));
                azimuth = Wrap(azimuth);
                PanelPreview p = preview(tilt, azimuth);
                evaluations++;
                if (!p.CanPlace || p.EnergyPerYearWh <= bestEnergy)
                    return;
                found = true;
                bestEnergy = p.EnergyPerYearWh;
                bestTilt = tilt;
                bestAzimuth = azimuth;
            }

            // Coarse grid. A flat panel faces the same way at every azimuth, so tilt 0 is tried once.
            const double tiltStep = 10.0 * Deg, azimuthStep = 30.0 * Deg;
            Try(0.0, 0.0);
            for (int t = 1; t <= 9; t++)
                for (int a = 0; a < 12; a++)
                    Try(t * tiltStep, a * azimuthStep);

            if (!found)
                return new PanelAngles(false, 0.0, 0.0, 0.0, evaluations);

            double dt = tiltStep, da = azimuthStep;
            for (int round = 0; round < refinements; round++)
            {
                dt *= 0.5;
                da *= 0.5;
                double centreTilt = bestTilt, centreAzimuth = bestAzimuth;
                for (int i = -1; i <= 1; i++)
                    for (int j = -1; j <= 1; j++)
                        if (i != 0 || j != 0)
                            Try(centreTilt + i * dt, centreAzimuth + j * da);
            }

            // A flat panel has no facing, so its azimuth is reported as 0.
            return new PanelAngles(true, bestTilt, bestTilt < 1e-9 ? 0.0 : bestAzimuth, bestEnergy, evaluations);
        }

        /// <summary>Energy as a share of the best (0 when the best is unknown).</summary>
        public static double ShareOfBest(double energyPerYearWh, double bestEnergyPerYearWh) =>
            bestEnergyPerYearWh > 0.0 ? Math.Max(0.0, energyPerYearWh / bestEnergyPerYearWh) : 0.0;

        public static AngleRating Rate(double shareOfBest) =>
            shareOfBest >= OptimalShare ? AngleRating.Optimal
            : shareOfBest >= GoodShare ? AngleRating.Good
            : AngleRating.Off;

        static double Wrap(double azimuth)
        {
            azimuth %= FullTurn;
            return azimuth < 0.0 ? azimuth + FullTurn : azimuth;
        }
    }
}
