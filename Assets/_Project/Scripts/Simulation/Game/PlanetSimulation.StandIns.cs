using System.Collections.Generic;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// Stand-ins in building bills (director 2026-11-15: a colony must be able to start on every planet). A building is
    /// wired with copper when the colony has the copper for it, else with aluminium refined from rock when it has that;
    /// lithium and sodium cells the same way. A site nothing has gone into yet switches to whichever the stock can
    /// supply, so a site placed before the metal is there waits for copper or aluminium, whichever comes first. Once
    /// materials go in, the choice is kept (and saved), so taking the building down gives back what it was made of.
    /// </summary>
    public sealed partial class PlanetSimulation
    {
        readonly Dictionary<(int Tile, int Slot), StandIn> standIns = new Dictionary<(int, int), StandIn>();

        static readonly (Material Ore, Material Sub, StandIn Flag, double Ratio)[] Swaps =
        {
            (Material.Copper, Material.Aluminium, StandIn.AluminiumForCopper, StandIns.AluminiumPerCopperKg),
            (Material.Lithium, Material.Sodium, StandIn.SodiumForLithium, StandIns.SodiumPerLithiumKg),
        };

        static bool Unlocked(Material sub, int milestones) => milestones >= RobotTech.UnlockMilestone(sub);

        /// <summary>
        /// The stand-ins for a (copper/lithium) bill, given what is free in stock and what was used so far: switch to the
        /// stand-in only when the stock has it and not the ore metal; switch back only when it has the ore metal and not
        /// the stand-in. Otherwise keep what it was.
        /// </summary>
        StandIn ChooseStandIns(MaterialBill bill, MaterialBill reserved, StandIn was = StandIn.None)
        {
            StandIn use = was;
            foreach (var (ore, sub, flag, ratio) in Swaps)
            {
                double kg = bill[ore];
                if (kg <= 0.0) { use &= ~flag; continue; }
                bool oreCovers = Materials[ore] - reserved[ore] >= kg - 1e-9;
                bool subCovers = Unlocked(sub, UnlockLevel) && Materials[sub] - reserved[sub] >= kg * ratio - 1e-9;
                if (!oreCovers && subCovers) use |= flag;
                else if (oreCovers && !subCovers) use &= ~flag;
            }
            return use;
        }

        /// <summary>
        /// Sites nothing has gone into yet take whichever metal the stock can supply (in queue order, each after the
        /// ones before it). Called before work is handed out.
        /// </summary>
        void ReconsiderStandIns()
        {
            if (Build.QueueCount == 0) return;
            var reserved = new MaterialBill();
            var changes = new List<(int Tile, int Slot, MaterialBill Bill, StandIn Use)>();
            foreach (var site in Build.Sites())
            {
                MaterialBill bill = site.Bill;
                if (site.Share <= 0.0 && site.WorkDone <= 0.0)
                {
                    StandIn was = StandInsAt(site.Tile, site.Slot);
                    MaterialBill plain = Undo(bill, was);
                    StandIn use = ChooseStandIns(plain, reserved, was);
                    if (use != was)
                    {
                        bill = StandIns.Apply(plain, use);
                        changes.Add((site.Tile, site.Slot, bill, use));
                    }
                }
                reserved = reserved.Plus(bill.Times(1.0 - site.Share));
            }
            foreach (var c in changes)
            {
                Build.Rebill(c.Tile, c.Slot, c.Bill);
                if (c.Use != StandIn.None) standIns[(c.Tile, c.Slot)] = c.Use;
                else standIns.Remove((c.Tile, c.Slot));
            }
        }

        /// <summary>The copper/lithium bill a stand-in bill was made from.</summary>
        static MaterialBill Undo(MaterialBill bill, StandIn use)
        {
            foreach (var (ore, sub, flag, ratio) in Swaps)
                if ((use & flag) != 0 && bill[sub] > 0.0)
                    bill = bill.Without(sub).Add(ore, bill[sub] / ratio);
            return bill;
        }

        /// <summary>What a building of this type would take if placed now, with its stand-ins (for the build bar).</summary>
        public MaterialBill BillToBuild(BuildingType type)
        {
            MaterialBill bill = BuildBill(type);
            return StandIns.Apply(bill, ChooseStandIns(bill, Build.Remaining));
        }

        /// <summary>The stand-ins a building (or site) uses.</summary>
        public StandIn StandInsAt(int tileId, int slot) => standIns.TryGetValue((tileId, slot), out StandIn s) ? s : StandIn.None;

        /// <summary>A bill with the stand-ins of the building at this slot.</summary>
        MaterialBill WithStandIns(MaterialBill bill, int tileId, int slot) => StandIns.Apply(bill, StandInsAt(tileId, slot));

        /// <summary>Start a construction site, choosing its stand-ins. True when it needs nothing (finished now).</summary>
        bool StartSite(int tileId, int slot, MaterialBill bill, double workHours)
        {
            if (Debug.FreeBuilding)
            {
                standIns.Remove((tileId, slot));
                return Build.Start(tileId, slot, new MaterialBill(), 0.0);   // debug: nothing to pay, nothing to wait for
            }
            StandIn use = ChooseStandIns(bill, Build.Remaining);
            if (use != StandIn.None) standIns[(tileId, slot)] = use;
            else standIns.Remove((tileId, slot));
            return Build.Start(tileId, slot, StandIns.Apply(bill, use), workHours);
        }
    }
}
