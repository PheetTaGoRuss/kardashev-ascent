using System;
using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Operations;
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
        public const string Materials = "Materials", Ground = "Ground", Buildings = "Buildings", Robots = "Robots", Research = "Research", Operations = "Operations";
        public static readonly string[] Categories = { Materials, Ground, Buildings, Robots, GuideScience.Energy, GuideScience.Science, Research, Operations };

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
            pages.AddRange(OperationPages());
            pages.AddRange(GuideScience.Pages());
            // the older categories take the same page template as the new ones (UI information design, finish round)
            var titles = pages.Select(p => p.Title).Distinct().ToList();
            for (int i = 0; i < pages.Count; i++)
                if (pages[i].Category == Materials || pages[i].Category == Ground || pages[i].Category == Buildings || pages[i].Category == Robots)
                    pages[i] = ToTemplate(pages[i], titles);
            return pages;
        }

        /// <summary>
        /// Reshape a page into the template: In play (every game fact, each line keeping its old heading so nothing is lost), How to use (the headings that
        /// say what to do), Real science (the headings that explain why) and Related (titles of other pages the text mentions). A section with nothing is left out.
        /// </summary>
        public static GuidePage ToTemplate(GuidePage old, IList<string> allTitles)
        {
            var page = new GuidePage(old.Category, old.Title, old.Summary) { Material = old.Material, Ground = old.Ground };
            var inPlay = new List<string>(); var howTo = new List<string>(); var science = new List<string>();
            foreach (var (heading, lines) in old.Sections)
            {
                bool use = heading == "How to get it" || heading == "How to find it" || heading == "What to do" || heading == "Choosing where to land" || heading == "How to use";
                bool why = heading == "Why" || heading == "Good to know" || heading == "Real science";
                foreach (string line in lines)
                    (use ? howTo : why ? science : inPlay).Add(use || why || heading == "In play" ? line : heading + ": " + line);
            }
            if (inPlay.Count > 0) page.Add("In play", inPlay.ToArray());
            if (howTo.Count > 0) page.Add("How to use", howTo.ToArray());
            if (science.Count > 0) page.Add("Real science", science.ToArray());
            string text = string.Join(" ", old.Sections.SelectMany(s => s.Lines));
            var related = allTitles.Where(t => t != old.Title && t.Length >= 4 && text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0).Take(6).ToArray();
            if (related.Length > 0) page.Add("Related", related);
            return page;
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
                            || (m == Material.RareEarth && DepositMap.IsDeepGround(t))
                            || (m == Material.Anorthosite && (t == TerrainType.Highland || t == TerrainType.Mountain))
                            || (m == Material.Salt && DepositMap.IsSaltFlat(t));
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
                case Material.Alumina:
                case Material.PureAluminium:
                case Material.Sodium:
                    foreach (RefineRecipe r in Refining.All)
                        if (r.Output == m)
                            page.Add(r.Name, $"{Refining.ModuleName(r.Module)}: {r.InputKg:0} kg {Resources.Materials.Name(r.Input).ToLowerInvariant()} → {r.OutputKg:0} kg in {Days(r.Hours)}, {r.EnergyWh / 1000.0:0} kWh.",
                                     r.What);
                    page.Add("Refinery", "Refinery modules go in slots touching a Robot Workshop. Each runs one batch at a time; build more to make more at once.",
                             "Batches pause in a brownout. Auto keeps a chosen amount in stock (Manufacturing, U).");
                    break;
                case Material.Anorthosite:
                    page.Add("Where", $"Highlands and mountains: {GroundList(grounds)} (about {DepositMap.AnorthositeShare * 100:0} % of them).",
                             $"About {Refining.AnorthositeAluminaShare * 100:0} % alumina, twice ordinary rock ({Refining.RockAluminaShare * 100:0} %): the Moon's pale highlands are anorthosite.");
                    page.Add("Used for", "Alumina plant: step 1 of aluminium.");
                    break;
                case Material.Salt:
                    page.Add("Where", $"Salt flats on {GroundList(grounds)} of planets that had seas (evaporites, like Death Valley or Mars's chloride deposits).",
                             "Any shore of a liquid sea: evaporate sea water (about 2.7 % salt). Never runs out.");
                    page.Add("Used for", $"Sodium: {Refining.SaltPerSodium:0.00} kg salt per kg (the rest leaves as chlorine).");
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
                     "1. Land, build a Robot Workshop first (the lander's copper covers it).",
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
                case Material.Aluminium: return "Light metal: alumina, then electrolysis.";
                case Material.Sodium: return "Battery metal made from salt.";
                case Material.Anorthosite: return "Aluminium ore: pale highland rock.";
                case Material.Salt: return "Salt flats and sea water: sodium's source.";
                case Material.Alumina: return "White powder: step 1 of aluminium.";
                case Material.PureAluminium: return "99.99 % aluminium for chips.";
                case Material.RareEarth: return "Deep ore for strong magnets.";
                default: return "Waste rock left by refining.";
            }
        }

        static string Days(double hours) => hours >= 48.0 ? $"{hours / 24.0:0.#} days" : $"{hours:0} h";

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
                { "Robot workshop", "Builds robots and upgrades. Refinery modules attach to it." },
                { "Storage yard", "Holds bulk solids (ore, rock, metals) on a tile. A tile outside the lander keeps stock in its own depot; robots haul between depots." },
                { "Sealed store", "Holds liquids and hazardous solids such as sodium and lithium. Without one, a tile cannot keep them." },
                { "Gas tank", "Holds pressurised gas such as oxygen. The capacity is the ideal gas law with a compressibility factor: mass = p V M / (Z R T)." },
                { "Alumina plant", "Refinery module next to a Workshop: rock or anorthosite to alumina (3–4 days a batch)." },
                { "Electrolysis cell", "Refinery module next to a Workshop: alumina to aluminium, salt to sodium (days to a week a batch)." },
                { "Charging station", "Robots can only recharge at a charger (or a battery yard): build one first. On the landing tile the lander's RTG feeds it." },
                { "Relay", "Extends the colony network; needs grid power." },
                { "Research station", "Where robots measure the planet and run experiments." },
                { "High-voltage line", "The same wiring as a power line with a far higher capacity (1 GW against 20 MW). Opens at milestone 1. Put one on a tile where its power line is overloaded." },
                { "Power line", "Nothing is wired for you. Drag a power line onto a generator, battery, charger or station to wire it in (drop cable, instant); until then a generator makes no power and a consumer does not work. Wire mode (X or the Power window) marks each building still waiting: blue OUT gives power, orange IN takes it. Tiles are joined with the wire tool: click a tile, then the next tile along.\n\nWires a tile into the colony grid. A chain of tiles with power lines carries power out from the lander tile; a generator off the grid makes nothing for the colony, and stations and storage can only be built where the grid reaches. Copper, iron and rock. Each tile of line between a generator and the lander tile loses 2 % of its power." },
                { "AC line", "Three-phase line at 33 kV or 500 kV, 50 Hz. It joins the DC grid only through a Rectifier (where AC power comes in) and an Inverter (at the generating end); a Transformer joins the two AC classes. Every conversion loses a share as heat. Opens at milestone 2. Beyond about 100 km under water AC cannot be used." },
                { "Transformer", "Joins the 33 kV and 500 kV AC classes on a tile (about 99.5 % efficient). Opens at milestone 2." },
                { "Inverter", "At the generating end of an AC line: turns DC generation into AC (about 98.5 % at 33 kV, 99 % at 500 kV). Opens at milestone 2." },
                { "Rectifier", "Where an AC line comes into the DC grid: turns AC into DC (about 98.5 % at 33 kV, 99 % at 500 kV). Opens at milestone 2." },
                { "Tidal barrage", "A dam across a bay: the tide fills the basin and is let out through turbines once a tide. Big power from a big tide, but a late, big build that weakens the tide behind it and stops boats crossing. Needs a bay with at least 4 m of tide, the basin survey, and milestone 1." },
                { "Wave converter", "A floating line of hinged floats on shallow sea. Power follows the waves: none in a flat calm, a rating cap in a good sea, shut down in a storm. Mid-latitude seas run roughest. Needs a boat robot and the wave buoy experiment." },
                { "Wind turbine", "A 100 kW class turbine on open land, one to a tile. Power follows the day's wind and the air density: none in a calm or above 25 m/s, almost none on a thin-air world. Needs the wind mast experiment." },
                { "Tidal turbine", "A free-flow rotor on shallow sea. Power follows the tide: none at slack water, most at mid-tide. Coasts with land around them funnel the tide and give the strongest stream. Needs a boat robot and a moon." },
            };
            foreach (var (name, bill) in Bills())
            {
                string key = what.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) ?? name;
                var page = new GuidePage(Buildings, name, what.TryGetValue(key, out string w) ? w : "");
                if (name == "Wind turbine") page.Add("Related", "Wind turbine physics", "Wind shear and air density");
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

        // ---- operations (mission operations: flight rules, hauling, byproducts, heat) ----

        static IEnumerable<GuidePage> OperationPages()
        {
            var rules = new GuidePage(Operations, "Flight rules", "Decisions written down in advance, because the answer from Earth takes years.");
            rules.Add("What a rule is", "When up to three conditions hold on what is known, do one thing, for a stated reason (the rationale is required). Rules are read top to bottom; the first that holds wins.");
            rules.Add("Conditions", Enum.GetValues(typeof(ConditionKind)).Cast<ConditionKind>().Select(k => k.ToString()).ToArray());
            rules.Add("Responses", Enum.GetValues(typeof(ResponseKind)).Cast<ResponseKind>().Select(k => k.ToString()).ToArray());
            rules.Add("Versions and uplink", "Every publish is a new version. A robot runs the version the colony network last gave it; one that is cut off keeps its last version and syncs when it is back. A rule never fires on something not measured yet.");
            rules.Add("Your orders win", "A direct order from you always beats a rule: a rule never overwrites a setting or a destination you gave a robot.");
            rules.Add("The starter rulebook", StarterRulebook.Rules().Select(r => $"{r.Name}: {r.Rationale}").ToArray());
            yield return rules;

            yield return new GuidePage(Operations, "Hauling", "Materials stay where they are mined or made; robots carry them where they are needed.")
                .Add("Where stock lies", "Each tile outside the lander keeps its own depot. A construction site draws only from its own tile's depot; mined ore lies in a pile on its mine tile.")
                .Add("What makes a haul", "A site short of a material, a plant waiting for its input, or a stock target you set. You never plan a trip.")
                .Add("What a haul costs", $"A robot carries up to {PlanetSimulation.BasePayloadKg:0} kg times its transport capacity. Driving with a load takes more power: drive power x (robot mass + cargo) / robot mass, and on a heavier planet more again (rolling resistance grows with weight). An empty robot costs exactly what it always did.")
                .Add("Handling", $"Loading or unloading takes about {RobotSpecs.HaulHandlingHours * 60.0:0} minutes of work at each stop.")
                .Add("Transport robots", $"The specialists: they carry {RobotCatalog.Profile(RobotKind.Transport)[RobotAttribute.TransportCapacity]:0.#} times the load and move {RobotCatalog.Profile(RobotKind.Transport)[RobotAttribute.Speed]:0.#} times as fast. From milestone {RobotTech.UnlockMilestone(RobotKind.Transport)}.");

            var by = new GuidePage(Operations, "Byproducts", "Refining gives off gas. It is real matter: store it or vent it.");
            foreach (RefineRecipe r in Refining.All.Where(x => x.Byproduct.HasValue))
                by.Add(Refining.ModuleName(r.Module) + ": " + r.Name, $"{r.VentedKg / r.OutputKg:0.00} kg of {Resources.Materials.Name(r.Byproduct.Value).ToLowerInvariant()} for every kg of {Resources.Materials.Name(r.Output).ToLowerInvariant()}.");
            by.Add("Store or vent", $"Each plant has a policy. Store: the gas goes to a gas tank (about {StorageSpecs.TankCapacityKg:0} kg at 20 MPa; mass = p V M / (Z R T)) and a full store stops the plant. Vent: it is released, logged, and chlorine is toxic.");
            yield return by;

            var heat = new GuidePage(Operations, "Heat ledger", "Electricity in = chemical energy kept in the product + waste heat out.");
            foreach (RefineProcess p in Refining.Processes)
            {
                IndustrialProcess proc = Refining.ProcessOf(p);
                heat.Add(Refining.Of(p).Name, $"{proc.ElectricityKWhPerBatch:0.#} kWh in, {proc.ProductEnthalpyKWhPerBatch:0.#} kWh kept in the product, {proc.WasteHeatKWhPerBatch:0.#} kWh as heat per batch.");
            }
            heat.Add("What it does", "The heat is counted and shown in the Flowsheet window; the planet does not warm from it yet.");
            yield return heat;
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
