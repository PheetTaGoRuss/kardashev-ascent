using System;
using KardashevAscent.Simulation.Energy;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// A robot's own battery (spec §6, §8): the grid's <see cref="Battery"/> for charge and discharge, plus health,
    /// which is separate from charge. Health falls with equivalent full cycles (energy delivered ÷ first capacity):
    /// H = 1 − (1 − H_EOL)·EFC / N_life, and the capacity is C₀·H (physics-verification.md, robot battery section).
    /// Replacing the battery restores health without replacing the robot.
    /// </summary>
    public sealed class RobotBattery
    {
        readonly Battery cell;

        public RobotBattery(double nominalWh = RobotSpecs.BatteryCapacityWh, double startFraction = 1.0)
        {
            if (nominalWh <= 0.0) throw new ArgumentOutOfRangeException(nameof(nominalWh));
            NominalWh = nominalWh;
            cell = new Battery(nominalWh, Battery.LithiumIonRoundTrip, 1.0, startFraction);
        }

        /// <summary>Capacity when new, Wh.</summary>
        public double NominalWh { get; }
        /// <summary>Energy delivered since this battery was fitted, Wh.</summary>
        public double DeliveredWh { get; private set; }
        public double EquivalentFullCycles => DeliveredWh / NominalWh;
        public double Health => HealthAfter(EquivalentFullCycles);
        public double CapacityWh => cell.CapacityWh;
        public double StoredWh => cell.StoredWh;
        public double StateOfCharge => cell.StateOfCharge;
        public bool WornOut => Health < RobotSpecs.BatteryEndOfLifeHealth;
        /// <summary>Most power the battery takes or gives, W (1 C on its present capacity).</summary>
        public double MaxPowerW => cell.MaxPowerW;

        /// <summary>Health after <paramref name="cycles"/> equivalent full cycles (0–1).</summary>
        public static double HealthAfter(double cycles) =>
            Math.Max(0.0, 1.0 - (1.0 - RobotSpecs.BatteryEndOfLifeHealth) * Math.Max(0.0, cycles) / RobotSpecs.BatteryCycleLife);

        /// <summary>Run a load of <paramref name="powerW"/> for <paramref name="hours"/>; returns the power delivered, W.</summary>
        public double Draw(double powerW, double hours)
        {
            double delivered = cell.Discharge(powerW, hours);
            DeliveredWh += delivered * hours;
            Fade();
            return delivered;
        }

        /// <summary>Offer charging power; returns the power taken, W.</summary>
        public double Charge(double powerW, double hours) => cell.Charge(powerW, hours);

        /// <summary>Power it would take now for <paramref name="hours"/> if offered <paramref name="limitW"/>, W.</summary>
        public double ChargeRequestW(double limitW, double hours)
        {
            if (hours <= 0.0) return 0.0;
            double room = Math.Max(0.0, cell.CapacityWh - cell.StoredWh) / (cell.OneWayEfficiency * hours);
            return Math.Max(0.0, Math.Min(Math.Min(limitW, cell.MaxPowerW), room));
        }

        /// <summary>Fit a new battery of the same kind; the energy stored stays (up to the new capacity).</summary>
        public void Replace()
        {
            DeliveredWh = 0.0;
            cell.AddCapacity(NominalWh - cell.CapacityWh);
        }

        /// <summary>Put back a saved battery: energy delivered over its life and energy stored now.</summary>
        public void Restore(double deliveredWh, double storedWh)
        {
            DeliveredWh = Math.Max(0.0, deliveredWh);
            if (cell.CapacityWh < NominalWh) cell.AddCapacity(NominalWh - cell.CapacityWh);
            Fade();
            cell.SetStored(storedWh);
        }

        void Fade()
        {
            double target = NominalWh * Health;
            if (cell.CapacityWh > target)
                cell.RemoveCapacity(cell.CapacityWh - target);
        }
    }
}
