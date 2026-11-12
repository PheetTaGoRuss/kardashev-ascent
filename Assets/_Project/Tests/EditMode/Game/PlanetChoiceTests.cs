using System;
using KardashevAscent.Simulation.Climate;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using KardashevAscent.Simulation.Tests.Planet;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>The stub simulation on planets from planets.json: star colour, clouds and tidal locking (issue #11).</summary>
    public class PlanetChoiceTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        static PlanetRecord Sunlike(string id = "sunlike")
        {
            var r = new PlanetRecord { id = id };
            r.solar.eccentricity = 0.0167;
            r.solar.obliquityDeg = 23.44;
            r.solar.perihelionLongitudeDeg = 282.94;
            r.solar.yearLengthHours = 365.256 * 24.0;
            r.solar.airTemperatureC = 25.0;
            return r;
        }

        static StubPlanetSimulation Create(PlanetRecord r) => new StubPlanetSimulation(Sphere, Surface, r);

        static double Tilt(StubPlanetSimulation sim) => Math.Abs(sim.GetTile(sim.BaseTileId).Latitude);

        [Test]
        public void Record_SetsIdStarAndClouds()
        {
            PlanetRecord r = Sunlike("viridia");
            r.flags.clouds = "earth";
            var sim = Create(r);
            Assert.AreEqual("viridia", sim.PlanetId);
            Assert.AreEqual(5772.0, sim.StarTemperatureK);
            Assert.AreEqual(CloudModel.EarthClimatology, sim.Clouds);
            Assert.AreEqual(1.0, sim.SpectralFactor, 2e-3);
        }

        [Test]
        public void RedDwarfLight_CutsSiliconOutputByFSpec()
        {
            // Same luminosity and orbit, only the star's colour differs: output scales by f_spec exactly.
            var sun = Create(Sunlike());
            PlanetRecord red = Sunlike();
            red.star.teffK = 2900.0;
            var dwarf = Create(red);
            int t = sun.BaseTileId;
            double expected = SpectralMatch.FSpec(CellMaterial.Silicon, 2900.0) / SpectralMatch.FSpec(CellMaterial.Silicon, 5772.0);
            Assert.AreEqual(expected,
                dwarf.PreviewPanel(t, Tilt(dwarf), Math.PI).EnergyPerYearWh / sun.PreviewPanel(t, Tilt(sun), Math.PI).EnergyPerYearWh,
                1e-9);
            Assert.Less(expected, 0.65, "silicon loses about half its output under a 2900 K star");
        }

        [Test]
        public void EarthClouds_LowerTheYearByTheLatitudesCloudFactor()
        {
            var clear = Create(Sunlike());
            PlanetRecord r = Sunlike();
            r.flags.clouds = "earth";
            var cloudy = Create(r);
            int t = clear.BaseTileId;
            double ratio = cloudy.PreviewPanel(t, Tilt(cloudy), Math.PI).EnergyPerYearWh
                           / clear.PreviewPanel(t, Tilt(clear), Math.PI).EnergyPerYearWh;
            double lat = cloudy.GetTile(t).Latitude;
            double min = double.MaxValue, max = 0.0;
            for (int m = 0; m < 12; m++)
            {
                double f = CloudClimate.Factor(lat, (m + 0.5) / 12.0);
                min = Math.Min(min, f);
                max = Math.Max(max, f);
            }
            Assert.That(ratio, Is.InRange(min - 0.01, max + 0.01));
            Assert.Less(ratio, 0.95);
        }

        [Test]
        public void EarthClouds_MonthsAddUpToTheYear()
        {
            PlanetRecord r = Sunlike();
            r.flags.clouds = "earth";
            var sim = Create(r);
            int t = sim.BaseTileId;
            double[] months = sim.MonthlyEnergyWh(t, Tilt(sim), Math.PI);
            double sum = 0.0;
            foreach (double m in months) sum += m;
            Assert.AreEqual(sim.PreviewPanel(t, Tilt(sim), Math.PI).EnergyPerYearWh, sum, sum * 1e-9);
        }

        [Test]
        public void TidallyLocked_BaseIsAlwaysLitAndPanelsWork()
        {
            var sim = Create(PlanetRecordTests.ProximaForOtherTests());
            int t = sim.BaseTileId;
            PanelPreview preview = sim.PreviewPanel(t, Tilt(sim), Math.PI);
            Assert.IsTrue(preview.CanPlace, preview.Reason);
            Assert.Greater(preview.EnergyPerYearWh, 0.0);
            Assert.IsNotNull(sim.MonthlyEnergyWh(t, Tilt(sim), Math.PI));

            Assert.IsTrue(sim.PlacePanel(t, 0, Tilt(sim), Math.PI));
            for (int i = 0; i < 12; i++)
            {
                sim.Tick(6.0);
                Assert.IsTrue(sim.IsDayAtBase, $"after {sim.TimeHours} h");
                Assert.Greater(sim.GenerationW - sim.RtgW, 0.0);
            }
        }

        [Test]
        public void TidallyLocked_PreviewMatchesLiveOutput()
        {
            // The star never moves on a circular locked orbit, so the preview (per Earth year) is power × Earth-year hours.
            var sim = Create(PlanetRecordTests.ProximaForOtherTests());
            int t = sim.BaseTileId;
            double tilt = Tilt(sim);
            Assert.IsTrue(sim.PlacePanel(t, 0, tilt, Math.PI));
            sim.Tick(1.0);
            double live = sim.GenerationW - sim.RtgW;
            double yearly = sim.PreviewPanel(t, tilt, Math.PI).EnergyPerYearWh;
            Assert.AreEqual(yearly / SolarConstants.EarthYearHours, live, live * 0.02);
        }

        [Test]
        public void Save_RemembersThePlanet()
        {
            var a = Create(Sunlike("viridia"));
            SimulationState state = a.CaptureState();
            Assert.AreEqual("viridia", state.PlanetId);
            Assert.AreEqual(22, state.Version);
            Assert.IsTrue(Create(Sunlike("viridia")).RestoreState(state));
            Assert.IsFalse(Create(Sunlike("elsewhere")).RestoreState(state), "a save from another planet");
            state.PlanetId = "";
            Assert.IsTrue(Create(Sunlike("elsewhere")).RestoreState(state), "old saves have no planet id");
        }
    }
}
