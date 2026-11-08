using System;
using KardashevAscent.Simulation.Energy;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Energy
{
    public class RtgTests
    {
        // docs/physics/physics-verification.md, "Kardashev, waste heat and other sources" table.
        [Test]
        public void FuelOnly_After14Years_Is98Point5W()
        {
            Assert.AreEqual(98.5, Rtg.Power(110.0, 14.0, ageingPerYear: 0.0), 0.1);
        }

        [Test]
        public void FuelDecay_Is0Point787PercentPerYear()
        {
            Assert.AreEqual(0.787, (1.0 - Math.Exp(-Rtg.FuelDecayPerYear)) * 100.0, 0.001);
        }

        [Test]
        public void Default_Matches72WAt17Years_And2Point46PercentPerYear()
        {
            Assert.AreEqual(72.0, Rtg.Power(110.0, 17.0), 1e-9);
            double yearly = 1.0 - Rtg.Power(110.0, 1.0) / 110.0;
            Assert.AreEqual(2.46, yearly * 100.0, 0.01);
        }
    }

    public class BatteryTests
    {
        [Test]
        public void RoundTrip_Returns90Percent()
        {
            var battery = new Battery(10_000.0, startFraction: 0.0);
            Assert.AreEqual(1000.0, battery.Charge(1000.0, 1.0), 1e-9);
            double back = 0.0;
            for (int i = 0; i < 10; i++)
                back += battery.Discharge(1000.0, 0.25) * 0.25;
            Assert.AreEqual(900.0, back, 1e-6);
            Assert.AreEqual(0.0, battery.StoredWh, 1e-9);
        }

        [Test]
        public void PowerIsLimitedByCRate_AndEnergyByRoom()
        {
            var battery = new Battery(1000.0, cRatePerHour: 0.5, startFraction: 0.0);
            Assert.AreEqual(500.0, battery.Charge(5000.0, 0.1), 1e-9, "0.5 C");

            var nearlyFull = new Battery(1000.0, startFraction: 0.99);
            double taken = nearlyFull.Charge(1000.0, 1.0);
            Assert.AreEqual(10.0 / Math.Sqrt(0.9), taken, 1e-9, "only the room left, grossed up for losses");
            Assert.AreEqual(1000.0, nearlyFull.StoredWh, 1e-9);
        }

        [Test]
        public void Discharge_StopsWhenEmpty()
        {
            var battery = new Battery(100.0, startFraction: 1.0);
            double delivered = battery.Discharge(1000.0, 1.0);
            Assert.AreEqual(100.0 * Math.Sqrt(0.9), delivered, 1e-9);
            Assert.AreEqual(0.0, battery.Discharge(1000.0, 1.0));
        }
    }

    public class PowerGridTests
    {
        const double LoadW = 100_000.0;

        static bool BrownsOutDuringNight(double capacityWh, double nightHours)
        {
            var grid = new PowerGrid(new Battery(capacityWh), LoadW);
            for (double t = 0.0; t < nightHours - 1e-9; t += 0.25)
                if (grid.Step(0.0, 0.25, 0.0).IsBrownout)
                    return true;
            return false;
        }

        // Issue #9: a night of D/2 hours needs a battery of at least P_load × D/2 (more, after discharge losses).
        [TestCase(24.0)]
        [TestCase(10.0)]
        [TestCase(70.0)]
        public void Night_NeedsLoadTimesHalfADay(double dayHours)
        {
            double night = dayHours / 2.0;
            double needed = LoadW * night;
            Assert.IsTrue(BrownsOutDuringNight(needed * 0.999, night), "less than P × D/2 is not enough");
            Assert.IsTrue(BrownsOutDuringNight(needed, night), "exactly P × D/2 still falls short by the discharge loss");
            Assert.IsFalse(BrownsOutDuringNight(needed / Math.Sqrt(Battery.LithiumIonRoundTrip) * 1.001, night));
        }

        [Test]
        public void Surplus_ChargesTheBattery_ThenIsCurtailed()
        {
            var grid = new PowerGrid(new Battery(100_000.0, startFraction: 0.0), LoadW);
            GridStep step = grid.Step(150_000.0, 0.5, 0.0);
            Assert.AreEqual(LoadW, step.ServedW, 1e-9);
            Assert.AreEqual(50_000.0, step.ChargeW, 1e-9);
            Assert.AreEqual(0.0, step.CurtailedW, 1e-9);

            for (int i = 0; i < 10; i++)
                step = grid.Step(150_000.0, 0.5, 0.0);
            Assert.AreEqual(1.0, grid.Battery.StateOfCharge, 1e-9);
            Assert.AreEqual(50_000.0, step.CurtailedW, 1e-9, "full battery: the surplus has nowhere to go");
            Assert.IsFalse(step.IsBrownout);
        }

        [Test]
        public void Brownout_IsRaisedOnceWhenItStartsAndOnceWhenItEnds()
        {
            var grid = new PowerGrid(new Battery(10_000.0), LoadW);
            int started = 0, ended = 0;
            grid.BrownoutChanged += on => { if (on) started++; else ended++; };

            for (int i = 0; i < 8; i++)
                grid.Step(0.0, 0.25, 0.0);
            Assert.IsTrue(grid.IsBrownout);
            Assert.Less(grid.Last.ServedW, LoadW);

            grid.Step(LoadW * 2.0, 0.25, 0.0);
            grid.Step(LoadW * 2.0, 0.25, 0.0);
            Assert.IsFalse(grid.IsBrownout);
            Assert.AreEqual(1, started);
            Assert.AreEqual(1, ended);
        }

        [Test]
        public void Rtg_KeepsSmallLoadsRunning()
        {
            var grid = new PowerGrid(new Battery(0.0), demandW: 400.0, rtgUnits: 4);
            GridStep step = grid.Step(0.0, 1.0, 0.0);
            Assert.AreEqual(440.0, step.RtgW, 1e-9);
            Assert.IsFalse(step.IsBrownout);
            Assert.AreEqual(4 * 72.0, grid.RtgPowerW(17.0), 1e-9, "fades like the MMRTG");
        }

        [Test]
        public void Lander_BatteryAloneLastsAbout19Hours()
        {
            PowerGrid grid = PowerGrid.Lander();
            double hours = 0.0;
            while (!grid.Step(0.0, 0.25, 0.0).IsBrownout && hours < 100.0)
                hours += 0.25;
            // 2 MWh × √0.9 / (100 kW − 440 W) ≈ 19.06 h.
            Assert.AreEqual(19.06, hours, 0.3);
        }
    }

    public class SimulationGridTests
    {
        static readonly PlanetSolarParams Earthlike = new PlanetSolarParams(
            SolarConstants.SolarLuminosity, SolarConstants.AstronomicalUnit, 0.0167, 23.44 * Math.PI / 180.0,
            282.94 * Math.PI / 180.0, 24.0, 365.256 * 24.0, 0.32, 0.3, 25.0);

        static StubPlanetSimulation Create()
        {
            var sphere = HexSphere.Create(12);
            return new StubPlanetSimulation(sphere, PlanetSurface.Generate(sphere, new SurfaceSettings(7)), Earthlike);
        }

        [Test]
        public void OnePanel_CarriesTheBaseThroughTheFirstNight()
        {
            // The colony only starts to grow after a full day without a brownout, so the first night is at base load.
            var sim = Create();
            int tile = sim.BaseTileId;
            Assert.IsTrue(sim.PlacePanel(tile, 0, Math.Abs(sim.GetTile(tile).Latitude), Math.PI));
            bool brownout = false;
            sim.BrownoutChanged += on => brownout |= on;
            double lowest = double.MaxValue;
            for (int i = 0; i < 30 * 4; i++)
            {
                sim.Tick(0.25);
                lowest = Math.Min(lowest, sim.BatteryStoredWh);
            }
            Assert.IsFalse(brownout);
            Assert.Less(lowest, sim.BatteryCapacityWh * 0.8, "the battery works at night");
        }

        [Test]
        public void Batteries_AreBuildings_ThatAddStorage()
        {
            var sim = Create();
            int tile = sim.BaseTileId;
            double before = sim.BatteryCapacityWh;
            Assert.IsTrue(sim.PlaceBattery(tile, 3));
            Assert.AreEqual(before + sim.BatteryUnitWh, sim.BatteryCapacityWh, 1e-6);
            Assert.AreEqual(BuildingType.Battery, sim.GetBuilding(tile, 3));
            Assert.AreEqual(1, sim.BatteryCount(tile));
            Assert.IsFalse(sim.PlacePanel(tile, 3, 0.3, Math.PI), "slot taken by the battery");
            Assert.IsFalse(sim.PlaceBattery(tile, 3), "one building per slot");

            Assert.IsTrue(sim.PlacePanel(tile, 4, 0.3, Math.PI));
            Assert.IsFalse(sim.PlaceBattery(tile, 4), "slot taken by the panel");
            Assert.AreEqual(BuildingType.SolarPanel, sim.GetBuilding(tile, 4));

            Assert.IsTrue(sim.RemoveBuilding(tile, 3));
            Assert.AreEqual(before, sim.BatteryCapacityWh, 1e-6);
            Assert.AreEqual(BuildingType.None, sim.GetBuilding(tile, 3));
            Assert.IsTrue(sim.RemoveBuilding(tile, 4), "removes panels too");
            Assert.IsFalse(sim.RemoveBuilding(tile, 4));

            int water = -1;
            for (int t = 0; t < sim.TileCount && water < 0; t++)
                if (sim.GetTile(t).IsWater) water = t;
            Assert.IsFalse(sim.PlaceBattery(water, 0), "not on water");
        }

        [Test]
        public void ColonyGrows_WithPanels_AndFurtherWithBatteries()
        {
            StubPlanetSimulation Run(int batteryCount, out int brownouts)
            {
                var sim = Create();
                int tile = sim.BaseTileId;
                double tilt = Math.Abs(sim.GetTile(tile).Latitude);
                for (int s = 0; s < 4; s++)
                    Assert.IsTrue(sim.PlacePanel(tile, s, tilt, Math.PI));
                for (int s = 0; s < batteryCount; s++)
                    Assert.IsTrue(sim.PlaceBattery(tile, 30 + 10 * s));   // battery yards cover two slots: keep them apart
                int count = 0;
                sim.BrownoutChanged += on => { if (on) count++; };
                sim.Tick(20 * 24.0);
                brownouts = count;
                return sim;
            }

            var noStorage = Run(0, out int brownoutsWithout);
            var storage = Run(2, out int brownoutsWith);
            Assert.Greater(noStorage.DemandW, 100_000.0, "panels let the colony grow a little");
            Assert.Greater(brownoutsWithout, 0, "but without storage the nights run dry");
            Assert.AreEqual(0, brownoutsWith, "two battery yards carry the nights");
            Assert.Greater(storage.DemandW, 2.0 * noStorage.DemandW, "storage lets it grow much further");
            Assert.AreEqual(storage.DemandTargetW, storage.DemandW, storage.DemandTargetW * 0.1, "close to what the panels support");
            Assert.Greater(storage.KardashevK, noStorage.KardashevK);
        }
    }
}
