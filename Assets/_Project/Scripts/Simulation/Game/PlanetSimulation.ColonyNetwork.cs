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
        public ColonyNetwork Network { get; private set; }

        /// <summary>The planet's rivers (drawn, more water deposits, slower to cross). Derived from the tiles: not saved.</summary>
        public RiverMap Rivers { get; private set; }

        public bool PlaceRelay(int tileId, int slot)
        {
            if (!CanPlace(BuildingType.Relay, tileId, slot))
                return false;
            Build.Start(tileId, slot, BuildCostWh(BuildingType.Relay), 0.0, false);
            relays.Add((tileId, slot));
            Occupy(tileId, FootprintFor(BuildingType.Relay, tileId, slot));
            Settle((tileId, slot));
            RefreshNetwork();
            return true;
        }

        /// <summary>Finished relays relay; sites don't. Power follows the grid: a brownout takes the whole network down.</summary>
        void RefreshNetwork()
        {
            if (Network == null)
                return;
            var live = new List<int>();
            foreach (var r in relays)
                if (!Build.IsQueued(r.Tile, r.Slot))
                    live.Add(r.Tile);
            Network.SetRelays(live);
            Network.SetPowered(!Grid.IsBrownout);
        }
    }
}
