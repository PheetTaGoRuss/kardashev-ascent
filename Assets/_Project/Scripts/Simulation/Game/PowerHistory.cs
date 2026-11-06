using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>One sample of the colony's power: averages over <see cref="PowerHistory.SampleHours"/>. Public fields so JsonUtility can save it.</summary>
    [Serializable]
    public struct PowerSample
    {
        /// <summary>Hours since landing at the end of the sample.</summary>
        public double TimeHours;
        public double GenerationW;
        public double DemandW;
        /// <summary>Power that reached the loads (below demand during a brownout).</summary>
        public double ServedW;
        /// <summary>Spare power turned into build parts.</summary>
        public double BuildPowerW;
        /// <summary>Battery charge at the end of the sample, 0–1.</summary>
        public double BatteryFraction;
    }

    /// <summary>
    /// The colony's recent power, for the overview graph: a fixed number of samples, each the average over
    /// <see cref="SampleHours"/> of game time (oldest dropped first), plus running totals since landing.
    /// </summary>
    public sealed class PowerHistory
    {
        readonly PowerSample[] samples;
        int start, count;

        // The sample being filled.
        double accHours, accGenerationWh, accDemandWh, accServedWh, accBuildWh;

        public PowerHistory(double sampleHours, int capacity)
        {
            if (sampleHours <= 0.0) throw new ArgumentOutOfRangeException(nameof(sampleHours));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            SampleHours = sampleHours;
            samples = new PowerSample[capacity];
        }

        public double SampleHours { get; }
        public int Capacity => samples.Length;
        public int Count => count;

        /// <summary>Sample <paramref name="i"/>, oldest first.</summary>
        public PowerSample this[int i]
        {
            get
            {
                if (i < 0 || i >= count) throw new ArgumentOutOfRangeException(nameof(i));
                return samples[(start + i) % samples.Length];
            }
        }

        public double TotalGeneratedWh { get; private set; }
        public double TotalDemandWh { get; private set; }
        public double TotalServedWh { get; private set; }
        public double TotalBuildWh { get; private set; }
        /// <summary>Game hours spent in a brownout since landing.</summary>
        public double BrownoutHours { get; private set; }
        /// <summary>Game hours recorded since landing.</summary>
        public double TotalHours { get; private set; }

        /// <summary>Share of the colony's demand that was met since landing, 0–1 (1 before anything is recorded).</summary>
        public double ServedShare => TotalDemandWh > 0.0 ? Math.Min(1.0, TotalServedWh / TotalDemandWh) : 1.0;

        /// <summary>
        /// Record <paramref name="hours"/> ending at <paramref name="endTimeHours"/> with these average powers (W).
        /// A long span is split across as many samples as it covers.
        /// </summary>
        public void Add(double hours, double endTimeHours, double generationW, double demandW, double servedW,
                        double buildPowerW, double batteryFraction, bool brownout)
        {
            if (hours <= 0.0)
                return;
            TotalGeneratedWh += generationW * hours;
            TotalDemandWh += demandW * hours;
            TotalServedWh += servedW * hours;
            TotalBuildWh += buildPowerW * hours;
            TotalHours += hours;
            if (brownout)
                BrownoutHours += hours;

            double left = hours;
            while (left > 0.0)
            {
                double take = Math.Min(left, SampleHours - accHours);
                accHours += take;
                accGenerationWh += generationW * take;
                accDemandWh += demandW * take;
                accServedWh += servedW * take;
                accBuildWh += buildPowerW * take;
                left -= take;
                if (accHours >= SampleHours - 1e-9)
                    Push(endTimeHours - left, batteryFraction);
            }
        }

        void Push(double timeHours, double batteryFraction)
        {
            var s = new PowerSample
            {
                TimeHours = timeHours,
                GenerationW = accGenerationWh / accHours,
                DemandW = accDemandWh / accHours,
                ServedW = accServedWh / accHours,
                BuildPowerW = accBuildWh / accHours,
                BatteryFraction = batteryFraction,
            };
            accHours = accGenerationWh = accDemandWh = accServedWh = accBuildWh = 0.0;
            Append(s);
        }

        void Append(PowerSample s)
        {
            if (count < samples.Length)
            {
                samples[(start + count) % samples.Length] = s;
                count++;
            }
            else
            {
                samples[start] = s;
                start = (start + 1) % samples.Length;
            }
        }

        /// <summary>Every sample, oldest first (for saving).</summary>
        public List<PowerSample> ToList()
        {
            var list = new List<PowerSample>(count);
            for (int i = 0; i < count; i++)
                list.Add(this[i]);
            return list;
        }

        /// <summary>Replace everything with saved samples and totals (the sample being filled starts empty).</summary>
        public void Restore(IList<PowerSample> saved, double generatedWh, double demandWh, double servedWh, double buildWh,
                            double brownoutHours, double totalHours)
        {
            start = count = 0;
            accHours = accGenerationWh = accDemandWh = accServedWh = accBuildWh = 0.0;
            if (saved != null)
                foreach (PowerSample s in saved)
                    Append(s);
            TotalGeneratedWh = generatedWh;
            TotalDemandWh = demandWh;
            TotalServedWh = servedWh;
            TotalBuildWh = buildWh;
            BrownoutHours = brownoutHours;
            TotalHours = totalHours;
        }
    }
}
