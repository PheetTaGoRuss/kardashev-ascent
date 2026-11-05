using System;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Solar
{
    /// <summary>P0 items 6 (radiative cell temperature) and 9 (energy payback) from docs/physics/physics-verification.md.</summary>
    public class PanelThermalAndEconomicsTests
    {
        const double Deg = SolarConstants.DegToRad;

        // Item 6: cell temperature without air

        [TestCase(1361.0, 0.85, 317.53)]   // both faces radiate, 1 AU
        [TestCase(1361.0, 0.0, 377.61)]    // insulated back
        [TestCase(590.0, 0.85, 257.65)]    // Mars distance
        public void Radiative_MatchesReport(double poa, double emissivityBack, double expectedK)
        {
            double t = PanelModel.RadiativeCellTemperature(poa, 0.2, emissivityBack: emissivityBack);
            Assert.AreEqual(expectedK - 273.15, t, 0.05);
        }

        [Test]
        public void Radiative_HotterWhenLessLightBecomesElectricity()
        {
            Assert.Greater(PanelModel.RadiativeCellTemperature(1000.0, 0.10), PanelModel.RadiativeCellTemperature(1000.0, 0.30));
        }

        [TestCase(0.0, 0.0)]
        [TestCase(0.006, 0.0)]     // Mars
        [TestCase(0.01, 0.0)]
        [TestCase(0.0316227766, 0.5)]
        [TestCase(0.1, 1.0)]
        [TestCase(1.0, 1.0)]       // Earth
        [TestCase(1.47, 1.0)]      // Titan
        public void ConvectiveWeight_ByPressure(double pressureEarth, double expected)
        {
            Assert.AreEqual(expected, PanelModel.ConvectiveWeight(pressureEarth), 1e-9);
        }

        [Test]
        public void CellTemperature_EarthAirUsesNoct()
        {
            Assert.AreEqual(56.25, PanelModel.CellTemperature(25.0, 1000.0, 45.0, 1.0, 0.2), 1e-9);
        }

        [Test]
        public void CellTemperature_VacuumIgnoresAirTemperature()
        {
            double a = PanelModel.CellTemperature(-170.0, 1361.0, 45.0, 0.0, 0.2);
            double b = PanelModel.CellTemperature(25.0, 1361.0, 45.0, 0.0, 0.2);
            Assert.AreEqual(a, b, 1e-12);
            Assert.AreEqual(44.38, a, 0.05);
        }

        [Test]
        public void CellTemperature_ThinAirBlendsBothModels()
        {
            double noct = PanelModel.CellTemperature(0.0, 800.0, 45.0);
            double rad = PanelModel.RadiativeCellTemperature(800.0, 0.2);
            double mid = PanelModel.CellTemperature(0.0, 800.0, 45.0, 0.0316227766, 0.2);
            Assert.AreEqual(0.5 * (noct + rad), mid, 1e-6);
        }

        [Test]
        public void Power_NeverNegativeWhenCellIsExtremelyHot()
        {
            Assert.AreEqual(0.0, PanelModel.Power(1000.0, 1.0, 0.2, 400.0));
        }

        [Test]
        public void AnnualYield_AirlessPlanetRunsColderThanEarthAir()
        {
            // Same light, no atmosphere: NOCT with 25 °C air vs radiative cooling. Both should give finite energy,
            // and the cooler vacuum panel (44 °C at full sun vs 56 °C) produces more.
            var withAir = new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0, 0.0, 0.0,
                                                24.0, 8766.0, 0.0, 0.3, 25.0);
            var airless = new PlanetSolarParams(SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0, 0.0, 0.0,
                                                24.0, 8766.0, 0.0, 0.3, 25.0, surfacePressureEarth: 0.0);
            var panel = new PanelParams(0.0, 0.0, Math.PI, 1.0, 0.2, 45.0, -0.004, 0.1, 1.0, 1.0, 0.0, 1.0);
            double eAir = AnnualYield.Calculate(withAir, panel).EnergyWh;
            double eVac = AnnualYield.Calculate(airless, panel).EnergyWh;
            Assert.IsFalse(double.IsNaN(eVac));
            Assert.Greater(eVac, eAir);
        }

        // Item 9: energy payback

        [Test]
        public void Embodied_FromReferencePayback()
        {
            Assert.AreEqual(266_475.0, Economics.EmbodiedWhPerM2(PanelTechnology.ModernMonoSi), 1e-6);
            Assert.AreEqual(679_575.0, Economics.EmbodiedWhPerM2(PanelTechnology.EarlyMonoSi), 1e-6);
        }

        [TestCase(PanelTechnology.ModernMonoSi)]
        [TestCase(PanelTechnology.EarlyMonoSi)]
        public void Embodied_PaysBackInReferenceYearsAtReferenceSite(PanelTechnology tech)
        {
            double yearWhPerM2 = Economics.ReferenceIrradiationKWhPerM2 * 1000.0
                                 * Economics.ReferenceEfficiency(tech) * Economics.ReferencePerformanceRatio;
            Assert.AreEqual(Economics.ReferencePaybackYears(tech),
                            Economics.PaybackYears(Economics.EmbodiedWhPerM2(tech), yearWhPerM2), 1e-12);
        }

        [TestCase(1700.0, 1.0)]
        [TestCase(680.0, 2.5)]     // Mars equator ≈ 40 % of the reference light
        [TestCase(2400.0, 0.708)]  // clear-sky Bangkok, tilted
        public void PaybackFromIrradiation_Modern(double irradiation, double expectedYears)
        {
            Assert.AreEqual(expectedYears, Economics.PaybackYearsFromIrradiation(PanelTechnology.ModernMonoSi, irradiation), 1e-3);
        }

        [Test]
        public void Eroi_ModernIsAboutThirty_EarlyAboutSeven()
        {
            Assert.AreEqual(30.0, Economics.EroiFromPayback(Economics.ReferencePaybackYears(PanelTechnology.ModernMonoSi)), 1e-12);
            Assert.AreEqual(7.32, Economics.EroiFromPayback(Economics.ReferencePaybackYears(PanelTechnology.EarlyMonoSi)), 0.01);
        }
    }
}
