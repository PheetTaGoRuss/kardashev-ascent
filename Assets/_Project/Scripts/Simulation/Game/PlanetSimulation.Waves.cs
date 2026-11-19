using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>Sea waves and wave-energy converters (director 2026-11-19): the sea state per tile and the converter lines.</summary>
    public sealed partial class PlanetSimulation
    {
        readonly HashSet<(int Tile, int Slot)> waveConverters = new HashSet<(int, int)>();

        /// <summary>Wind at the tile, m/s: by latitude, with a fixed jitter per tile, weaker where the air is thin. Game rule, TBD.</summary>
        double WindAt(int tileId)
        {
            double airRatio = PressurePaAt(tileId) / Experiments.StandardPressurePa;
            if (airRatio <= 0.0) return 0.0;
            uint h = unchecked((uint)(tileId * 2246822519u) ^ (uint)(TerrainSeed * 3266489917u));
            double jitter = 0.75 + 0.5 * ((h >> 8) & 0xFFFF) / 65535.0;
            return Waves.WindMs(Sphere.Latitude(tileId)) * jitter * Math.Min(1.0, Math.Pow(airRatio, 1.0 / 3.0));
        }

        public double MeanWaveHeightAtM(int tileId)
        {
            if (tileId < 0 || tileId >= TileCount || !GetTile(tileId).IsWater) return 0.0;
            return Waves.DevelopmentShare * Waves.SignificantHeightM(WindAt(tileId), SurfaceGravityMs2);
        }

        /// <summary>The mean height with a weather swing: calm and rough spells of about three days.</summary>
        public double WaveHeightAtM(int tileId)
        {
            double mean = MeanWaveHeightAtM(tileId);
            if (mean <= 0.0) return 0.0;
            double phase = (tileId % 97) * 0.37;
            double swing = 1.0 + 0.45 * Math.Sin(2.0 * Math.PI * TimeHours / 72.0 + phase) + 0.2 * Math.Sin(2.0 * Math.PI * TimeHours / 29.0 + phase * 1.7);
            return mean * Math.Max(0.1, swing);
        }

        double EnergyPeriodAt(int tileId) => Waves.EnergyPeriodShare * Waves.PeakPeriodS(WindAt(tileId), SurfaceGravityMs2);

        /// <summary>Finished converter lines only.</summary>
        public double WavePowerW()
        {
            if (waveConverters.Count == 0)
                return 0.0;
            double total = 0.0;
            foreach (var key in waveConverters)
            {
                if (Build.QueueCount > 0 && Build.IsQueued(key.Tile, key.Slot))
                    continue;
                total += Waves.LinePowerW(WaveHeightAtM(key.Tile), EnergyPeriodAt(key.Tile), SurfaceGravityMs2);
            }
            return total;
        }

        public bool PlaceWaveConverter(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.WaveConverter, tileId, slot))
                return false;
            StartSite(tileId, slot, BuildBill(BuildingType.WaveConverter), BuildTimes.Hours(BuildingType.WaveConverter));
            waveConverters.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.WaveConverter, tileId, slot));
            GenerationW = ComputeGeneration() + RtgW;
            Settle((tileId, slot));
            return true;
        }
    }
}
