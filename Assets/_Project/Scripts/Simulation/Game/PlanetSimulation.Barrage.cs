using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>Tidal barrage (director 2026-11-19): a dam across a bay, three slots wide, with coast effects.</summary>
    public sealed partial class PlanetSimulation
    {
        readonly HashSet<(int Tile, int Slot)> barrages = new HashSet<(int, int)>();

        /// <summary>A finished barrage dams this tile.</summary>
        public bool HasBarrage(int tileId)
        {
            foreach (var key in barrages)
                if (key.Tile == tileId && !(Build.QueueCount > 0 && Build.IsQueued(key.Tile, key.Slot)))
                    return true;
            return false;
        }

        /// <summary>A barrage needs a bay (land on at least two sides), a big enough tide, and only one per tile.</summary>
        public string BarrageBlocker(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount || GetTile(tileId).Terrain != TerrainType.ShallowOcean)
                return "A barrage goes across shallow sea.";
            foreach (var key in barrages)
                if (key.Tile == tileId)
                    return "This tile already has a barrage.";
            int land = 0, around = 0;
            foreach (int nb in Sphere.Neighbors[tileId])
            {
                around++;
                if (!GetTile(nb).IsWater) land++;
            }
            if (around == 0 || (double)land / around < Tides.BarrageMinLandShare)
                return "Not a bay: a barrage needs land on at least two sides.";
            if (TidalRangeBaseM(tileId) < Tides.BarrageMinRangeM)
                return $"The tide here is {TidalRangeBaseM(tileId):0.#} m; a barrage needs {Tides.BarrageMinRangeM:0} m or more.";
            return null;
        }

        /// <summary>Finished barrages only, one emptying per tide, ebb generation. Capped by the planet's tidal ceiling.</summary>
        public double BarragePowerW()
        {
            if (barrages.Count == 0)
                return 0.0;
            double period = TidePeriodHours;
            double fraction = period > 0.0 && !double.IsInfinity(period) ? TimeHours / period : 0.0;
            double total = 0.0, lost = 0.0;
            foreach (var key in barrages)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(key.Tile, key.Slot))
                    continue;
                if (!IsPowered(key.Tile)) continue;
                double energy = Tides.BarrageEnergyPerTideJ(Tides.BarrageBasinM2, TidalRangeBaseM(key.Tile), SurfaceGravityMs2);
                double raw = Tides.BarragePowerW(energy, period, fraction);
                double efficiency = GridEfficiency(key.Tile);
                total += raw * efficiency;
                lost += raw * (1.0 - efficiency);
            }
            barrageLossW = lost;
            return Math.Min(total, Tides.PlanetCeilingW(Moon));
        }

        public bool PlaceBarrage(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.TidalBarrage, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(BuildingType.TidalBarrage), BuildTimes.Hours(BuildingType.TidalBarrage));
            barrages.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.TidalBarrage, tileId, slot));
            GenerationW = ComputeGeneration() + RtgW;
            RefreshBarrageBlocks();
            Settle((tileId, slot));
            return true;
        }

        /// <summary>Boats cannot cross a tile with a finished barrage (the coast effect of the dam).</summary>
        void RefreshBarrageBlocks()
        {
            if (Routes == null)
                return;
            var dammed = new HashSet<int>();
            foreach (var key in barrages)
                if (!(Build.QueueCount > 0 && Build.IsQueued(key.Tile, key.Slot)))
                    dammed.Add(key.Tile);
            for (int t = 0; t < TileCount; t++)
                Routes.SetDammed(t, false);
            foreach (int t in dammed)
                Routes.SetDammed(t, true);
        }
    }
}
