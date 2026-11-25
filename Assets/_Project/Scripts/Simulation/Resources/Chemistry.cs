using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Resources
{
    /// <summary>How a stock is kept (M3, decided 2026-11-25): what the building around it protects it from.</summary>
    public enum Shelter
    {
        /// <summary>A pile in the open: air, damp, rain and runoff (also a wreck).</summary>
        OpenPile,
        /// <summary>A storage yard: roofed, so rain and runoff are kept off, but the air and its damp still reach it (assumption).</summary>
        Yard,
        /// <summary>A sealed store, a gas tank or the lander's hold: cut off from the air and the damp.</summary>
        Sealed,
    }

    /// <summary>The air and ground a stock sits in.</summary>
    public readonly struct ChemEnvironment
    {
        public readonly double TempC, RelativeHumidityPercent, PressurePa, OxygenFraction;
        /// <summary>A coast tile: sea salt in the air (ISO 9223 chloride class S1 or above).</summary>
        public readonly bool Coast;
        public ChemEnvironment(double tempC, double rh, double pressurePa, double oxygenFraction, bool coast)
        { TempC = tempC; RelativeHumidityPercent = Math.Max(0.0, Math.Min(100.0, rh)); PressurePa = Math.Max(0.0, pressurePa); OxygenFraction = Math.Max(0.0, Math.Min(1.0, oxygenFraction)); Coast = coast; }
    }

    /// <summary>What one stock loses and gains in a step: the new masses (kg of each material) and the exchange with the environment, all in kg.</summary>
    public sealed class ChemResult
    {
        /// <summary>Change of the stock by material, kg (negative: consumed or lost; positive: made).</summary>
        public readonly Dictionary<Material, double> Delta = new Dictionary<Material, double>();
        /// <summary>Taken from the air (oxygen, water vapour), kg.</summary>
        public double OxygenFromAir, WaterFromAir;
        /// <summary>Hydrogen vented to the air (sodium and lithium with water), kg.</summary>
        public double HydrogenVented;
        /// <summary>Lost by draining away or leaking: melt water, brine, a gas, kg (by material).</summary>
        public readonly Dictionary<Material, double> Drained = new Dictionary<Material, double>();
        /// <summary>Water that sublimated into the air from ice, kg.</summary>
        public double WaterSublimated;
        public void Add(Material m, double kg) { if (Math.Abs(kg) > 1e-15) Delta[m] = (Delta.TryGetValue(m, out double v) ? v : 0.0) + kg; }
        public void Drain(Material m, double kg) { if (kg > 1e-15) Drained[m] = (Drained.TryGetValue(m, out double v) ? v : 0.0) + kg; }
        /// <summary>Mass balance: the change of the stock plus what drained, vented and sublimated, less what came from the air, is zero.</summary>
        public double Imbalance()
        {
            double d = 0.0; foreach (var kv in Delta) d += kv.Value;
            double drained = 0.0; foreach (var kv in Drained) drained += kv.Value;
            return d + drained + HydrogenVented + WaterSublimated - OxygenFromAir - WaterFromAir;
        }
    }

    /// <summary>
    /// What the weather and the air do to stored materials (decided 2026-11-25: "material can be destroyed or changed by the environment if not stored correctly, based on real
    /// chemical reactions"). A pure model: <see cref="Step"/> gives the change of a stock of one material in a time step. Every reaction conserves mass and the exchange with the
    /// environment is returned so a ledger can close. Rates marked game rule or assumption are TBD.
    /// </summary>
    public static class Chemistry
    {
        // ---- iron: atmospheric corrosion (ISO 9223:2012 corrosivity categories) ----

        /// <summary>
        /// Carbon steel first-year corrosion rate by corrosivity category, micrometres a year: C1 up to 1.3, C2 1.3 to 25, C3 25 to 50, C4 50 to 80, C5 80 to 200 (ISO 9223:2012, Table 2);
        /// the middle of each band is used (C1 0.7).
        /// </summary>
        public static readonly double[] SteelRateUmPerYear = { 0.7, 13.0, 37.5, 65.0, 140.0 };

        /// <summary>
        /// The corrosivity category (0 for C1 to 4 for C5) from the time of wetness, the share of the year with RH over 80 % and T over 0 C (ISO 9223 time-of-wetness classes tau1 to tau5:
        /// under 10 h a year, 10 to 250, 250 to 2 500, 2 500 to 5 500, over 5 500 hours), with chloride from the coast raising it by one class (ISO 9223 salinity classes S0 to S3; game rule: coast = one class).
        /// With low-pollution inland air the time-of-wetness classes map to C1, C2, C3, C4 and C4 (ISO 9223 Annex C, simplified).
        /// </summary>
        public static int CorrosivityCategory(double wetShareOfYear, bool coast)
        {
            double h = Math.Max(0.0, Math.Min(1.0, wetShareOfYear)) * 8760.0;
            int cat = h < 10.0 ? 0 : h < 250.0 ? 1 : h < 2500.0 ? 2 : h < 5500.0 ? 3 : 4;
            if (coast) cat = Math.Min(4, cat + 1);
            return cat;
        }

        /// <summary>The share of the time the air is wet (RH over 80 %) for a mean and a spread of the relative humidity, percent (a normal distribution of the daily value), zero at or below freezing.</summary>
        public static double WetShare(double meanRhPercent, double sdPercent, double tempC)
        {
            if (tempC <= 0.0) return 0.0;
            double sd = Math.Max(1.0, sdPercent), z = (80.0 - meanRhPercent) / sd;
            return 0.5 * (1.0 - Erf(z / Math.Sqrt(2.0)));
        }

        static double Erf(double x)
        {
            double t = 1.0 / (1.0 + 0.3275911 * Math.Abs(x));
            double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return x >= 0 ? y : -y;
        }

        /// <summary>Surface area of a stored pile of iron, m2 per kg (bar, plate and scrap; assumption, TBD).</summary>
        public const double IronAreaM2PerKg = 0.05;
        public const double IronDensity = 7874.0, FeOOHPerFe = (55.845 + 2 * 15.999 + 1.008) / 55.845;
        /// <summary>Oxygen and water taken per kg of iron: Fe + 3/4 O2 + 1/2 H2O gives FeOOH.</summary>
        public static readonly double OxygenPerFe = 0.75 * 31.998 / 55.845, WaterPerFe = 0.5 * 18.015 / 55.845;

        /// <summary>The first-year steel corrosion rate, share of the mass a year, for a surface of area per kg in a corrosivity category.</summary>
        public static double IronLossPerYear(int category, double areaM2PerKg) => SteelRateUmPerYear[Math.Max(0, Math.Min(4, category))] * 1e-6 * IronDensity * areaM2PerKg;

        /// <summary>Saturation vapour pressure of water, Pa: Buck (1981) above 0 C, the ice formula below.</summary>
        public static double WaterVapourPressurePa(double tempC) =>
            tempC >= 0.0 ? 611.21 * Math.Exp((18.678 - tempC / 234.5) * (tempC / (257.14 + tempC))) : IceVapourPressurePa(tempC + 273.15);

        /// <summary>How much water vapour the air holds against saturation at 20 C (2 339 Pa): relative humidity times the vapour pressure it is a share of. Dry cold air, as on Mars, is almost nothing.</summary>
        public static double VapourFactor(double tempC, double rhPercent) => Math.Min(1.5, rhPercent / 100.0 * WaterVapourPressurePa(tempC) / 2339.0);

        // ---- sodium and lithium: reaction with moisture ----

        /// <summary>
        /// 2 Na + 2 H2O gives 2 NaOH + H2 and 2 Li + 2 H2O gives 2 LiOH + H2 (the alkali metals react with water; lithium more slowly, and it also takes up N2 to Li3N, ignored here).
        /// Rates, share of the metal a day in air saturated at 20 C: sodium 0.5, lithium 0.1, proportional to the water vapour in the air (game rule, TBD; a clean sodium surface tarnishes in minutes in damp air).
        /// </summary>
        public const double SodiumPerDayAtSaturation = 0.5, LithiumPerDayAtSaturation = 0.1;
        static readonly double NaOHPerNa = (22.990 + 15.999 + 1.008) / 22.990, LiOHPerLi = (6.941 + 15.999 + 1.008) / 6.941;
        static readonly double WaterPerNa = 18.015 / 22.990, HydrogenPerNa = 1.008 / 22.990;   // 2 Na + 2 H2O gives 2 NaOH + H2: per Na, one H2O in and half an H2 out (1.008 per mole of Na)      // per kg of metal: water taken and hydrogen given off
        static readonly double WaterPerLi = 18.015 / 6.941, HydrogenPerLi = 1.008 / 6.941;

        // ---- ice: melting and sublimation ----

        /// <summary>Latent heat of fusion 334 kJ/kg and of sublimation 2 834 kJ/kg of ice (CRC Handbook).</summary>
        public const double FusionJPerKg = 334_000.0, SublimationJPerKg = 2_834_000.0;
        /// <summary>Surface of an ice pile, m2 per kg (assumption, TBD) and the convective heat-transfer coefficient of still air, W/m2 K (natural convection, typical 5 to 25).</summary>
        public const double IceAreaM2PerKg = 0.005, ConvectionWPerM2K = 10.0;

        /// <summary>Saturation vapour pressure over ice, Pa (Murphy and Koop 2005, Q. J. R. Meteorol. Soc. 131: ln p = 9.550426 - 5723.265 / T + 3.53068 ln T - 0.00728332 T).</summary>
        public static double IceVapourPressurePa(double tempK) =>
            tempK <= 0.0 ? 0.0 : Math.Exp(9.550426 - 5723.265 / tempK + 3.53068 * Math.Log(tempK) - 0.00728332 * tempK);

        /// <summary>
        /// Hertz-Knudsen sublimation flux into a vacuum, kg per m2 per s: J = alpha (p_sat - p_vapour) / sqrt(2 pi R T / M), with alpha = 1 (Hertz 1882, Knudsen 1915; Schrage correction ignored).
        /// In air the flux is limited by diffusion of the vapour away through the gas; the game damps it by 1 / (1 + P / 0.02 Pa) (game rule, TBD, chosen so exposed ice on Mars loses millimetres a month when warm, as Phoenix saw), and caps it by the heat the surface can supply, 100 W/m2 over the latent heat of sublimation.
        /// </summary>
        public static double SublimationFlux(double tempC, double rhPercent, double pressurePa)
        {
            double tK = tempC + 273.15;
            double dp = IceVapourPressurePa(tK) * (1.0 - Math.Min(1.0, rhPercent / 100.0));
            if (dp <= 0.0) return 0.0;
            double j = dp / Math.Sqrt(2.0 * Math.PI * 8.314462618 * tK / 0.01801528);
            return Math.Min(j / (1.0 + pressurePa / 0.02), 100.0 / SublimationJPerKg);
        }

        // ---- salt: deliquescence ----

        /// <summary>Sodium chloride deliquesces above 75.3 % relative humidity at 25 C (Greenspan 1977, J. Res. NBS 81A); it is almost flat in temperature. Game rule: an open pile loses 0.3 % a day per 10 points of RH above it, as brine that drains away.</summary>
        public const double SaltDeliquescenceRhPercent = 75.3, SaltBrinePerDayPer10Points = 0.003;

        // ---- the step ----

        /// <summary>
        /// The change of <paramref name="kg"/> of <paramref name="m"/> kept in <paramref name="shelter"/> in the environment over <paramref name="days"/> days.
        /// <paramref name="wetShare"/> is the share of the year the air is wet, for iron. Materials with nothing to say (rock, slag, anorthosite, alumina, rare earths, aluminium) are stable.
        /// </summary>
        public static ChemResult Step(Material m, double kg, Shelter shelter, ChemEnvironment env, double wetShare, double days)
        {
            var r = new ChemResult();
            if (kg <= 0.0 || days <= 0.0) return r;
            double air = shelter == Shelter.Sealed ? 0.002 : 1.0;   // a seal leaks a little (game rule, TBD): 0.2 % of the open exchange
            double humid = VapourFactor(env.TempC, env.RelativeHumidityPercent);
            double o2 = Math.Min(1.0, env.OxygenFraction * (env.PressurePa / 101325.0) / 0.2095);   // the oxygen there is, against Earth's air
            switch (m)
            {
                case Material.Iron:
                {
                    int cat = CorrosivityCategory(wetShare, env.Coast);
                    double rate = IronLossPerYear(cat, IronAreaM2PerKg) * (shelter == Shelter.Yard ? 0.6 : 1.0) * air * o2;   // a yard is out of the rain (assumption: 0.6)
                    double lost = kg * Math.Min(1.0, rate * days / 365.25);
                    if (lost <= 0.0) break;
                    r.Add(Material.Iron, -lost); r.Add(Material.Rust, lost * FeOOHPerFe);
                    r.OxygenFromAir = lost * OxygenPerFe; r.WaterFromAir = lost * WaterPerFe;
                    break;
                }
                case Material.Sodium:
                case Material.Lithium:
                {
                    bool na = m == Material.Sodium;
                    double rate = (na ? SodiumPerDayAtSaturation : LithiumPerDayAtSaturation) * humid * air;
                    double lost = kg * (1.0 - Math.Exp(-rate * days));
                    if (lost <= 0.0) break;
                    r.Add(m, -lost); r.Add(na ? Material.SodiumHydroxide : Material.LithiumHydroxide, lost * (na ? NaOHPerNa : LiOHPerLi));
                    r.WaterFromAir = lost * (na ? WaterPerNa : WaterPerLi); r.HydrogenVented = lost * (na ? HydrogenPerNa : HydrogenPerLi);
                    break;
                }
                case Material.Water:
                {
                    if (shelter == Shelter.Sealed) break;   // a tank keeps it, as ice or as liquid
                    double melt = 0.0;
                    if (env.TempC > 0.0) melt = Math.Min(kg, kg * Math.Min(1.0, ConvectionWPerM2K * IceAreaM2PerKg * env.TempC * 86400.0 / FusionJPerKg) * days);
                    if (shelter == Shelter.OpenPile && melt > 0.0) { r.Add(Material.Water, -melt); r.Drain(Material.Water, melt); }   // the melt runs off an open pile
                    double sub = Math.Min(kg - melt, kg * Math.Min(0.5, SublimationFlux(Math.Min(env.TempC, 0.0), env.RelativeHumidityPercent, env.PressurePa) * IceAreaM2PerKg * 86400.0) * days);
                    if (sub > 0.0) { r.Add(Material.Water, -sub); r.WaterSublimated = sub; }
                    break;
                }
                case Material.Salt:
                {
                    if (shelter != Shelter.OpenPile || env.RelativeHumidityPercent <= SaltDeliquescenceRhPercent) break;
                    double lost = kg * Math.Min(1.0, SaltBrinePerDayPer10Points * (env.RelativeHumidityPercent - SaltDeliquescenceRhPercent) / 10.0 * days);
                    r.Add(Material.Salt, -lost); r.Drain(Material.Salt, lost);   // the brine drains away
                    break;
                }
                case Material.Oxygen:
                case Material.Chlorine:
                    if (shelter != Shelter.Sealed) { double lost = kg; r.Add(m, -lost); r.Drain(m, lost); }   // a gas outside a tank leaks to the air
                    break;
            }
            return r;
        }

        /// <summary>One line for the tile panel and the Guide: what happens to this material kept like this, or null when it is stable.</summary>
        public static string Describe(Material m, Shelter shelter)
        {
            switch (m)
            {
                case Material.Iron: return "rusts in damp air (Fe + O2 + H2O gives FeOOH, ISO 9223 rates); a sealed store stops it";
                case Material.Copper: return "stable: its patina is under 6 micrometres a year even in the worst air";
                case Material.Aluminium:
                case Material.PureAluminium: return "stable (oxide layer)";
                case Material.Sodium: return "reacts with moisture (2 Na + 2 H2O gives 2 NaOH + H2): needs a sealed store";
                case Material.Lithium: return "reacts with moisture (gives LiOH): needs a sealed store";
                case Material.Water: return shelter == Shelter.Sealed ? "kept in a tank" : "melts above 0 C and runs off an open pile; sublimates in thin air";
                case Material.Salt: return "takes up water above 75 % humidity and drains away as brine from an open pile";
                case Material.Oxygen:
                case Material.Chlorine: return "leaks to the air unless it is in a gas tank";
                case Material.Rock: case Material.Slag: case Material.Anorthosite: case Material.Alumina: case Material.RareEarth: return "stable on game timescales";
                default: return null;
            }
        }
    }
}
