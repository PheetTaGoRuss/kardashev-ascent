using KardashevAscent.Simulation.Resources;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Resources
{
    public class IndustrialProductionTests
    {
        static IndustrialProcess AluminiumRefining()
        {
            return new IndustrialProcess("aluminium-refining", "Aluminium refining")
            {
                ElectricityKWhPerBatch = 150.0,
                ProcessHeatKWhPerBatch = 20.0,
                WasteHeatKWhPerBatch = 120.0,
                RequiresControlledEnvironment = true,
            }
            .AddInput(Material.Rock, 60.0)
            .AddOutput(Material.Aluminium, 10.0);
        }

        [Test]
        public void Storage_RejectsAIncompatibleMaterialClass()
        {
            var storage = new MaterialStorage(StorageClass.BulkSolid, 100.0);

            Assert.IsTrue(storage.TryStore(Material.Iron, 25.0));
            Assert.IsFalse(storage.TryStore(Material.Lithium, 1.0));
            Assert.AreEqual(25.0, storage.UsedKg, 1e-9);
        }

        [Test]
        public void Process_DoesNotConsumeInputsWhenCoolingIsUnavailable()
        {
            var input = new MaterialStock();
            input.Add(Material.Rock, 60.0);
            var output = new MaterialStorage(StorageClass.BulkSolid, 100.0);

            ProcessResult result = IndustrialProcessor.TryRun(AluminiumRefining(), input, output,
                new ProcessEnvironment(200.0, 30.0, 119.0, true));

            Assert.AreEqual(IndustrialBlocker.InsufficientCooling, result.Blocker);
            Assert.AreEqual(60.0, input[Material.Rock], 1e-9);
            Assert.AreEqual(0.0, output[Material.Aluminium], 1e-9);
            StringAssert.Contains("cooling capacity", result.Report);
        }

        [Test]
        public void Process_ConsumesInputsAndCreatesStoredOutputWhenAllRequirementsAreMet()
        {
            var input = new MaterialStock();
            input.Add(Material.Rock, 60.0);
            var output = new MaterialStorage(StorageClass.BulkSolid, 100.0);

            ProcessResult result = IndustrialProcessor.TryRun(AluminiumRefining(), input, output,
                new ProcessEnvironment(200.0, 30.0, 120.0, true));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(0.0, input[Material.Rock], 1e-9);
            Assert.AreEqual(10.0, output[Material.Aluminium], 1e-9);
            StringAssert.Contains("completed", result.Report);
        }

        [Test]
        public void Process_ReportsTheSpecificMissingRequirement()
        {
            var input = new MaterialStock();
            input.Add(Material.Rock, 60.0);
            var output = new MaterialStorage(StorageClass.BulkSolid, 100.0);

            ProcessResult result = IndustrialProcessor.TryRun(AluminiumRefining(), input, output,
                new ProcessEnvironment(200.0, 30.0, 120.0, false));

            Assert.AreEqual(IndustrialBlocker.EnvironmentUnavailable, result.Blocker);
            StringAssert.Contains("controlled environment", result.Report);
        }
    }
}
