using System;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Solar
{
    /// <summary>Circumbinary light; Kepler-16 values from docs/physics/notes/stellar_exoplanet.md (Doyle et al. 2011).</summary>
    public class BinaryStarTests
    {
        const double SunL = 3.828e26;
        const double Au = SolarConstants.AstronomicalUnit;
        const double EarthFlux = 1361.0;
        const double Deg = SolarConstants.DegToRad;
        static readonly double Kepler16bDistance = 0.7048 * Au;

        [Test]
        public void Kepler16_Luminosities()
        {
            Assert.AreEqual(0.149, BinaryStar.Kepler16.A.Luminosity / SunL, 0.002);
            Assert.AreEqual(0.0055, BinaryStar.Kepler16.B.Luminosity / SunL, 0.0003);
        }

        [Test]
        public void Kepler16b_InsolationAveragesAboutPoint31EarthAndVaries()
        {
            double sum = 0.0, minClear = double.MaxValue, minAll = double.MaxValue, max = 0.0;
            const int n = 3600;
            for (int i = 0; i < n; i++)
            {
                BinaryStar.View(BinaryStar.Kepler16, Kepler16bDistance, 0.0, 2.0 * Math.PI * i / n, out StarView a, out StarView b);
                double s = (a.Irradiance + b.Irradiance) / EarthFlux;
                sum += s;
                minAll = Math.Min(minAll, s);
                if (a.VisibleFraction == 1.0 && b.VisibleFraction == 1.0)
                    minClear = Math.Min(minClear, s);
                max = Math.Max(max, s);
            }
            Assert.AreEqual(0.31, sum / n, 0.01);
            Assert.AreEqual(0.28, minClear, 0.015);   // research range 0.279–0.354, no eclipses
            Assert.AreEqual(0.35, max, 0.015);
            Assert.AreEqual(0.22, minAll, 0.01);      // B in front of A hides ~25 % of A
        }

        [Test]
        public void Kepler16_StarBAddsAFewPercent()
        {
            BinaryStar.View(BinaryStar.Kepler16, Kepler16bDistance, 0.0, Math.PI / 2.0, out StarView a, out StarView b);
            Assert.AreEqual(0.037, b.Irradiance / (a.Irradiance + b.Irradiance), 0.01);
        }

        [Test]
        public void Aligned_NearStarEclipsesFarStar()
        {
            // Phase 0: star B between planet and barycentre, star A behind it.
            BinaryStar.View(BinaryStar.Kepler16, Kepler16bDistance, 0.0, 0.0, out StarView a, out StarView b);
            Assert.AreEqual(0.0, a.OffsetAngle, 1e-12);
            Assert.AreEqual(0.0, b.OffsetAngle, 1e-12);
            // Central transit: hidden share = (θ_B / θ_A)², θ = R / d. d_A = 0.748 AU, d_B = 0.531 AU → 1 − 0.246.
            Assert.AreEqual(0.754, a.VisibleFraction, 0.005);
            Assert.AreEqual(1.0, b.VisibleFraction);
        }

        [Test]
        public void Quadrature_NoEclipseAndStarsSpreadApart()
        {
            BinaryStar.View(BinaryStar.Kepler16, Kepler16bDistance, 0.0, Math.PI / 2.0, out StarView a, out StarView b);
            Assert.AreEqual(1.0, a.VisibleFraction);
            Assert.AreEqual(1.0, b.VisibleFraction);
            // a_bin / a_planet ≈ 0.318 rad between the stars at most.
            Assert.AreEqual(Math.Atan(0.22431 / 0.7048), Math.Abs(a.OffsetAngle - b.OffsetAngle), 0.02);
        }

        [Test]
        public void VisibleFraction_DiscGeometry()
        {
            Assert.AreEqual(1.0, BinaryStar.VisibleFraction(3.0, 1.0, 1.0));                // apart
            Assert.AreEqual(0.0, BinaryStar.VisibleFraction(0.0, 2.0, 1.0));                // fully hidden
            Assert.AreEqual(0.75, BinaryStar.VisibleFraction(0.0, 0.5, 1.0), 1e-12);        // small disc in front
            Assert.AreEqual(0.6090, BinaryStar.VisibleFraction(1.0, 1.0, 1.0), 1e-4);       // equal discs, d = r
        }

        [Test]
        public void SynodicPeriod_Kepler16bIsAbout50Days()
        {
            Assert.AreEqual(50.07, BinaryStar.SynodicPeriodHours(41.079220 * 24.0, 228.776 * 24.0) / 24.0, 0.05);
        }

        [Test]
        public void AnnualYield_CloseToOneCombinedStar()
        {
            // Two stars ≈ one star with their total light, give or take geometry and f_spec of the cooler star B.
            var planet = new PlanetSolarParams(BinaryStar.Kepler16.TotalLuminosity, Kepler16bDistance, 0.0069, 0.0, 0.0,
                                               24.0, 228.776 * 24.0, 0.0, 0.3, 25.0);
            var panel = new PanelParams(20.0 * Deg, 20.0 * Deg, Math.PI, 1.0, 0.2, 45.0, -0.004, 0.2, 1.0,
                                        SpectralMatch.FSpec(CellMaterial.Silicon, 4450.0), 0.0, 1.0);
            double single = AnnualYield.Calculate(planet, panel).EnergyWh;
            double binary = BinaryStar.AnnualYield(planet, panel, BinaryStar.Kepler16, CellMaterial.Silicon).EnergyWh;
            Assert.AreEqual(1.0, binary / single, 0.03);
        }

        [Test]
        public void AnnualYield_RejectsTidallyLocked()
        {
            var planet = new PlanetSolarParams(SunL, Au, 0.0, 0.0, 0.0, 0.0, 8766.0, 0.3, 0.3, 25.0, tidallyLocked: true);
            var panel = new PanelParams(0.0, 0.0, Math.PI, 1.0, 0.2, 45.0, -0.004, 0.2, 1.0, 1.0, 0.0, 1.0);
            Assert.Throws<ArgumentException>(() => BinaryStar.AnnualYield(planet, panel, BinaryStar.Kepler16, CellMaterial.Silicon));
        }
    }
}
