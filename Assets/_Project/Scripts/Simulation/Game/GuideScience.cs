using System.Collections.Generic;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Electrical;
using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The guide pages for the electrical system and the planet's physics (UI information design, stage 3, decided 2026-11-24). They follow the page
    /// template: a one-line summary, then In play (game rules, marked ◆ where a number is a tuning value), How to use, Real science (the physics and its
    /// sources) and Related (the titles of other pages, shown as links). The numbers are read from the code's own constants, so the pages cannot drift.
    /// </summary>
    public static class GuideScience
    {
        public const string Energy = "Energy & grid", Science = "Planet science";
        const string Rule = "◆ ";

        public static IEnumerable<GuidePage> Pages()
        {
            yield return new GuidePage(Energy, "Cables and ratings", "A power line carries current up to its rating; more than that and the grid cuts generators back.")
                .Add("In play",
                     $"A power line is {PlanetSimulation.PlainLineVolts / 1000.0:0} kV and carries {PlanetSimulation.PlainLineRatingA:0} A; a high-voltage line is {PlanetSimulation.HighLineVolts / 1000.0:0} kV and {PlanetSimulation.HighLineRatingA:0} A. Both are 500 mm² copper.",
                     "A three-phase AC line carries √3 times the rating of one conductor.",
                     $"{Rule}A line costs {BuildCosts.CableCopperKg:0.#} kg of copper a tile: a quarter of what one mine yields in a day, set so the lander's copper pays for two lines.",
                     $"{Rule}Until the air pressure is measured, cables are rated at a share of that (never below {PlanetSimulation.MinimumCableShare * 100.0:0} %); measuring it lifts the margin.")
                .Add("How to use", "Wire tiles with the wire tool (X) or the Grid window (F3).", "Fit a fuse or breaker so a hot line opens before it is damaged.", "Meter a line, or inspect it, to see its load.")
                .Add("Real science",
                     "Heat in a conductor is I²R, with resistance R = ρL/A; copper's resistivity rises with temperature. A cable's safe current is set by how fast it sheds that heat.",
                     "The 33 kV and 500 kV classes are real transmission voltages.")
                .Add("Related", "Corona and air density", "AC, converters and the milestone", "Air pressure and density");

            yield return new GuidePage(Energy, "Corona and air density", "In thin air a high-voltage cable starts to glow and lose power at a lower voltage.")
                .Add("In play",
                     $"{Rule}Peek's law is used where the air is between {0.1:0.#} and {2.0:0.#} times Earth's density. Thinner air (Mars-like) is the Paschen region, where gas breaks down at the lowest voltage, with a labelled fallback. With almost no gas there is no corona.",
                     "The cable's usable voltage share is capped at 1: a cable never runs above its rating.")
                .Add("How to use", "Measure the air pressure (Research, T): the barometer experiment fits the pressure and the scale height.")
                .Add("Real science",
                     "Peek's law gives the field at a wire's surface where corona starts: E = 30 m δ (1 + 0.301 / √(δ r)) kV/cm, with δ the relative air density, r the wire radius in cm and m a surface factor.",
                     "Paschen's law: the breakdown voltage of a gas has a minimum at one pressure times gap, so thin air can break down at a lower voltage than thick air.")
                .Add("Related", "Air pressure and density", "Cables and ratings");

            yield return new GuidePage(Energy, "AC, converters and the milestone", "AC joins the DC grid only through a rectifier or an inverter; it opens at a milestone.")
                .Add("In play",
                     $"AC lines come in two classes, {VoltageLadder.Ac33k / 1000.0:0} kV and {VoltageLadder.Ac500k / 1000.0:0} kV, joined by a transformer. AC opens at milestone {PlanetSimulation.AcMilestone}.",
                     "An inverter puts DC generation onto an AC line; a rectifier brings AC into the DC grid. Each conversion loses some power as heat.")
                .Add("How to use", "Place an inverter at the generating end, an AC line, and a rectifier where it enters the grid.")
                .Add("Real science", "Transformers change voltage and current together, so line loss I²R falls as voltage rises: that is why long lines run at high voltage. Converters have losses of a few per cent.")
                .Add("Related", "Cables and ratings");

            yield return new GuidePage(Energy, "Batteries and charging", "Robots and the colony store energy in batteries, which prefer mild temperatures.")
                .Add("In play",
                     $"A robot's battery holds {RobotSpecs.BatteryCapacityWh / 1000.0:0.0} kWh (Perseverance's two 43 Ah packs at 28 V). A robot goes back to charge at {RobotSpecs.ReturnToChargeFraction * 100.0:0} % charge.",
                     $"{Rule}A battery charges only between {RobotThermal.BatteryChargeMinC:0} and {RobotThermal.BatteryChargeMaxC:0} °C; a cold robot at a charger is warmed first. Discharging works between {RobotThermal.BatteryDischargeMinC:0} and {RobotThermal.BatteryDischargeMaxC:0} °C.",
                     $"{Rule}Until the surface temperature is measured, colony batteries charge only to {PlanetSimulation.BatteryUnknownTemperatureShare * 100.0:0} %.")
                .Add("How to use", "Build charging stations and battery yards; a depot can have a charging bay so hauls can be staged through it.")
                .Add("Real science", "Lithium plating: charging a lithium-ion cell below 0 °C deposits metallic lithium on the graphite and damages it (Yang et al., PNAS 115, 2018).")
                .Add("Related", "Temperature and the day-night swing");

            yield return new GuidePage(Science, "Air pressure and density", "How thick the air is decides how cables, boiling water and light behave.")
                .Add("In play", "The pressure is only an estimate (a wide range) until the barometer experiment is done; its puzzle fits the readings at several heights.",
                     "Water boils at a temperature set by the pressure, and cannot be liquid below its triple point.")
                .Add("How to use", "Research (T) ► Air pressure ► fit the readings (the physics is shown above the plot).")
                .Add("Real science",
                     "The isothermal barometric formula: p = p0 exp(−h/H), with the scale height H = R T / (M g). The air thins by a factor e every H metres.",
                     $"Water's triple point is {Experiments.WaterTriplePointPa:0.0} Pa; the boiling point follows the Antoine equation.")
                .Add("Related", "Corona and air density", "Temperature and the day-night swing");

            yield return new GuidePage(Science, "Temperature and the day-night swing", "Air temperature has a yearly value and a swing between day and night.")
                .Add("In play",
                     $"{Rule}The day-night amplitude is {PlanetSimulation.DiurnalBaseK:0} K under thick air, rising by up to {PlanetSimulation.DiurnalThinAirK:0} K under thin air. A tidally locked planet has no day, so no swing.",
                     $"A robot works without its heater between {RobotThermal.OperatingMinC:0} and {RobotThermal.OperatingMaxC:0} °C; outside that a heater draws power. A robot with an empty battery out in the weather hibernates and cold-soaks: see Robots and the weather.",
                     "A haul's heater cost follows the temperature over the hours of its legs, so a night haul costs more.")
                .Add("How to use", "Measure the surface temperature (Research, T): the puzzle fits mean, amplitude and phase of a day's readings. Send hauls at the warm part of the day, as the haul text suggests.")
                .Add("Real science",
                     "On Mars, Curiosity's REMS saw daily air temperatures from about −70 °C to 0 °C at Gale crater, a range of about 70 K (JPL, PIA16913). Thin air holds little heat, so the swing is large.",
                     "Electronics are rated −40 to +50 °C on Curiosity (NASA, MSL heat-exchanger paper).")
                .Add("Related", "Batteries and charging", "Air pressure and density");

            yield return new GuidePage(Science, "Tides", "A moon pulls the sea up and down; the shape of a coast can pile the tide higher.")
                .Add("In play", "The tide gauge experiment reads the open-sea tide and the high-tide period; until then the tide is only a range.",
                     $"{Rule}A tidal barrage needs a bay with at least {Tides.BarrageMinRangeM:0} m of tide.")
                .Add("How to use", "Research (T) ► Tide gauge ► fit the amplitude and phase of the sea-level series (the moon's period is known).")
                .Add("Real science", "The equilibrium tide is the sea's response to the moon's pull; its range falls as the cube of the moon's distance and rises with its mass.")
                .Add("Related", "Relief and climbing");

            yield return new GuidePage(Science, "Weather", "Day-to-day weather is noise around the climate: it moves clouds, temperature, pressure and wind.")
                .Add("In play",
                     "Weather is a pure function of the map seed, the place and the time, so it is the same every run and nothing about it is saved. Its long-run mean is the climate the game already had.",
                     $"{Rule}Weather systems are about {WeatherModel.RegionKm:0} km across; the spread and the day-to-day persistence come from NASA data for Earth and are used for other worlds with air (pressure scaled by the planet pressure). A world with almost no air ({WeatherModel.MinPressureEarth * 100.0:0.#} % of Earth or less) has none.",
                     "Where it matters: panels use the day clouds and dust; robots feel the day temperature and wind; cables use the day pressure and temperature; a storm sends robots to shelter.",
                     "A tile weather is measured (●) at the lander, at a Research Station and wherever a robot stands; elsewhere you only have the climate with a range (◐). From a measured today the colony forecasts 1 to 5 days, with a range that grows with lead time.",
                     $"{Rule}Before a loaded haul starts, the planner reads the forecast: if a storm is forecast on the route within the trip (planning one standard deviation toward the worse side, about the 84th percentile), the haul waits and says why; Go anyway overrides only that wait, never the energy check. The leg budget uses the forecast low temperature for today and tomorrow. Where no measurement is within one weather region ({WeatherModel.RegionKm:0} km), the colony only has the climatology and its wide range, so build a Research Station or keep a robot near the route to forecast it. Depot advice is worked out for the coming winter's cold, not today's.")
                .Add("How to use", "Open a tile (tile info, WEATHER) to read it. F6 shows the weather map: white cloud, brown dust, red storm.")
                .Add("Real science",
                     "Daily air temperature, pressure, wind and sunlight from NASA POWER (MERRA-2, 2001 to 2025): the game takes the standard deviation and the one-day autocorrelation of each, by latitude and month. Synoptic weather systems are 1 000 to 2 000 km across (Holton, An Introduction to Dynamic Meteorology).",
                     "A forecast has an error of sigma times the square root of (1 minus rho to the power 2k) at k days, where rho is the one-day autocorrelation: today predicts tomorrow well and next week hardly at all.")
                .Add("Related", "Wind chill and storms", "Mars dust storms", "Temperature and the day-night swing");

            yield return new GuidePage(Science, "Wind chill and storms", "Wind makes a robot heater work harder, and a strong enough wind stops outdoor work.")
                .Add("In play",
                     $"{Rule}A storm day is a day when the wind at a tile is above the local 98th percentile (about 2 days in 100). Robots there do no new outdoor work and go to the nearest shelter; the colony gets an ACT NOW entry in the log.",
                     $"{Rule}The heater cost grows with the wind (capped at {RobotThermal.MaxWindMs:0} m/s). The heater constant is for a {RobotThermal.ReferenceWindMs:0} m/s wind, so the usual weather of a planet costs what it always did and only gusts and storms cost more. In thin air the effect is weaker.",
                     "A robot with an empty battery out in the cold or heat with no shelter hibernates, and is destroyed only when its body leaves its storage range (see Robots and the weather).")
                .Add("How to use", "Keep robots charged and near shelter in storm season; the Power window shows a Storm chain when one reaches the base.")
                .Add("Real science", "The convective heat-transfer coefficient of a body in wind: h = 10.45 - v + 10 sqrt(v) (Siple and Passel 1945), so the loss grows by h(v) / h(0).")
                .Add("Related", "Weather", "Temperature and the day-night swing");

            yield return new GuidePage(Science, "Mars dust storms", "Thin CO2 air has a seasonal pressure cycle and dust that dims the panels, sometimes for months.")
                .Add("In play",
                     $"{Rule}A world with a thin air (from {ThinAirClimate.MinPressureEarth * 100.0:0.#} to {ThinAirClimate.MaxPressureEarth * 100.0:0} % of Earth pressure) gets the Mars-like climate: the pressure follows the CO2 cycle, the dust is heavier in the second half of the year, regional storms come and go in days, and about one year in three a global storm raises the optical depth to between {ThinAirClimate.GlobalPeakMin:0} and {ThinAirClimate.GlobalPeakMax:0}.",
                     "Which years have a global storm is fixed by the map seed and the year number. A dust storm dims panels but does not stop robots.")
                .Add("How to use", "Keep battery storage for a long storm; panels at tau 10 make almost nothing, and the RTG keeps the colony alive.")
                .Add("Real science",
                     $"Viking Lander 1 saw the pressure run from about {ThinAirClimate.MinHPa:0.0} to {ThinAirClimate.MaxHPa:0.0} hPa over the year as CO2 freezes onto and sublimes from the poles (Hess et al. 1980; Tillman et al. 1993). Global dust storms come in about one Mars year in three (Zurek and Martin 1993; Montabone et al. 2015); in 2018 Opportunity measured an optical depth of about 10.8 (NASA/JPL).")
                .Add("Related", "Weather", "Air pressure and density");

            yield return new GuidePage(Science, "Wind turbine physics", "A 100 kW class turbine makes power from the day's wind, the air's density and nothing else.")
                .Add("In play",
                     $"{Rule}One turbine stands on one open land tile (no wake model). Its power follows the manufacturer's power curve: nothing below {WindPower.CutInMs:0} m/s, the full {WindPower.RatedW / 1000.0:0} kW from {WindPower.RatedMs:0} m/s, and a shutdown above {WindPower.CutOutMs:0} m/s. The wind inside a day is not steady, so the day's power is the curve averaged over a Rayleigh distribution of the speed around the day's mean.",
                     "It needs the wind mast experiment first. An idle turbine says why (below cut-in, or above cut-out, or no air).",
                     $"{Rule}It costs the mass of the reference machine: {WindPower.MassKg:0} kg, split into steel, copper and aluminium (the split is an assumption, TBD).")
                .Add("How to use", "Run the wind mast experiment (Research). Put turbines in the windy mid-latitudes of a world with thick air; wire them like panels. On a thin-air world such as Mars a turbine makes almost nothing.")
                .Add("Real science",
                     "Reference machine: the Northern Power NPS 100C-21 (100 kW, rotor 20.7 m, hub 37 m, cut-in 3, rated 15, cut-out 25 m/s, nacelle and rotor 6 500 kg, tower 12 000 kg), manufacturer datasheet 2020. The game reproduces the datasheet's own annual energy table within 5 %.",
                     "P = 1/2 rho A v^3 Cp: the power coefficient Cp of this curve is about 0.39 at 8 m/s, under the Betz limit of 16/27 = 0.593 (Betz 1920). For wind on Mars see Hartwick et al. 2023, Nature Astronomy.")
                .Add("Related", "Wind shear and air density", "Wind chill and storms", "Weather");

            yield return new GuidePage(Science, "Wind shear and air density", "Wind is slower near the ground, and thin air carries less power at the same speed.")
                .Add("In play",
                     $"{Rule}The weather gives the wind at 10 m. A turbine's hub is at {WindPower.HubHeightM:0} m, where the wind is stronger: the 1/7 power law makes it {WindPower.HubWindMs(1.0):0.00} times the 10 m wind. The air density comes from the day's pressure and temperature, rho = p / (R_specific T), and a turbine makes power in proportion to it.",
                     "A cold day at sea level has denser air than a warm one, so the same wind makes more power; a mountain tile has thinner air than a coast.")
                .Add("How to use", "Read the hub wind and the air density in the tile info of a tile with a turbine; the measured values (●) come from the wind mast and the station, the rest is climatology with a range (◐).")
                .Add("Real science",
                     "The neutral boundary layer follows U(z) = U10 (z / 10)^(1/7) (Hsu, Meindl and Gilhousen 1994). Air density of an ideal gas: rho = p / (R T / M): about 1.225 kg/m3 at 15 C and one atmosphere, 0.02 kg/m3 on Mars.")
                .Add("Related", "Wind turbine physics", "Air pressure and density", "Weather");

            yield return new GuidePage(Science, "Robot wrecks and salvage", "A destroyed robot lies where it stopped, and what is left of it depends on how it died.")
                .Add("In play",
                     $"{Rule}A robot destroyed in the cold or with an empty battery leaves its metals whole: iron, rock, copper, aluminium and rare earths can be hauled home. Its circuit boards and cell become electronic waste, a hazardous material. A robot that dies in great heat is worse off: above {WreckModel.SolderC:0} C the boards go to slag, above {WreckModel.AluminiumMeltC:0} C the aluminium, above {WreckModel.CopperMeltC:0} C the copper, above {WreckModel.IronMeltC:0} C the iron and hull, and above {WreckModel.IronBoilC:0} C nothing is left at all.",
                     "The wreck is an open pile: it keeps degrading at the temperature of its tile. A recover haul is planned like any haul (the energy check, the storm wait). Unsynced findings are read back only if the electronics survived; otherwise they are lost.")
                .Add("How to use", "The log tells you the cause and what is left. Slag is not worth a trip. Keep a charged hauler near the base.")
                .Add("Real science", "Melting points (CRC Handbook): Al 660 C, Cu 1085 C, Fe 1538 C, Nd 1024 C; Fe boils at 2862 C. Lead-free solder melts at 217 to 220 C (IPC J-STD-006).")
                .Add("Related", "Storage and corrosion", "Temperature and the day-night swing", "Weather");

            yield return new GuidePage(Science, "Storage and corrosion", "Stored materials change with the air, the damp and the heat unless the store protects them.")
                .Add("In play",
                     $"{Rule}Iron rusts (Fe + O2 + H2O gives FeOOH) at the first-year carbon steel rates of ISO 9223: from under 1.3 micrometres a year in dry air to 80 to 200 in the worst, set by the time the air is wet (RH over 80 %, above freezing) and by sea salt on a coast. Rust weighs more than the iron: it takes oxygen and water from the air. There is no smelter in the game yet, so rust is a loss.",
                     "Sodium and lithium react with moisture (2 Na + 2 H2O gives 2 NaOH + H2): fast in damp air, so they need a sealed store; the hydrogen is vented. Ice melts above 0 C (meltwater runs off an open pile) and sublimates in thin air (Hertz-Knudsen). Salt takes up water above 75 % humidity and drains away as brine. Oxygen and chlorine leak unless they are in a gas tank. Aluminium, copper, rock, slag, anorthosite, alumina and rare earths are stable on game timescales.",
                     "A sealed store or the lander protects against air and damp; a storage yard keeps off rain and runoff only; an open pile is exposed. Every reaction conserves mass: what the air gave and what drained away is in a ledger.")
                .Add("How to use", "Tile info shows what each stored material is losing and whether the humidity is measured (●) or estimated (◐). Put reactive metals and electronic waste in a sealed store.")
                .Add("Real science", "ISO 9223:2012 corrosivity categories C1 to C5; Murphy and Koop 2005 for the vapour pressure of ice; Hertz 1882 and Knudsen 1915 for the sublimation flux; Greenspan 1977 for the 75.3 % deliquescence point of salt; NASA POWER RH2M for humidity (MERRA-2).")
                .Add("Related", "Robot wrecks and salvage", "Weather", "Air pressure and density");

            yield return new GuidePage(Science, "Rescue and the failure ladder", "A robot that runs dry in the field gets help before it is lost; a stuck task climbs a ladder until it needs you.")
                .Add("In play",
                     $"{Rule}A robot with an empty battery outside a charger asks for rescue. In heat or cold it has a deadline: when its battery runs out it hibernates and its body soaks toward the air, which takes tens of hours before it leaves its storage range. The nearest robot that can do it and still come home on its own reserve (the 20 % return charge, at the cold capacity of its battery) helps in whichever way costs less energy. A jump charge carries charge through a cable at {PlanetSimulation.JumpChargeEfficiency * 100.0:0} % efficiency, enough for the robot to drive to a charger; a cold battery is warmed from the cable first. A tow carries the stranded robot, as cargo of about {HaulPhysics.DefaultRobotMassKg:0} kg, to the nearest shelter.",
                     "The ladder: first the colony tries alternatives (another source, staging, waiting out a storm), then it asks a suitable robot for help, and only then does it need you: an ACT NOW entry, a Why? chain and one action. Progress is kept at every step. If nobody can reach the robot before its deadline, the log says why.")
                .Add("How to use", "In the Robots window a stranded robot shows its deadline and the ladder state; the Rescue button asks for help at once. A charger or battery yard near where robots work shortens every rescue. Task priorities (high, normal, low) per robot or group are in the Robots window.")
                .Add("Real science", "DC-DC converters run at 90 to 95 % efficiency (Erickson and Maksimovic, Fundamentals of Power Electronics); a lithium-ion cell cannot be charged below 0 C without plating, so a cold battery is warmed first.")
                .Add("Related", "Robot wrecks and salvage", "Temperature and the day-night swing", "Weather");

            yield return new GuidePage(Science, "Robots and the weather", "A robot is safe while it has energy; with none, it cools or warms like a lump of metal.")
                .Add("In play",
                     $"{Rule}With energy a robot holds its body at its operating limit with a heater or cooler ({RobotThermal.HeaterWPerK:0} W per kelvin outside {RobotThermal.OperatingMinC:0} to {RobotThermal.OperatingMaxC:0} C). With an empty battery outside shelter it hibernates: no heater, and its body follows the air with the time constant tau = m cp / G, about {RobotThermal.BodyTauHours():0} hours for a robot of 642 kg (mass-weighted specific heat about 0.58 kJ/kg K, G = {RobotThermal.HeaterWPerK:0} W/K).",
                     $"It is destroyed only when its body leaves the storage range, {RobotThermal.StorageMinC:0} to {RobotThermal.StorageMaxC:0} C (the narrower of the electronics' and the cell's), so a Mars night does not kill an empty robot at once: it gives a rescue most of a day. The wreck's cause is cold soak or heat soak.")
                .Add("How to use", "Keep robots charged and near shelter; in the cold a robot with an empty battery has hours, not minutes, for help to arrive (Rescue and the failure ladder).")
                .Add("Real science", "Lumped thermal capacitance: T(t) = T_air + (T0 - T_air) exp(-t / tau), tau = m cp / (h A) (Incropera and DeWitt, Fundamentals of Heat and Mass Transfer, ch. 5). Specific heats from the CRC Handbook. Electronics storage rating -55 to +125 C (MIL-STD-883 class B parts); lithium iron phosphate cells store between -40 and +60 C (A123 Systems ANR26650M1-B datasheet).")
                .Add("Related", "Rescue and the failure ladder", "Temperature and the day-night swing", "Wind chill and storms");

            yield return new GuidePage(Science, "Ore grade and tailings", "A deposit holds a finite tonne count at a real ore grade, and the grade falls as the rich ore is taken.")
                .Add("In play",
                     $"{Rule}Each deposit holds a finite amount (copper {DepositMap.ReserveKgAtGrade1(Material.Copper) / 1000.0:0} t, lithium {DepositMap.ReserveKgAtGrade1(Material.Lithium) / 1000.0:0} t, rare earths {DepositMap.ReserveKgAtGrade1(Material.RareEarth) / 1000.0:0} t at a tile's best), a share of a typical deposit scaled to one tile. The ore grade is the kg of product per kg of rock: copper {DepositMap.TypicalOreGrade(Material.Copper) * 100.0:0.#} %, iron {DepositMap.TypicalOreGrade(Material.Iron) * 100.0:0} %, lithium {DepositMap.TypicalOreGrade(Material.Lithium) * 100.0:0.#} %, rare earths {DepositMap.TypicalOreGrade(Material.RareEarth) * 100.0:0} %. The rest of the rock moved is tailings, so a poor ore costs more robot hours per kilogram.",
                     "Miners take the rich ore first: the grade falls to half by the time the deposit is nearly empty. A tile shows its grade, the rate and the share left. Both the tonnage and the grade are a range until the ore assay measures them.")
                .Add("How to use", "Tile info, Mine: read the grade and the share left; do an ore assay to turn the range into a measurement. When a deposit runs out its mine stops with a log entry; survey further out.")
                .Add("Real science", "Typical sizes and grades come from the USGS deposit models (Singer, Berger and Moring 2008, porphyry copper grade-tonnage; the 1e-4 share per tile is a game assumption). Mining a deposit lowers its mean grade because the richest part is taken first (the usual cut-off grade effect); the 0.5 to 1 curve is a game rule.")
                .Add("Related", "Iron electrolysis", "Storage and corrosion");

            yield return new GuidePage(Science, "Iron electrolysis", "Rust goes back to iron with electricity alone: molten oxide electrolysis, with oxygen as the only waste.")
                .Add("In play",
                     $"{Rule}An Electrolysis cell next to a Workshop can run Iron from rust: {Refining.Of(RefineProcess.IronFromRust).InputKg:0.0} kg of rust gives {Refining.Of(RefineProcess.IronFromRust).OutputKg:0} kg of iron, with {Refining.OxygenPerRust:0.000} kg of oxygen and {Refining.WaterPerRust:0.000} kg of steam per kg of rust, by stoichiometry (FeOOH is 88.85 g/mol, iron 55.845). It takes about {Refining.IronWhPerKg / 1000.0:0.#} kWh per kg of iron and a batch of {Refining.Of(RefineProcess.IronFromRust).Hours:0} hours.",
                     "Rust comes from iron that rusted in storage (Storage and corrosion). Mined iron, an abstraction of ore, is unchanged: the cell is a way to win back what the damp took.")
                .Add("How to use", "Keep iron in a sealed store and it never rusts; where it did, put the rust in the lander and order Iron from rust in Manufacturing, Refining.")
                .Add("Real science", "Molten oxide electrolysis with an inert chromium-iron alloy anode makes iron and oxygen from iron oxide at about 1 600 C (Allanore, Yin and Sadoway 2013, Nature 497: 353-356). The energy at scale is estimated at 3.9 to 4 kWh per kg of iron (Allanore 2017, ECS Interface 26(2)).")
                .Add("Related", "Storage and corrosion", "Robot wrecks and salvage");

            yield return new GuidePage(Science, "Rebuilding robots", "A robot that died of cold or of long heat, not of fire, can be put back together at the Workshop.")
                .Add("In play",
                     $"{Rule}Recover the wreck first (a haul brings its metals home). If its structure survived (a cold or heat soak below {WreckModel.AluminiumMeltC:0} C), the Workshop rebuilds it in 6 hours and 5 kWh, from the recovered metals and a new cell and boards (about 2 kg of lithium and 10 kg of copper). The robot comes back with a new id and the findings that were read back from the wreck.",
                     "A wreck that melted (aluminium at 660 C, copper at 1085 C, iron at 1538 C) or whose electronics burnt gives only its salvaged metals. The Manufacturing window says why a wreck cannot be rebuilt: the electronics melted, no Workshop, or the copper it needs.")
                .Add("How to use", "Manufacturing, Robots tab: REBUILD FROM WRECKS lists each recovered wreck with a Rebuild button or the reason it cannot be rebuilt.")
                .Add("Real science", "Melting points (CRC Handbook); the electronics share is the cell plus a quarter of the wiring (game assumption).")
                .Add("Related", "Robot wrecks and salvage", "Robots and the weather");

            yield return new GuidePage(Science, "Relief and climbing", "How high the land rises decides what a haul costs a robot.")
                .Add("In play",
                     "Until the terrain relief survey is done, the highest point is only a range, and hauls are planned for the high end.",
                     $"{Rule}Climbing costs {HaulPhysics.ClimbEffortShare * 100.0:0.#} % of m·g·Δh (uphill only, no regeneration).")
                .Add("How to use", "Research (T) ► Terrain relief survey ► set the spike threshold, then read the highest point. Contour routes go round climbs when that is cheaper.")
                .Add("Real science",
                     $"Earth's relief runs about {ReliefScale.EarthMinM / 1000.0:0.#} to +{ReliefScale.EarthMaxM / 1000.0:0.#} km; Mars's {ReliefScale.MarsMinM / 1000.0:0.#} to +{ReliefScale.MarsMaxM / 1000.0:0.#} km (MOLA, Smith et al. 1999). Other worlds scale Earth's by 1/g, an assumption.",
                     "Lifting a mass m through a height Δh takes m g Δh of energy.")
                .Add("Related", "Tides", "Temperature and the day-night swing");
        }
    }
}
