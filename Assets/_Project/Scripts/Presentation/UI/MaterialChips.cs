using System;
using KardashevAscent.Simulation.Resources;
using UnityEngine;
using UnityEngine.UIElements;
using Material = KardashevAscent.Simulation.Resources.Material;

namespace KardashevAscent.Presentation.UI
{
    /// <summary>
    /// Materials as icons, like other building games (director 2026-11-15: fewer words): a small coloured square with
    /// the material's symbol, then the amount. An amount the colony is short of is red. Hover a chip for its name.
    /// </summary>
    public static class MaterialChips
    {
        public static Color ColourOf(Material m)
        {
            switch (m)
            {
                case Material.Iron: return new Color(0.62f, 0.66f, 0.72f);
                case Material.Copper: return new Color(0.85f, 0.50f, 0.28f);
                case Material.Lithium: return new Color(0.78f, 0.70f, 0.95f);
                case Material.Water: return new Color(0.45f, 0.72f, 0.95f);
                case Material.Aluminium: return new Color(0.86f, 0.88f, 0.90f);
                case Material.Sodium: return new Color(0.95f, 0.85f, 0.40f);
                case Material.Slag: return new Color(0.40f, 0.38f, 0.36f);
                case Material.RareEarth: return new Color(0.55f, 0.85f, 0.62f);   // pale green, like bastnäsite and neodymium salts
                case Material.Anorthosite: return new Color(0.86f, 0.84f, 0.78f);   // the Moon's pale highlands
                case Material.Salt: return new Color(0.96f, 0.95f, 0.92f);
                case Material.Alumina: return new Color(0.98f, 0.98f, 1.00f);       // white powder
                case Material.PureAluminium: return new Color(0.80f, 0.90f, 1.00f);
                default: return new Color(0.62f, 0.52f, 0.42f);   // rock
            }
        }

        public static string SymbolOf(Material m)
        {
            switch (m)
            {
                case Material.Iron: return "Fe";
                case Material.Copper: return "Cu";
                case Material.Lithium: return "Li";
                case Material.Water: return "H₂O";
                case Material.Aluminium: return "Al";
                case Material.Sodium: return "Na";
                case Material.Slag: return "Sl";
                case Material.RareEarth: return "Nd";
                case Material.Anorthosite: return "An";
                case Material.Salt: return "NaCl";
                case Material.Alumina: return "Al₂O₃";
                case Material.PureAluminium: return "Al+";
                default: return "Rk";
            }
        }

        /// <summary>The icon alone: a square with the symbol in dark text.</summary>
        public static VisualElement Icon(Material m, float size = 18f)
        {
            var icon = new Label(SymbolOf(m));
            icon.style.width = size;
            icon.style.height = size;
            icon.style.minWidth = size;
            icon.style.backgroundColor = ColourOf(m);
            icon.style.color = UiPalette.OnAccent;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            icon.style.fontSize = UiType.Px(size * (SymbolOf(m).Length > 2 ? 0.42f : 0.55f));
            icon.style.paddingLeft = icon.style.paddingRight = icon.style.paddingTop = icon.style.paddingBottom = 0;
            icon.style.marginLeft = icon.style.marginRight = icon.style.marginTop = icon.style.marginBottom = 0;
            icon.tooltip = Materials.Name(m);
            return icon;
        }

        /// <summary>A chip: icon then the amount (red when <paramref name="isShort"/>).</summary>
        public static VisualElement Chip(Material m, double kg, bool isShort = false, float size = 18f)
        {
            var chip = new VisualElement();
            chip.style.flexDirection = FlexDirection.Row;
            chip.style.alignItems = Align.Center;
            chip.style.marginRight = 10;
            chip.style.marginTop = 2;
            chip.Add(Icon(m, size));
            var amount = new Label(Kg(kg));
            amount.style.marginLeft = 4;
            amount.style.paddingLeft = amount.style.paddingRight = 0;
            amount.style.fontSize = UiType.Px(size * 0.78f);
            amount.style.color = isShort ? UiPalette.Critical : UiPalette.Text;
            if (isShort) amount.style.unityFontStyleAndWeight = FontStyle.Bold;
            chip.Add(amount);
            chip.tooltip = $"{Materials.Name(m)}: {Kg(kg)}" + (isShort ? " (not enough in stock)" : "");
            return chip;
        }

        /// <summary>
        /// A row of chips for a bill. With <paramref name="stock"/>, an item the stock can't cover is red.
        /// Fills <paramref name="row"/> (cleared first) so a panel can keep one row and refresh it.
        /// </summary>
        public static void Fill(VisualElement row, MaterialBill bill, MaterialStock stock = null, float size = 18f)
        {
            row.Clear();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            if (bill == null) return;
            foreach (var (m, kg) in bill.Items())
                row.Add(Chip(m, kg, stock != null && stock[m] + 1e-6 < kg, size));
        }

        /// <summary>Stock on hand as chips (the HUD): every listed material, red when there is none.</summary>
        public static void FillStock(VisualElement row, MaterialStock stock, Material[] which, float size = 16f)
        {
            row.Clear();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            if (stock == null) return;
            foreach (Material m in which)
                row.Add(Chip(m, stock[m], stock[m] <= 0.0, size));
        }

        /// <summary>"325 kg", "1.5 t", "12 t".</summary>
        public static string Kg(double kg)
        {
            if (kg >= 10_000.0) return $"{kg / 1000.0:0} t";
            if (kg >= 1000.0) return $"{kg / 1000.0:0.#} t";
            return $"{Math.Round(kg):0} kg";
        }
    }
}
