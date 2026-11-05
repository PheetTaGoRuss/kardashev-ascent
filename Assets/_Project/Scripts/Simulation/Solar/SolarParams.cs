namespace KardashevAscent.Simulation.Solar
{
    /// <summary>The planet values the solar model needs. Angles in radians.</summary>
    public readonly struct PlanetSolarParams
    {
        /// <summary>Star luminosity, W.</summary>
        public readonly double Luminosity;
        /// <summary>Orbit semi-major axis, m.</summary>
        public readonly double SemiMajorAxis;
        public readonly double Eccentricity;
        /// <summary>Axial tilt ε.</summary>
        public readonly double Obliquity;
        /// <summary>Star's ecliptic longitude at periapsis (equinox → periapsis), so λ = ν + this. Earth ≈ 283°.</summary>
        public readonly double PerihelionLongitude;
        /// <summary>Solar day, hours.</summary>
        public readonly double DayLengthHours;
        /// <summary>Orbital period, hours.</summary>
        public readonly double YearLengthHours;
        /// <summary>Broadband optical depth τ of the atmosphere.</summary>
        public readonly double OpticalDepth;
        /// <summary>k in the game's diffuse-light model (≈ 0.3–0.5).</summary>
        public readonly double DiffuseK;
        /// <summary>Air temperature used for cell heating, °C (constant for now).</summary>
        public readonly double AirTemperatureC;

        public PlanetSolarParams(double luminosity, double semiMajorAxis, double eccentricity, double obliquity,
                                 double perihelionLongitude, double dayLengthHours, double yearLengthHours,
                                 double opticalDepth, double diffuseK, double airTemperatureC)
        {
            Luminosity = luminosity;
            SemiMajorAxis = semiMajorAxis;
            Eccentricity = eccentricity;
            Obliquity = obliquity;
            PerihelionLongitude = perihelionLongitude;
            DayLengthHours = dayLengthHours;
            YearLengthHours = yearLengthHours;
            OpticalDepth = opticalDepth;
            DiffuseK = diffuseK;
            AirTemperatureC = airTemperatureC;
        }
    }

    /// <summary>One panel and where it stands. Angles in radians; azimuth clockwise from north.</summary>
    public readonly struct PanelParams
    {
        public readonly double Latitude;
        public readonly double Tilt;
        public readonly double Azimuth;
        /// <summary>m².</summary>
        public readonly double Area;
        /// <summary>Efficiency at 25 °C cell temperature.</summary>
        public readonly double Efficiency;
        /// <summary>Nominal operating cell temperature, °C.</summary>
        public readonly double Noct;
        /// <summary>Power change per °C above 25 °C (silicon ≈ −0.004).</summary>
        public readonly double TempCoefficient;
        /// <summary>Ground reflectance ρ_g.</summary>
        public readonly double Albedo;
        /// <summary>p/p₀ at the panel's altitude.</summary>
        public readonly double PressureRatio;
        /// <summary>How well the star's spectrum suits the cell material (1 = perfect).</summary>
        public readonly double FSpec;
        /// <summary>Fraction of light blocked by dust (0 = clean).</summary>
        public readonly double FDust;
        /// <summary>Direct-light factor: 0 in shadow, 1 in full sun.</summary>
        public readonly double FShade;

        public PanelParams(double latitude, double tilt, double azimuth, double area, double efficiency,
                           double noct, double tempCoefficient, double albedo, double pressureRatio,
                           double fSpec, double fDust, double fShade)
        {
            Latitude = latitude;
            Tilt = tilt;
            Azimuth = azimuth;
            Area = area;
            Efficiency = efficiency;
            Noct = noct;
            TempCoefficient = tempCoefficient;
            Albedo = albedo;
            PressureRatio = pressureRatio;
            FSpec = fSpec;
            FDust = fDust;
            FShade = fShade;
        }
    }

    public readonly struct AnnualYieldResult
    {
        public readonly double EnergyWh;
        /// <summary>Energy / (rated power at 1000 W/m² × hours in the year).</summary>
        public readonly double CapacityFactor;

        public AnnualYieldResult(double energyWh, double capacityFactor)
        {
            EnergyWh = energyWh;
            CapacityFactor = capacityFactor;
        }
    }
}
