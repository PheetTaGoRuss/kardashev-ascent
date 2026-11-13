using System.Collections.Generic;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Which slots of a tile each building covers. A building is stored at its anchor slot; the other slots it covers
    /// point back to it. Pure bookkeeping: it knows nothing about what the building is (see <see cref="BuildingFootprints"/>).
    /// </summary>
    public sealed class FootprintMap
    {
        /// <summary>Covered slot (not the anchor) to its building's anchor slot.</summary>
        readonly Dictionary<(int Tile, int Slot), int> coveredBy = new Dictionary<(int, int), int>();

        /// <summary>Anchor to every slot the building covers (anchor first).</summary>
        readonly Dictionary<(int Tile, int Slot), List<int>> footprints = new Dictionary<(int, int), List<int>>();

        /// <summary>Record a building: <paramref name="footprint"/>[0] is its anchor.</summary>
        public void Occupy(int tile, IReadOnlyList<int> footprint)
        {
            int anchor = footprint[0];
            footprints[(tile, anchor)] = new List<int>(footprint);
            for (int i = 1; i < footprint.Count; i++)
                coveredBy[(tile, footprint[i])] = anchor;
        }

        /// <summary>Forget the building anchored at <paramref name="anchor"/> (its slots are free again).</summary>
        public void Vacate(int tile, int anchor)
        {
            if (!footprints.TryGetValue((tile, anchor), out List<int> f))
                return;
            for (int i = 1; i < f.Count; i++)
                coveredBy.Remove((tile, f[i]));
            footprints.Remove((tile, anchor));
        }

        /// <summary>The anchor of the building covering a slot, or the slot itself when nothing here covers it.</summary>
        public int AnchorOrSelf(int tile, int slot) => coveredBy.TryGetValue((tile, slot), out int anchor) ? anchor : slot;

        /// <summary>Every slot of the building anchored at <paramref name="anchor"/> (anchor first).</summary>
        public bool TryGetFootprint(int tile, int anchor, out List<int> footprint) => footprints.TryGetValue((tile, anchor), out footprint);

        public int BuildingCount => footprints.Count;
    }
}
