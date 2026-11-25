using System;
using System.Collections.Generic;
using System.Numerics;
using KardashevAscent.Simulation.Civilization;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Resources;

namespace KardashevAscent.Simulation.Game
{
    public sealed partial class PlanetSimulation
    {
        public int SiteRings { get; set; } = TileSite.DefaultRings;

        public bool PlacePanel(int tileId, int slot, double tilt, double azimuth)
        {
            if (!CanPlacePanel(tileId, slot, tilt, azimuth))
                return false;
            double area = NewPanelAreaM2(tileId);
            // It waits as a site until a robot works on it; an idle robot takes it now (design D2).
            StartSite(tileId, slot, PanelCost(area), BuildTimes.Hours(BuildingType.SolarPanel, area));
            AddPanel(tileId, slot, tilt, azimuth, area);
            Occupy(tileId, FootprintFor(BuildingType.SolarPanel, tileId, slot));
            Settle((tileId, slot));
            return true;
        }

        /// <summary>A building is going away: a site gives back its materials, a finished one is salvaged.</summary>
        void Dismantle(int tileId, int slot, BuildingType type, bool wasFloating = false)
        {
            salvageOrders.Remove((tileId, slot));
            adjustOrders.RemoveAll(o => o.Tile == tileId && o.Slot == slot);
            if (Build.IsQueued(tileId, slot))
            {
                Build.Cancel(tileId, slot, StockAt(tileId));
                Workforce.Tasks.MarkCancelled(new WorkTask(TaskKind.Build, tileId, slot));
                standIns.Remove((tileId, slot));
                return;
            }
            // A panel gives back a share of what its own size cost, not the current tier's.
            MaterialBill made = (type == BuildingType.SolarPanel || type == BuildingType.FloatingSolar) && panels.TryGetValue((tileId, slot), out var p)
                ? PanelCost(p.Area).Times(wasFloating ? FloatingCostFactor : 1.0) : BuildBill(type);
            // It gives back what it was made of: aluminium if it was wired with aluminium.
            Build.Salvage(WithStandIns(made, tileId, slot), StockAt(tileId));
            standIns.Remove((tileId, slot));
            if (type == BuildingType.Battery)
            {
                if (islandBatteries.Remove((tileId, slot))) { }   // an island battery was never part of the colony battery
                else Grid.Battery.RemoveCapacity(BatteryUnitWh);
            }
        }

        /// <summary>Panels that float (anchor slots): the Solar and cost rules differ a little.</summary>
        readonly HashSet<(int Tile, int Slot)> floatingPanels = new HashSet<(int, int)>();

