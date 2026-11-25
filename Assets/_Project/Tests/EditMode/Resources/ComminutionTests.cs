using System;
using KardashevAscent.Simulation.Resources;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Resources
{
    /// <summary>Bond's law for crushing and grinding (B5).</summary>
    public class ComminutionTests
    {
        [Test]
        public void BondsLaw_MatchesTheFormula()
        {
            double expected = 10.0 * 13.13 * (1.0 / Math.Sqrt(150.0) - 1.0 / Math.Sqrt(10_000.0)) * 1.10231;
            Assert.AreEqual(expected, Comminution.KWhPerTonneRock(Material.Copper), 1e-9);
            Assert.That(Comminution.KWhPerTonneRock(Material.Copper), Is.InRange(10.0, 20.0), "about 10 to 20 kWh per tonne of ore, the usual grinding figure");
        }

        [Test]
        public void LowGradeCopper_CostsMWhPerTonneOfCopper_AndRichIronCostsNothing()
        {
            double perTonneCu = Comminution.KWhPerTonneProduct(Material.Copper, 0.005);
            TestContext.WriteLine($"copper at 0.5 %: {perTonneCu:0} kWh per tonne of Cu");
            Assert.That(perTonneCu, Is.InRange(1500.0, 4000.0), "on the order of MWh per tonne");
            Assert.AreEqual(0.0, Comminution.KWhPerTonneProduct(Material.Iron, 0.6));
            Assert.Greater(Comminution.KWhPerTonneProduct(Material.Iron, 0.4), 0.0);
            Assert.AreEqual(0.0, Comminution.KWhPerTonneProduct(Material.Rock, 1.0));
        }

        [Test]
        public void PoorerOre_CostsProportionallyMore()
        {
            Assert.AreEqual(2.0 * Comminution.KWhPerTonneProduct(Material.Copper, 0.01), Comminution.KWhPerTonneProduct(Material.Copper, 0.005), 1e-9);
        }
    }
}
