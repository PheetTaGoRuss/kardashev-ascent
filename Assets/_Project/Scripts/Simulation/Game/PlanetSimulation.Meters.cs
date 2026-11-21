using System;
using System.Collections.Generic;
using System.Linq;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Electrical redesign stage 7 (W-29, W-30): measured, not omniscient. The simulation always knows what a line carries; the
    /// readout says whether the colony knows it. A line is measured when a meter sits on it, or when an inspection of it is recent;
    /// otherwise the readout shows only the design figures (rating, nominal voltage) and a range. Meters cost nothing and an
    /// inspection is instant for now (stand-ins, TBD until the robot task and a price exist). How much the interface hides without
    /// meters is gate G3: today it reports honestly and the windows decide what to show.
    /// </summary>
    public sealed partial class PlanetSimulation : IElectricalMeters
    {
        /// <summary>How long an inspection stays a valid reading, hours (game rule, TBD).</summary>
        public const double InspectionValidHours = 24.0;

        readonly HashSet<int> meteredLines = new HashSet<int>();
        readonly Dictionary<int, double> inspectedLines = new Dictionary<int, double>();

        bool LineExists(int tileId) => tileId != BaseTileId && poweredTiles.Contains(tileId) && lineParent.TryGetValue(tileId, out int p) && p >= 0;

        public bool InstallLineMeter(int tileId)
        {
            if (WiredGrid) EnsureWiring();
            if (!LineExists(tileId)) return false;
            return meteredLines.Add(tileId);
        }

        public bool RemoveLineMeter(int tileId) => meteredLines.Remove(tileId);

        public bool HasLineMeter(int tileId) => meteredLines.Contains(tileId);

        public bool InspectLine(int tileId)
        {
            if (WiredGrid) EnsureWiring();
            if (!LineExists(tileId)) return false;
            inspectedLines[tileId] = TimeHours;
            return true;
        }

        public LineReadout LineReadout(int tileId)
        {
            if (WiredGrid) EnsureWiring();
            if (!LineExists(tileId)) return default;
            double capacity = LineCapacityW(tileId);
            bool metered = meteredLines.Contains(tileId);
            bool inspected = inspectedLines.TryGetValue(tileId, out double at) && TimeHours - at <= InspectionValidHours;
            if (!metered && !inspected)
                return new LineReadout(true, ReadingSource.None, 0.0, double.NaN, false, 0.0, capacity);
            double flow = LineFlowW(tileId);
            return new LineReadout(true, metered ? ReadingSource.Meter : ReadingSource.Inspection, metered ? 0.0 : TimeHours - at,
                                   flow, IsLineOverloaded(tileId), flow, capacity);
        }

        void CaptureMeters(SimulationState state)
        {
            state.MeteredLines.AddRange(meteredLines.OrderBy(t => t));
            foreach (var kv in inspectedLines.OrderBy(k => k.Key)) state.InspectedLines.Add(new InspectedLineRecord { Tile = kv.Key, AtHours = kv.Value });
        }

        void RestoreMeters(SimulationState state)
        {
            meteredLines.Clear();
            inspectedLines.Clear();
            if (state.Version < 45) return;
            if (state.MeteredLines != null) foreach (int t in state.MeteredLines) meteredLines.Add(t);
            if (state.InspectedLines != null) foreach (var r in state.InspectedLines) inspectedLines[r.Tile] = r.AtHours;
        }
    }
}
