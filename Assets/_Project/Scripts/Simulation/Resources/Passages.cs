using KardashevAscent.Simulation.Planet;

namespace KardashevAscent.Simulation.Resources
{
    /// <summary>
    /// Ways to open a barrier tile (director 2026-11-15): a mountain is drilled (a tunnel) or blasted, a canyon is
    /// bridged. Saved as integers: append new members at the end only.
    /// </summary>
    public enum PassageKind { None, Tunnel, Blast, Bridge }

    /// <summary>
    /// What each way of opening a barrier takes. All amounts are game rules (TBD) shaped by how it is done on Earth:
    /// a tunnel takes little material (steel rock bolts and arches) but a long time to bore; blasting is quick but takes
    /// a lot (explosives are not modelled yet, so their cost stands as iron for drill steel and casings and copper for
    /// detonator wire); a bridge is a steel truss on rock-fill piers. Both ways through a mountain bring out the rock they
    /// cut (the muck), which goes to the stock.
    /// </summary>
    public static class Passages
    {
        /// <summary>How a barrier of this ground can be opened (empty for ground that is no barrier).</summary>
        public static PassageKind[] For(TerrainType t) =>
            t == TerrainType.Mountain ? new[] { PassageKind.Tunnel, PassageKind.Blast }
            : t == TerrainType.Canyon ? new[] { PassageKind.Bridge }
            : new PassageKind[0];

        public static string Name(PassageKind k) =>
            k == PassageKind.Tunnel ? "Tunnel" : k == PassageKind.Blast ? "Blast a pass" : k == PassageKind.Bridge ? "Bridge" : "None";

        /// <summary>One line for the player.</summary>
        public static string What(PassageKind k) =>
            k == PassageKind.Tunnel ? "Robots bore through the mountain: little steel, a long time."
            : k == PassageKind.Blast ? "Blast a pass through the mountain: quick, but takes a lot of metal."
            : k == PassageKind.Bridge ? "A steel truss across the gorge on rock-fill piers."
            : "";

        /// <summary>A cave already goes some way in: a tunnel takes this share of the work and steel (game rule, TBD).</summary>
        public const double CaveTunnelShare = 0.5;

        /// <summary>Materials to open a barrier this way, taken when it is ordered. Game rule, TBD.</summary>
        public static MaterialBill Bill(PassageKind k, bool cave)
        {
            switch (k)
            {
                case PassageKind.Tunnel: return new MaterialBill().Add(Material.Iron, 300.0 * (cave ? CaveTunnelShare : 1.0));
                case PassageKind.Blast: return new MaterialBill().Add(Material.Iron, 1200.0).Add(Material.Copper, 30.0);
                case PassageKind.Bridge: return new MaterialBill().Add(Material.Iron, 1500.0).Add(Material.Rock, 500.0);
                default: return new MaterialBill();
            }
        }

        /// <summary>Robot hours to open it (one General robot; a crew is faster). Game rule, TBD.</summary>
        public static double WorkHours(PassageKind k, bool cave)
        {
            switch (k)
            {
                case PassageKind.Tunnel: return 150.0 * (cave ? CaveTunnelShare : 1.0);
                case PassageKind.Blast: return 12.0;
                case PassageKind.Bridge: return 60.0;
                default: return 0.0;
            }
        }

        /// <summary>Rock brought out when it is done, kg (the cut rock). Game rule, TBD.</summary>
        public static double MuckKg(PassageKind k, bool cave) =>
            k == PassageKind.Tunnel ? 2000.0 * (cave ? CaveTunnelShare : 1.0) : k == PassageKind.Blast ? 5000.0 : 0.0;

        /// <summary>Robots that can work on one at once.</summary>
        public const int CrewCapacity = 3;
    }
}
