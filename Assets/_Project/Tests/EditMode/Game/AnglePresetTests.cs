using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>Saved panel angle layouts: name, note, slot and where on the planet they were saved.</summary>
    public class AnglePresetTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Surface = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);
        const double Deg = Math.PI / 180.0;

        static PlanetSimulation WithPanel(out int tile)
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            tile = sim.BaseTileId;
            Assert.IsTrue(sim.PlacePanel(tile, 3, 30.0 * Deg, 200.0 * Deg));
            return sim;
        }

        [Test]
        public void Save_KeepsAnglesPlaceNameAndNote()
        {
            PlanetSimulation sim = WithPanel(out int tile);
            AnglePreset p = sim.SaveAnglePreset(tile, 3, "  Noon bank  ", "Faces the dune");
            Assert.IsNotNull(p);
            Assert.AreEqual("Noon bank", p.Name, "the name is trimmed");
            Assert.AreEqual("Faces the dune", p.Note);
            Assert.AreEqual(30.0, p.TiltDeg, 1e-9);
            Assert.AreEqual(200.0, p.FacingDeg, 1e-9);
            Assert.AreEqual(3, p.Slot);
            Assert.AreEqual(Sphere.Latitude(tile) / Deg, p.LatitudeDeg, 1e-9);
            Assert.AreEqual(Sphere.Longitude(tile) / Deg, p.LongitudeDeg, 1e-9);
            Assert.AreEqual(1, sim.AnglePresets.Count);
        }

        [Test]
        public void Save_WithoutAName_UsesTheSlot_AndNothingSavesWithoutAPanel()
        {
            PlanetSimulation sim = WithPanel(out int tile);
            Assert.AreEqual("Slot 4", sim.SaveAnglePreset(tile, 3, "", null).Name);
            Assert.IsNull(sim.SaveAnglePreset(tile, 10, "x", ""), "no panel in slot 10");
            Assert.AreEqual(1, sim.AnglePresets.Count);
        }

        [Test]
        public void SaveAt_WorksForASlotWithNoPanelYet_AndClampsTilt()
        {
            var sim = new PlanetSimulation(Sphere, Surface, Earthlike);
            int tile = sim.BaseTileId;
            AnglePreset p = sim.SaveAnglePresetAt(tile, 7, "Plan", "later", 120.0 * Deg, -90.0 * Deg);
            Assert.IsNotNull(p);
            Assert.AreEqual(90.0, p.TiltDeg, 1e-9, "tilt is kept in 0-90");
            Assert.AreEqual(270.0, p.FacingDeg, 1e-9, "facing wraps into 0-359");
            Assert.IsNull(sim.SaveAnglePresetAt(tile, 999, "x", "", 0.0, 0.0), "no such slot");
        }

        [Test]
        public void Save_CapsLongNames_AndNotes()
        {
            PlanetSimulation sim = WithPanel(out int tile);
            AnglePreset p = sim.SaveAnglePreset(tile, 3, new string('n', 100), new string('x', 500));
            Assert.AreEqual(AnglePreset.MaxNameLength, p.Name.Length);
            Assert.AreEqual(AnglePreset.MaxNoteLength, p.Note.Length);
        }

        [Test]
        public void UpdateDeleteApply()
        {
            PlanetSimulation sim = WithPanel(out int tile);
            Assert.IsTrue(sim.PlacePanel(tile, 5, 10.0 * Deg, 90.0 * Deg));
            AnglePreset p = sim.SaveAnglePreset(tile, 3, "A", "");
            Assert.IsTrue(sim.UpdateAnglePreset(p.Id, "B", "note"));
            Assert.AreEqual("B", sim.AnglePresets[0].Name);
            Assert.IsTrue(sim.ApplyAnglePreset(p.Id, tile, 5), "orders a robot to turn slot 5");
            // A built panel waits for a robot (an order); a panel still under construction takes the angles at once.
            bool ordered = sim.TryGetAdjustOrder(tile, 5, out double tilt, out double az);
            if (!ordered) Assert.IsTrue(sim.TryGetPanelAngles(tile, 5, out tilt, out az));
            Assert.AreEqual(30.0 * Deg, tilt, 1e-9);
            Assert.AreEqual(200.0 * Deg, az, 1e-9);
            Assert.IsTrue(sim.DeleteAnglePreset(p.Id));
            Assert.IsFalse(sim.DeleteAnglePreset(p.Id));
            Assert.IsFalse(sim.ApplyAnglePreset(p.Id, tile, 5));
        }

        [Test]
        public void SaveAndLoad_KeepsPresets_AndOldSavesHaveNone()
        {
            PlanetSimulation sim = WithPanel(out int tile);
            sim.SaveAnglePreset(tile, 3, "Keep me", "a note");
            SimulationState state = sim.CaptureState();
            Assert.AreEqual(SimulationState.CurrentVersion, state.Version);
            var other = new PlanetSimulation(Sphere, Surface, Earthlike);
            Assert.IsTrue(other.RestoreState(state));
            Assert.AreEqual(1, other.AnglePresets.Count);
            Assert.AreEqual("Keep me", other.AnglePresets[0].Name);
            Assert.AreEqual("a note", other.AnglePresets[0].Note);
            AnglePreset next = other.SaveAnglePreset(tile, 3, "second", "");
            Assert.Greater(next.Id, other.AnglePresets[0].Id, "ids keep counting up after a load");

            state.AnglePresets = null;   // a save from before layouts existed
            Assert.IsTrue(other.RestoreState(state));
            Assert.AreEqual(0, other.AnglePresets.Count);
        }
    }
}
