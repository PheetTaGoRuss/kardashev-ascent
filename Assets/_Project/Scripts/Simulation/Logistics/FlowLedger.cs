using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Logistics
{
    /// <summary>Where a flow of mass was counted (mission operations 6, the Flowsheet view). Saved as integers: append only.</summary>
    public enum FlowStage
    {
        /// <summary>Taken out of the ground by mining.</summary>
        Mined,
        /// <summary>Unloaded at a depot by a haul.</summary>
        Hauled,
        /// <summary>Taken into a plant batch (Key: the process).</summary>
        PlantIn,
        /// <summary>The product of a batch (Key: the process).</summary>
        PlantProduct,
        PlantSlag,
        /// <summary>Gas a batch kept in its output buffer.</summary>
        PlantGasStored,
        /// <summary>Gas a batch released to the air.</summary>
        PlantVented,
    }

    /// <summary>One counted flow in one day.</summary>
    [Serializable]
    public sealed class FlowEntry
    {
        public int Day;
        public FlowStage Stage;
        public int Key;
        public Material Material;
        public double Kg;
    }

    /// <summary>
    /// The last week of mass flows: mined, hauled, taken into plants, made, kept or vented. A read model for the Flowsheet
    /// view, which draws it and computes nothing (mission operations 6). Pure bookkeeping.
    /// </summary>
    public sealed class FlowLedger
    {
        public const int DaysKept = 7;
        public const double HoursPerDay = 24.0;
        readonly List<FlowEntry> entries = new List<FlowEntry>();

        public IReadOnlyList<FlowEntry> Entries => entries;

        public static int DayOf(double hours) => (int)Math.Floor(hours / HoursPerDay);

        public void Add(double timeHours, FlowStage stage, int key, Material material, double kg)
        {
            if (kg <= 0.0) return;
            int day = DayOf(timeHours);
            foreach (FlowEntry e in entries)
                if (e.Day == day && e.Stage == stage && e.Key == key && e.Material == material)
                {
                    e.Kg += kg;
                    return;
                }
            entries.Add(new FlowEntry { Day = day, Stage = stage, Key = key, Material = material, Kg = kg });
            entries.RemoveAll(x => x.Day < day - (DaysKept - 1));
        }

        /// <summary>The flows of the last <paramref name="days"/> days up to now, merged by stage, key and material.</summary>
        public List<FlowEntry> Last(double nowHours, int days)
        {
            int today = DayOf(nowHours);
            var merged = new Dictionary<(FlowStage, int, Material), FlowEntry>();
            foreach (FlowEntry e in entries)
            {
                if (e.Day <= today - days) continue;
                var k = (e.Stage, e.Key, e.Material);
                if (merged.TryGetValue(k, out FlowEntry m)) m.Kg += e.Kg;
                else merged[k] = new FlowEntry { Day = today, Stage = e.Stage, Key = e.Key, Material = e.Material, Kg = e.Kg };
            }
            var list = new List<FlowEntry>(merged.Values);
            list.Sort((a, b) => a.Stage != b.Stage ? a.Stage.CompareTo(b.Stage) : a.Key != b.Key ? a.Key.CompareTo(b.Key) : ((int)a.Material).CompareTo((int)b.Material));
            return list;
        }

        public void Restore(IEnumerable<FlowEntry> saved)
        {
            entries.Clear();
            if (saved == null) return;
            foreach (FlowEntry e in saved)
                if (e != null && e.Kg > 0.0) entries.Add(e);
        }
    }
}
