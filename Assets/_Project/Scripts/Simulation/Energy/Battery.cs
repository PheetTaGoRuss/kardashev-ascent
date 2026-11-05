using System;

namespace KardashevAscent.Simulation.Energy
{
    /// <summary>
    /// A battery bank. Round-trip efficiency is split evenly between charging and discharging
    /// (η_in = η_out = √RTE), so storing 1 Wh and taking it back out returns RTE Wh.
    /// Power in and out is limited by a C-rate (capacity per hour).
    /// Li-ion round trip ≈ 83–90 % (docs/physics/notes/energy_sources.md §13).
    /// </summary>
    public sealed class Battery
    {
        public const double LithiumIonRoundTrip = 0.90;

        public Battery(double capacityWh, double roundTripEfficiency = LithiumIonRoundTrip,
                       double cRatePerHour = 1.0, double startFraction = 1.0)
        {
            if (capacityWh < 0.0) throw new ArgumentOutOfRangeException(nameof(capacityWh));
            if (roundTripEfficiency <= 0.0 || roundTripEfficiency > 1.0) throw new ArgumentOutOfRangeException(nameof(roundTripEfficiency));
            if (cRatePerHour <= 0.0) throw new ArgumentOutOfRangeException(nameof(cRatePerHour));
            CapacityWh = capacityWh;
            RoundTripEfficiency = roundTripEfficiency;
            CRatePerHour = cRatePerHour;
            StoredWh = capacityWh * Math.Max(0.0, Math.Min(1.0, startFraction));
        }

        public double CapacityWh { get; private set; }
        public double StoredWh { get; private set; }

        /// <summary>Set the stored energy (loading a saved game), clamped to 0..capacity.</summary>
        public void SetStored(double wh) => StoredWh = Math.Max(0.0, Math.Min(CapacityWh, wh));
        public double RoundTripEfficiency { get; }
        public double CRatePerHour { get; }

        /// <summary>One-way efficiency, √RTE.</summary>
        public double OneWayEfficiency => Math.Sqrt(RoundTripEfficiency);
        /// <summary>Largest charge or discharge power, W.</summary>
        public double MaxPowerW => CapacityWh * CRatePerHour;
        public double StateOfCharge => CapacityWh > 0.0 ? StoredWh / CapacityWh : 0.0;

        /// <summary>Add capacity (a new battery built), keeping the energy already stored.</summary>
        public void AddCapacity(double wh)
        {
            if (wh < 0.0) throw new ArgumentOutOfRangeException(nameof(wh));
            CapacityWh += wh;
        }

        /// <summary>Take capacity away (a battery demolished); energy above the new capacity is lost.</summary>
        public void RemoveCapacity(double wh)
        {
            if (wh < 0.0) throw new ArgumentOutOfRangeException(nameof(wh));
            CapacityWh = Math.Max(0.0, CapacityWh - wh);
            StoredWh = Math.Min(StoredWh, CapacityWh);
        }

        /// <summary>Offer <paramref name="powerW"/> for <paramref name="hours"/>; returns the power actually taken from the grid, W.</summary>
        public double Charge(double powerW, double hours)
        {
            if (powerW <= 0.0 || hours <= 0.0)
                return 0.0;
            double room = CapacityWh - StoredWh;
            double taken = Math.Min(Math.Min(powerW, MaxPowerW), room / (OneWayEfficiency * hours));
            taken = Math.Max(0.0, taken);
            StoredWh = Math.Min(CapacityWh, StoredWh + taken * OneWayEfficiency * hours);
            return taken;
        }

        /// <summary>Ask for <paramref name="powerW"/> for <paramref name="hours"/>; returns the power actually delivered, W.</summary>
        public double Discharge(double powerW, double hours)
        {
            if (powerW <= 0.0 || hours <= 0.0)
                return 0.0;
            double available = StoredWh * OneWayEfficiency / hours;
            double delivered = Math.Max(0.0, Math.Min(Math.Min(powerW, MaxPowerW), available));
            StoredWh = Math.Max(0.0, StoredWh - delivered * hours / OneWayEfficiency);
            return delivered;
        }
    }
}
