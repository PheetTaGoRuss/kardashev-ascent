using System;

namespace KardashevAscent.Simulation.Energy
{
    /// <summary>What happened on the grid during one step. Powers in W, averaged over the step.</summary>
    public readonly struct GridStep
    {
        public readonly double SolarW;
        public readonly double RtgW;
        public readonly double DemandW;
        /// <summary>Power that reached the loads (≤ demand).</summary>
        public readonly double ServedW;
        /// <summary>Power taken from the grid into the battery.</summary>
        public readonly double ChargeW;
        /// <summary>Power the battery delivered to the loads.</summary>
        public readonly double DischargeW;
        /// <summary>Surplus with nowhere to go (battery full or at its power limit).</summary>
        public readonly double CurtailedW;

        public GridStep(double solarW, double rtgW, double demandW, double servedW,
                        double chargeW, double dischargeW, double curtailedW)
        {
            SolarW = solarW;
            RtgW = rtgW;
            DemandW = demandW;
            ServedW = servedW;
            ChargeW = chargeW;
            DischargeW = dischargeW;
            CurtailedW = curtailedW;
        }

        public double GenerationW => SolarW + RtgW;
        public bool IsBrownout => ServedW < DemandW - Math.Max(1e-6, DemandW * 1e-9);
    }

    /// <summary>
    /// The colony's electric grid (#9): each step, generation (solar + RTG) − demand charges the battery or is
    /// drawn from it; when the battery is empty and generation falls short, the loads brown out.
    /// </summary>
    public sealed class PowerGrid
    {
        public PowerGrid(Battery battery, double demandW, int rtgUnits = 0, double rtgUnitStartW = Rtg.MmrtgPowerW)
        {
            Battery = battery ?? throw new ArgumentNullException(nameof(battery));
            DemandW = demandW;
            RtgUnits = rtgUnits;
            RtgUnitStartW = rtgUnitStartW;
        }

        /// <summary>
        /// What the lander brings: a 2 MWh Li-ion bank (full), 4 MMRTGs (≈ 440 W, enough for a few sensors, not the base)
        /// and a 100 kW base. On its own the battery lasts about 20 hours, so the first panels have to go up on day one.
        /// </summary>
        public static PowerGrid Lander() => new PowerGrid(new Battery(2_000_000.0), 100_000.0, rtgUnits: 4);

        public Battery Battery { get; }
        /// <summary>Power the colony asks for, W.</summary>
        public double DemandW { get; set; }
        public int RtgUnits { get; set; }
        public double RtgUnitStartW { get; }
        public GridStep Last { get; private set; }
        public bool IsBrownout { get; private set; }

        /// <summary>Brownout started (true) or ended (false).</summary>
        public event Action<bool> BrownoutChanged;

        public double RtgPowerW(double yearsSinceLanding) => RtgUnits * Rtg.Power(RtgUnitStartW, yearsSinceLanding);

        /// <summary>Run the grid for <paramref name="hours"/> with <paramref name="solarW"/> of solar power.</summary>
        public GridStep Step(double solarW, double hours, double yearsSinceLanding)
        {
            if (hours <= 0.0)
                return Last;
            solarW = Math.Max(0.0, solarW);
            double rtgW = RtgPowerW(yearsSinceLanding);
            double generation = solarW + rtgW;
            double demand = Math.Max(0.0, DemandW);

            double served, charge = 0.0, discharge = 0.0, curtailed = 0.0;
            if (generation >= demand)
            {
                served = demand;
                double surplus = generation - demand;
                charge = Battery.Charge(surplus, hours);
                curtailed = surplus - charge;
            }
            else
            {
                discharge = Battery.Discharge(demand - generation, hours);
                served = generation + discharge;
            }

            Last = new GridStep(solarW, rtgW, demand, served, charge, discharge, curtailed);
            if (Last.IsBrownout != IsBrownout)
            {
                IsBrownout = Last.IsBrownout;
                BrownoutChanged?.Invoke(IsBrownout);
            }
            return Last;
        }
    }
}
