using System;

namespace KardashevAscent.Simulation.Electrical
{
    public enum ConverterKind { Transformer, Rectifier, Inverter, DcDc }

    /// <summary>
    /// A converter (electrical redesign stage 10, W-42): the only thing that joins networks of different kind or voltage class. It is a
    /// device with two terminals, one on each network, so the graph keeps the two networks apart. The rules follow the brief:
    /// a transformer takes AC to AC at a different voltage and does not change the frequency; a rectifier takes AC to DC; an inverter DC to
    /// AC; a DC-DC converter DC to DC at a different voltage. Efficiency is a number the caller sets (game rule, TBD: no source chosen);
    /// what a converter loses is heat.
    /// </summary>
    public sealed class ConverterSpec
    {
        public readonly string Name;
        public readonly ConverterKind Kind;
        public readonly int InputClass, OutputClass;
        /// <summary>Frequency on the AC side(s), Hz; the same on both sides of a transformer.</summary>
        public readonly double FrequencyHz;
        /// <summary>Output power over input power, 0 to 1 (game rule, TBD).</summary>
        public readonly double Efficiency;

        public ConverterSpec(string name, ConverterKind kind, int inputClass, int outputClass, double frequencyHz, double efficiency)
        {
            Name = name; Kind = kind; InputClass = inputClass; OutputClass = outputClass; FrequencyHz = frequencyHz; Efficiency = efficiency;
        }

        public NetworkType InputType => Kind == ConverterKind.Transformer || Kind == ConverterKind.Rectifier ? NetworkType.Ac : NetworkType.Dc;
        public NetworkType OutputType => Kind == ConverterKind.Transformer || Kind == ConverterKind.Inverter ? NetworkType.Ac : NetworkType.Dc;

        /// <summary>Null when the spec obeys the rules of its kind; otherwise what is wrong with it.</summary>
        public string Problem()
        {
            if (Efficiency <= 0.0 || Efficiency > 1.0) return "Efficiency must be above 0 and at most 1.";
            if ((Kind == ConverterKind.Transformer || Kind == ConverterKind.DcDc) && InputClass == OutputClass)
                return "A transformer or DC-DC converter changes the voltage class: input and output classes must differ.";
            if (InputType == NetworkType.Ac || OutputType == NetworkType.Ac)
                if (FrequencyHz <= 0.0) return "An AC side needs a nominal frequency.";
            return null;
        }

        /// <summary>Power out for a power in, W.</summary>
        public double Output(double inputW) => inputW * Efficiency;

        /// <summary>Power lost as heat for a power in, W (conservation: in = out + heat).</summary>
        public double HeatW(double inputW) => inputW * (1.0 - Efficiency);
    }

    public static class ConverterInstaller
    {
        /// <summary>
        /// Places a converter between two nodes: a device with an input terminal on one network and an output terminal on the other.
        /// Refuses a bad spec, or nodes of the wrong kind, class or frequency. The two networks stay separate.
        /// </summary>
        public static ConnectResult Install(ElectricalGraph g, ConverterSpec spec, NodeId inputNode, NodeId outputNode, int tile = -1, int slot = -1)
        {
            string problem = spec.Problem();
            if (problem != null) return ConnectResult.Fail(problem);
            var inN = g.Node(inputNode);
            var outN = g.Node(outputNode);
            if (inN == null || outN == null) return ConnectResult.Fail("A converter end has no node.");
            if (g.SameNetwork(inputNode, outputNode)) return ConnectResult.Fail("A converter must stand between two different networks.");
            var dev = g.AddDevice(tile, slot, (int)spec.Kind);
            var tIn = g.AddTerminal(dev, spec.InputType, spec.InputClass, TerminalRole.Load);
            var tOut = g.AddTerminal(dev, spec.OutputType, spec.OutputClass, TerminalRole.Source);
            g.Terminal(tIn).FrequencyHz = spec.InputType == NetworkType.Ac ? spec.FrequencyHz : 0.0;
            g.Terminal(tOut).FrequencyHz = spec.OutputType == NetworkType.Ac ? spec.FrequencyHz : 0.0;
            var a = g.Attach(tIn, inputNode);
            if (!a.Ok) return a;
            var b = g.Attach(tOut, outputNode);
            if (!b.Ok) { g.Detach(tIn); return b; }
            return ConnectResult.Success(dev.Value);
        }
    }

    /// <summary>
    /// Steady-state AC in RMS terms, resistance only (stage 10, W-41): no waveform, no phase angle, power factor 1 (reactive behaviour is a
    /// later, separately approved step). A balanced three-phase line carries P = sqrt(3) V_LL I; each phase sees V_LL / sqrt(3) and a third
    /// of the power, so the DC nodal solver, run on that per-phase equivalent, gives the AC answer.
    /// </summary>
    public static class AcSteadyState
    {
        public static readonly double Sqrt3 = Math.Sqrt(3.0);

        /// <summary>Line current for a three-phase power at a line-to-line RMS voltage, A.</summary>
        public static double LineCurrentA(double powerW, double lineToLineV) => powerW / (Sqrt3 * lineToLineV);

        /// <summary>Loss of a three-phase line with this resistance per phase, W: 3 I^2 R.</summary>
        public static double LossW(double powerW, double lineToLineV, double resistancePerPhaseOhm)
        {
            double i = LineCurrentA(powerW, lineToLineV);
            return 3.0 * i * i * resistancePerPhaseOhm;
        }

        /// <summary>The per-phase single-conductor equivalent of a three-phase power and line voltage, for the DC solver.</summary>
        public static (double PowerW, double Volts) PerPhase(double powerW, double lineToLineV) => (powerW / 3.0, lineToLineV / Sqrt3);
    }
}
