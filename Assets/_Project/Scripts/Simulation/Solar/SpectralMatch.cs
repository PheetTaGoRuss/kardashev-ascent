using System;

namespace KardashevAscent.Simulation.Solar
{
    /// <summary>Single-junction cell materials. Order matches the rows of <see cref="SpectralMatchTable"/>.</summary>
    public enum CellMaterial
    {
        Silicon,
        GaAs,
        CdTe,
        Germanium,
        Perovskite,
    }

    /// <summary>
    /// How well a star's light suits a cell material (docs/physics/physics-verification.md, P0 item 5).
    /// Detailed-balance efficiency under a blackbody star, from prototypes/solar/spectral_match.py.
    /// f_spec = η(star) / η(5778 K): Silicon under a 2600 K red dwarf keeps only ~0.43 of its Sun-like output,
    /// while Germanium does better under red dwarfs than under the Sun.
    /// </summary>
    public static class SpectralMatch
    {
        /// <summary>Wien's constant b, m·K.</summary>
        public const double WienB = 2.897771955e-3;

        /// <summary>hc in eV·nm, so λ_cutoff(nm) = 1239.84 / E_g(eV).</summary>
        public const double HcEvNm = 1239.84;

        /// <summary>Bandgap at 300 K, eV.</summary>
        public static double BandgapEv(CellMaterial material)
        {
            switch (material)
            {
                case CellMaterial.Silicon: return 1.12;
                case CellMaterial.GaAs: return 1.42;
                case CellMaterial.CdTe: return 1.50;
                case CellMaterial.Germanium: return 0.67;
                case CellMaterial.Perovskite: return 1.55;
                default: throw new ArgumentOutOfRangeException(nameof(material));
            }
        }

        /// <summary>Longest wavelength the cell can use, nm: photons beyond it pass through.</summary>
        public static double CutoffWavelengthNm(double bandgapEv) => HcEvNm / bandgapEv;

        /// <summary>Peak of the star's blackbody spectrum (Wien), nm.</summary>
        public static double WienPeakNm(double temperatureK) => WienB / temperatureK * 1e9;

        /// <summary>
        /// Detailed-balance (Shockley–Queisser) efficiency under a blackbody star at 1000 W/m², cell at 300 K.
        /// Temperatures outside 2300–7500 K are clamped to the table.
        /// </summary>
        public static double DetailedBalanceEfficiency(CellMaterial material, double starTemperatureK)
        {
            double[] row = SpectralMatchTable.Efficiency[(int)material];
            double x = (starTemperatureK - SpectralMatchTable.FirstTemperatureK) / SpectralMatchTable.StepK;
            if (x <= 0.0)
                return row[0];
            if (x >= row.Length - 1)
                return row[row.Length - 1];
            int i = (int)x;
            double f = x - i;
            return row[i] + f * (row[i + 1] - row[i]);
        }

        /// <summary>f_spec for <see cref="PanelParams.FSpec"/>: efficiency under this star ÷ under a Sun-like (5778 K) star.</summary>
        public static double FSpec(CellMaterial material, double starTemperatureK) =>
            DetailedBalanceEfficiency(material, starTemperatureK) / SpectralMatchTable.SunReference[(int)material];

        /// <summary>The material with the highest f_spec × Sun-like efficiency under this star.</summary>
        public static CellMaterial BestMaterial(double starTemperatureK)
        {
            CellMaterial best = CellMaterial.Silicon;
            double bestEta = -1.0;
            foreach (CellMaterial m in (CellMaterial[])Enum.GetValues(typeof(CellMaterial)))
            {
                double eta = DetailedBalanceEfficiency(m, starTemperatureK);
                if (eta > bestEta) { bestEta = eta; best = m; }
            }
            return best;
        }
    }
}
