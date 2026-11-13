using System;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// A panel angle layout the player saved from a slot: the tilt and facing, where on the planet it was saved
    /// (latitude and longitude of the tile), the slot, and the player's own name and note. Public fields so
    /// Unity's JsonUtility can save it. Angles are in degrees because the player reads and edits them that way.
    /// </summary>
    [Serializable]
    public sealed class AnglePreset
    {
        public const int MaxNameLength = 40;
        public const int MaxNoteLength = 200;

        public int Id;
        /// <summary>The player's name for it (for example "Noon bank"); defaults to the slot.</summary>
        public string Name = "";
        public string Note = "";
        public int TileId;
        public int Slot;
        /// <summary>Where the tile is: degrees, + north.</summary>
        public double LatitudeDeg;
        /// <summary>Where the tile is: degrees, + east.</summary>
        public double LongitudeDeg;
        public double TiltDeg;
        /// <summary>Facing as the sliders show it: 0–359 degrees (the panel's azimuth).</summary>
        public double FacingDeg;
        /// <summary>Game hours since landing when it was saved.</summary>
        public double SavedHours;

        public static string CleanName(string name, string fallback)
        {
            string n = (name ?? "").Trim();
            if (n.Length == 0) n = fallback;
            return n.Length > MaxNameLength ? n.Substring(0, MaxNameLength) : n;
        }

        public static string CleanNote(string note)
        {
            string n = (note ?? "").Trim();
            return n.Length > MaxNoteLength ? n.Substring(0, MaxNoteLength) : n;
        }
    }
}
