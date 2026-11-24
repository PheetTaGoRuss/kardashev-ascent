using System.Collections.Generic;
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
                     $"A robot works without its heater between {RobotThermal.OperatingMinC:0} and {RobotThermal.OperatingMaxC:0} °C; outside that a heater draws power, and a robot with an empty battery out in the weather is destroyed.",
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
