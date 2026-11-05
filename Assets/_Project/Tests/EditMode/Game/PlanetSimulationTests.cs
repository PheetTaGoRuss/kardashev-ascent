using System;
using System.Numerics;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    public class PlanetFrameTests
    {
        const double Deg = Math.PI / 180.0;

        [TestCase(0.0, 0.0)]
        [TestCase(45.0, 90.0)]
        [TestCase(-30.0, -120.0)]
        [TestCase(80.0, 179.0)]
        public void LatLon_RoundTrips(double latDeg, double lonDeg)
        {
            Vector3 v = PlanetFrame.FromLatLon(latDeg * Deg, lonDeg * Deg);
            Assert.AreEqual(latDeg, PlanetFrame.Latitude(v) / Deg, 1e-4);
            Assert.AreEqual(lonDeg, PlanetFrame.Longitude(v) / Deg, 1e-4);
        }

        [Test]
        public void Axes_FollowTheConvention()
        {
            Assert.AreEqual(1f, PlanetFrame.FromLatLon(90 * Deg, 0).Y, 1e-6f, "north pole is +Y");
            Assert.AreEqual(1f, PlanetFrame.FromLatLon(0, 0).Z, 1e-6f, "longitude 0 is +Z");
            Assert.AreEqual(1f, PlanetFrame.FromLatLon(0, 90 * Deg).X, 1e-6f, "longitude +90 (east) is +X");
            Assert.AreEqual(1.0, PlanetFrame.ToLocal(new Vector3(1, 0, 0), 0, 0).East, 1e-6, "east at (0,0) is +X");
        }

        [TestCase(13.75, 100.5)]
        [TestCase(-45.0, -60.0)]
        public void LocalFrame_RoundTripsAndUpIsTheSurfaceNormal(double latDeg, double lonDeg)
        {
            double lat = latDeg * Deg, lon = lonDeg * Deg;
            Double3 up = PlanetFrame.ToLocal(PlanetFrame.FromLatLon(lat, lon), lat, lon);
            Assert.AreEqual(1.0, up.Up, 1e-6);

            var local = new Double3(0.3, -0.5, 0.81);
            Vector3 back = PlanetFrame.FromLocal(local, lat, lon);
            Double3 again = PlanetFrame.ToLocal(back, lat, lon);
            Assert.AreEqual(local.East, again.East, 1e-6);
            Assert.AreEqual(local.North, again.North, 1e-6);
            Assert.AreEqual(local.Up, again.Up, 1e-6);
        }

        [TestCase(13.75, 100.5, 20.0, 30.0)]
        [TestCase(-33.0, -70.0, -23.44, -75.0)]
        [TestCase(60.0, 10.0, 5.0, 0.0)]
        public void StarDirection_MatchesSolarModelHourAngle(double latDeg, double lonDeg, double declDeg, double hourAngleDeg)
        {
            double lat = latDeg * Deg, lon = lonDeg * Deg, decl = declDeg * Deg, h = hourAngleDeg * Deg;
            Vector3 star = PlanetFrame.StarDirection(decl, lon - h);
            Double3 fromFrame = PlanetFrame.ToLocal(star, lat, lon);
            Double3 fromSolar = SunPosition.Direction(lat, decl, h);
            Assert.AreEqual(fromSolar.East, fromFrame.East, 1e-6);
            Assert.AreEqual(fromSolar.North, fromFrame.North, 1e-6);
            Assert.AreEqual(fromSolar.Up, fromFrame.Up, 1e-6);
        }

        [Test]
        public void HexSphereLongitude_UsesTheSameConvention()
        {
            var sphere = HexSphere.Create(6);
            for (int t = 0; t < sphere.TileCount; t += 7)
            {
                Assert.AreEqual(PlanetFrame.Latitude(sphere.Centers[t]), sphere.Latitude(t), 1e-5);
                if (Math.Abs(sphere.Centers[t].Y) < 0.999f)
                    Assert.AreEqual(PlanetFrame.Longitude(sphere.Centers[t]), sphere.Longitude(t), 1e-5);
            }
        }
    }

    public class StubPlanetSimulationTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.4, 25.0);

        static StubPlanetSimulation Create()
        {
            var sphere = HexSphere.Create(12);
            var surface = PlanetSurface.Generate(sphere, new SurfaceSettings(7));
            return new StubPlanetSimulation(sphere, surface, Earthlike);
        }

        [Test]
        public void StartsInTheMorningOnLandWithNoPower()
        {
            var sim = Create();
            Assert.AreEqual(TerrainType.Lowland, sim.GetTile(sim.BaseTileId).Terrain);
            Assert.IsTrue(sim.IsDayAtBase);
            Assert.AreEqual(0.0, sim.GenerationW);
            Assert.AreEqual(1e16, sim.TypeITargetW, 1e14, "Earth-like planet's Type I target");
        }

        [Test]
        public void StarGoesAroundOncePerDay_WithSunriseAndSunsetEvents()
        {
            var sim = Create();
            Vector3 start = sim.StarDirection;
            int sunrises = 0, sunsets = 0;
            sim.DayNightChanged += day => { if (day) sunrises++; else sunsets++; };

            sim.Tick(24.0);

            Assert.AreEqual(1, sunsets);
            Assert.AreEqual(1, sunrises);
            Assert.Greater(Vector3.Dot(start, sim.StarDirection), 0.999f, "star back near the same place after one day");
        }

        [Test]
        public void Panels_OnlyOnLand_AndProduceInDaylight()
        {
            var sim = Create();
            int water = FindTile(sim, info => info.IsWater);
            Assert.IsFalse(sim.PlacePanel(water, 0.3, Math.PI));
            Assert.IsFalse(sim.PreviewPanel(water, 0.3, Math.PI).CanPlace);

            TileInfo baseTile = sim.GetTile(sim.BaseTileId);
            double tilt = Math.Abs(baseTile.Latitude);
            Assert.IsTrue(sim.PlacePanel(sim.BaseTileId, tilt, Math.PI));
            Assert.IsFalse(sim.PlacePanel(sim.BaseTileId, tilt, Math.PI), "second panel on the same tile");
            Assert.IsTrue(sim.HasPanel(sim.BaseTileId));
            Assert.Greater(sim.GenerationW, 0.0, "morning sun on the new panel");

            Assert.IsTrue(sim.RemovePanel(sim.BaseTileId));
            Assert.AreEqual(0.0, sim.GenerationW);
        }

        [Test]
        public void Preview_UsesTheAnnualSolarModel()
        {
            var sim = Create();
            TileInfo tile = sim.GetTile(sim.BaseTileId);
            PanelPreview preview = sim.PreviewPanel(tile.Id, Math.Abs(tile.Latitude), Math.PI);

            Assert.IsTrue(preview.CanPlace);
            Assert.Greater(preview.CapacityFactor, 0.1);
            Assert.Less(preview.CapacityFactor, 0.4);
            Assert.AreEqual(preview.EnergyPerYearWh * sim.PanelLifetimeYears / (sim.EmbodiedWhPerM2 * sim.PanelAreaM2),
                            preview.Eroi, 1e-9);
        }

        [Test]
        public void NoPanels_BatteryRunsOut_AndBrownoutStarts()
        {
            var sim = Create();
            bool? brownout = null;
            sim.BrownoutChanged += b => brownout = b;

            sim.Tick(12.0);

            Assert.AreEqual(true, brownout);
            Assert.IsTrue(sim.IsBrownout);
            Assert.AreEqual(0.0, sim.BatteryStoredWh, 1e-9);
        }

        static int FindTile(IPlanetSimulation sim, Func<TileInfo, bool> match)
        {
            for (int t = 0; t < sim.TileCount; t++)
                if (match(sim.GetTile(t)))
                    return t;
            throw new InvalidOperationException("No matching tile.");
        }
    }
}
