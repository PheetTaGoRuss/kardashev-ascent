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
        /// <summary>Power that reached the priority load (robots charging below 20 %; design D6), W.</summary>
        public readonly double PriorityServedW;
        /// <summary>Power the lander's RTGs gave robots charging at the lander, W.</summary>
        public readonly double DockServedW;
        /// <summary>Power that reached the other robot charging (above 20 %), W (version 67: served before the colony's growth).</summary>
        public readonly double TopUpServedW;
        /// <summary>Power that reached the colony's baseline (life support), W.</summary>
        public readonly double BaselineServedW;

        public GridStep(double solarW, double rtgW, double demandW, double servedW,
                        double chargeW, double dischargeW, double curtailedW, double priorityServedW = 0.0, double dockServedW = 0.0, double topUpServedW = 0.0, double baselineServedW = 0.0)
        {
            TopUpServedW = topUpServedW;
            BaselineServedW = baselineServedW;
            PriorityServedW = priorityServedW;
            DockServedW = dockServedW;
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
        /// <summary>The colony's baseline load (life support), W: served first of all but the lander dock (load order, decided 2026-11-25).</summary>
        public double BaselineW { get; set; }
        public int RtgUnits { get; set; }
        public double RtgUnitStartW { get; }
        public GridStep Last { get; private set; }
        public bool IsBrownout { get; private set; }

        /// <summary>Brownout started (true) or ended (false).</summary>
        public event Action<bool> BrownoutChanged;

        public double RtgPowerW(double yearsSinceLanding) => RtgUnits * Rtg.Power(RtgUnitStartW, yearsSinceLanding);

        /// <summary>
        /// Run the grid for <paramref name="hours"/> with <paramref name="solarW"/> of solar power.
        /// <paramref name="priorityLoadW"/> is served right after the colony's demand, from generation and then the
        /// battery, before the battery charges (robots charging below 20 %; design D6). It never causes a brownout.
        /// <paramref name="dockLoadW"/> (robots charging at the lander) is served before everything else, straight from
        /// the lander's RTGs and never more than they make, so a robot can always recover even in a brownout (D10).
        /// </summary>
        public GridStep Step(double solarW, double hours, double yearsSinceLanding, double priorityLoadW = 0.0, double dockLoadW = 0.0, double topUpLoadW = 0.0)
        {
            if (hours <= 0.0)
                return Last;
            solarW = Math.Max(0.0, solarW);
            double rtgW = RtgPowerW(yearsSinceLanding);
            double priority = Math.Max(0.0, priorityLoadW);
            double dock = Math.Min(rtgW, Math.Max(0.0, dockLoadW));
            double generation = solarW + rtgW - dock;
            double demand = Math.Max(0.0, DemandW);
            double topUp = Math.Max(0.0, topUpLoadW);
            double baseline = Math.Min(demand, Math.Max(0.0, BaselineW));
            double total = demand + priority + topUp;

            // Load order (rule, director's delegate 2026-11-25): the baseline, robots charging below 20 % and the colony's other priority loads,
            // the other robot charging, then the colony's growth above its baseline. A brownout sheds from the bottom up.
            double served, priorityServed, topUpServed, baselineServed, charge = 0.0, discharge = 0.0, curtailed = 0.0;
            if (generation >= total)
            {
                served = demand;
                priorityServed = priority;
                topUpServed = topUp;
                baselineServed = baseline;
                double surplus = generation - total;
                charge = Battery.Charge(surplus, hours);
                curtailed = surplus - charge;
            }
            else
            {
                discharge = Battery.Discharge(total - generation, hours);
                double available = generation + discharge;
                baselineServed = Math.Min(baseline, available);
                double left = available - baselineServed;
                priorityServed = Math.Min(priority, left);
                left -= priorityServed;
                topUpServed = Math.Min(topUp, left);
                left -= topUpServed;
                double growthServed = Math.Min(demand - baseline, left);
                served = baselineServed + growthServed;
            }

            Last = new GridStep(solarW, rtgW, demand, served, charge, discharge, curtailed, priorityServed, dock, topUpServed, baselineServed);
            if (Last.IsBrownout != IsBrownout)
            {
                IsBrownout = Last.IsBrownout;
                BrownoutChanged?.Invoke(IsBrownout);
            }
            return Last;
        }
    }
}
