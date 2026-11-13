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
        /// <summary>Dust the model's panels carry: none (robots keep them clean). A dust model would set this per planet.</summary>
        const double ModelDustShare = 0.0;

        public HexSphere Sphere { get; }
        public int TileCount => Sphere.TileCount;
        public double DayLengthHours => planet.DayLengthHours;
        public double YearLengthHours => planet.YearLengthHours;
        public double BatteryStoredWh => Grid.Battery.StoredWh;
        public double BatteryCapacityWh => Grid.Battery.CapacityWh;
        public bool IsBrownout => Grid.IsBrownout;

        public event Action<bool> BrownoutChanged;
        public event Action<bool> DayNightChanged;
        public event Action TargetReached;
        public event Action<int, int> BuildingCompleted;
        public event Action<int, int> PanelAdjusted;

        double dayEnergyWh, dayHours;

        /// <summary>The averaging window for milestones: the planet's day, or 24 h on a tidally locked planet.</summary>
        double MilestoneDayHours => DayLengthHours > 0.0 ? DayLengthHours : 24.0;

        public event Action<int> MilestoneReached;
    }
}
