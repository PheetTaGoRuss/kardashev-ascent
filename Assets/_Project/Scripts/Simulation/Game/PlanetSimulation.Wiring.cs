using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using KardashevAscent.Simulation.Civilization;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The wired grid (director 2026-11-20: "like Oxygen Not Included, more realistic but simple"). Power lines on tiles link
    /// generators to the lander tile. Power flows along the lines (a tree out from the lander tile): every tile of line loses a
    /// share to resistance, and a line can only carry so much, so a thin line overloads and curtails what is beyond it.
    /// Two tiers: the power line (copper cable) and the high-voltage line (opens at milestone 1).
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        readonly HashSet<(int Tile, int Slot)> powerLines = new HashSet<(int, int)>();
        readonly HashSet<(int Tile, int Slot)> highVoltageLines = new HashSet<(int, int)>();
        readonly HashSet<int> poweredTiles = new HashSet<int>();
        readonly Dictionary<int, int> lineHops = new Dictionary<int, int>();      // tiles of line from the lander tile
        readonly Dictionary<int, int> lineParent = new Dictionary<int, int>();    // the tile toward the lander tile
        double wiringStamp = double.NaN;

        // the last flow: raw power per tile and source, what each tile's line carries, and how much of its power gets through
        readonly Dictionary<int, double> rawPanels = new Dictionary<int, double>();
        readonly Dictionary<int, double> rawTidal = new Dictionary<int, double>();
        readonly Dictionary<int, double> rawWave = new Dictionary<int, double>();
        readonly Dictionary<int, double> rawBarrage = new Dictionary<int, double>();
        readonly Dictionary<int, double> tileFactor = new Dictionary<int, double>();
        readonly Dictionary<int, double> tileFlow = new Dictionary<int, double>();
        readonly HashSet<int> overloadedTiles = new HashSet<int>();
        double flowStamp = double.NaN;
        bool flowDirty = true;
        double flowDelivered, flowLoss, flowOverload;

        /// <summary>Wired grid on or off (new missions turn it on; old saves and the simulation tests leave it off).</summary>
        public bool WiredGrid { get; set; }

        /// <summary>Share of a generator's power lost for every tile of line between it and the lander tile (resistive losses; real lines lose a few percent per thousand km and a tile is hundreds of km). Game rule, TBD (EN-10).</summary>
        public const double LineLossPerHop = 0.02;

        /// <summary>What one tile of power line (copper cable) can carry, W (about a 33 kV line). Game rule, TBD.</summary>
        public const double LineCapacityPlainW = 20e6;

        /// <summary>What one tile of high-voltage line can carry, W (about a 500 kV line). Game rule, TBD.</summary>
        public const double LineCapacityHighW = 1e9;

        /// <summary>The milestone at which the high-voltage line opens. TBD.</summary>
        public const int HighVoltageMilestone = 1;

        public int PoweredTileCount
        {
            get
            {
                EnsureWiring();
                return poweredTiles.Count;
            }
        }

        /// <summary>Stations and storage need a connection; generators make power only with one; lines connect.</summary>
        static bool NeedsPower(BuildingType type) =>
            type == BuildingType.Battery || type == BuildingType.Workshop || type == BuildingType.ChargingStation || type == BuildingType.Relay
            || type == BuildingType.ResearchStation || type == BuildingType.AluminaPlant || type == BuildingType.Electrolyser;

        public bool IsPowered(int tileId)
        {
            if (!WiredGrid)
                return true;
            EnsureWiring();
            return poweredTiles.Contains(tileId);
        }

        bool HasFinishedLine((int Tile, int Slot) key) => !(Build.QueueCount > 0 && Build.IsQueued(key.Tile, key.Slot));

        /// <summary>The powered tiles, worked out again once per step and whenever a line is placed, finished or removed.</summary>
        void EnsureWiring()
        {
            if (!double.IsNaN(wiringStamp) && wiringStamp == TimeHours)
                return;
            wiringStamp = TimeHours;
            flowDirty = true;
            poweredTiles.Clear();
            lineHops.Clear();
            lineParent.Clear();
            var lined = new HashSet<int>();
            foreach (var key in powerLines) if (HasFinishedLine(key)) lined.Add(key.Tile);
            foreach (var key in highVoltageLines) if (HasFinishedLine(key)) lined.Add(key.Tile);
            var queue = new Queue<int>();
            poweredTiles.Add(BaseTileId);
            lineHops[BaseTileId] = 0;
            lineParent[BaseTileId] = -1;
            queue.Enqueue(BaseTileId);
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                foreach (int nb in Sphere.Neighbors[t])
                    if (lined.Contains(nb) && poweredTiles.Add(nb))
                    {
                        lineHops[nb] = lineHops[t] + 1;
                        lineParent[nb] = t;
                        queue.Enqueue(nb);
                    }
            }
        }

        /// <summary>What a tile's line can carry now, W: the high-voltage line if one is finished there, else the plain line; the lander tile has no limit.</summary>
        public double LineCapacityW(int tileId)
        {
            if (tileId == BaseTileId)
                return double.PositiveInfinity;
            foreach (var key in highVoltageLines)
                if (key.Tile == tileId && HasFinishedLine(key))
                    return LineCapacityHighW;
            return LineCapacityPlainW;
        }

        // ---- the flow ----

        void EnsureFlow()
        {
            EnsureWiring();
            if (!flowDirty && flowStamp == TimeHours)
                return;
            ComputeFlow();
        }

        /// <summary>
        /// Every source's raw power per tile, then the flow to the lander tile. Bottom-up, each tile carries its own generation
        /// plus what its children deliver (after their line loss), clipped to the line's capacity; top-down, the share of each
        /// tile's power that gets through is the product along its path. Returns what reaches the lander tile.
        /// </summary>
        double ComputeFlow()
        {
            EnsureWiring();
            rawPanels.Clear(); rawTidal.Clear(); rawWave.Clear(); rawBarrage.Clear();
            RawPanelsByTile(rawPanels);
            RawTidalByTile(rawTidal);
            RawWaveByTile(rawWave);
            RawBarrageByTile(rawBarrage);
            tileFactor.Clear();
            tileFlow.Clear();
            overloadedTiles.Clear();

            var own = new Dictionary<int, double>();
            double rawOnGrid = 0.0;
            foreach (var map in new[] { rawPanels, rawTidal, rawWave, rawBarrage })
                foreach (var kv in map)
                {
                    if (!WiredGrid || poweredTiles.Contains(kv.Key))
                    {
                        own[kv.Key] = (own.TryGetValue(kv.Key, out double o) ? o : 0.0) + kv.Value;
                        rawOnGrid += kv.Value;
                    }
                }

            if (!WiredGrid)
            {
                flowDelivered = rawOnGrid;
                flowLoss = flowOverload = 0.0;
                flowStamp = TimeHours;
                flowDirty = false;
                return flowDelivered;
            }

            // bottom-up: deepest tiles first
            var order = new List<int>(poweredTiles);
            order.Sort((a, b) => lineHops[b].CompareTo(lineHops[a]));
            var carriedUncapped = new Dictionary<int, double>();
            var clip = new Dictionary<int, double>();
            var carried = new Dictionary<int, double>();
            double noClipDelivered = 0.0;
            foreach (int t in order)
            {
                double sum = own.TryGetValue(t, out double mine) ? mine : 0.0;
                foreach (int nb in Sphere.Neighbors[t])
                    if (lineParent.TryGetValue(nb, out int parent) && parent == t && carried.TryGetValue(nb, out double fromChild))
                        sum += fromChild * (1.0 - LineLossPerHop);
                carriedUncapped[t] = sum;
                double capacity = LineCapacityW(t);
                double ratio = sum > capacity ? capacity / sum : 1.0;
                clip[t] = ratio;
                carried[t] = sum * ratio;
                if (ratio < 1.0) overloadedTiles.Add(t);
                tileFlow[t] = carried[t];
            }
            // top-down: the share of each tile's own power that reaches the lander tile
            var byDepth = new List<int>(poweredTiles);
            byDepth.Sort((a, b) => lineHops[a].CompareTo(lineHops[b]));
            var through = new Dictionary<int, double>();
            foreach (int t in byDepth)
            {
                double f = clip[t];
                if (lineParent[t] >= 0)
                    f *= (1.0 - LineLossPerHop) * through[lineParent[t]];
                through[t] = t == BaseTileId ? 1.0 : f;
                // the lander tile has no line to clip: its own power is delivered whole
                tileFactor[t] = t == BaseTileId ? 1.0 : f;
                noClipDelivered += (own.TryGetValue(t, out double m) ? m : 0.0) * Math.Pow(1.0 - LineLossPerHop, lineHops[t]);
            }
            // `through` above carries the clip of every tile on the path, so the factor of a tile's own power is clip[t] times the path to the lander tile
            double delivered = 0.0;
            foreach (int t in byDepth)
                delivered += (own.TryGetValue(t, out double m) ? m : 0.0) * tileFactor[t];
            flowDelivered = delivered;
            flowLoss = Math.Max(0.0, rawOnGrid - noClipDelivered);
            flowOverload = Math.Max(0.0, noClipDelivered - delivered);
            flowStamp = TimeHours;
            flowDirty = false;
            return delivered;
        }

        /// <summary>Share of a generator's power on this tile that reaches the lander tile: 1 on the lander tile, less for each tile of line out and for any overloaded line on the way; 0 off the grid. Always 1 when the grid is not wired.</summary>
        public double GridEfficiency(int tileId)
        {
            if (!WiredGrid)
                return 1.0;
            EnsureFlow();
            return tileFactor.TryGetValue(tileId, out double f) ? f : 0.0;
        }

        double Delivered(Dictionary<int, double> raw)
        {
            EnsureFlow();
            double total = 0.0;
            foreach (var kv in raw)
                total += kv.Value * (WiredGrid ? (tileFactor.TryGetValue(kv.Key, out double f) ? f : 0.0) : 1.0);
            return total;
        }

        /// <summary>Power the lines lose to resistance between the generators and the lander tile now, W.</summary>
        public double TransmissionLossW { get { EnsureFlow(); return flowLoss; } }

        /// <summary>Power a generator makes but a line is too thin to carry, W.</summary>
        public double OverloadLossW { get { EnsureFlow(); return flowOverload; } }

        /// <summary>What this tile's line carries now, W (0 off the grid).</summary>
        public double LineFlowW(int tileId)
        {
            EnsureFlow();
            return tileFlow.TryGetValue(tileId, out double f) ? f : 0.0;
        }

        /// <summary>A line is carrying more than it can.</summary>
        public bool IsLineOverloaded(int tileId)
        {
            EnsureFlow();
            return overloadedTiles.Contains(tileId);
        }

        /// <summary>Tiles that have a power line (finished or not), for drawing the wires.</summary>
        public IEnumerable<int> LineTiles()
        {
            var seen = new HashSet<int>();
            foreach (var key in powerLines) if (seen.Add(key.Tile)) yield return key.Tile;
            foreach (var key in highVoltageLines) if (seen.Add(key.Tile)) yield return key.Tile;
        }

        public bool HasHighVoltageLine(int tileId)
        {
            foreach (var key in highVoltageLines)
                if (key.Tile == tileId && HasFinishedLine(key)) return true;
            return false;
        }

        public bool HasLineOn(int tileId, bool finishedOnly)
        {
            foreach (var key in powerLines) if (key.Tile == tileId && (!finishedOnly || HasFinishedLine(key))) return true;
            foreach (var key in highVoltageLines) if (key.Tile == tileId && (!finishedOnly || HasFinishedLine(key))) return true;
            return false;
        }

        public int GeneratorsOffGrid
        {
            get
            {
                if (!WiredGrid)
                    return 0;
                int n = 0;
                foreach (var key in panels.Keys) if (!IsPowered(key.Tile)) n++;
                foreach (var key in tidalTurbines) if (!IsPowered(key.Tile)) n++;
                foreach (var key in waveConverters) if (!IsPowered(key.Tile)) n++;
                foreach (var key in barrages) if (!IsPowered(key.Tile)) n++;
                return n;
            }
        }

        public bool PlacePowerLine(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.PowerLine, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(BuildingType.PowerLine), BuildTimes.Hours(BuildingType.PowerLine));
            powerLines.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.PowerLine, tileId, slot));
            wiringStamp = double.NaN;
            flowDirty = true;
            Settle((tileId, slot));
            return true;
        }

        public bool PlaceHighVoltageLine(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.HighVoltageLine, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(BuildingType.HighVoltageLine), BuildTimes.Hours(BuildingType.HighVoltageLine));
            highVoltageLines.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.HighVoltageLine, tileId, slot));
            wiringStamp = double.NaN;
            flowDirty = true;
            Settle((tileId, slot));
            return true;
        }

        // ---- raw power per tile, by source (before the lines) ----

        void RawPanelsByTile(Dictionary<int, double> map)
        {
            double meanAnomaly = CurrentMeanAnomaly();
            foreach (var pair in panels)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(pair.Key.Tile, pair.Key.Slot))
                    continue;   // a site makes nothing until it is finished
                Double3 sun = SunAt(pair.Key.Tile);
                if (sun.Up <= 0.0)
                    continue;
                PanelParams p = InSeason(MakePanel(pair.Key.Tile, pair.Value.Tilt, pair.Value.Azimuth,
                                                   CloudFactor(pair.Key.Tile, meanAnomaly), pair.Value.Area), pair.Key.Tile, meanAnomaly);
                double power = PanelPowerNow(p, sun, AirTemperatureAt(pair.Key.Tile, meanAnomaly), OpticalDepthAt(pair.Key.Tile, meanAnomaly),
                                             out _, out _, out _, out _);
                double raw = floatingPanels.Contains(pair.Key) ? power * (1.0 + FloatingCoolingGain) : power;
                // Designed for the cautious end of whatever the colony hasn't measured yet (director 2026-11-14).
                map[pair.Key.Tile] = (map.TryGetValue(pair.Key.Tile, out double v) ? v : 0.0) + raw * SolarDesignShare;
            }
        }

        void RawTidalByTile(Dictionary<int, double> map)
        {
            if (tidalTurbines.Count == 0) return;
            double total = 0.0;
            foreach (var key in tidalTurbines)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(key.Tile, key.Slot)) continue;
                double raw = Tides.TurbinePowerW(StreamAtMs(key.Tile));
                map[key.Tile] = (map.TryGetValue(key.Tile, out double v) ? v : 0.0) + raw;
                total += raw;
            }
            double ceiling = Tides.PlanetCeilingW(Moon);
            if (total > ceiling && total > 0.0)
            {
                double scale = ceiling / total;
                foreach (int t in new List<int>(map.Keys)) map[t] *= scale;
            }
        }

        void RawWaveByTile(Dictionary<int, double> map)
        {
            if (waveConverters.Count == 0) return;
            foreach (var key in waveConverters)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(key.Tile, key.Slot)) continue;
                double raw = Waves.LinePowerW(WaveHeightAtM(key.Tile), EnergyPeriodAt(key.Tile), SurfaceGravityMs2);
                map[key.Tile] = (map.TryGetValue(key.Tile, out double v) ? v : 0.0) + raw;
            }
        }

        void RawBarrageByTile(Dictionary<int, double> map)
        {
            if (barrages.Count == 0) return;
            double period = TidePeriodHours;
            double fraction = period > 0.0 && !double.IsInfinity(period) ? TimeHours / period : 0.0;
            double total = 0.0;
            foreach (var key in barrages)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(key.Tile, key.Slot)) continue;
                double energy = Tides.BarrageEnergyPerTideJ(Tides.BarrageBasinM2, TidalRangeBaseM(key.Tile), SurfaceGravityMs2);
                double raw = Tides.BarragePowerW(energy, period, fraction);
                map[key.Tile] = (map.TryGetValue(key.Tile, out double v) ? v : 0.0) + raw;
                total += raw;
            }
            double ceiling = Tides.PlanetCeilingW(Moon);
            if (total > ceiling && total > 0.0)
            {
                double scale = ceiling / total;
                foreach (int t in new List<int>(map.Keys)) map[t] *= scale;
            }
        }

        public double TidalPowerW() => Delivered(rawTidalAfterEnsure());
        public double WavePowerW() => Delivered(rawWaveAfterEnsure());
        public double BarragePowerW() => Delivered(rawBarrageAfterEnsure());

        Dictionary<int, double> rawTidalAfterEnsure() { EnsureFlow(); return rawTidal; }
        Dictionary<int, double> rawWaveAfterEnsure() { EnsureFlow(); return rawWave; }
        Dictionary<int, double> rawBarrageAfterEnsure() { EnsureFlow(); return rawBarrage; }
    }
}
