using KardashevAscent.Simulation.Energy;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Energy
{
    public class ColonyDemandTests
    {
        const double Base = 100_000.0;

        static void Run(ColonyDemand colony, double hours, double generationW, double servedW, bool brownout)
        {
            for (double t = 0.0; t < hours - 1e-9; t += 0.25)
                colony.Step(0.25, generationW, servedW, brownout);
        }

        [Test]
        public void SteadySupply_GrowsTowardTheUsableShare()
        {
            var colony = new ColonyDemand(Base, 24.0);
            Run(colony, 40 * 24.0, 1_000_000.0, 1_000_000.0, false);
            Assert.AreEqual(ColonyDemand.UsableShare * 1_000_000.0, colony.TargetW, 1.0);
            Assert.AreEqual(colony.TargetW, colony.DemandW, colony.TargetW * 0.01);
            Assert.IsFalse(colony.IsGrowing, "settled");
        }

        [Test]
        public void AfterABrownout_WaitsADayBeforeGrowing()
        {
            var colony = new ColonyDemand(Base, 24.0);
            colony.Step(0.25, 1_000_000.0, Base, true);
            double after = colony.DemandW;
            Run(colony, 23.0, 1_000_000.0, Base, false);
            Assert.AreEqual(after, colony.DemandW, 1e-9, "no growth within the day after a brownout");
            Run(colony, 6.0, 1_000_000.0, Base, false);
            Assert.Greater(colony.DemandW, after, "grows again after a clean day");
            Assert.IsTrue(colony.IsGrowing);
        }

        [Test]
        public void Brownout_CutsBackTowardWhatWasServed_ButNotBelowBase()
        {
            var colony = new ColonyDemand(Base, 24.0);
            Run(colony, 30 * 24.0, 2_000_000.0, 2_000_000.0, false);
            double grown = colony.DemandW;
            Assert.Greater(grown, 1_000_000.0);

            Run(colony, 6.0, 0.0, 500_000.0, true);
            Assert.Less(colony.DemandW, grown, "cuts back");
            Assert.Greater(colony.DemandW, 500_000.0, "not below what it still gets");
            Run(colony, 48.0, 0.0, 0.0, true);
            Assert.AreEqual(Base, colony.DemandW, Base * 0.01, "down to life support");
        }

        [Test]
        public void TidallyLockedPlanet_UsesA24HourDay()
        {
            Assert.AreEqual(24.0, new ColonyDemand(Base, 0.0).DayHours);
        }
    }
}
