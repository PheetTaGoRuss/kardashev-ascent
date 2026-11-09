using System;
using KardashevAscent.Simulation.Energy;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>
    /// A robot's own battery (spec v2 §7): the grid's <see cref="Battery"/> for charge and discharge, plus health,
    /// which is separate from charge (physics-verification.md, robot battery sections 1 and 2).
    /// <list type="bullet">
    /// <item><b>Ageing by depth of discharge:</b> each cycle (from the end of one charge to the start of the next) of
    /// depth D costs (1 − H_EOL)/N₁₀₀ · D^b of health (Wöhler form; b is a TBD placeholder). Discharging to 0 % adds
    /// <see cref="ZeroDischargeDamage"/> each time.</item>
    /// <item><b>Cold:</b> below room temperature part of the capacity can't be used (<see cref="ColdCapacityFactor"/>,
    /// Bressan et al. 2025). The energy stays in the cell and comes back when it is warm; it is not ageing.</item>
    /// <item>Minor ageing can be repaired; a worn-out battery is replaced without replacing the robot.</item>
    /// </list>
    /// </summary>
    public sealed class RobotBattery
    {
        /// <summary>Cycles to end of life at 100 % depth of discharge (energy_sources.md §13, low end of LFP/NMC).</summary>
        public const double CyclesAtFullDepth = RobotSpecs.BatteryCycleLife;
        /// <summary>Wöhler exponent: deeper cycles age the battery more than in proportion. TBD (no fitted value found).</summary>
        public const double DepthExponent = 1.5;
        /// <summary>Extra health lost each time the battery is run to 0 %. TBD (spec v2 §7 confirms the effect, no source for the size).</summary>
        public const double ZeroDischargeDamage = 0.002;
        /// <summary>Minor ageing (health at or above this) can be repaired in maintenance. TBD.</summary>
        public const double MinorRepairAbove = 0.90;

        readonly Battery cell;
        double coldFactor = 1.0;
        double cycleTop;      // state of charge (of the whole cell) when the last charge ended
        double cycleLow;      // lowest since then
        bool atZero;

        public RobotBattery(double nominalWh = RobotSpecs.BatteryCapacityWh, double startFraction = 1.0)
        {
            if (nominalWh <= 0.0) throw new ArgumentOutOfRangeException(nameof(nominalWh));
            NominalWh = nominalWh;
            cell = new Battery(nominalWh, Battery.LithiumIonRoundTrip, 1.0, startFraction);
            cycleTop = cycleLow = cell.StateOfCharge;
        }

        /// <summary>Capacity when new, Wh.</summary>
        public double NominalWh { get; }
        /// <summary>Energy delivered since this battery was fitted, Wh.</summary>
        public double DeliveredWh { get; private set; }
        public double EquivalentFullCycles => DeliveredWh / NominalWh;
        /// <summary>Health lost so far (cycles by depth, plus discharges to 0 %), minus repairs.</summary>
        public double Damage { get; private set; }
        /// <summary>Times it was run down to 0 %.</summary>
        public int ZeroDischarges { get; private set; }
        public double Health => Math.Max(0.0, 1.0 - Damage);
        public bool WornOut => Health < RobotSpecs.BatteryEndOfLifeHealth;

        /// <summary>Capacity now (health applied), Wh.</summary>
        public double CapacityWh => cell.CapacityWh;
        /// <summary>Energy in the cell, Wh (some of it may be locked by the cold).</summary>
        public double StoredWh => cell.StoredWh;
        /// <summary>Share of the capacity usable at the present temperature (1 when warm).</summary>
        public double ColdFactor
        {
            get => coldFactor;
            set => coldFactor = Math.Max(0.05, Math.Min(1.0, value));
        }
        /// <summary>Energy locked by the cold, Wh.</summary>
        public double LockedWh => (1.0 - coldFactor) * cell.CapacityWh;
        /// <summary>Energy it can use now, Wh.</summary>
        public double UsableWh => Math.Max(0.0, cell.StoredWh - LockedWh);
        /// <summary>Usable charge as a share of the usable capacity: what the 20 %/80 % thresholds look at.</summary>
        public double StateOfCharge => cell.CapacityWh > 0.0 ? UsableWh / (coldFactor * cell.CapacityWh) : 0.0;
        /// <summary>Most power the battery takes or gives, W (1 C on its present capacity).</summary>
        public double MaxPowerW => cell.MaxPowerW;

        /// <summary>Health after <paramref name="cycles"/> full-depth cycles (0–1).</summary>
        public static double HealthAfter(double cycles) =>
            Math.Max(0.0, 1.0 - (1.0 - RobotSpecs.BatteryEndOfLifeHealth) * Math.Max(0.0, cycles) / CyclesAtFullDepth);

        /// <summary>Health one cycle of depth <paramref name="depth"/> (0–1) costs: (1 − H_EOL)/N₁₀₀ · D^b.</summary>
        public static double CycleDamage(double depth) =>
            (1.0 - RobotSpecs.BatteryEndOfLifeHealth) / CyclesAtFullDepth * Math.Pow(Math.Max(0.0, Math.Min(1.0, depth)), DepthExponent);

        /// <summary>
        /// Share of capacity usable at <paramref name="temperatureC"/> (Bressan et al. 2025, LFP module): 1 at 25 °C,
        /// 0.85 at −10 °C, 0.65 at −20 °C, straight lines between, held at 0.65 below −20 °C (no data there).
        /// </summary>
        public static double ColdCapacityFactor(double temperatureC)
        {
            if (temperatureC >= 25.0) return 1.0;
            if (temperatureC >= -10.0) return 0.85 + 0.15 * (temperatureC + 10.0) / 35.0;
            if (temperatureC >= -20.0) return 0.65 + 0.20 * (temperatureC + 20.0) / 10.0;
            return 0.65;
        }

        /// <summary>Run a load of <paramref name="powerW"/> for <paramref name="hours"/>; returns the power delivered, W.</summary>
        public double Draw(double powerW, double hours)
        {
            if (powerW <= 0.0 || hours <= 0.0)
                return 0.0;
            double usableW = UsableWh * cell.OneWayEfficiency / hours;
            double delivered = cell.Discharge(Math.Min(powerW, usableW), hours);
            DeliveredWh += delivered * hours;
            cycleLow = Math.Min(cycleLow, cell.StateOfCharge);
            if (UsableWh <= 1e-6 && delivered < powerW - 1e-9)
            {
                if (!atZero)
                {
                    ZeroDischarges++;
                    Damage += ZeroDischargeDamage;
                    atZero = true;
                }
            }
            Fade();
            return delivered;
        }

        /// <summary>Offer charging power; returns the power taken, W. A charge ends the cycle that ran since the last one.</summary>
        public double Charge(double powerW, double hours)
        {
            double taken = cell.Charge(powerW, hours);
            if (taken > 0.0)
            {
                if (cycleTop - cycleLow > 1e-6)
                    Damage += CycleDamage(cycleTop - cycleLow);
                cycleTop = cycleLow = cell.StateOfCharge;
                atZero = false;
                Fade();
            }
            return taken;
        }

        /// <summary>Power it would take now for <paramref name="hours"/> if offered <paramref name="limitW"/>, W.</summary>
        public double ChargeRequestW(double limitW, double hours)
        {
            if (hours <= 0.0) return 0.0;
            double room = Math.Max(0.0, cell.CapacityWh - cell.StoredWh) / (cell.OneWayEfficiency * hours);
            return Math.Max(0.0, Math.Min(Math.Min(limitW, cell.MaxPowerW), room));
        }

        /// <summary>Repair minor ageing (health at or above <see cref="MinorRepairAbove"/>) by up to <paramref name="health"/>; returns what was restored.</summary>
        public double RepairMinor(double health)
        {
            if (Health < MinorRepairAbove || Damage <= 0.0 || health <= 0.0)
                return 0.0;
            double gain = Math.Min(Damage, health);
            Damage -= gain;
            cell.AddCapacity(NominalWh * Health - cell.CapacityWh);
            return gain;
        }

        /// <summary>Fit a new battery of the same kind; the energy stored stays (up to the new capacity).</summary>
        public void Replace()
        {
            DeliveredWh = 0.0;
            Damage = 0.0;
            ZeroDischarges = 0;
            atZero = false;
            cell.AddCapacity(NominalWh - cell.CapacityWh);
            cycleTop = cycleLow = cell.StateOfCharge;
        }

        /// <summary>
        /// Put back a saved battery from before save version 15: health from full cycles (b = 1, as version 1 counted it).
        /// </summary>
        public void Restore(double deliveredWh, double storedWh) =>
            Restore(deliveredWh, storedWh, (1.0 - RobotSpecs.BatteryEndOfLifeHealth) * Math.Max(0.0, deliveredWh) / NominalWh / CyclesAtFullDepth, -1.0, -1.0, 0);

        /// <summary>Put back a saved battery (save version 15): damage, the open cycle, discharges to 0 %.</summary>
        public void Restore(double deliveredWh, double storedWh, double damage, double cycleTopSoc, double cycleLowSoc, int zeroDischarges)
        {
            DeliveredWh = Math.Max(0.0, deliveredWh);
            Damage = Math.Max(0.0, damage);
            ZeroDischarges = Math.Max(0, zeroDischarges);
            if (cell.CapacityWh < NominalWh) cell.AddCapacity(NominalWh - cell.CapacityWh);
            Fade();
            cell.SetStored(storedWh);
            cycleTop = cycleTopSoc >= 0.0 ? cycleTopSoc : cell.StateOfCharge;
            cycleLow = cycleLowSoc >= 0.0 ? cycleLowSoc : cell.StateOfCharge;
            atZero = false;
        }

        public double CycleTop => cycleTop;
        public double CycleLow => cycleLow;

        void Fade()
        {
            double target = NominalWh * Health;
            if (cell.CapacityWh > target)
                cell.RemoveCapacity(cell.CapacityWh - target);
        }
    }
}