        public bool PlaceFloatingPanel(int tileId, int slot, double tilt, double azimuth)
        {
            if (!CanPlace(BuildingType.FloatingSolar, tileId, slot) || !PreviewFloatingPanel(tileId, tilt, azimuth).CanPlace)
                return false;
            double area = NewPanelAreaM2(tileId);
            StartSite(tileId, slot, PanelCost(area).Times(FloatingCostFactor), BuildTimes.Hours(BuildingType.FloatingSolar, area));
            AddPanel(tileId, slot, tilt, azimuth, area);
            floatingPanels.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.FloatingSolar, tileId, slot));
            Settle((tileId, slot));
            return true;
        }

        public bool RemovePanel(int tileId, int slot)
        {
            slot = A(tileId, slot);
            if (!panels.ContainsKey((tileId, slot)))
                return false;
            bool floating = floatingPanels.Remove((tileId, slot));
            Dismantle(tileId, slot, floating ? BuildingType.FloatingSolar : BuildingType.SolarPanel, floating);
            panels.Remove((tileId, slot));
            Vacate(tileId, slot);
            panelsPerTile[tileId] = PanelCount(tileId) - 1;
            GenerationW = ComputeGeneration() + RtgW;
            return true;
        }

        public bool HasPanel(int tileId, int slot) => panels.ContainsKey((tileId, A(tileId, slot)));

        public int SlotCount(int tileId)
        {
            if (!sites.TryGetValue(tileId, out TileSite site))
                sites[tileId] = site = TileSite.Create(Sphere, tileId, SiteRings);
            return site.SlotCount;
        }

        public int PanelCount(int tileId) => panelsPerTile.TryGetValue(tileId, out int n) ? n : 0;

        /// <summary>One battery yard: about five grid-scale Li-ion units (≈ 4 MWh each) on a slot, Wh.</summary>
        public double BatteryUnitWh { get; set; } = 20_000_000.0;

        /// <summary>What covers a slot: the building stored there, or the one whose footprint covers it.</summary>
        public BuildingType GetBuilding(int tileId, int slot) => GetBuildingAt(tileId, A(tileId, slot));

        BuildingType GetBuildingAt(int tileId, int slot) =>
            panels.ContainsKey((tileId, slot)) ? (floatingPanels.Contains((tileId, slot)) ? BuildingType.FloatingSolar : BuildingType.SolarPanel)
            : batteries.Contains((tileId, slot)) ? BuildingType.Battery
            : workshops.Contains((tileId, slot)) ? BuildingType.Workshop
            : stations.Contains((tileId, slot)) ? BuildingType.ChargingStation
            : relays.Contains((tileId, slot)) ? BuildingType.Relay
            : tidalTurbines.Contains((tileId, slot)) ? BuildingType.TidalTurbine
            : waveConverters.Contains((tileId, slot)) ? BuildingType.WaveConverter
            : windTurbines.Contains((tileId, slot)) ? BuildingType.WindTurbine
            : barrages.Contains((tileId, slot)) ? BuildingType.TidalBarrage
            : powerLines.Contains((tileId, slot)) ? BuildingType.PowerLine
            : highVoltageLines.Contains((tileId, slot)) ? BuildingType.HighVoltageLine
            : wireBook.AcLines.Contains((tileId, slot)) ? BuildingType.AcLine
            : converterBuildings.TryGetValue((tileId, slot), out BuildingType conv) ? conv
            : research.Stations.Contains((tileId, slot)) ? BuildingType.ResearchStation
            : refineryModules.TryGetValue((tileId, slot), out BuildingType module) ? module
            : storageBuildings.TryGetValue((tileId, slot), out BuildingType store) ? store
            : BuildingType.None;

        public bool PlaceBattery(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.Battery, tileId, slot))
                return false;
            bool finished = StartSite(tileId, slot, BuildBill(BuildingType.Battery), BuildTimes.Hours(BuildingType.Battery));
            AddBattery(tileId, slot);
            Occupy(tileId, FootprintFor(BuildingType.Battery, tileId, slot));
            if (finished)
                AddBatteryCapacity(tileId, slot);
            Settle((tileId, slot));
            return true;
        }

        void AddBattery(int tileId, int slot)
        {
            batteries.Add((tileId, slot));
            batteriesPerTile[tileId] = BatteryCount(tileId) + 1;
        }

        public int BatteryCount(int tileId) => batteriesPerTile.TryGetValue(tileId, out int n) ? n : 0;

        public bool RemoveBuilding(int tileId, int slot)
        {
            flowDirty = true;   // a removed generator or line changes the flow
            slot = A(tileId, slot);
            wiredBuildings.Remove((tileId, slot));
            if (RemovePanel(tileId, slot))
                return true;
            if (workshops.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.Workshop);
                workshops.Remove((tileId, slot));
                Vacate(tileId, slot);
                return true;
            }
            if (relays.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.Relay);
                relays.Remove((tileId, slot));
                Vacate(tileId, slot);
                RefreshNetwork();
                return true;
            }
            if (RemoveConverter(tileId, slot))
                return true;
            if (wireBook.AcLines.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.AcLine);
                wireBook.AcLines.Remove((tileId, slot));
                wireBook.AcClass.Remove((tileId, slot));
                linkTargets.Remove((tileId, slot));
                Vacate(tileId, slot);
                wiringStamp = double.NaN;
                flowDirty = true;
                return true;
            }
            if (highVoltageLines.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.HighVoltageLine);
                highVoltageLines.Remove((tileId, slot));
                linkTargets.Remove((tileId, slot));
                Vacate(tileId, slot);
                wiringStamp = double.NaN;
                flowDirty = true;
                return true;
            }
            if (powerLines.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.PowerLine);
                powerLines.Remove((tileId, slot));
                linkTargets.Remove((tileId, slot));
                Vacate(tileId, slot);
                wiringStamp = double.NaN;
                flowDirty = true;
                return true;
            }
            if (barrages.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.TidalBarrage);
                barrages.Remove((tileId, slot));
                Vacate(tileId, slot);
                RefreshBarrageBlocks();
                return true;
            }
            if (windTurbines.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.WindTurbine);
                windTurbines.Remove((tileId, slot));
                Vacate(tileId, slot);
                return true;
            }
            if (waveConverters.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.WaveConverter);
                waveConverters.Remove((tileId, slot));
                Vacate(tileId, slot);
                return true;
            }
            if (tidalTurbines.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.TidalTurbine);
                tidalTurbines.Remove((tileId, slot));
                Vacate(tileId, slot);
                return true;
            }
            if (research.Stations.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.ResearchStation);
                research.Stations.Remove((tileId, slot));
                Vacate(tileId, slot);
                return true;
            }
            if (RemoveStorage(tileId, slot))
                return true;
            if (RemoveRefinery(tileId, slot))
                return true;
            if (stations.Contains((tileId, slot)))
            {
                Dismantle(tileId, slot, BuildingType.ChargingStation);
                stations.Remove((tileId, slot));
                RemoveStationCircuit(tileId, slot);
                Vacate(tileId, slot);
                return true;
            }
            if (!batteries.Contains((tileId, slot)))
                return false;
            Dismantle(tileId, slot, BuildingType.Battery);
            batteries.Remove((tileId, slot));
            Vacate(tileId, slot);
            batteriesPerTile[tileId] = BatteryCount(tileId) - 1;
            return true;
        }
    }
}
