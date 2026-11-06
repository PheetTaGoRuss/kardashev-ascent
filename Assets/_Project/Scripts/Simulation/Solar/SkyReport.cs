namespace KardashevAscent.Simulation.Solar
{
    /// <summary>
    /// Everything the sky view shows about one panel right now (degrees, W/m², W), from the simulation's own state.
    /// The player sees these numbers only after measuring or working them out.
    /// </summary>
    public readonly struct SkyReport
    {
        public readonly bool Valid;
        public readonly bool TidallyLocked;
        /// <summary>Tile latitude φ, degrees (+ north).</summary>
        public readonly double LatitudeDeg;
        /// <summary>Axial tilt ε of the planet, degrees.</summary>
        public readonly double ObliquityDeg;
        /// <summary>The star's ecliptic longitude λ now (0 = northern spring equinox), degrees.</summary>
        public readonly double EclipticLongitudeDeg;
        /// <summary>Declination δ now, degrees.</summary>
        public readonly double DeclinationDeg;
        /// <summary>Hour angle H, degrees: 0 at local noon, negative in the morning.</summary>
        public readonly double HourAngleDeg;
        /// <summary>Local solar time, hours (NaN on a tidally locked planet).</summary>
        public readonly double LocalSolarHours;
        /// <summary>Sun altitude h and azimuth A_s (clockwise from north), degrees.</summary>
        public readonly double SunAltitudeDeg, SunAzimuthDeg;
        /// <summary>Panel tilt β and azimuth A_p, degrees.</summary>
        public readonly double TiltDeg, PanelAzimuthDeg;
        /// <summary>cos θ between the sun and the panel's normal (negative: behind the panel).</summary>
        public readonly double CosIncidence;
        /// <summary>Light outside the atmosphere, direct (beam) and diffuse light at the ground, on the panel, W/m².</summary>
        public readonly double TopOfAtmosphereWm2, DirectNormalWm2, DiffuseHorizontalWm2, PlaneOfArrayWm2;
        /// <summary>Cell temperature, °C, and output of one new panel here, W.</summary>
        public readonly double CellTemperatureC, PanelPowerW;
        /// <summary>Spectral match f_spec and cloud factor in effect.</summary>
        public readonly double SpectralFactor, CloudFactor;

        public SkyReport(bool tidallyLocked, double latitudeDeg, double obliquityDeg, double eclipticLongitudeDeg,
                         double declinationDeg, double hourAngleDeg, double localSolarHours,
                         double sunAltitudeDeg, double sunAzimuthDeg, double tiltDeg, double panelAzimuthDeg,
                         double cosIncidence, double topOfAtmosphereWm2, double directNormalWm2,
                         double diffuseHorizontalWm2, double planeOfArrayWm2, double cellTemperatureC,
                         double panelPowerW, double spectralFactor, double cloudFactor)
        {
            Valid = true;
            TidallyLocked = tidallyLocked;
            LatitudeDeg = latitudeDeg;
            ObliquityDeg = obliquityDeg;
            EclipticLongitudeDeg = eclipticLongitudeDeg;
            DeclinationDeg = declinationDeg;
            HourAngleDeg = hourAngleDeg;
            LocalSolarHours = localSolarHours;
            SunAltitudeDeg = sunAltitudeDeg;
            SunAzimuthDeg = sunAzimuthDeg;
            TiltDeg = tiltDeg;
            PanelAzimuthDeg = panelAzimuthDeg;
            CosIncidence = cosIncidence;
            TopOfAtmosphereWm2 = topOfAtmosphereWm2;
            DirectNormalWm2 = directNormalWm2;
            DiffuseHorizontalWm2 = diffuseHorizontalWm2;
            PlaneOfArrayWm2 = planeOfArrayWm2;
            CellTemperatureC = cellTemperatureC;
            PanelPowerW = panelPowerW;
            SpectralFactor = spectralFactor;
            CloudFactor = cloudFactor;
        }

        public bool SunUp => Valid && SunAltitudeDeg > 0.0;
    }
}
