using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>Tides and free-flow tidal power (director 2026-11-19): the moon, the tide cycle, coast funnelling and the turbines.</summary>
    public sealed partial class PlanetSimulation
    {
        readonly HashSet<(int Tile, int Slot)> tidalTurbines = new HashSet<(int, int)>();
        Moon? moon;
        double tidePeriodHours = double.NaN;

        /// <summary>The generated moon: the same world always gets the same one (it is an assumption, not NASA data).</summary>
        public Moon Moon => moon ??= Tides.Generate(TerrainSeed);

        /// <summary>Hours between high tides: half a lunar day. A tidally locked planet turns once a year.</summary>
        public double TidePeriodHours
        {
            get
            {
                if (double.IsNaN(tidePeriodHours))
                {
                    double mass = SurfaceGravityMs2 * planetRadiusM * planetRadiusM / GravitationalConstant;
                    double rotation = DayLengthHours > 0.0 ? DayLengthHours : YearLengthHours;
                    tidePeriodHours = Tides.TidePeriodHours(rotation, Tides.MoonPeriodHours(Moon, mass, planetRadiusM));
                }
                return tidePeriodHours;
            }
        }

        /// <summary>
        /// Open-ocean tide times how much this coast funnels it: a tile with more land around it is a bay or estuary and
        /// raises the tide up to about 8 times (real coasts: Fundy is 16 m on a 1 m ocean tide). Game rule, TBD.
        /// </summary>
        public double TidalRangeAtM(int tileId) => TidalRangeBaseM(tileId) * (HasBarrage(tileId) ? 1.0 - Tides.BarrageRangeDrop : 1.0);

        /// <summary>The tide at a tile before any barrage weakens it.</summary>
        public double TidalRangeBaseM(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount || !GetTile(tileId).IsWater)
                return 0.0;
            int land = 0, around = 0;
            foreach (int nb in Sphere.Neighbors[tileId])
            {
                around++;
                if (!GetTile(nb).IsWater) land++;
            }
            double landShare = around > 0 ? (double)land / around : 0.0;
            uint h = unchecked((uint)(tileId * 2654435761u) ^ (uint)(TerrainSeed * 40503));
            double jitter = 0.7 + 0.6 * ((h >> 8) & 0xFFFF) / 65535.0;
            double funnel = 1.0 + 7.0 * landShare * jitter;
            return Tides.CoastRangeM(Tides.EquilibriumRangeM(Moon, planetRadiusM), funnel) * (Debug.StrongTides ? 8.0 : 1.0);
        }

        public double PeakStreamAtMs(int tileId) => Tides.PeakStreamMs(TidalRangeAtM(tileId));

        public double StreamAtMs(int tileId) => Tides.StreamMs(PeakStreamAtMs(tileId), TimeHours, TidePeriodHours);

        /// <summary>Finished turbines only. Capped by what the whole planet's tides can give.</summary>
        public double TidalPowerW()
        {
            if (tidalTurbines.Count == 0)
                return 0.0;
            double total = 0.0;
            foreach (var key in tidalTurbines)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(key.Tile, key.Slot))
                    continue;
                total += Tides.TurbinePowerW(StreamAtMs(key.Tile));
            }
            return Math.Min(total, Tides.PlanetCeilingW(Moon));
        }

        public bool PlaceTidalTurbine(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.TidalTurbine, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(BuildingType.TidalTurbine), BuildTimes.Hours(BuildingType.TidalTurbine));
            tidalTurbines.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.TidalTurbine, tileId, slot));
            GenerationW = ComputeGeneration() + RtgW;
            Settle((tileId, slot));
            return true;
        }
    }
}
