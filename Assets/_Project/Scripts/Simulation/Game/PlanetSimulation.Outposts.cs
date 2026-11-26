using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Leapfrog outposts (decided 2026-11-25, director's delegate). A build trip to a site that is not on the lander grid, with a charging station and solar panels
    /// among its sites, may be one-way when the robot's battery covers the outbound leg and the build at the cold capacity, and the outpost's own panels then charge it
    /// to its return reserve before it is lost. Otherwise the KI-46 round-trip rule stands. The materials come through the build bill as for any site (no separate kit).
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        /// <summary>The result of the one-way check for a robot and an outpost tile.</summary>
        public readonly struct OutpostVerdict
        {
            public readonly bool IsOutpost, Allowed;
            public readonly int Sol;
            public readonly string Text;
            public OutpostVerdict(bool isOutpost, bool allowed, int sol, string text) { IsOutpost = isOutpost; Allowed = allowed; Sol = sol; Text = text; }
        }

        /// <summary>Longest wait for the outpost's panels to charge a robot, sols (game rule, TBD).</summary>
        public const int OutpostMaxSols = 10;

        /// <summary>The panel sites and buildings, and the station sites, on a tile that is off the lander grid.</summary>
        bool OutpostSites(int tile, out int panelCount, out double buildHours)
        {
            panelCount = 0; buildHours = 0.0;
            bool station = false;
            for (int s = 0; s < SlotCount(tile); s++)
            {
                BuildingType type = GetBuilding(tile, s);
                bool queued = Build.QueueCount > 0 && Build.IsQueued(tile, s);
                if (type == BuildingType.SolarPanel) { panelCount++; if (queued) buildHours += BuildTimes.Hours(type); }
                else if (type == BuildingType.ChargingStation) { station = true; if (queued) buildHours += BuildTimes.Hours(type); }
                else if (type == BuildingType.Battery && queued) buildHours += BuildTimes.Hours(type);
            }
            return station && panelCount > 0 && buildHours > 0.0;
        }

        /// <summary>Mass of the materials still to be fitted on a tile, kg (the kit that must be hauled to the outpost's depot).</summary>
        double KitKg(int tile)
        {
            double kg = 0.0;
            for (int s = 0; s < SlotCount(tile); s++) if (GetBuilding(tile, s) != BuildingType.None && Build.QueueCount > 0 && Build.IsQueued(tile, s)) kg += Build.RemainingAt(tile, s).TotalKg;
            return kg;
        }

        /// <summary>Is a one-way build trip to this tile allowed for this robot? Simulated hour by hour from the robot's arrival.</summary>
        readonly Dictionary<(int Robot, int Tile, int Sites), (double Stamp, double Wh, int Tile2, OutpostVerdict Verdict)> verdictCache = new Dictionary<(int, int, int), (double, double, int, OutpostVerdict)>();

        /// <summary>The verdict, kept for three hours of game time while the robot's charge has not moved by more than 100 Wh (the hourly simulation is not cheap).</summary>
        public OutpostVerdict OneWayVerdict(Robot r, int tile)
        {
            var key = (r.Id, tile, Build.QueueCount * 1000 + SlotsUsedOn(tile));
            if (verdictCache.TryGetValue(key, out var c) && TimeHours - c.Stamp < 3.0 && TimeHours >= c.Stamp && Math.Abs(c.Wh - r.Battery.UsableWh) < 100.0 && c.Tile2 == r.Tile) return c.Verdict;
            OutpostVerdict v = ComputeOneWayVerdict(r, tile);
            verdictCache[key] = (TimeHours, r.Battery.UsableWh, r.Tile, v);
            return v;
        }

        int SlotsUsedOn(int tile) { int n = 0; for (int sl = 0; sl < SlotCount(tile); sl++) if (GetBuilding(tile, sl) != BuildingType.None) n++; return n; }

        OutpostVerdict ComputeOneWayVerdict(Robot r, int tile, bool assumeFullBattery = false)
        {
            Movement mv = Workforce.Movement;
            if (!WiredGrid || IsPowered(tile) || mv == null) return new OutpostVerdict(false, false, 0, "");
            if (!OutpostSites(tile, out int panels, out double buildHours)) return new OutpostVerdict(false, false, 0, "");
            double speed = r.Attribute(RobotAttribute.Speed);
            double go = mv.RoutesFor(r).HoursBetween(r.Tile, tile) / speed;
            double back = mv.HoursToChargerFor(r)[tile] / speed;   // the way home from the outpost, if it must
            if (double.IsInfinity(go)) return new OutpostVerdict(true, false, 0, "refused: there is no route to tile " + tile);
            double airDest = LiveAirTemperatureAt(tile);
            bool cold = airDest < RobotThermal.OperatingMinC;
            double heaterW = RobotThermal.SteadyHeaterW(airDest, 1.0, r.Has(RobotModule.ThermalInsulation), RobotSpecs.BuildPowerW);
            // the cold capacity: the battery is held at the operating limit outdoors (Bressan et al. 2025 curve, RobotBattery.ColdCapacityFactor)
            double coldFactor = cold ? RobotBattery.ColdCapacityFactor(RobotThermal.OperatingMinC) : 1.0;
            double cap = r.Battery.CapacityWh * Math.Min(r.Battery.ColdFactor, coldFactor);
            double have = Math.Max(0.0, (assumeFullBattery ? r.Battery.CapacityWh * r.Battery.ColdFactor : r.Battery.UsableWh) - r.Battery.CapacityWh * Math.Max(0.0, r.Battery.ColdFactor - coldFactor));
            // the builder travels light: the kit is hauled to the outpost's depot beforehand (ordinary round-trip hauls, KI-46), so only its own mass is counted here
            double outbound = mv.EnergyWh(go) / r.Attribute(RobotAttribute.EnergyEfficiency) + mv.TripClimbWh(r, r.Tile, tile) + heaterW * go;
            // Panel-first bootstrap (decided 2026-11-25, director's delegate): the outpost is built in stages, panels first, then the station, then the rest. A finished panel feeds a robot parked on
            // its tile directly (its DC output, at most its present output): first the robot's heater, then the battery with the surplus (a field technique; the surplus warming the battery
            // enough to charge is a game rule, TBD). Once the station stands the robot is sheltered: no heater, all the output charges it. The stages are simulated hour by hour from arrival.
            var stages = new List<(double Hours, int Kind)>();   // Kind 0 panel, 1 station, 2 battery
            int panelsDone = 0, panelsToBuild = 0; bool stationDone = false;
            for (int sl = 0; sl < SlotCount(tile); sl++)
            {
                BuildingType bt = GetBuilding(tile, sl);
                bool queued = Build.QueueCount > 0 && Build.IsQueued(tile, sl);
                if (bt == BuildingType.SolarPanel) { if (queued) { stages.Add((BuildTimes.Hours(bt), 0)); panelsToBuild++; } else panelsDone++; }
                else if (bt == BuildingType.ChargingStation) { if (queued) stages.Add((BuildTimes.Hours(bt), 1)); else stationDone = true; }
                else if (bt == BuildingType.Battery && queued) stages.Add((BuildTimes.Hours(bt), 2));
            }
            stages.Sort((a, b) => a.Kind.CompareTo(b.Kind));
            double perPanelMeanW = PreviewPanel(tile, 0.35, Math.PI).EnergyPerYearWh / Solar.SolarConstants.EarthYearHours;
            double returnNeed = RobotSpecs.ReturnToChargeFraction * cap + (mv.EnergyWh(back) / r.Attribute(RobotAttribute.EnergyEfficiency) + heaterW * back) + 0.05 * cap;
            double day = DayLengthHours, e = have - outbound;
            if (e < 0.0)
                return new OutpostVerdict(true, false, 0, $"refused: its battery would run out {(-e):0} Wh short of reaching tile {tile}");
            // the body starts from the temperature the robot has after the outbound leg: the shelter temperature cooling over the trip hours (one lumped body)
            double body = RobotThermal.TripHeater(r.BodyTempC, airDest, go, 1.0, r.Has(RobotModule.ThermalInsulation), mv.DrivePowerW).EndC;
            int stageIndex = 0; double stageDone = 0.0;
            int hoursMax = OutpostMaxSols * (int)Math.Ceiling(day);
            for (int h = 0; h < hoursMax; h++)
            {
                double now = TimeHours + go + h;
                double air = DiurnalAirTemperatureAt(tile, now);
                bool sheltered = stationDone;
                bool workingNow = stageIndex < stages.Count;
                var lump = RobotThermal.Step(body, air, 1.0, 1.0, r.Has(RobotModule.ThermalInsulation), workingNow ? RobotSpecs.BuildPowerW : 0.0, e > 0.0, sheltered);
                double heater = lump.HeaterW;
                double phase = (now % day) / day;
                double panelW = panelsDone * perPanelMeanW * Math.PI * (phase < 0.5 ? Math.Sin(Math.PI * phase * 2.0) : 0.0);   // half-sine day; the planning estimate (marked as such in the UI)
                bool working = stageIndex < stages.Count;
                double load = heater + (working ? RobotSpecs.BuildPowerW : 0.0);
                double net = panelW - load;                      // the panel feeds the load first, the battery takes the rest or covers the shortfall
                if (e <= 0.0 && net < 0.0) { net = panelW - 0.0; working = false; }   // empty: it cannot build; hibernating
                double batteryDelta = net >= 0.0 ? 0.95 * net : net;
                if (e + batteryDelta <= 0.0 && panelW < load) { e = 0.0; working = false; }
                else e = Math.Min(cap, e + batteryDelta);
                body = lump.BodyC;
                if (body < RobotThermal.StorageMinC)
                    return new OutpostVerdict(true, false, 0, $"refused: it would freeze at {body:0} C before sunrise (stage {stageIndex + 1} of {stages.Count} unfinished)");
                if (working && e > 0.0)
                {
                    stageDone += 1.0;
                    if (stageDone >= stages[stageIndex].Hours - 1e-9)
                    {
                        if (stages[stageIndex].Kind == 0) panelsDone++;
                        else if (stages[stageIndex].Kind == 1) stationDone = true;
                        stageIndex++; stageDone = 0.0;
                    }
                }
                if (stageIndex >= stages.Count && e >= returnNeed)
                    return new OutpostVerdict(true, true, (int)(h / day) + 1, $"one-way: the outpost will charge it by sol {(int)(h / day) + 1} (built in stages, panel first)");
            }
            return new OutpostVerdict(true, false, 0, stageIndex < stages.Count
                ? $"refused: after {OutpostMaxSols} sols stage {stageIndex + 1} of {stages.Count} is still unfinished (the panels cannot keep it supplied)"
                : $"refused: the outpost's {panelsDone} panel(s) would not charge it to its return reserve within {OutpostMaxSols} sols");
        }

        /// <summary>The verdict for the first robot as if its battery were full: whether this tile can be an outpost at all (planning).</summary>
        public string OutpostTextAtFullCharge(int tile)
        {
            if (Workforce.Robots.Count == 0) return "";
            return ComputeOneWayVerdict(Workforce.Robots[0], tile, true).Text;
        }

        /// <summary>The text for the Why? chain of a build site that is an outpost candidate (empty for other tiles).</summary>
        public string OutpostText(int tile)
        {
            if (Workforce.Robots.Count == 0) return "";
            OutpostVerdict first = default;
            foreach (Robot r in Workforce.Robots)
            {
                var v = OneWayVerdict(r, tile);
                if (!v.IsOutpost) return "";
                if (v.Allowed) return v.Text;
                if (!first.IsOutpost) first = v;
            }
            return first.Text;
        }

        /// <summary>
        /// Order a leapfrog outpost on a tile (2026-11-25): two panels (wired), a charging station (wired) and the hauls of their materials to the tile's depot. The builder departs by itself
        /// once the kit is there and the one-way check passes. Returns null when ordered, else why not.
        /// </summary>
        public string OrderOutpost(int tile, int panels = 2)
        {
            if (!WiredGrid) return "This mission has no wired grid: build chargers as usual.";
            if (tile == BaseTileId || IsPowered(tile)) return "That tile is on the lander grid: build there as usual.";
            if (GetTile(tile).IsWater || !IsOpenGround(tile)) return "An outpost needs open land.";
            var slots = new List<int>();
            int placed = 0;
            for (int sl = 0; sl < SlotCount(tile) && placed < panels; sl++)
                if (PlacePanel(tile, sl, 0.35, Math.PI)) { placed++; slots.Add(sl); PlaceBuildingWire(tile, sl); }
            if (placed == 0) return "No room for panels on that tile.";
            bool station = false;
            for (int sl = 0; sl < SlotCount(tile) && !station; sl++)
                if (GetBuilding(tile, sl) == BuildingType.None && PlaceChargingStation(tile, sl)) { station = true; slots.Add(sl); PlaceBuildingWire(tile, sl); }
            if (!station) { foreach (int sl in slots) OrderSalvage(tile, sl); return "No room for a charging station on that tile."; }
            // the kit goes by ordinary hauls, which are round trips from a charger (KI-46): if one cannot be planned, the outpost is refused with the depot advice
            double kit = KitKg(tile);
            double load = FeasiblePayloadKg(BaseTileId, tile, Math.Min(kit, BasePayloadKg));
            if (load <= 0.0)
            {
                string haul = HaulCostText(BaseTileId, tile, MinHaulLoadKg);
                foreach (int sl in slots) OrderSalvage(tile, sl);
                var advice = DepotSites(1);
                string tip = advice.Count > 0 ? $" Depot advice: build a depot with a charging bay at tile {advice[0].Tile}." : "";
                return $"Out of reach: even a {MinHaulLoadKg:0} kg load cannot be hauled to tile {tile} and back ({haul}): the empty round trip does not fit.{tip}";
            }
            SetKitTargets(tile);
            return null;
        }

        /// <summary>Ask for the hauls that bring the outpost's remaining materials to its depot.</summary>
        public void SetKitTargets(int tile)
        {
            if (!LogisticsEnabled) return;
            MaterialBill left = new MaterialBill();
            for (int s = 0; s < SlotCount(tile); s++)
                if (GetBuilding(tile, s) != BuildingType.None && Build.QueueCount > 0 && Build.IsQueued(tile, s)) left = left.Plus(Build.RemainingAt(tile, s));
            foreach (Resources.Material m in Resources.Materials.All)
                if (left[m] > 1e-6) SetStockTarget(tile, m, left[m]);
        }

        /// <summary>The stages of an outpost for the Why? chain: kit delivered, then the builder; empty when the tile is not an outpost site.</summary>
        public string OutpostStages(int tile)
        {
            if (!WiredGrid || IsPowered(tile) || !OutpostSites(tile, out _, out _)) return "";
            MaterialBill left = new MaterialBill();
            for (int s = 0; s < SlotCount(tile); s++)
                if (GetBuilding(tile, s) != BuildingType.None && Build.QueueCount > 0 && Build.IsQueued(tile, s)) left = left.Plus(Build.RemainingAt(tile, s));
            int kinds = 0, delivered = 0;
            foreach (Resources.Material m in Resources.Materials.All)
                if (left[m] > 1e-6) { kinds++; if (KgAt(tile, m) >= left[m] - 1e-6) delivered++; }
            double kitKg = KitKg(tile);
            double load = FeasiblePayloadKg(BaseTileId, tile, Math.Min(kitKg, BasePayloadKg));
            string trips = load > 0.0 && kitKg > load ? $" (kit: {Math.Ceiling(kitKg / load):0} trips of {load:0} kg, battery reach)" : "";
            return delivered >= kinds ? "kit delivered: the builder departs when the one-way check passes" : $"kit {delivered} of {kinds} materials delivered: the builder departs when complete{trips}";
        }
    }
}
