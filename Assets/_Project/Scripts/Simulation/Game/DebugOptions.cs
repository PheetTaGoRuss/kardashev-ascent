namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Testing switches (the debug panel, F10): they bypass the game's resource rules so a new feature can be tried at
    /// once. Not saved and never on by default; <see cref="EverUsed"/> stays true for the rest of the session so the
    /// screen can say "DEBUG" and no one mistakes a test run for a played one.
    /// </summary>
    public sealed class DebugOptions
    {
        bool freeBuilding, infiniteMaterials, unlockAll, fullBattery, strongTides;

        /// <summary>Buildings cost no materials and no robot time: they stand as soon as they are placed.</summary>
        public bool FreeBuilding { get => freeBuilding; set { freeBuilding = value; Touch(value); } }

        /// <summary>The material stock is kept topped up (a large stock of everything).</summary>
        public bool InfiniteMaterials { get => infiniteMaterials; set { infiniteMaterials = value; Touch(value); } }

        /// <summary>Everything the milestones unlock is available: panel tiers, robots, modules, refining, stand-ins.</summary>
        public bool UnlockAll { get => unlockAll; set { unlockAll = value; Touch(value); } }

        /// <summary>The colony battery is kept full.</summary>
        public bool FullBattery { get => fullBattery; set { fullBattery = value; Touch(value); } }

        /// <summary>Every coast has eight times the tide (to try tidal turbines on a world whose moon is weak).</summary>
        public bool StrongTides { get => strongTides; set { strongTides = value; Touch(value); } }

        /// <summary>A switch was on at some time this session.</summary>
        public bool EverUsed { get; private set; }

        public bool AnyOn => freeBuilding || infiniteMaterials || unlockAll || fullBattery || strongTides;

        /// <summary>The milestone level the unlock rules see when <see cref="UnlockAll"/> is on.</summary>
        public const int AllUnlockedLevel = 10;

        /// <summary>The stock the materials are topped up to, kg.</summary>
        public const double MaterialFloorKg = 1_000_000_000.0;

        /// <summary>One-off debug actions (add materials, a robot, survey everything) count as use too.</summary>
        public void EverUsedTouch() => EverUsed = true;

        void Touch(bool on)
        {
            if (on) EverUsed = true;
        }
    }
}
