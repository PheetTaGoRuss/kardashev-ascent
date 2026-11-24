using System;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    public sealed partial class PlanetSimulation
    {
        public DebugOptions Debug { get; } = new DebugOptions();

        /// <summary>The milestone level that unlock rules read: the real one, or the top level with the debug switch on.</summary>
        int UnlockLevel => Debug.UnlockAll ? DebugOptions.AllUnlockedLevel : MilestonesReached;

        /// <summary>Called at the start of every step: the switches that keep things topped up.</summary>
        void ApplyDebugOptions()
        {
            if (Debug.InfiniteMaterials)
                foreach (Material m in Resources.Materials.All)
                    if (Materials[m] < DebugOptions.MaterialFloorKg)
                        Materials.Set(m, DebugOptions.MaterialFloorKg);
            if (Debug.FullBattery && Grid.Battery.CapacityWh > 0.0)
                Grid.Battery.SetStored(Grid.Battery.CapacityWh);
        }

        /// <summary>Set the milestone count (tests about what milestones unlock).</summary>
        internal void SetMilestonesForTests(int milestones) => MilestonesReached = milestones;

        public void DebugAddMaterials(double kg)
        {
            if (kg <= 0.0) return;
            Debug.EverUsedTouch();
            foreach (Material m in Resources.Materials.All)
                Materials.Add(m, kg);
        }

        public bool DebugAddRobot(RobotKind kind)
        {
            Debug.EverUsedTouch();
            return Workforce.AddRobot(kind, BaseTileId) != null;
        }

        public int DebugSurveyAll()
        {
            Debug.EverUsedTouch();
            int n = 0;
            for (int t = 0; t < TileCount; t++)
                if (!GetTile(t).IsWater && !Deposits.IsSurveyed(t))
                {
                    Deposits.MarkSurveyed(t);
                    n++;
                }
            return n;
        }
        /// <summary>Empty the colony battery (a dev command, to see a brownout).</summary>
        public void EmptyBatteryForTests() { Grid.Battery.SetStored(0.0); }

        /// <summary>Make the colony want far more power than it makes (a dev command, to see a brownout).</summary>
        public void ForceBrownoutForTests() { Grid.Battery.SetStored(0.0); Grid.DemandW = Math.Max(Grid.DemandW, 50.0 * Math.Max(1.0, GenerationW)); DemandW = Grid.DemandW; }
    }
}
