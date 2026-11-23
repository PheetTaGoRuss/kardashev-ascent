namespace KardashevAscent.Simulation.Resources
{
    /// <summary>
    /// What buildings cost in real materials (director, 2026-11-14: buildings are built from materials, not power).
    /// A building's bill is the real material mass of the whole installation times <see cref="OnSiteShare"/>: the colony
    /// supplies that share from what its robots mine and refine; the rest (wafers, cells, electronics) stands for parts
    /// the colony can't make yet. The share is a game rule. The game has no glass, polymer or phosphate yet, so rock
    /// stands in for them (glass and silicon are made from silica, phosphate from phosphate rock); that is a game rule too.
    /// </summary>
    public static class BuildCosts
    {
        /// <summary>Share of an installation's real material mass the colony supplies on site (game rule, TBD).</summary>
        public const double OnSiteShare = 0.01;

        /// <summary>Share of a finished building's materials that comes back when it is taken down (game rule).</summary>
        public const double SalvageShare = 0.5;

        // ---- Crystalline silicon PV module ----

        /// <summary>
        /// Module mass per m², kg. TBD: typical 60-cell modules weigh about 18–20 kg for 1.6–1.7 m²; no single source
        /// checked yet.
        /// </summary>
        public const double PanelModuleKgPerM2 = 11.5;

        /// <summary>
        /// Mass shares of a typical c-Si module: glass 75 %, polymers 12 %, aluminium frame 9 %, silicon 3 %, copper 1 %
        /// (Michigan State University study citing a 2022 source; other sources give glass 67–75 %, aluminium 9–18 %,
        /// silicon 3–4 %, copper ~1 %).
        /// </summary>
        public const double PanelGlassShare = 0.75, PanelPolymerShare = 0.12, PanelFrameShare = 0.09,
                            PanelSiliconShare = 0.03, PanelCopperShare = 0.01;

        /// <summary>
        /// Materials for <paramref name="areaM2"/> of panels. The frame is steel (iron) rather than aluminium, which
        /// the colony can't refine at first (game rule; steel-framed modules exist).
        /// </summary>
        public static MaterialBill Panel(double areaM2)
        {
            double kg = PanelModuleKgPerM2 * areaM2 * OnSiteShare;
            return new MaterialBill()
                .Add(Material.Rock, kg * (PanelGlassShare + PanelSiliconShare + PanelPolymerShare))
                .Add(Material.Iron, kg * PanelFrameShare)
                .Add(Material.Copper, kg * PanelCopperShare);
        }

        // ---- LFP (lithium iron phosphate) storage ----

        /// <summary>LiFePO₄ cathode material per kWh of cell capacity, kg (Guan et al., as tabulated in RSC supplementary data).</summary>
        public const double LfpCathodeKgPerKWh = 2.05;

        /// <summary>Mass shares of LiFePO₄ (molar masses: Li 6.94, Fe 55.845, LiFePO₄ 157.76 g/mol).</summary>
        public const double LfpLithiumShare = 6.94 / 157.76, LfpIronShare = 55.845 / 157.76;

        /// <summary>
        /// Cathode materials of a LFP battery of <paramref name="capacityWh"/>, at full mass (robot batteries are made
        /// whole). The anode, foils and casing aren't counted yet (no sourced figures).
        /// </summary>
        public static MaterialBill BatteryCells(double capacityWh)
        {
            double cathode = LfpCathodeKgPerKWh * capacityWh / 1000.0;
            return new MaterialBill()
                .Add(Material.Lithium, cathode * LfpLithiumShare)
                .Add(Material.Iron, cathode * LfpIronShare)
                .Add(Material.Rock, cathode * (1.0 - LfpLithiumShare - LfpIronShare));
        }

        /// <summary>Materials for a battery building of <paramref name="capacityWh"/> (its on-site share).</summary>
        public static MaterialBill Battery(double capacityWh) => BatteryCells(capacityWh).Times(OnSiteShare);

        // ---- Stations (game rules, TBD: no installation data yet) ----

        public static readonly MaterialBill Workshop = new MaterialBill()
            .Add(Material.Iron, 600.0).Add(Material.Rock, 400.0).Add(Material.Copper, 30.0);

        public static readonly MaterialBill ChargingStation = new MaterialBill()
            .Add(Material.Iron, 150.0).Add(Material.Rock, 50.0).Add(Material.Copper, 20.0);

        public static readonly MaterialBill Relay = new MaterialBill()
            .Add(Material.Iron, 60.0).Add(Material.Rock, 30.0).Add(Material.Copper, 10.0);

        /// <summary>Research Station: a small lab with instruments and a test bench (game rule, TBD).</summary>
        public static readonly MaterialBill ResearchStation = new MaterialBill()
            .Add(Material.Iron, 200.0).Add(Material.Rock, 100.0).Add(Material.Copper, 25.0);

        /// <summary>Free-flow tidal turbine: a rotor, nacelle, tower and a seabed mount (game rule, TBD).</summary>
        public static readonly MaterialBill TidalTurbine = new MaterialBill()
            .Add(Material.Iron, 1200.0).Add(Material.Rock, 400.0).Add(Material.Copper, 90.0);

        /// <summary>Wave converter line: hinged steel floats, hydraulics and a seabed anchor (game rule, TBD).</summary>
        public static readonly MaterialBill WaveConverter = new MaterialBill()
            .Add(Material.Iron, 900.0).Add(Material.Rock, 150.0).Add(Material.Copper, 120.0);

        /// <summary>Tidal barrage: a long dam of rock and steel with gates and turbines (game rule, TBD; about ten turbines' worth).</summary>
        public static readonly MaterialBill TidalBarrage = new MaterialBill()
            .Add(Material.Iron, 12000.0).Add(Material.Rock, 20000.0).Add(Material.Copper, 800.0);

        /// <summary>
        /// Real conductor mass of one tile of line, kg: one 500 mm2 copper conductor over a nominal 300 km tile, density x length x area
        /// = 8 890 x 300 000 x 5e-4 (the cable spec of the physical flow; see Electrical.CableEconomics).
        /// </summary>
        public const double CableConductorMassKg = 8890.0 * 300_000.0 * 500e-6;

        /// <summary>
        /// Share of the real conductor mass a line takes on site: option B of the cable-bill decision (2026-11-20 C now, B later; 2026-11-23 B).
        /// Balance run: one Mining robot at a copper mine with a forward charger and battery yard (8 game days, the deposit ran out) made about
        /// 450 kg of copper a day. A line at the full "one day of one mine" would cost 450 kg, more than the 250 kg of copper the lander carries,
        /// and stations only go where the grid reaches, so the share is set for about a quarter of a mine day, 112 kg (game rule, TBD): the
        /// lander's copper then pays for two lines. Logged in balance-findings.md.
        /// </summary>
        public const double CableCopperShare = 112.5 / CableConductorMassKg;

        /// <summary>Copper of one tile of single-conductor line: the real mass times the share (about 112 kg).</summary>
        public static double CableCopperKg => CableConductorMassKg * CableCopperShare;

        /// <summary>Power line: real conductor copper (mass times the share) and poles and a switch box (iron and rock, game rule, TBD).</summary>
        public static readonly MaterialBill PowerLine = new MaterialBill()
            .Add(Material.Copper, CableCopperKg).Add(Material.Iron, 30.0).Add(Material.Rock, 20.0);

        /// <summary>High-voltage line: the same 500 mm2 conductor (the same copper), a tower and insulators for one tile (iron and rock, game rule, TBD).</summary>
        public static readonly MaterialBill HighVoltageLine = new MaterialBill()
            .Add(Material.Copper, CableCopperKg).Add(Material.Iron, 120.0).Add(Material.Rock, 60.0);

        /// <summary>AC line: three conductors, towers and insulators for one tile (game rule, TBD).</summary>
        public static readonly MaterialBill AcLine = new MaterialBill()
            .Add(Material.Copper, 3.0 * CableCopperKg).Add(Material.Iron, 90.0).Add(Material.Rock, 40.0);   // three conductors

        /// <summary>Transformer: a core of iron and copper windings in a bay (game rule, TBD).</summary>
        public static readonly MaterialBill Transformer = new MaterialBill()
            .Add(Material.Copper, 150.0).Add(Material.Iron, 400.0).Add(Material.Rock, 60.0);

        /// <summary>Inverter or rectifier: a converter station (game rule, TBD).</summary>
        public static readonly MaterialBill Converter = new MaterialBill()
            .Add(Material.Copper, 200.0).Add(Material.Iron, 150.0).Add(Material.Rock, 40.0);

        /// <summary>Storage yard: a levelled, fenced hardstanding with a gantry (game rule, TBD).</summary>
        public static readonly MaterialBill StorageYard = new MaterialBill()
            .Add(Material.Iron, 150.0).Add(Material.Rock, 300.0);

        /// <summary>Sealed store: a lined, closed vessel for liquids and reactive metals (game rule, TBD).</summary>
        public static readonly MaterialBill SealedStore = new MaterialBill()
            .Add(Material.Iron, 250.0).Add(Material.Rock, 100.0).Add(Material.Copper, 10.0);

        /// <summary>Gas tank: a pressure vessel with valves and piping (game rule, TBD).</summary>
        public static readonly MaterialBill GasTank = new MaterialBill()
            .Add(Material.Iron, 400.0).Add(Material.Rock, 50.0).Add(Material.Copper, 15.0);

        /// <summary>Alumina plant: a kiln, leaching tanks and a precipitator (game rule, TBD).</summary>
        public static readonly MaterialBill AluminaPlant = new MaterialBill()
            .Add(Material.Iron, 400.0).Add(Material.Rock, 200.0).Add(Material.Copper, 15.0);

        /// <summary>Electrolysis cell: a steel shell with a refractory lining and heavy busbars (game rule, TBD).</summary>
        public static readonly MaterialBill Electrolyser = new MaterialBill()
            .Add(Material.Iron, 300.0).Add(Material.Rock, 150.0).Add(Material.Copper, 60.0);

        /// <summary>
        /// What the lander brings: about one solar panel, battery yard, charging station, Workshop and Research Station, with some to spare
        /// (game rule, TBD).
        /// </summary>
        public static readonly MaterialBill LanderCargo = new MaterialBill()
            .Add(Material.Rock, 2500.0).Add(Material.Iron, 1500.0).Add(Material.Copper, 250.0).Add(Material.Lithium, 25.0);

        // ---- Before save version 26: buildings cost "build parts" in Wh ----

        /// <summary>The old on-site share of embodied energy, for reading old saves only.</summary>
        public const double LegacyOnSiteShare = 0.01;
        /// <summary>The old Li-ion embodied energy per Wh, for reading old saves only.</summary>
        public const double LegacyLiIonWhPerWh = 100.0;
        public const double LegacyWorkshopWh = 2_000_000.0, LegacyChargingStationWh = 500_000.0, LegacyRelayWh = 200_000.0;
        public static double LegacyPanelWh(double embodiedWhPerM2, double areaM2) => LegacyOnSiteShare * embodiedWhPerM2 * areaM2;
        public static double LegacyBatteryWh(double capacityWh) => LegacyOnSiteShare * LegacyLiIonWhPerWh * capacityWh;
    }
}
