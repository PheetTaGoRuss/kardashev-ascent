using KardashevAscent.Simulation.Climate;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>Values from docs/physics/physics-verification.md (waste heat) and notes/kardashev_thermal.md.</summary>
    public class WasteHeatTests
    {
        const double SunL = 3.828e26;
        const double Au = 1.495978707e11;
        const double EarthR = 6.371e6;
        const double EarthAlbedo = 0.306;

        [Test]
        public void Earth_EquilibriumTemperatureIs254K()
        {
            Assert.AreEqual(254.04, WasteHeat.EquilibriumTemperatureK(SunL, Au, EarthAlbedo, EarthR), 0.05);
        }

        [Test]
        public void Earth_TypeIWasteHeatRaisesEquilibriumBy5K()
        {
            Assert.AreEqual(5.116, WasteHeat.EquilibriumWarmingK(SunL, Au, EarthAlbedo, EarthR, 1e16), 0.01);
        }

        [Test]
        public void Earth_TypeIWasteHeatWithFeedbackIsAbout15K()
        {
            Assert.AreEqual(15.35, WasteHeat.SurfaceWarmingK(SunL, Au, EarthAlbedo, EarthR, 1e16), 0.05);
        }

        [Test]
        public void FullBalance_IsBelowLinearShortcutAtHighPower()
        {
            // 6 × 10¹⁶ W: exact 27.0 K, linear shortcut T/4 · P/P_abs gives 31.6 K.
            Assert.AreEqual(27.0, WasteHeat.EquilibriumWarmingK(SunL, Au, EarthAlbedo, EarthR, 6e16), 0.1);
        }

        [Test]
        public void Forcing_TypeIOnEarth()
        {
            Assert.AreEqual(19.605, WasteHeat.ForcingWm2(1e16, EarthR), 0.005);
        }

        [Test]
        public void NoWasteHeat_NoWarming()
        {
            Assert.AreEqual(0.0, WasteHeat.EquilibriumWarmingK(SunL, Au, EarthAlbedo, EarthR, 0.0), 1e-12);
            Assert.AreEqual(0.0, WasteHeat.EquilibriumWarmingK(SunL, Au, EarthAlbedo, EarthR, -1e16), 1e-12);
        }

        [TestCase(EnergySourceKind.SolarPV, false)]
        [TestCase(EnergySourceKind.Wind, false)]
        [TestCase(EnergySourceKind.Hydro, false)]
        [TestCase(EnergySourceKind.Tidal, false)]
        [TestCase(EnergySourceKind.Fossil, true)]
        [TestCase(EnergySourceKind.Fission, true)]
        [TestCase(EnergySourceKind.Fusion, true)]
        [TestCase(EnergySourceKind.OrbitalSolarBeamed, true)]
        [TestCase(EnergySourceKind.Antimatter, true)]
        public void AddsWasteHeat_OnlyForNewEnergy(EnergySourceKind source, bool expected)
        {
            Assert.AreEqual(expected, WasteHeat.AddsWasteHeat(source));
        }

        [Test]
        public void Panels_DarkenThePlanetSlightly()
        {
            // 1 % of Earth's surface on sand (ρ 0.35) → A drops by 0.01 × 0.25.
            double surface = 4.0 * System.Math.PI * EarthR * EarthR;
            Assert.AreEqual(EarthAlbedo - 0.0025, WasteHeat.BondAlbedoWithPanels(EarthAlbedo, 0.01 * surface, EarthR, 0.35), 1e-9);
            Assert.AreEqual(EarthAlbedo, WasteHeat.BondAlbedoWithPanels(EarthAlbedo, 0.0, EarthR, 0.35), 1e-12);
        }
    }
}
