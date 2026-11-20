using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Resources
{
    public enum MaterialPhase { Solid, Liquid, Gas }

    public enum StorageClass { BulkSolid, SealedLiquid, PressurizedGas, Hazardous }

    public enum IndustrialBlocker
    {
        None,
        MissingInput,
        InsufficientElectricity,
        InsufficientProcessHeat,
        InsufficientCooling,
        EnvironmentUnavailable,
        OutputStorageUnavailable,
    }

    public readonly struct MaterialProperties
    {
        public readonly MaterialPhase Phase;
        public readonly StorageClass StorageClass;
        public readonly double DensityKgPerM3;

        public MaterialProperties(MaterialPhase phase, StorageClass storageClass, double densityKgPerM3)
        {
            Phase = phase;
            StorageClass = storageClass;
            DensityKgPerM3 = densityKgPerM3;
        }
    }

    /// <summary>Physical properties used by the first industrial simulation layer. Values are deliberately broad gameplay bands.</summary>
    public static class MaterialCatalog
    {
        public static MaterialProperties PropertiesOf(Material material)
        {
            switch (material)
            {
                case Material.Water: return new MaterialProperties(MaterialPhase.Solid, StorageClass.BulkSolid, 917.0);
                case Material.Sodium: return new MaterialProperties(MaterialPhase.Solid, StorageClass.Hazardous, 968.0);
                case Material.Lithium: return new MaterialProperties(MaterialPhase.Solid, StorageClass.Hazardous, 534.0);
                case Material.Aluminium: return new MaterialProperties(MaterialPhase.Solid, StorageClass.BulkSolid, 2700.0);
                // Gas densities at 0 C and 1 atm (recalled from standard tables; to verify): O2 1.429, Cl2 3.214 kg/m3. Stored compressed in a gas tank.
                case Material.Oxygen: return new MaterialProperties(MaterialPhase.Gas, StorageClass.PressurizedGas, 1.429);
                case Material.Chlorine: return new MaterialProperties(MaterialPhase.Gas, StorageClass.PressurizedGas, 3.214);
                case Material.Copper: return new MaterialProperties(MaterialPhase.Solid, StorageClass.BulkSolid, 8960.0);
                case Material.Iron: return new MaterialProperties(MaterialPhase.Solid, StorageClass.BulkSolid, 7874.0);
                default: return new MaterialProperties(MaterialPhase.Solid, StorageClass.BulkSolid, 2650.0);
            }
        }
    }

    public readonly struct MaterialAmount
    {
        public readonly Material Material;
        public readonly double Kg;

        public MaterialAmount(Material material, double kg)
        {
            Material = material;
            Kg = kg;
        }
    }

    /// <summary>A physical store with an explicit material class and finite mass capacity.</summary>
    public sealed class MaterialStorage
    {
        readonly Dictionary<Material, double> contents = new Dictionary<Material, double>();

        public MaterialStorage(StorageClass storageClass, double capacityKg)
        {
            if (capacityKg < 0.0) throw new ArgumentOutOfRangeException(nameof(capacityKg));
            StorageClass = storageClass;
            CapacityKg = capacityKg;
        }

        public StorageClass StorageClass { get; }
        public double CapacityKg { get; }
        public double UsedKg { get; private set; }
        public double FreeKg => Math.Max(0.0, CapacityKg - UsedKg);
        public double this[Material material] => contents.TryGetValue(material, out double kg) ? kg : 0.0;

        public bool CanStore(Material material, double kg)
        {
            return kg >= 0.0 && MaterialCatalog.PropertiesOf(material).StorageClass == StorageClass && kg <= FreeKg + 1e-9;
        }

        public bool CanStoreAll(IReadOnlyList<MaterialAmount> amounts)
        {
            if (amounts == null) return false;
            double requiredKg = 0.0;
            foreach (MaterialAmount amount in amounts)
            {
                if (amount.Kg < 0.0 || MaterialCatalog.PropertiesOf(amount.Material).StorageClass != StorageClass)
                    return false;
                requiredKg += amount.Kg;
            }
            return requiredKg <= FreeKg + 1e-9;
        }

        public bool TryStore(Material material, double kg)
        {
            if (!CanStore(material, kg)) return false;
            contents[material] = this[material] + kg;
            UsedKg += kg;
            return true;
        }

        public bool TryTake(Material material, double kg)
        {
            if (kg < 0.0 || this[material] < kg - 1e-9) return false;
            contents[material] = Math.Max(0.0, this[material] - kg);
            UsedKg = Math.Max(0.0, UsedKg - kg);
            return true;
        }
    }

    /// <summary>One process recipe and the services it must receive for a single batch.</summary>
    public sealed class IndustrialProcess
    {
        public readonly List<MaterialAmount> Inputs = new List<MaterialAmount>();
        public readonly List<MaterialAmount> Outputs = new List<MaterialAmount>();

        public string Id { get; }
        public string DisplayName { get; }
        public double ElectricityKWhPerBatch { get; set; }
        public double ProcessHeatKWhPerBatch { get; set; }
        public double WasteHeatKWhPerBatch { get; set; }
        /// <summary>Chemical energy the products hold above the inputs, kWh per batch (reaction enthalpy x amount; zero where not a chemical change or not sourced yet).</summary>
        public double ProductEnthalpyKWhPerBatch { get; set; }
        public bool RequiresControlledEnvironment { get; set; }

        public IndustrialProcess(string id, string displayName)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A process needs a stable id.", nameof(id));
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        }

        public IndustrialProcess AddInput(Material material, double kg)
        {
            if (kg <= 0.0) throw new ArgumentOutOfRangeException(nameof(kg));
            Inputs.Add(new MaterialAmount(material, kg));
            return this;
        }

        public IndustrialProcess AddOutput(Material material, double kg)
        {
            if (kg <= 0.0) throw new ArgumentOutOfRangeException(nameof(kg));
            Outputs.Add(new MaterialAmount(material, kg));
            return this;
        }
    }

    /// <summary>Where a process takes its inputs from and puts its outputs: a plant's input and output buffers.</summary>
    public sealed class ProcessPorts
    {
        public System.Func<Material, double> Available;
        public System.Func<Material, double, bool> Take;
        /// <summary>Can all these outputs be put away together?</summary>
        public System.Func<IReadOnlyList<MaterialAmount>, bool> CanStoreAll;
        public System.Action<Material, double> Store;
    }

    public readonly struct ProcessEnvironment
    {
        public readonly double AvailableElectricityKWh;
        public readonly double AvailableProcessHeatKWh;
        public readonly double AvailableCoolingKWh;
        public readonly bool IsControlled;

        public ProcessEnvironment(double availableElectricityKWh, double availableProcessHeatKWh, double availableCoolingKWh, bool isControlled)
        {
            AvailableElectricityKWh = Math.Max(0.0, availableElectricityKWh);
            AvailableProcessHeatKWh = Math.Max(0.0, availableProcessHeatKWh);
            AvailableCoolingKWh = Math.Max(0.0, availableCoolingKWh);
            IsControlled = isControlled;
        }
    }

    public readonly struct ProcessResult
    {
        public readonly IndustrialBlocker Blocker;
        public readonly string Report;
        public readonly double BatchesCompleted;

        public ProcessResult(IndustrialBlocker blocker, string report, double batchesCompleted)
        {
            Blocker = blocker;
            Report = report;
            BatchesCompleted = batchesCompleted;
        }

        public bool Succeeded => Blocker == IndustrialBlocker.None;
    }

    /// <summary>A short, player-facing explanation of an industrial event or constraint.</summary>
    [Serializable]
    public sealed class IndustrialReport
    {
        public double TimeHours;
        public string Facility;
        public IndustrialBlocker Blocker;
        public string Message;

        public bool IsProblem => Blocker != IndustrialBlocker.None;
    }

    /// <summary>Bounded chronological report history used by robot reports and the offline assistant.</summary>
    public sealed class IndustrialReportLog
    {
        const int Capacity = 12;
        readonly List<IndustrialReport> reports = new List<IndustrialReport>();

        public IReadOnlyList<IndustrialReport> Reports => reports;

        public void Add(double timeHours, string facility, IndustrialBlocker blocker, string message)
        {
            reports.Add(new IndustrialReport { TimeHours = timeHours, Facility = facility ?? "Industry", Blocker = blocker, Message = message ?? "" });
            if (reports.Count > Capacity)
                reports.RemoveAt(0);
        }

        public void Restore(IEnumerable<IndustrialReport> savedReports)
        {
            reports.Clear();
            if (savedReports == null) return;
            foreach (IndustrialReport report in savedReports)
            {
                if (report != null)
                    reports.Add(report);
            }
            while (reports.Count > Capacity)
                reports.RemoveAt(0);
        }
    }

    /// <summary>Executes one complete batch only when material, energy, heat, environment, and storage are all available.</summary>
    public static class IndustrialProcessor
    {
        public static ProcessResult TryRun(IndustrialProcess process, MaterialStock inputStock, MaterialStorage outputStorage, ProcessEnvironment environment, double batches = 1.0)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            if (inputStock == null) throw new ArgumentNullException(nameof(inputStock));
            if (outputStorage == null) throw new ArgumentNullException(nameof(outputStorage));
            if (batches <= 0.0) throw new ArgumentOutOfRangeException(nameof(batches));

            if (process.RequiresControlledEnvironment && !environment.IsControlled)
                return Blocked(IndustrialBlocker.EnvironmentUnavailable, process, "a controlled environment");
            if (environment.AvailableElectricityKWh + 1e-9 < process.ElectricityKWhPerBatch * batches)
                return Blocked(IndustrialBlocker.InsufficientElectricity, process, "electricity");
            if (environment.AvailableProcessHeatKWh + 1e-9 < process.ProcessHeatKWhPerBatch * batches)
                return Blocked(IndustrialBlocker.InsufficientProcessHeat, process, "process heat");
            if (environment.AvailableCoolingKWh + 1e-9 < process.WasteHeatKWhPerBatch * batches)
                return Blocked(IndustrialBlocker.InsufficientCooling, process, "cooling capacity");

            foreach (MaterialAmount input in process.Inputs)
                if (inputStock[input.Material] + 1e-9 < input.Kg * batches)
                    return Blocked(IndustrialBlocker.MissingInput, process, Materials.Name(input.Material).ToLowerInvariant());

            var outputs = new List<MaterialAmount>(process.Outputs.Count);
            foreach (MaterialAmount output in process.Outputs)
                outputs.Add(new MaterialAmount(output.Material, output.Kg * batches));
            if (!outputStorage.CanStoreAll(outputs))
                return Blocked(IndustrialBlocker.OutputStorageUnavailable, process, "compatible output storage");

            foreach (MaterialAmount input in process.Inputs)
                inputStock.TryTake(input.Material, input.Kg * batches);
            foreach (MaterialAmount output in outputs)
                outputStorage.TryStore(output.Material, output.Kg);

            return new ProcessResult(IndustrialBlocker.None, $"{process.DisplayName} completed {batches:0.##} batch(es).", batches);
        }

        /// <summary>The same, through ports (a plant's buffers) instead of one stock and one storage.</summary>
        public static ProcessResult TryRun(IndustrialProcess process, ProcessPorts ports, ProcessEnvironment environment, double batches = 1.0)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            if (ports == null) throw new ArgumentNullException(nameof(ports));
            if (batches <= 0.0) throw new ArgumentOutOfRangeException(nameof(batches));
            if (process.RequiresControlledEnvironment && !environment.IsControlled)
                return Blocked(IndustrialBlocker.EnvironmentUnavailable, process, "a controlled environment");
            if (environment.AvailableElectricityKWh + 1e-9 < process.ElectricityKWhPerBatch * batches)
                return Blocked(IndustrialBlocker.InsufficientElectricity, process, "electricity");
            if (environment.AvailableProcessHeatKWh + 1e-9 < process.ProcessHeatKWhPerBatch * batches)
                return Blocked(IndustrialBlocker.InsufficientProcessHeat, process, "process heat");
            if (environment.AvailableCoolingKWh + 1e-9 < process.WasteHeatKWhPerBatch * batches)
                return Blocked(IndustrialBlocker.InsufficientCooling, process, "cooling capacity");
            foreach (MaterialAmount input in process.Inputs)
                if (ports.Available(input.Material) + 1e-9 < input.Kg * batches)
                    return Blocked(IndustrialBlocker.MissingInput, process, Materials.Name(input.Material).ToLowerInvariant());
            var outputs = new List<MaterialAmount>(process.Outputs.Count);
            foreach (MaterialAmount output in process.Outputs)
                outputs.Add(new MaterialAmount(output.Material, output.Kg * batches));
            if (!ports.CanStoreAll(outputs))
                return Blocked(IndustrialBlocker.OutputStorageUnavailable, process, "room in the output store");
            foreach (MaterialAmount input in process.Inputs)
                ports.Take(input.Material, input.Kg * batches);
            foreach (MaterialAmount output in outputs)
                ports.Store(output.Material, output.Kg);
            return new ProcessResult(IndustrialBlocker.None, $"{process.DisplayName} completed {batches:0.##} batch(es).", batches);
        }

        static ProcessResult Blocked(IndustrialBlocker blocker, IndustrialProcess process, string requirement)
        {
            return new ProcessResult(blocker, $"{process.DisplayName} is waiting for {requirement}.", 0.0);
        }
    }
}
