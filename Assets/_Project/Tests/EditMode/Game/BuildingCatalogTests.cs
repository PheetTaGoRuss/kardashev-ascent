using System;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Robots;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>The building definitions table (docs/architecture/data-architecture.md, step D2).</summary>
    public class BuildingCatalogTests
    {
        [Test]
        public void EveryBuildingType_HasADefinition_WithAName()
        {
            foreach (BuildingType type in Enum.GetValues(typeof(BuildingType)))
            {
                if (type == BuildingType.None) { Assert.IsNull(BuildingCatalog.Get(type)); continue; }
                BuildingDefinition d = BuildingCatalog.Get(type);
                Assert.IsNotNull(d, type + " needs a row in BuildingCatalog");
                Assert.IsFalse(string.IsNullOrWhiteSpace(d.Name), type + " needs a name");
                Assert.GreaterOrEqual(d.FootprintSlots, 1, type + " covers at least one slot");
            }
        }

        [Test]
        public void FixedBills_AreTheStationBills()
        {
            Assert.AreSame(KardashevAscent.Simulation.Energy.BuildCosts.Workshop, BuildingCatalog.Get(BuildingType.Workshop).FixedBill);
            Assert.AreSame(KardashevAscent.Simulation.Energy.BuildCosts.ChargingStation, BuildingCatalog.Get(BuildingType.ChargingStation).FixedBill);
            Assert.AreSame(KardashevAscent.Simulation.Energy.BuildCosts.Relay, BuildingCatalog.Get(BuildingType.Relay).FixedBill);
            Assert.IsNull(BuildingCatalog.Get(BuildingType.SolarPanel).FixedBill, "panels are priced from their area");
            Assert.IsNull(BuildingCatalog.Get(BuildingType.Battery).FixedBill, "batteries are priced from their size");
        }

        [Test]
        public void Footprints_ComeFromTheTable()
        {
            Assert.AreEqual(2, BuildingFootprints.SlotsFor(BuildingType.Battery, 0));
            Assert.AreEqual(3, BuildingFootprints.SlotsFor(BuildingType.Workshop, 0));
            Assert.AreEqual(1, BuildingFootprints.SlotsFor(BuildingType.Relay, 0));
            Assert.AreEqual(7, BuildingFootprints.SlotsFor(BuildingType.SolarPanel, 3), "solar installations grow with their tier");
        }
    }
}
