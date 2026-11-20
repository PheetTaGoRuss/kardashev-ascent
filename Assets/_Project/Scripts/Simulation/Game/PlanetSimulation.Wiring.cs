using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The wired grid (director 2026-11-20: "add wired system that you have to connect generator with the station/building").
    /// Chains of tiles with finished power lines carry power out from the lander tile. With <see cref="WiredGrid"/> on, a
    /// generator off the grid makes nothing for the colony, and stations and storage can only be built on the grid.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        readonly HashSet<(int Tile, int Slot)> powerLines = new HashSet<(int, int)>();
        readonly HashSet<int> poweredTiles = new HashSet<int>();
        double wiringStamp = double.NaN;

        /// <summary>Wired grid on or off (new missions turn it on; old saves and the simulation tests leave it off).</summary>
        public bool WiredGrid { get; set; }

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

        /// <summary>The powered tiles, worked out again once per step and whenever a line is placed, finished or removed.</summary>
        void EnsureWiring()
        {
            if (!double.IsNaN(wiringStamp) && wiringStamp == TimeHours)
                return;
            wiringStamp = TimeHours;
            poweredTiles.Clear();
            var lined = new HashSet<int>();
            foreach (var key in powerLines)
                if (!(Build.QueueCount > 0 && Build.IsQueued(key.Tile, key.Slot)))
                    lined.Add(key.Tile);
            var queue = new Queue<int>();
            poweredTiles.Add(BaseTileId);
            queue.Enqueue(BaseTileId);
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                foreach (int nb in Sphere.Neighbors[t])
                    if (lined.Contains(nb) && poweredTiles.Add(nb))
                        queue.Enqueue(nb);
            }
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
            Settle((tileId, slot));
            return true;
        }
    }
}
