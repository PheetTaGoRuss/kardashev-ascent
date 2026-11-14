using System;
using System.Collections.Generic;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>Where a planet value comes from (tools/exoplanets/README.md). The game shows it to the player.</summary>
    public enum ValueOrigin
    {
        /// <summary>From the NASA Exoplanet Archive, only converted to SI units.</summary>
        Nasa,
        /// <summary>Calculated from archive values with a physics formula.</summary>
        Derived,
        /// <summary>Not measurable for this planet yet: a game value.</summary>
        Assumed,
        /// <summary>No provenance entry for the field.</summary>
        Unknown,
    }

    /// <summary>Is there ground to build on, from the planet's radius (Rogers 2015; Fulton et al. 2017).</summary>
    public enum SurfaceKind { Rocky, Uncertain, Gaseous }

    /// <summary>What weather the panels see.</summary>
    public enum CloudModel
    {
        /// <summary>Clear sky every day (cloud cover is unknown for exoplanets).</summary>
        None,
        /// <summary>Earth's cloud climatology from NASA POWER, by latitude and month (Earth-like planets only).</summary>
        EarthClimatology,
    }

    /// <summary>
    /// One planet as written by tools/exoplanets/fetch_exoplanets.py into Assets/_Project/Data/Planets/planets.json.
    /// Field names match the JSON so Unity's JsonUtility can read it straight in; values are SI, angles in degrees.
    /// </summary>
    [Serializable]
    public sealed class PlanetRecord
    {
        public string id = "";
        public string displayName = "";
        public string archiveName = "";
        public string source = "";
        public string retrieved = "";
        public SolarRecord solar = new SolarRecord();
        public StarRecord star = new StarRecord();
        public BodyRecord planet = new BodyRecord();
        public FlagsRecord flags = new FlagsRecord();
        public LookRecord look = new LookRecord();
        public ChemistryRecord chemistry = new ChemistryRecord();
        public List<ProvenanceRecord> provenance = new List<ProvenanceRecord>();

        /// <summary>What the planet is made of, for resources (geology_resources.md claim 10).</summary>
        [Serializable]
        public sealed class ChemistryRecord
        {
            /// <summary>Host star metallicity [Fe/H] in dex (archive st_met; 0 = solar when unknown).</summary>
            public double metallicityDex;
            /// <summary>Planet iron mass fraction (0.32 Earth-like when not measured).</summary>
            public double ironMassFraction = 0.32;
        }

        /// <summary>Chemistry for the deposit rules: metals from the star, iron measured or assumed.</summary>
        public PlanetChemistry ToChemistry() =>
            new PlanetChemistry(chemistry?.metallicityDex ?? 0.0, chemistry?.ironMassFraction ?? 0.32,
                                          OriginOf("chemistry.ironMassFraction") == ValueOrigin.Derived, HasLiquidWater);

        [Serializable]
        public sealed class LookRecord
        {
            /// <summary>Noise seed for the continents (game value; Viridia keeps 7, the editor default).</summary>
            public int terrainSeed = 7;
            /// <summary>"liquid" (seas, frozen where cold) or "none" (dry basins). Not measurable yet: assumed.</summary>
            public string water = "liquid";
            /// <summary>Share of the surface in low basins (seas or dry basins).</summary>
            public double oceanFraction = 0.62;
        }

        /// <summary>Seas on the surface (frozen where it is cold), or none.</summary>
        public bool HasLiquidWater => look == null || !string.Equals(look.water, "none", StringComparison.OrdinalIgnoreCase);

        /// <summary>How the planet's surface is generated: its own continents, water or not, and ice from its air temperature.</summary>
        public SurfaceSettings ToSurfaceSettings() =>
            SurfaceSettings.ForClimate(look?.terrainSeed ?? 7, solar.airTemperatureC, flags.tidallyLocked,
                                       HasLiquidWater, look != null && look.oceanFraction > 0.0 ? look.oceanFraction : 0.62);

        [Serializable]
        public sealed class SolarRecord
        {
            public double luminosityW = SolarConstants.SolarLuminosity;
            public double semiMajorAxisM = SolarConstants.AstronomicalUnit;
            public double eccentricity;
            public double yearLengthHours = 365.256 * 24.0;
            /// <summary>Solar day; 0 on a tidally locked planet (no day cycle).</summary>
            public double dayLengthHours = 24.0;
            public double obliquityDeg;
            public double perihelionLongitudeDeg;
            public double opticalDepth = 0.32;
            public double diffuseK = 0.3;
            public double airTemperatureC = 15.0;
            public double surfacePressureEarth = 1.0;
        }

        [Serializable]
        public sealed class StarRecord
        {
            public double teffK = 5772.0;
            public double radiusM;
            public double massKg;
            public string spectralType = "";
            public int count = 1;
        }

        [Serializable]
        public sealed class BodyRecord
        {
            public double insolationWm2;
            public double radiusM = 6.371e6;
            public double massKg;
            public double surfaceGravityMs2;
            public double escapeVelocityMs;
            public double bondAlbedo = 0.3;
            public double equilibriumTempK;
        }

        [Serializable]
        public sealed class FlagsRecord
        {
            public bool tidallyLocked;
            public bool circumbinary;
            /// <summary>"rocky", "uncertain" or "gaseous".</summary>
            public string surface = "rocky";
            /// <summary>"earth" or "none".</summary>
            public string clouds = "none";
        }

        [Serializable]
        public sealed class ProvenanceRecord
        {
            /// <summary>JSON path such as "solar.luminosityW".</summary>
            public string field = "";
            /// <summary>"nasa", "derived" or "assumed".</summary>
            public string origin = "";
            public string note = "";
        }

        /// <summary>The values the solar model needs (angles converted to radians).</summary>
        public PlanetSolarParams ToSolarParams()
        {
            const double deg = Math.PI / 180.0;
            return new PlanetSolarParams(
                solar.luminosityW, solar.semiMajorAxisM, solar.eccentricity, solar.obliquityDeg * deg,
                solar.perihelionLongitudeDeg * deg, flags.tidallyLocked ? 0.0 : solar.dayLengthHours,
                solar.yearLengthHours, solar.opticalDepth, solar.diffuseK, solar.airTemperatureC,
                flags.tidallyLocked, solar.surfacePressureEarth > 0.0 ? solar.surfacePressureEarth : 1.0);
        }

        public SurfaceKind Surface =>
            string.Equals(flags.surface, "gaseous", StringComparison.OrdinalIgnoreCase) ? SurfaceKind.Gaseous
            : string.Equals(flags.surface, "uncertain", StringComparison.OrdinalIgnoreCase) ? SurfaceKind.Uncertain
            : SurfaceKind.Rocky;

        public CloudModel Clouds =>
            string.Equals(flags.clouds, "earth", StringComparison.OrdinalIgnoreCase) ? CloudModel.EarthClimatology : CloudModel.None;

        /// <summary>Starlight at the planet's mean distance, in multiples of Earth's (S⊕ = 1361 W/m²).</summary>
        public double InsolationEarth =>
            Orbit.ExtraterrestrialIrradiance(solar.luminosityW, solar.semiMajorAxisM) / Civilization.Kardashev.EarthIrradiance;

        /// <summary>Planet radius in Earth radii.</summary>
        public double RadiusEarth => planet.radiusM / Civilization.Kardashev.EarthRadiusM;

        /// <summary>
        /// Why the colony cannot land here, or null if it can. A gas giant has no ground for panels; the game does
        /// not yet simulate light from two stars on the ground (BinaryStar exists but the colony uses one star).
        /// </summary>
        public string WhyNotPlayable
        {
            get
            {
                if (Surface == SurfaceKind.Gaseous)
                    return $"No solid surface: at {RadiusEarth:0.#} Earth radii it is almost certainly a gas planet.";
                if (flags.circumbinary)
                    return "Orbits two stars; the colony model uses one star so far.";
                if (solar.luminosityW <= 0.0 || solar.semiMajorAxisM <= 0.0 || solar.yearLengthHours <= 0.0)
                    return "Star or orbit values are missing.";
                if (!flags.tidallyLocked && solar.dayLengthHours <= 0.0)
                    return "Day length is missing.";
                return null;
            }
        }

        public bool IsPlayable => WhyNotPlayable == null;

        /// <summary>Where a JSON field (such as "solar.luminosityW") comes from.</summary>
        public ValueOrigin OriginOf(string field)
        {
            ProvenanceRecord p = Find(field);
            if (p == null) return ValueOrigin.Unknown;
            switch (p.origin)
            {
                case "nasa": return ValueOrigin.Nasa;
                case "derived": return ValueOrigin.Derived;
                case "assumed": return ValueOrigin.Assumed;
                default: return ValueOrigin.Unknown;
            }
        }

        /// <summary>The provenance note for a field, or "".</summary>
        public string NoteOf(string field) => Find(field)?.note ?? "";

        ProvenanceRecord Find(string field)
        {
            if (provenance == null) return null;
            foreach (ProvenanceRecord p in provenance)
                if (p != null && p.field == field)
                    return p;
            return null;
        }
    }

    /// <summary>The whole planets.json file.</summary>
    [Serializable]
    public sealed class PlanetCatalogRecord
    {
        public string generatedBy = "";
        public string archive = "";
        public string retrieved = "";
        public List<PlanetRecord> planets = new List<PlanetRecord>();
    }
}
