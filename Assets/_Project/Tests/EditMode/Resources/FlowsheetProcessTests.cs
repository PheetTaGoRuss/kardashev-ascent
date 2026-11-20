using System.Collections.Generic;
using System.Linq;
using KardashevAscent.Simulation.Resources;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Resources
{
    /// <summary>The refining recipes as industrial processes (MO-06, MO-08): mass balances, named byproducts, heat balances.</summary>
    public class FlowsheetProcessTests
    {
        static double In(IndustrialProcess p) => p.Inputs.Sum(a => a.Kg);
        static double Out(IndustrialProcess p) => p.Outputs.Sum(a => a.Kg);

        [Test]
        public void EveryProcess_BalancesInMass_ToANanogram()
        {
            foreach (RefineProcess p in Refining.Processes)
            {
                IndustrialProcess proc = Refining.ProcessOf(p);
                Assert.AreEqual(In(proc), Out(proc), 1e-9, p + ": inputs = outputs + byproducts");
            }
        }

        [Test]
        public void EveryProcess_NamesEveryOutput_NoGasDisappears()
        {
            foreach (RefineProcess p in Refining.Processes)
            {
                RefineRecipe r = Refining.Of(p);
                IndustrialProcess proc = Refining.ProcessOf(p);
                if (r.VentedKg > 0.0)
                {
                    Assert.IsTrue(r.Byproduct.HasValue, p + " gives off a gas that has a name");
                    Assert.AreEqual(r.VentedKg, proc.Outputs.First(a => a.Material == r.Byproduct.Value).Kg, 1e-9);
                    Assert.AreEqual(StorageClass.PressurizedGas, MaterialCatalog.PropertiesOf(r.Byproduct.Value).StorageClass, p + " byproduct is a gas");
                }
                else
                    Assert.IsFalse(r.Byproduct.HasValue, p.ToString());
            }
        }

        [Test]
        public void HallHeroult_MakesOxygen_AndTheDownsCell_Chlorine()
        {
            Assert.AreEqual(Material.Oxygen, Refining.Of(RefineProcess.Aluminium).Byproduct);
            Assert.AreEqual(Material.Chlorine, Refining.Of(RefineProcess.SodiumFromSalt).Byproduct);
            // chlorine per kg sodium: 70.90 / 45.98 = 1.542 (stoichiometry, 2NaCl -> 2Na + Cl2)
            RefineRecipe na = Refining.Of(RefineProcess.SodiumFromSalt);
            Assert.AreEqual(1.542, na.VentedKg / na.OutputKg, 0.005);
            // oxygen per kg aluminium at least the stoichiometric 0.890 (the recipe lumps a little loss in with it)
            RefineRecipe al = Refining.Of(RefineProcess.Aluminium);
            Assert.GreaterOrEqual(al.VentedKg / al.OutputKg, 0.889);
            Assert.LessOrEqual(al.VentedKg / al.OutputKg, 0.99);
        }

        [Test]
        public void ElectricityIsAlwaysAtLeastTheChemicalEnergyOfTheProduct()
        {
            foreach (RefineProcess p in Refining.Processes)
            {
                IndustrialProcess proc = Refining.ProcessOf(p);
                Assert.GreaterOrEqual(proc.ElectricityKWhPerBatch, proc.ProductEnthalpyKWhPerBatch, p + ": the recipe's energy is below the reaction enthalpy");
            }
        }

        [Test]
        public void Electricity_EqualsProductEnthalpyPlusWasteHeat()
        {
            foreach (RefineProcess p in Refining.Processes)
            {
                IndustrialProcess proc = Refining.ProcessOf(p);
                Assert.AreEqual(proc.ElectricityKWhPerBatch + proc.ProcessHeatKWhPerBatch, proc.ProductEnthalpyKWhPerBatch + proc.WasteHeatKWhPerBatch, 1e-9, p.ToString());
                Assert.GreaterOrEqual(proc.WasteHeatKWhPerBatch, 0.0, p.ToString());
            }
        }

        [Test]
        public void TheElectrolysisEnthalpies_MatchTheStandardTables()
        {
            // 8.63 kWh per kg Al and 4.97 kWh per kg Na (formation enthalpies of Al2O3 and NaCl).
            Assert.AreEqual(8.63, Refining.AluminiumReactionWhPerKg / 1000.0, 0.01);
            Assert.AreEqual(4.97, Refining.SodiumReactionWhPerKg / 1000.0, 0.01);
            RefineRecipe al = Refining.Of(RefineProcess.Aluminium);
            // 13.5 kWh/kg paid, 8.63 kWh/kg kept as chemical energy: the rest is heat
            Assert.AreEqual((13_500.0 - 8_628.0) * al.OutputKg, al.WasteHeatWh, 1e-6);
            Assert.Greater(al.WasteHeatW, 0.0);
        }

        static ProcessPorts Ports(MaterialStock input, MaterialStock output, double outputRoomKg)
        {
            return new ProcessPorts
            {
                Available = m => input[m],
                Take = (m, kg) => input.TryTake(m, kg),
                CanStoreAll = list => list.Sum(a => a.Kg) <= outputRoomKg - output.TotalKgForTests() + 1e-9,
                Store = (m, kg) => output.Add(m, kg),
            };
        }

        [Test]
        public void TryRunThroughPorts_MovesInputsToOutputs_AndConservesMass()
        {
            var input = new MaterialStock();
            var output = new MaterialStock();
            IndustrialProcess proc = Refining.ProcessOf(RefineProcess.SodiumFromSalt);
            input.Add(Material.Salt, 100.0);
            var env = new ProcessEnvironment(1000.0, 0.0, 1000.0, true);
            ProcessResult result = IndustrialProcessor.TryRun(proc, Ports(input, output, 1000.0), env);
            Assert.IsTrue(result.Succeeded, result.Report);
            Assert.AreEqual(10.0, output[Material.Sodium], 1e-9);
            Assert.AreEqual(proc.Outputs.First(a => a.Material == Material.Chlorine).Kg, output[Material.Chlorine], 1e-9);
            Assert.AreEqual(100.0, input[Material.Salt] + output.TotalKgForTests(), 1e-9, "100 kg of salt became 100 kg of products");
        }

        [Test]
        public void ANoRoomOutputStore_StopsThePlant_WithTheRightBlocker()
        {
            var input = new MaterialStock();
            var output = new MaterialStock();
            input.Add(Material.Salt, 100.0);
            var env = new ProcessEnvironment(1000.0, 0.0, 1000.0, true);
            ProcessResult result = IndustrialProcessor.TryRun(Refining.ProcessOf(RefineProcess.SodiumFromSalt), Ports(input, output, 5.0), env);
            Assert.AreEqual(IndustrialBlocker.OutputStorageUnavailable, result.Blocker);
            Assert.AreEqual(100.0, input[Material.Salt], 1e-12, "nothing was taken");
        }

        [Test]
        public void MissingInputAndMissingPower_AreReportedWithTheirOwnBlockers()
        {
            var input = new MaterialStock();
            var output = new MaterialStock();
            IndustrialProcess proc = Refining.ProcessOf(RefineProcess.SodiumFromSalt);
            var env = new ProcessEnvironment(1000.0, 0.0, 1000.0, true);
            Assert.AreEqual(IndustrialBlocker.MissingInput, IndustrialProcessor.TryRun(proc, Ports(input, output, 1000.0), env).Blocker);
            input.Add(Material.Salt, 100.0);
            Assert.AreEqual(IndustrialBlocker.InsufficientElectricity,
                            IndustrialProcessor.TryRun(proc, Ports(input, output, 1000.0), new ProcessEnvironment(0.0, 0.0, 1000.0, true)).Blocker);
        }

        [Test]
        public void SlagCanStandInForRock_OneForOne()
        {
            var bill = new MaterialBill().Add(Material.Rock, 200.0).Add(Material.Iron, 50.0);
            MaterialBill swapped = StandIns.Apply(bill, StandIn.SlagForRock);
            Assert.AreEqual(0.0, swapped[Material.Rock], 1e-12);
            Assert.AreEqual(200.0, swapped[Material.Slag], 1e-12);
            Assert.AreEqual(50.0, swapped[Material.Iron], 1e-12);
            Assert.AreEqual(bill.TotalKg, swapped.TotalKg, 1e-12, "same mass");
        }
    }

    static class StockTestExtensions
    {
        public static double TotalKgForTests(this MaterialStock s) => Materials.All.Sum(m => s[m]);
    }
}
