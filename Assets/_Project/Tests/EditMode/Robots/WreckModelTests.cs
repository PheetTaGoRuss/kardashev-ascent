using System.Linq;
using KardashevAscent.Simulation.Resources;
using KardashevAscent.Simulation.Robots;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Robots
{
    /// <summary>What is left of a destroyed robot by cause and peak temperature (OQ-9).</summary>
    public class WreckModelTests
    {
        static readonly System.Collections.Generic.Dictionary<Material, double> Bill = WreckModel.BillOf(RobotKind.General, false);
        static double Kg(System.Collections.Generic.Dictionary<Material, double> d, Material m) => d.TryGetValue(m, out double v) ? v : 0.0;

        [Test]
        public void Cold_LeavesTheStructureIntact_AndTheElectronicsAreWaste()
        {
            var r = WreckModel.Residue(Bill, -60.0);
            Assert.AreEqual(Kg(Bill, Material.Iron), Kg(r, Material.Iron), 1e-9);
            Assert.AreEqual(Kg(Bill, Material.Rock), Kg(r, Material.Rock), 1e-9);
            Assert.AreEqual(WreckModel.Total(Bill), WreckModel.Total(r), 1e-9, "mass is conserved");
            Assert.Greater(Kg(r, Material.ElectronicWaste), 2.0, "the cell and the boards");
            Assert.AreEqual(0.0, Kg(r, Material.Slag));
            Assert.IsTrue(WreckModel.ElectronicsSurvive(DeathCause.ColdOrEmptyBattery, -60.0));
        }

        [Test]
        public void HeatTakesPartsToSlagAboveTheirThresholds_AndMassIsNeverGained()
        {
            double bill = WreckModel.Total(Bill);
            var solder = WreckModel.Residue(Bill, 300.0);
            Assert.AreEqual(0.0, Kg(solder, Material.ElectronicWaste), "boards above solder are slag");
            Assert.Greater(Kg(solder, Material.Slag), 0.0); Assert.AreEqual(Kg(Bill, Material.Iron), Kg(solder, Material.Iron), 1e-9);
            var al = WreckModel.Residue(Bill, 700.0);
            Assert.AreEqual(0.0, Kg(al, Material.Aluminium));
            Assert.Greater(Kg(al, Material.Copper), 0.0);
            var cu = WreckModel.Residue(Bill, 1200.0);
            Assert.AreEqual(0.0, Kg(cu, Material.Copper)); Assert.Greater(Kg(cu, Material.Iron), 0.0);
            var fe = WreckModel.Residue(Bill, 1600.0);
            Assert.AreEqual(0.0, Kg(fe, Material.Iron)); Assert.AreEqual(bill, Kg(fe, Material.Slag), 1e-9, "only slag, all of the mass");
            Assert.AreEqual(0.0, WreckModel.Total(WreckModel.Residue(Bill, 3000.0)), "above iron's boiling point nothing is left");
            foreach (double t in new[] { -50.0, 100.0, 300.0, 700.0, 1200.0, 1600.0, 2900.0 }) Assert.LessOrEqual(WreckModel.Total(WreckModel.Residue(Bill, t)), bill + 1e-9);
            Assert.IsFalse(WreckModel.ElectronicsSurvive(DeathCause.Heat, 100.0));
            Assert.IsFalse(WreckModel.ElectronicsSurvive(DeathCause.ColdOrEmptyBattery, 300.0));
        }
    }
}
