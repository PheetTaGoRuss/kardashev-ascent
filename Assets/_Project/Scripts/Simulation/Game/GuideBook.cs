using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>One page of the guide book: a short line for the list, then sections of facts.</summary>
    public sealed class GuidePage
    {
        public GuidePage(string category, string title, string summary)
        {
            Category = category;
            Title = title;
            Summary = summary;
        }

        public string Category { get; }
        public string Title { get; }
        /// <summary>One line: what it is.</summary>
        public string Summary { get; }
        /// <summary>Headed sections, in order (heading, lines).</summary>
        public List<(string Heading, List<string> Lines)> Sections { get; } = new List<(string, List<string>)>();
        /// <summary>A material the page is about (for its icon), if any.</summary>
        public Material? Material { get; set; }
        /// <summary>A ground the page is about (for its picture), if any.</summary>
        public TerrainType? Ground { get; set; }

        public GuidePage Add(string heading, params string[] lines)
        {
            Sections.Add((heading, lines.Where(l => !string.IsNullOrEmpty(l)).ToList()));
            return this;
        }

        /// <summary>Does the page mention this text (case-insensitive)? For the guide's search box.</summary>
        public bool Mentions(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string t = text.Trim();
            bool Has(string s) => s != null && s.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
            return Has(Title) || Has(Summary) || Sections.Any(s => Has(s.Heading) || s.Lines.Any(Has));
        }
    }

    /// <summary>
    /// The guide book (director 2026-11-15): everything the player may want to look up — where each material is found,
    /// what each ground is good for, what buildings cost and do, what robots and research do. Built from the game's own
    /// rules (deposit rules, costs, catalogues), so it can't drift from what the game does. Game text, not physics.
    /// </summary>
    public static class GuideBook
    {
        public const string Materials = "Materials", Ground = "Ground", Buildings = "Buildings", Robots = "Robots", Research = "Research";
        public static readonly string[] Categories = { Materials, Ground, Buildings, Robots, Research };

        /// <summary>Battery yard size used for its cost line (the game's default yard).</summary>
        public const double DefaultBatteryWh = 20_000_000.0;
        /// <summary>A base panel field's area, m² (1 ha), for its cost line.</summary>
        public const double DefaultPanelM2 = 10_000.0;

        public static List<GuidePage> All()
        {
            var pages = new List<GuidePage>();
            foreach (Material m in Resources.Materials.All)
                pages.Add(MaterialPage(m));
            pages.Add(NoOrePage());
            foreach (TerrainType t in (TerrainType[])Enum.GetValues(typeof(TerrainType)))
                pages.Add(GroundPage(t));
            pages.AddRange(BuildingPages());
            pages.AddRange(RobotPages());
            pages.AddRange(ResearchPages());
            return pages;
        }

        // ---- materials ----

        /// <summary>The kinds of ground where a mined material can be found (empty for made materials).</summary>
        public static List<TerrainType> GroundsWith(Material m)
        {
            var list = new List<TerrainType>();
            foreach (TerrainType t in (TerrainType[])Enum.GetValues(typeof(TerrainType)))
            {
                if (Terrains.IsWater(t)) continue;
                bool here = m == Material.Rock || m == Material.Iron
                            || (m == Material.Water && DepositMap.IsIceGround(t))
                            || (m == Material.Copper && DepositMap.IsCopperGround(t))
                            || (m == Material.Lithium && DepositMap.IsLithiumGround(t))
                            || (m == Material.RareEarth && DepositMap.IsDeepGround(t));
                if (here) list.Add(t);
            }
            return list;
        }

        /// <summary>Every building whose bill needs this material.</summary>
        public static List<string> BuildingsUsing(Material m)
        {
            var list = new List<string>();
            foreach (var (name, bill) in Bills())
                if (bill[m] > 0.0) list.Add(name);
            return list;
        }

        static IEnumerable<(string Name, MaterialBill Bill)> Bills()
        {
            yield return ("Solar panel", BuildCosts.Panel(DefaultPanelM2));
            yield return ("Battery yard", BuildCosts.Battery(DefaultBatteryWh));
            foreach (BuildingDefinition d in BuildingCatalog.All)
                if (d.FixedBill != null)
                    yield return (Capital(d.Name), d.FixedBill);
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static string GroundList(IEnumerable<TerrainType> grounds) => string.Join(", ", grounds.Select(Terrains.Name));

        static GuidePage MaterialPage(Material m)
        {
            string name = Resources.Materials.Name(m);
            List<TerrainType> grounds = GroundsWith(m);
            var page = new GuidePage(Materials, name, Summary(m)) { Material = m };
            switch (m)
            {
                case Material.Rock:
                    page.Add("Where", "Every land tile (grade 1): no survey needed.");
                    break;
                case Material.Iron:
                    page.Add("Where", $"Every land tile: {GroundList(grounds)}.",
                             $"Grade varies ±{DepositMap.Spread * 100:0} % from tile to tile; the planet's iron share (from the star's metals) scales it.");
                    break;
                case Material.Water:
                    page.Add("Where", $"Ice on {GroundList(grounds)} (tundra holds about {DepositMap.TundraIceGrade * 100:0} % as much).",
                             "Any land tile next to a sea: pump it.");
                    break;
                case Material.Copper:
                    page.Add("Where", $"High ground: {GroundList(grounds)}.",
                             $"Best within {DepositMap.ArcReachTiles} tiles of a sea, on a planet that had water (about {DepositMap.CopperShare * 100:0} % of such tiles).",
                             $"Poorer sulfide copper on high ground anywhere (about {DepositMap.SulfideCopperShare * 100:0} % of tiles, grade ×{DepositMap.SulfideCopperGrade:0.0}).",
                             "The sea floor has copper too, but robots can't reach it yet.");
                    break;
                case Material.Lithium:
                    page.Add("Where", $"Low ground: {GroundList(grounds)}.",
                             "Only inland (no sea next to it), beside high ground (highland, mountain or volcanic rock), on a planet that had water.",
                             $"About {DepositMap.LithiumShare * 100:0} % of such tiles hold a brine.");
                    break;
                case Material.Aluminium:
                    page.Add("Where", "Not mined: a Workshop refines it from rock, on any planet.",
                             $"{Unlock(Material.Aluminium)}; {Kg(RobotTech.RefineBatchKg(Material.Aluminium))} a batch.");
                    break;
                case Material.Sodium:
                    page.Add("Where", "Not mined: a Workshop refines it from rock, on any planet.",
                             $"{Unlock(Material.Sodium)}; {Kg(RobotTech.RefineBatchKg(Material.Sodium))} a batch.");
                    break;
                case Material.RareEarth:
                    page.Add("Where", $"Deep ore in {GroundList(grounds)}: canyons most often, then mountains and volcanic rock.",
                             "Carbonatites form in rifts (the East African Rift has many) and sit deep in old igneous mountains (Mountain Pass, California).",
                             "A cave doubles the chance: it reaches deeper rock.");
                    page.Add("How to find it", "Run the seismic survey at the Research Station (it needs gravity first). Then surveyed mountains, canyons and caves show it.",
                             "Mountains and canyons must be opened (tunnel, blasting, bridge) before robots can mine there.");
                    page.Add("Used for", $"Faster drive: {RobotTech.RareEarthPerDriveKg:0} kg for its NdFeB magnets (neodymium-iron-boron, the strongest permanent magnets).");
                    break;
                default:
                    page.Add("Where", "What is left of rock after refining.");
                    break;
            }
            if (grounds.Count > 0 && m != Material.Rock)
                page.Add("How to get it", "Survey the tile (tile panel ► Survey) to see its grade, then ► Mine. Grade 1 = an ordinary deposit on Earth.");
            if (DepositMap.ReserveKgAtGrade1(m) > 0.0)
                page.Add("How much", $"A grade-1 deposit holds {Kg(DepositMap.ReserveKgAtGrade1(m))} within the robots' reach; grade 2 holds twice that.",
                         m == Material.Water ? "Ice runs out; sea water pumped from a shore never does." : "It runs out: mining stops when it is empty, so survey further out.");
            else if (m == Material.Rock)
                page.Add("How to get it", "► Mine rock on any land tile.");
            List<string> uses = BuildingsUsing(m);
            if (uses.Count > 0)
                page.Add("Used for", string.Join(", ", uses) + ".");
            if (m == Material.Aluminium)
                page.Add("Used for", $"Wiring instead of copper: {string.Join(", ", BuildingsUsing(Material.Copper))}, robots.",
                         $"{StandIns.AluminiumPerCopperKg:0.00} kg for each kg of copper: it conducts {StandIns.AluminiumIacs * 100:0} % as well, so the wire is thicker, but it is {StandIns.CopperDensity / StandIns.AluminiumDensity:0.0} times lighter.");
            if (m == Material.Sodium)
                page.Add("Used for", $"Sodium-ion cells instead of lithium: {string.Join(", ", BuildingsUsing(Material.Lithium))}, robots.",
                         $"{StandIns.SodiumPerLithiumKg:0.0} kg for each kg of lithium: one sodium ion carries the same charge as one lithium ion, and weighs {StandIns.SodiumPerLithiumKg:0.0} times as much.");
            return page;
        }

        static string Unlock(Material refined) =>
            RobotTech.UnlockMilestone(refined) == 0 ? "Open from the start" : $"Unlocks at milestone {RobotTech.UnlockMilestone(refined)}";

        /// <summary>How to start on a planet with no copper or lithium ore (director 2026-11-15).</summary>
        static GuidePage NoOrePage() =>
            new GuidePage(Materials, "No copper or lithium?", "Every rocky planet can be started: rock gives aluminium and sodium.")
                .Add("Why", "Copper and lithium ores form with water: copper near old seas, lithium in dried-up salt lakes.",
                     "Dry planets (Mars, Proxima b, Kepler-452 b) have little or none.")
                .Add("What to do",
                     "1. Land, mine rock and iron, and build a Robot Workshop (the lander brings its copper).",
                     "2. Mine rock and refine aluminium and sodium at the Workshop (Manufacturing, U).",
                     "3. Place buildings as usual: with no copper or lithium in stock they use aluminium wiring and sodium cells.")
                .Add("Good to know",
                     "A site that is still waiting takes whichever metal arrives first, copper or aluminium.",
                     $"Refining costs power: about {RobotTech.Refine(Material.Aluminium).EnergyWh / RobotTech.RefineBatchKg(Material.Aluminium) / 1000.0:0} kWh for each kg of aluminium.",
                     "Taking a building down gives back what it was made of.")
                .Add("Choosing where to land", "Land near rock you can mine and in strong light: refining needs power. On a wet planet, high ground near a sea has copper.");

        static string Summary(Material m)
        {
            switch (m)
            {
                case Material.Rock: return "Bulk building stone, everywhere on land.";
                case Material.Iron: return "The main structural metal, on every land tile.";
                case Material.Water: return "Ice or sea water, near cold ground and shores.";
                case Material.Copper: return "Wiring metal, in high ground.";
                case Material.Lithium: return "Battery metal, in dry inland basins.";
                case Material.Aluminium: return "Light metal refined from rock.";
                case Material.Sodium: return "Battery metal refined from rock.";
                default: return "Waste rock left by refining.";
            }
        }

        static string Kg(double kg) => kg >= 1000.0 ? $"{kg / 1000.0:0.#} t" : $"{kg:0} kg";

        // ---- ground ----

        static GuidePage GroundPage(TerrainType t)
        {
            string name = Terrains.Name(t);
            string use = Terrains.IsWater(t) ? (Terrains.AllowsFloating(t) ? "Floating solar only" : "No building")
                       : Terrains.AllowsFloating(t) ? "Floating solar only (too soft for land buildings)"
                       : "Land buildings";
            var page = new GuidePage(Ground, name, use + ".") { Ground = t };
            var materials = Resources.Materials.Mined.Where(m => GroundsWith(m).Contains(t)).Select(Resources.Materials.Name).ToList();
            page.Add("Can hold", materials.Count > 0 ? string.Join(", ", materials) + (t == TerrainType.Lowland || t == TerrainType.Desert || t == TerrainType.DryBasin ? " (lithium only inland beside high ground)" : "") + "."
                                                     : "Nothing robots can mine yet.");
            if (!Terrains.IsWater(t))
                page.Add("Also", "Next to a sea: water to pump.");
            page.Add("Landing", Terrains.LandingProblem(t) ?? "You can land here.");
            if (Terrains.IsBarrier(t))
            {
                var ways = Passages.For(t);
                page.Add("Barrier", "Robots can't drive across it, build on it or mine it until it is opened. They can still survey it from the edge.",
                         string.Join(" · ", ways.Select(k => $"{Passages.Name(k)}: {Passages.Bill(k, false).Describe()}, {Passages.WorkHours(k, false):0} h")) + ".");
                if (t == TerrainType.Mountain)
                    page.Add("Caves", "A cave (found by a survey) halves a tunnel's work and steel. Both ways through bring out the cut rock.");
            }
            if (DepositMap.IsDeepGround(t))
                page.Add("Deep ore", $"Its deposits hold {DepositMap.DeepReserveFactor:0}× as much (ore reached deep in the rock), and rare earths can be here (seismic survey).");
            double hop = RouteFinder.HopHours(t);
            page.Add("Robots", double.IsInfinity(hop) ? "Need a boat hull to work here." : $"Cross it in {hop:0.#} h ({hop / RouteFinder.FlatHopHours:0.#}× flat ground).");
            page.Add("Sunlight", $"Reflects {Terrains.Albedo(t) * 100:0} % of light (ground shine for tilted panels).");
            return page;
        }

        // ---- buildings ----

        static IEnumerable<GuidePage> BuildingPages()
        {
            var what = new Dictionary<string, string>
            {
                { "Solar panel", "Power from starlight. Tilt and facing matter; bigger tiers at milestones." },
                { "Battery yard", "Stores power for the night; robots can charge there too." },
                { "Floating solar", "A panel on shallow sea or wetland, cooled by the water. Needs a boat robot and the water experiments." },
                { "Robot workshop", "Builds robots and upgrades, refines aluminium and sodium." },
                { "Charging station", "Charges several robots at once from the grid." },
                { "Relay", "Extends the colony network; needs grid power." },
                { "Research station", "Where robots measure the planet and run experiments." },
            };
            foreach (var (name, bill) in Bills())
            {
                string key = what.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) ?? name;
                var page = new GuidePage(Buildings, name, what.TryGetValue(key, out string w) ? w : "");
                page.Add("Costs", bill.Describe() + (name == "Solar panel" ? " (a 1 ha field)" : name == "Battery yard" ? " (a 20 MWh yard)" : ""));
                yield return page;
            }
            yield return new GuidePage(Buildings, "Floating solar", what["Floating solar"])
                .Add("Costs", "1.5× a land panel of the same size.");
        }

        // ---- robots ----

        static IEnumerable<GuidePage> RobotPages()
        {
            foreach (RobotKind k in (RobotKind[])Enum.GetValues(typeof(RobotKind)))
            {
                var tasks = RobotCatalog.Capabilities.Where(c => c.Robot == k && !c.Secondary).Select(c => c.Task.ToString().ToLowerInvariant()).Distinct().ToList();
                var later = RobotCatalog.Capabilities.Where(c => c.Robot == k && c.Secondary).Select(c => $"{c.Task.ToString().ToLowerInvariant()} (milestone {c.UnlockMilestone})").ToList();
                var page = new GuidePage(Robots, $"{k} robot", tasks.Count > 0 ? "Does: " + string.Join(", ", tasks) + "." : "No jobs in the game yet (planned).");
                if (later.Count > 0) page.Add("Learns later", string.Join(", ", later) + ".");
                page.Add("Made at", k == RobotKind.General ? "You start with three. More from a Workshop." : $"A Workshop, from milestone {RobotTech.UnlockMilestone(k)}.");
                yield return page;
            }
        }

        // ---- research ----

        static IEnumerable<GuidePage> ResearchPages()
        {
            foreach (SolarParameter p in (SolarParameter[])Enum.GetValues(typeof(SolarParameter)))
                yield return new GuidePage(Research, SolarUnknowns.Name(p), "A value that sets how much your panels make.")
                    .Add("Measured with", SolarUnknowns.Instrument(p) + ", at a Research Station (Research, T).")
                    .Add("Why", "Until it is measured, panels run with a safety margin.");
            foreach (ExperimentKind k in Experiments.All)
            {
                var page = new GuidePage(Research, Experiments.Name(k), Experiments.What(k))
                    .Add("Takes", $"{Experiments.WorkHours(k):0} h of robot work at a Research Station.");
                ExperimentKind? first = Experiments.Needs(k);
                if (first.HasValue) page.Add("Needs first", Experiments.Name(first.Value) + ".");
                yield return page;
            }
        }
    }
}
