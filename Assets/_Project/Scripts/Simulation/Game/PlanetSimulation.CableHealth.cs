using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Electrical;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Cable heating, ageing and faults (electrical redesign stage 8, W-31 to W-33). Each link keeps a conductor temperature that
    /// follows the heat of its own current; above the continuous limit its insulation ages faster; at zero condition the line fails:
    /// it opens, the network splits there, and the generators behind it are off the grid until it is repaired.
    /// Under the approved overload rule (curtail, G1) the current never exceeds the rating, so heating alone lifts a cable above 90 C
    /// only on a hot world. Repair is instant and takes copper, iron and rock in proportion to the damage (stand-in for a robot task, TBD).
    /// The heat of the line loss is <see cref="TransmissionLossW"/>; it is a readout and is not added to the industrial waste heat,
    /// so nothing is counted twice.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        readonly Dictionary<(int, int), double> edgeTemperature = new Dictionary<(int, int), double>();
        /// <summary>Protection fitted to a link (by its two tiles, smaller first). A link without an entry has none.</summary>
        readonly Dictionary<(int, int), ProtectionKind> lineProtection = new Dictionary<(int, int), ProtectionKind>();

        /// <summary>What a protection device costs (game rule, TBD): a fuse is a cheap one-shot, a breaker a resettable switch.</summary>
        public static Resources.MaterialBill ProtectionPrice(ProtectionKind kind) =>
            kind == ProtectionKind.Fuse ? new MaterialBill().Add(Material.Copper, 1.0)
            : kind == ProtectionKind.Breaker ? new MaterialBill().Add(Material.Copper, 5.0).Add(Material.Iron, 2.0)
            : new MaterialBill();

        static (int, int) EdgeKey(int a, int b) => (Math.Min(a, b), Math.Max(a, b));
        double thermalStamp = double.NaN;

        /// <summary>A line works unless it has failed or been opened.</summary>
        bool LineWorks((int Tile, int Slot) key) =>
            !wireBook.CableState.TryGetValue(key, out var cs) || (cs.Condition > 0.0 && !cs.Open);

        /// <summary>Conductor temperature of the link between two tiles, C (the air temperature when it has not been worked out yet).</summary>
        public double LineTemperatureC(int tileA, int tileB) =>
            edgeTemperature.TryGetValue((Math.Min(tileA, tileB), Math.Max(tileA, tileB)), out double t) ? t : CableCelsius(tileA, tileB);

        /// <summary>Test hook: set the conductor temperature of a link.</summary>
        public void SetLineTemperatureForTests(int tileA, int tileB, double celsius) =>
            edgeTemperature[(Math.Min(tileA, tileB), Math.Max(tileA, tileB))] = celsius;

        /// <summary>Called by the flow with the solved currents (on the plain-line base) of every link, in the order of the edge list.</summary>
        void UpdateCableHealth(List<FlowEdge> edges, double[] baseCurrentA)
        {
            double now = TimeHours;
            double dt = double.IsNaN(thermalStamp) ? 0.0 : Math.Min(24.0, now - thermalStamp);
            thermalStamp = now;
            double mean = CurrentMeanAnomaly();
            bool changed = false;
            for (int e = 0; e < edges.Count; e++)
            {
                var fe = edges[e];
                var key = (Math.Min(fe.A, fe.B), Math.Max(fe.A, fe.B));
                var spec = KindSpec(fe.Kind);
                double air = 0.5 * (WeatherAt(fe.A).AirC + WeatherAt(fe.B).AirC);   // climatology plus the day's weather
                double actualA = Math.Abs(baseCurrentA[e]) * PlainLineVolts / fe.Volts / KindRatingFactor(fe.Kind);   // a three-phase line carries 1/sqrt(3) of the flow current in each conductor
                double t = edgeTemperature.TryGetValue(key, out double known) ? known : air;
                if (dt > 0.0)
                {
                    double density = CableEnvironment.RelativeAirDensity(0.5 * (WeatherAt(fe.A).PressurePa + WeatherAt(fe.B).PressurePa), air);
                    t = CableThermal.Step(t, air, CableThermal.HeatPerM(spec, actualA, t), spec, dt, density);
                    edgeTemperature[key] = t;
                    if (CableThermal.Overheated(t) && lineProtection.TryGetValue(key, out var guard) && guard != ProtectionKind.None)
                    {
                        // The fuse blows or the breaker trips before the insulation suffers: the link opens, the condition is kept.
                        changed |= OpenLinesOfEdge(fe.A, fe.B);
                        continue;
                    }
                    double loss = CableThermal.ConditionLoss(t, dt);
                    if (loss > 0.0) changed |= AgeLinesOfEdge(fe.A, fe.B, loss);
                }
                else if (!edgeTemperature.ContainsKey(key)) edgeTemperature[key] = t;
            }
            if (changed) { wiringStamp = double.NaN; flowDirty = true; legacyGraph = null; }
        }

        /// <summary>Takes condition off the lines that make the link; true when one of them has just failed.</summary>
        bool AgeLinesOfEdge(int a, int b, double loss)
        {
            bool failed = false;
            foreach (var key in LinesOfEdge(a, b))
            {
                double cond = wireBook.CableState.TryGetValue(key, out var cs) ? cs.Condition : 1.0;
                if (cond <= 0.0) continue;
                cond = Math.Max(0.0, cond - loss);
                bool open = wireBook.CableState.TryGetValue(key, out cs) && cs.Open;
                if (cond >= 1.0 && !open) wireBook.CableState.Remove(key); else wireBook.CableState[key] = (cond, open);
                if (cond <= 0.0) failed = true;
            }
            return failed;
        }

        bool OpenLinesOfEdge(int a, int b)
        {
            bool any = false;
            foreach (var key in LinesOfEdge(a, b).ToList())
            {
                double cond = wireBook.CableState.TryGetValue(key, out var cs) ? cs.Condition : 1.0;
                if (cs.Open && wireBook.CableState.ContainsKey(key)) continue;
                wireBook.CableState[key] = (cond, true);
                any = true;
            }
            return any;
        }

        IEnumerable<(int Tile, int Slot)> LinesOfEdge(int a, int b)
        {
            foreach (var key in wireBook.PowerLines.Concat(wireBook.HighVoltageLines).Concat(wireBook.AcLines))
                if (wireBook.LinkTargets.TryGetValue(key, out int to) && ((key.Tile == a && to == b) || (key.Tile == b && to == a)))
                    yield return key;
        }

        /// <summary>The protection fitted to the line from this tile toward the lander.</summary>
        public ProtectionKind LineProtectionOf(int tileId) =>
            ParentOfTile(tileId, out int p) && p >= 0 && lineProtection.TryGetValue(EdgeKey(tileId, p), out var k) ? k : ProtectionKind.None;

        /// <summary>The condition of a line, 0 (failed) to 1 (whole); 1 for a line with no wear.</summary>
        public double LineCondition(int tileId, int slot) => wireBook.CableState.TryGetValue((tileId, slot), out var cs) ? cs.Condition : 1.0;

        /// <summary>Fits a fuse or breaker to the line from this tile toward the lander now and takes its price. False when there is no line, it already has that, or the stock is short.</summary>
        public bool InstallLineProtection(int tileId, ProtectionKind kind)
        {
            if (WiredGrid) EnsureWiring();
            if (kind == ProtectionKind.None || !LineExists(tileId)) return false;
            ParentOfTile(tileId, out int parentTile);
            var key = EdgeKey(tileId, parentTile);
            if (lineProtection.TryGetValue(key, out var have) && have == kind) return false;
            var price = ProtectionPrice(kind);
            MaterialBillCheck(price, out string missing);
            if (missing != null) return false;
            foreach (Material m in Enum.GetValues(typeof(Material))) if (price[m] > 0.0) Materials.TryTake(m, price[m]);
            lineProtection[key] = kind;
            return true;
        }

        /// <summary>Lines that have failed or been opened, as (tile, slot) of the line's own slot, in order.</summary>
        public IEnumerable<(int Tile, int Slot, double Condition)> FailedLines() =>
            wireBook.CableState.Where(kv => kv.Value.Condition <= 0.0 || kv.Value.Open).OrderBy(kv => kv.Key.Tile).ThenBy(kv => kv.Key.Slot)
                .Select(kv => (kv.Key.Tile, kv.Key.Slot, kv.Value.Condition)).ToList();

        /// <summary>What a repair of this line takes now: the line's build bill times the share of condition lost (stand-in, TBD).</summary>
        public MaterialBill RepairBill(int tileId, int slot)
        {
            var key = (tileId, slot);
            if (!wireBook.CableState.TryGetValue(key, out var cs)) return new MaterialBill();
            // A line that only tripped (its condition is whole) costs what its protection costs to replace or reset.
            if (cs.Condition > 0.0 && cs.Open)
            {
                if (!wireBook.LinkTargets.TryGetValue(key, out int to)) return new MaterialBill();
                // a breaker is only reset (free); a blown fuse is replaced (its price)
                return lineProtection.TryGetValue(EdgeKey(tileId, to), out var kind) && kind == ProtectionKind.Fuse ? ProtectionPrice(kind) : new MaterialBill();
            }
            BuildingType type = wireBook.AcLines.Contains(key) ? BuildingType.AcLine : wireBook.HighVoltageLines.Contains(key) ? BuildingType.HighVoltageLine : BuildingType.PowerLine;
            return BuildBill(type).Times(1.0 - Math.Max(0.0, Math.Min(1.0, cs.Condition)));
        }

        /// <summary>Repairs a line: takes the repair bill from stock and restores full condition. Null when done, else why not.</summary>
        public string RepairLine(int tileId, int slot)
        {
            var key = (tileId, slot);
            if (!wireBook.CableState.TryGetValue(key, out var cs)) return "That line needs no repair.";
            MaterialBill bill = RepairBill(tileId, slot);
            string missing = "";
            foreach (Material m in Enum.GetValues(typeof(Material)))
                if (bill[m] > 0.0 && Materials[m] < bill[m] - 1e-9)
                    missing += (missing.Length > 0 ? ", " : "") + $"{bill[m] - Materials[m]:0.#} kg {Resources.Materials.Name(m).ToLowerInvariant()}";
            if (missing.Length > 0) return "Missing " + missing + ".";
            foreach (Material m in Enum.GetValues(typeof(Material)))
                if (bill[m] > 0.0) Materials.TryTake(m, bill[m]);
if (cs.Open && cs.Condition > 0.0)
                wireBook.CableState[key] = (cs.Condition, false);   // a reset closes the line; the wear it has stays
            else
                wireBook.CableState.Remove(key);
            edgeTemperature.Clear();   // a repaired or reset link starts cool
            wiringStamp = double.NaN; flowDirty = true; legacyGraph = null;
            return null;
        }
    }
}
