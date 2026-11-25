using System;

namespace KardashevAscent.Simulation.Resources
{
    /// <summary>
    /// Crushing and grinding energy to free the mineral from the rock (Bond's third theory of comminution, Bond 1961, "Crushing and grinding calculations", Brit. Chem. Eng. 6: 378 and 543):
    /// W = 10 Wi (1/sqrt(P80) - 1/sqrt(F80)), kWh per short ton, with P80 and F80 in micrometres (the product and feed sizes through which 80 % passes) and Wi the work index of the ore.
    /// </summary>
    public static class Comminution
    {
        /// <summary>Work index, kWh per tonne, from the tabulated Bond work indexes (Bond 1953, "Work Indexes Tabulated", Trans. AIME; as listed in the 911Metallurgist table of Bond work indices by mineral, https://www.911metallurgist.com/blog/table-of-bond-work-index-by-minerals): copper ore 12.7, hematite iron ore 12.5, granite 15.1 (stand-in for lithium pegmatite), rare-earth ore 13 (TBD: no table value found). Checked against that secondary table only, not the primary paper; short-ton and metric columns differ by 10 %.</summary>
        public static double WorkIndex(Material m)
        {
            switch (m)
            {
                case Material.Copper: return 12.7;
                case Material.Iron: return 12.5;
                case Material.Lithium: return 15.1;
                case Material.RareEarth: return 13.0;
                default: return 0.0;
            }
        }

        /// <summary>Feed size after primary crushing, micrometres (typical 10 mm, game rule) and the product size of a flotation or leach feed, micrometres (typical 150, game rule).</summary>
        public const double F80Um = 10_000.0, P80Um = 150.0;

        public const double ShortTonPerTonne = 1.0;   // the table values are per tonne already

        /// <summary>Energy per tonne of rock ground, kWh (metric).</summary>
        public static double KWhPerTonneRock(Material m)
        {
            double wi = WorkIndex(m);
            return wi <= 0.0 ? 0.0 : 10.0 * wi * (1.0 / Math.Sqrt(P80Um) - 1.0 / Math.Sqrt(F80Um)) * ShortTonPerTonne;
        }

        /// <summary>Whether the ore needs concentrating: copper, lithium and rare earths always; iron when its ore grade is below 50 %. Rock, ice, salt and anorthosite are the product as dug.</summary>
        public static bool NeedsConcentrating(Material m, double oreGrade)
        {
            return m == Material.Copper || m == Material.Lithium || m == Material.RareEarth || (m == Material.Iron && oreGrade < 0.5);
        }

        /// <summary>Energy per tonne of product, kWh: the rock moved per tonne of product (1 / ore grade) times the energy per tonne of rock.</summary>
        public static double KWhPerTonneProduct(Material m, double oreGrade)
        {
            return !NeedsConcentrating(m, oreGrade) ? 0.0 : KWhPerTonneRock(m) / Math.Max(1e-9, oreGrade);
        }
    }
}
