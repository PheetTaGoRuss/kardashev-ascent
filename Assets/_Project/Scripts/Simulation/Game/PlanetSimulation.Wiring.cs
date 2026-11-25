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
        readonly WireBook wireBook = new WireBook();   // the wires and the wired buildings (architecture step M1)
        HashSet<(int Tile, int Slot)> powerLines => wireBook.PowerLines;
        HashSet<(int Tile, int Slot)> highVoltageLines => wireBook.HighVoltageLines;
        Dictionary<(int Tile, int Slot), int> linkTargets => wireBook.LinkTargets;
        readonly HashSet<int> poweredTiles = new HashSet<int>();
        readonly Dictionary<(int, int), double> edgeCapacity = new Dictionary<(int, int), double>();
        readonly Dictionary<int, int> lineHops = new Dictionary<int, int>();      // tiles of line from the lander tile
        readonly Dictionary<int, int> lineParent = new Dictionary<int, int>();    // the tile toward the lander tile
        double wiringStamp = double.NaN;
        /// <summary>Buildings the player has wired to their tile's grid (director 2026-11-20: nothing is wired automatically).</summary>
        HashSet<(int Tile, int Slot)> wiredBuildings => wireBook.WiredBuildings;

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
        public const double LineCapacityPlainW = PlainLineVolts * PlainLineRatingA;

        /// <summary>What one tile of high-voltage line can carry, W (about a 500 kV line). Game rule, TBD.</summary>
        public const double LineCapacityHighW = HighLineVolts * HighLineRatingA;

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

        /// <summary>Makes power (panels, turbines, wave lines, barrages): it must be wired to give any.</summary>
        public static bool IsWireOutput(BuildingType type) =>
            type == BuildingType.SolarPanel || type == BuildingType.FloatingSolar || type == BuildingType.TidalTurbine
            || type == BuildingType.WaveConverter || type == BuildingType.TidalBarrage || type == BuildingType.WindTurbine;

        /// <summary>Takes power (storage, stations, refinery modules): it must be wired to work as a robot facility.</summary>
        public static bool IsWireInput(BuildingType type) => NeedsPower(type);

        /// <summary>This building has been wired to the grid (true for everything when the wired grid is off).</summary>
        public bool IsBuildingWired(int tileId, int slot) => !WiredGrid || wiredBuildings.Contains((tileId, A(tileId, slot)));

        bool WiredOrOff((int Tile, int Slot) key) => !WiredGrid || wiredBuildings.Contains(key);

        /// <summary>Wire the building on this slot to the grid: a drop cable, instant (a power line dragged onto it). Game rule, TBD.</summary>
        public bool PlaceBuildingWire(int tileId, int slot)
        {
            WireProblem = null;
            if (!WiredGrid) { WireProblem = "This mission has no wired grid."; return false; }
            BuildingType type = GetBuilding(tileId, slot);
            if (!IsWireOutput(type) && !IsWireInput(type)) { WireProblem = "Nothing to wire here: drop the power line on a generator, battery or station."; return false; }
            var key = (tileId, A(tileId, slot));
            if (wiredBuildings.Contains(key)) { WireProblem = "Already wired."; return false; }
            string blocker = KnowledgeBlocker(BuildingType.PowerLine);
            if (blocker != null) { WireProblem = blocker; return false; }
            wiredBuildings.Add(key);
            wiringStamp = double.NaN;
            flowDirty = true;
            RefreshNetwork();   // a wired relay starts relaying
            return true;
        }

        /// <summary>Wire everything built so far (what skipping the setup does, and loading a save from before building wires).</summary>
        public void WireAllBuildings()
        {
            foreach (var b in WireableBuildings()) wiredBuildings.Add((b.Tile, b.Slot));
            wiringStamp = double.NaN;
            flowDirty = true;
            RefreshNetwork();
        }

        /// <summary>Every building that makes or takes power, with where it stands.</summary>
        public IEnumerable<(int Tile, int Slot, BuildingType Type)> WireableBuildings()
        {
            foreach (var kv in panels) yield return (kv.Key.Tile, kv.Key.Slot, floatingPanels.Contains(kv.Key) ? BuildingType.FloatingSolar : BuildingType.SolarPanel);
            foreach (var k in tidalTurbines) yield return (k.Tile, k.Slot, BuildingType.TidalTurbine);
            foreach (var k in waveConverters) yield return (k.Tile, k.Slot, BuildingType.WaveConverter);
            foreach (var k in windTurbines) yield return (k.Tile, k.Slot, BuildingType.WindTurbine);
            foreach (var k in barrages) yield return (k.Tile, k.Slot, BuildingType.TidalBarrage);
            foreach (var k in batteries) yield return (k.Tile, k.Slot, BuildingType.Battery);
            foreach (var k in workshops) yield return (k.Tile, k.Slot, BuildingType.Workshop);
            foreach (var k in stations) yield return (k.Tile, k.Slot, BuildingType.ChargingStation);
            foreach (var k in relays) yield return (k.Tile, k.Slot, BuildingType.Relay);
            foreach (var k in research.Stations) yield return (k.Tile, k.Slot, BuildingType.ResearchStation);
            foreach (var kv in refineryModules) yield return (kv.Key.Tile, kv.Key.Slot, kv.Value);
        }

        /// <summary>Buildings still waiting for a wire (none when the grid is not wired).</summary>
        public IEnumerable<(int Tile, int Slot, BuildingType Type)> UnwiredBuildings()
        {
            if (!WiredGrid) yield break;
            foreach (var b in WireableBuildings())
                if (!wiredBuildings.Contains((b.Tile, b.Slot)))
                    yield return b;
        }

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
            reachedTiles.Clear();
            acOnlyParent.Clear();
            tileConvEff.Clear();
            edgeKind.Clear();
            lineHops.Clear();
            lineParent.Clear();
            // The wires are links between neighbouring tiles (director 2026-11-20: "connect from tile to tile"); finished links only.
            edgeCapacity.Clear();
            highEdges.Clear();
            var adjacent = new Dictionary<int, List<int>>();
            void AddLink(int a, int b, double capacity)
            {
                if (!adjacent.TryGetValue(a, out List<int> la)) adjacent[a] = la = new List<int>();
                if (!adjacent.TryGetValue(b, out List<int> lb)) adjacent[b] = lb = new List<int>();
                if (!la.Contains(b)) la.Add(b);
                if (!lb.Contains(a)) lb.Add(a);
                var edge = (Math.Min(a, b), Math.Max(a, b));
                edgeCapacity[edge] = edgeCapacity.TryGetValue(edge, out double c) ? Math.Max(c, capacity) : capacity;
            }
            foreach (var key in powerLines) if (HasFinishedLine(key) && LineWorks(key) && linkTargets.TryGetValue(key, out int to)) AddLink(key.Tile, to, LineCapacityPlainW);
            foreach (var key in highVoltageLines) if (HasFinishedLine(key) && LineWorks(key) && linkTargets.TryGetValue(key, out int to)) { AddLink(key.Tile, to, LineCapacityHighW); highEdges.Add((Math.Min(key.Tile, to), Math.Max(key.Tile, to))); }
            if (AcActive)
            {
                // AC in the colony: the reach goes through the media (DC, AC 33 kV, AC 500 kV) and the converters between them.
                edgeCapacity.Clear();
                highEdges.Clear();
                ReachMedia();
                return;
            }
            foreach (var edge in edgeCapacity.Keys) edgeKind[edge] = highEdges.Contains(edge) ? LineKind.High : LineKind.Plain;
            var queue = new Queue<int>();
            poweredTiles.Add(BaseTileId);
            lineHops[BaseTileId] = 0;
            lineParent[BaseTileId] = -1;
            queue.Enqueue(BaseTileId);
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                if (!adjacent.TryGetValue(t, out List<int> next)) continue;
                foreach (int nb in next)
                    if (poweredTiles.Add(nb))
                    {
                        lineHops[nb] = lineHops[t] + 1;
                        lineParent[nb] = t;
                        queue.Enqueue(nb);
                    }
            }
            islandAdjacent = adjacent;
            islandOf.Clear();
        }

        // ---- island grids (decided 2026-11-25, director's delegate; replaces "0 off the grid", EN-18) ----
        // A connected network of tiles that does not reach the lander tile and has a wired source works as an island: its generation serves the chargers standing on it.
        // Nothing is wired automatically (EN-22). Cable to the lander tile is still how power is shared. An island has no battery (a battery belongs to the lander grid: game rule, TBD).
        Dictionary<int, List<int>> islandAdjacent = new Dictionary<int, List<int>>();
        readonly Dictionary<int, int> islandOf = new Dictionary<int, int>();   // tile -> island id (the lowest tile id of its network), filled on demand

        int IslandIdOf(int tile)
        {
            if (!WiredGrid) return -1;
            EnsureWiring();
            if (poweredTiles.Contains(tile)) return -1;
            if (islandOf.TryGetValue(tile, out int id)) return id;
            var seen = new HashSet<int> { tile };
            var queue = new Queue<int>(); queue.Enqueue(tile);
            int lowest = tile;
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                if (islandAdjacent != null && islandAdjacent.TryGetValue(t, out List<int> nbs))
                    foreach (int nb in nbs) if (seen.Add(nb)) { queue.Enqueue(nb); lowest = Math.Min(lowest, nb); }
            }
            foreach (int t in seen) islandOf[t] = lowest;
            return lowest;
        }

        /// <summary>Raw power of the sources on the tiles of this island, W (0 for a tile on the lander grid or with no source).</summary>
        public double IslandGenerationW(int tile)
        {
            int id = IslandIdOf(tile);
            if (id < 0) return 0.0;
            EnsureFlow();
            double sum = 0.0;
            foreach (var map in new[] { rawPanels, rawTidal, rawWave, rawBarrage, rawWind })
                foreach (var kv in map)
                    if (!poweredTiles.Contains(kv.Key) && IslandIdOf(kv.Key) == id) sum += kv.Value;
            return sum;
        }

        /// <summary>True when a wired source stands on this tile's island (even at night: the panels are there), so stations may be built and used here.</summary>
        public bool IslandHasSource(int tile, bool includeSites = false)
        {
            int id = IslandIdOf(tile);
            if (id < 0) return false;
            foreach (var b in WireableBuildings())
                if (IsWireOutput(b.Type) && wiredBuildings.Contains((b.Tile, b.Slot)) && IslandIdOf(b.Tile) == id && (includeSites || !(Build.QueueCount > 0 && Build.IsQueued(b.Tile, b.Slot)))) return true;
            return false;
        }

        /// <summary>What the wire from this tile toward the lander tile can carry now, W: the high-voltage link if one is finished there, else the plain link; the lander tile has no limit.</summary>
        public double LineCapacityW(int tileId)
        {
            if (tileId == BaseTileId)
                return double.PositiveInfinity;
            EnsureWiring();
            if (!ParentOfTile(tileId, out int parent) || parent < 0)
                return LineCapacityPlainW;
            return edgeCapacity.TryGetValue((Math.Min(tileId, parent), Math.Max(tileId, parent)), out double c) ? c : LineCapacityPlainW;
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
            rawPanels.Clear(); rawTidal.Clear(); rawWave.Clear(); rawBarrage.Clear(); rawWind.Clear();
            RawPanelsByTile(rawPanels);
            RawTidalByTile(rawTidal);
            RawWaveByTile(rawWave);
            RawWindByTile(rawWind);
            RawBarrageByTile(rawBarrage);
            tileFactor.Clear();
            tileFlow.Clear();
            overloadedTiles.Clear();

            var own = new Dictionary<int, double>();
            double rawOnGrid = 0.0;
            foreach (var map in new[] { rawPanels, rawTidal, rawWave, rawBarrage, rawWind })
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

            // The lander network solved as an electrical network (stage 3, see PlanetSimulation.PhysicalFlow.cs).
            var solved = SolvePhysicalFlow(own);
            double delivered = solved.Delivered;
            flowDelivered = delivered;
            flowLoss = solved.Loss;
            flowOverload = solved.Curtailed;
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
            if (tileFactor.TryGetValue(tileId, out double f)) return f;
            if (!poweredTiles.Contains(tileId)) return 0.0;
            return tileFactor[tileId] = ProbeEfficiency(tileId);
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

        /// <summary>Tiles that a wire touches (finished or a site).</summary>
        public IEnumerable<int> LineTiles()
        {
            var seen = new HashSet<int>();
            foreach (var key in powerLines)
            {
                if (seen.Add(key.Tile)) yield return key.Tile;
                if (linkTargets.TryGetValue(key, out int to) && seen.Add(to)) yield return to;
            }
            foreach (var key in highVoltageLines)
            {
                if (seen.Add(key.Tile)) yield return key.Tile;
                if (linkTargets.TryGetValue(key, out int to) && seen.Add(to)) yield return to;
            }
            foreach (var key in wireBook.AcLines)
            {
                if (seen.Add(key.Tile)) yield return key.Tile;
                if (linkTargets.TryGetValue(key, out int to) && seen.Add(to)) yield return to;
            }
        }

        /// <summary>Every wire, tile to tile: the two tiles, whether it is finished, and whether it is high voltage.</summary>
        public IEnumerable<(int From, int To, bool Built, bool HighVoltage)> WireLinks()
        {
            foreach (var key in powerLines)
                if (linkTargets.TryGetValue(key, out int to)) yield return (key.Tile, to, HasFinishedLine(key), false);
            foreach (var key in highVoltageLines)
                if (linkTargets.TryGetValue(key, out int to)) yield return (key.Tile, to, HasFinishedLine(key), true);
        }

        public bool HasHighVoltageLine(int tileId)
        {
            return wireBook.TouchesTile(tileId, true, HasFinishedLine);
        }

        /// <summary>A wire touches this tile (finished only, or a site too).</summary>
        public bool HasLineOn(int tileId, bool finishedOnly)
        {
            Func<(int Tile, int Slot), bool> counts = key => !finishedOnly || HasFinishedLine(key);
            return wireBook.TouchesTile(tileId, false, counts) || wireBook.TouchesTile(tileId, true, counts);
        }

        /// <summary>The two tiles are linked (a wire between them, finished or a site). One per tier.</summary>
        bool Linked(int a, int b, bool highVoltage)
        {
            return wireBook.Linked(a, b, highVoltage);
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
                foreach (var key in windTurbines) if (!IsPowered(key.Tile)) n++;
                foreach (var key in barrages) if (!IsPowered(key.Tile)) n++;
                return n;
            }
        }

        /// <summary>A wire from this tile to a neighbouring one, built on a slot of this tile (director 2026-11-20: tile to tile).</summary>
        public bool PlacePowerLine(int tileId, int slot, int targetTile)
        {
            if (!CanLink(tileId, targetTile, false) || !CanPlace(BuildingType.PowerLine, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(BuildingType.PowerLine), BuildTimes.Hours(BuildingType.PowerLine));
            powerLines.Add((tileId, slot));
            linkTargets[(tileId, slot)] = targetTile;
            Occupy(tileId, FootprintFor(BuildingType.PowerLine, tileId, slot));
            wiringStamp = double.NaN;
            flowDirty = true;
            Settle((tileId, slot));
            return true;
        }

        public bool PlaceHighVoltageLine(int tileId, int slot, int targetTile)
        {
            if (!CanLink(tileId, targetTile, true) || !CanPlace(BuildingType.HighVoltageLine, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(BuildingType.HighVoltageLine), BuildTimes.Hours(BuildingType.HighVoltageLine));
            highVoltageLines.Add((tileId, slot));
            linkTargets[(tileId, slot)] = targetTile;
            Occupy(tileId, FootprintFor(BuildingType.HighVoltageLine, tileId, slot));
            wiringStamp = double.NaN;
            flowDirty = true;
            Settle((tileId, slot));
            return true;
        }

        /// <summary>Two neighbouring tiles a wire could join: both land or shallow sea, no wire of this tier between them yet.</summary>
        bool CanLink(int a, int b, bool highVoltage)
        {
            if (a < 0 || a >= TileCount || b < 0 || b >= TileCount || a == b || Array.IndexOf(Sphere.Neighbors[a], b) < 0)
                return false;
            bool Wirable(int t) { TerrainType g = GetTile(t).Terrain; return Terrains.IsBuildableLand(g) || g == TerrainType.ShallowOcean; }
            return Wirable(a) && Wirable(b) && !Linked(a, b, highVoltage);
        }

        /// <summary>
        /// Run a wire from one tile to a neighbouring one, on whichever end is on the grid (so a robot can reach it), else on the first.
        /// The tool on the planet calls this tile after tile to draw a path. Returns false with the reason in <see cref="WireProblem"/>.
        /// </summary>
        public bool PlaceWire(int fromTile, int toTile, bool highVoltage)
        {
            WireProblem = null;
            if (!CanLink(fromTile, toTile, highVoltage))
            {
                WireProblem = LinkReason(fromTile, toTile, highVoltage);
                return false;
            }
            string blocker = KnowledgeBlocker(highVoltage ? BuildingType.HighVoltageLine : BuildingType.PowerLine);
            if (blocker != null) { WireProblem = blocker; return false; }
            int host = IsPowered(toTile) && !IsPowered(fromTile) ? toTile : fromTile;
            int other = host == fromTile ? toTile : fromTile;
            BuildingType type = highVoltage ? BuildingType.HighVoltageLine : BuildingType.PowerLine;
            // The wire runs over the tiles whatever stands on them; its post takes a free slot on one end or the other.
            for (int pass = 0; pass < 2; pass++)
            {
                int h = pass == 0 ? host : other, o = pass == 0 ? other : host;
                for (int s = 0; s < SlotCount(h); s++)
                    if (CanPlace(type, h, s))
                        return highVoltage ? PlaceHighVoltageLine(h, s, o) : PlacePowerLine(h, s, o);
            }
            WireProblem = "Neither tile has a free slot for the wire's post.";
            return false;
        }

        /// <summary>Why the last <see cref="PlaceWire"/> failed.</summary>
        public string WireProblem { get; private set; }

        string LinkReason(int a, int b, bool highVoltage)
        {
            if (a < 0 || b < 0 || a >= TileCount || b >= TileCount || a == b) return "Pick two tiles.";
            if (Array.IndexOf(Sphere.Neighbors[a], b) < 0) return "A wire runs between neighbouring tiles: pick the next tile along.";
            if (Linked(a, b, highVoltage)) return highVoltage ? "These tiles already have a high-voltage wire." : "These tiles are already wired.";
            return "A wire needs land or shallow sea at both ends.";
        }

        // ---- raw power per tile, by source (before the lines) ----

        void RawPanelsByTile(Dictionary<int, double> map)
        {
            double meanAnomaly = CurrentMeanAnomaly();
            foreach (var pair in panels)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(pair.Key.Tile, pair.Key.Slot))
                    continue;   // a site makes nothing until it is finished
                if (!WiredOrOff(pair.Key))
                    continue;   // not wired: it makes nothing
                Double3 sun = SunAt(pair.Key.Tile);
                if (sun.Up <= 0.0)
                    continue;
                // the day's weather: cloud, air temperature and dust on top of the climatology (Weather, PL-07)
                WeatherSample w = WeatherAt(pair.Key.Tile);
                PanelParams p = InSeason(MakePanel(pair.Key.Tile, pair.Value.Tilt, pair.Value.Azimuth,
                                                   w.CloudFactor, pair.Value.Area), pair.Key.Tile, meanAnomaly);
                double power = PanelPowerNow(p, sun, w.AirC, w.DustTau,
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
                if (!WiredOrOff(key)) continue;
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
                if (!WiredOrOff(key)) continue;
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
                if (!WiredOrOff(key)) continue;
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
