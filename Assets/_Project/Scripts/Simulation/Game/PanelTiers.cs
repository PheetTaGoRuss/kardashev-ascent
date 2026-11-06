using System;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// What one building slot's solar installation grows into as the colony reaches milestones (game rule).
    /// A slot starts as a 1 ha panel field; later milestones unlock farms, parks and arrays, each covering far more
    /// ground per slot (capped at the slot's share of its tile). Built panels keep their size; rebuilding makes them
    /// the current tier. The physics per m² is unchanged: bigger tiers only mean more m².
    /// </summary>
    public static class PanelTiers
    {
        public readonly struct Tier
        {
            /// <summary>Milestone that unlocks this tier.</summary>
            public readonly int Milestone;
            public readonly string Name;
            /// <summary>Area relative to the base panel field (1 ha by default).</summary>
            public readonly double Scale;

            public Tier(int milestone, string name, double scale)
            {
                Milestone = milestone;
                Name = name;
                Scale = scale;
            }
        }

        /// <summary>Panel field 1 ha → solar farm 1 km² (milestone 3) → park 100 km² (5) → array 1,000 km² (7).</summary>
        public static readonly Tier[] All =
        {
            new Tier(0, "Panel field", 1.0),
            new Tier(3, "Solar farm", 100.0),
            new Tier(5, "Solar park", 10_000.0),
            new Tier(7, "Solar array", 100_000.0),
        };

        /// <summary>Index of the biggest tier unlocked with this many milestones.</summary>
        public static int For(int milestones)
        {
            int tier = 0;
            for (int i = 1; i < All.Length; i++)
                if (milestones >= All[i].Milestone)
                    tier = i;
            return tier;
        }

        /// <summary>The tier a built panel belongs to, from its area relative to the base panel field.</summary>
        public static int OfScale(double scale)
        {
            int tier = 0;
            for (int i = 1; i < All.Length; i++)
                if (scale >= All[i].Scale * 0.5)
                    tier = i;
            return tier;
        }

        /// <summary>"1 ha", "1 km²", "100 km²" …</summary>
        public static string FormatArea(double m2) =>
            m2 >= 1e6 ? $"{m2 / 1e6:#,0.##} km²" : $"{m2 / 1e4:#,0.##} ha";

        /// <summary>The tier that this milestone unlocks, or −1.</summary>
        public static int UnlockedAt(int milestone) => Array.FindIndex(All, t => t.Milestone == milestone && t.Milestone > 0);
    }
}
