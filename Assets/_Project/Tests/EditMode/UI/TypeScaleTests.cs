using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.UI
{
    /// <summary>UI information design, stage 1 (decided 2026-11-24): the type scale lives in USS classes and nothing is below 14 px.</summary>
    public class TypeScaleTests
    {
        static string Root()
        {
            // Unity runs tests from the project folder; the command-line runner from its bin folder.
            foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                string d = start;
                while (d != null && !Directory.Exists(Path.Combine(d, "Assets", "_Project"))) d = Path.GetDirectoryName(d);
                if (d != null) return Path.Combine(d, "Assets", "_Project");
            }
            string dir = null;
            if (dir == null) Assert.Ignore("project folder not found");
            return Path.Combine(dir, "Assets", "_Project");
        }

        [Test]
        public void NoStyleSheetSetsATextSizeBelow14px()
        {
            foreach (string f in Directory.GetFiles(Path.Combine(Root(), "UI"), "*.uss", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"font-size:\s*(\d+)px"))
                    Assert.GreaterOrEqual(int.Parse(m.Groups[1].Value), 14, Path.GetFileName(f) + ": " + m.Value);
        }

        [Test]
        public void TheSixScaleClassesExist()
        {
            string css = File.ReadAllText(Path.Combine(Root(), "UI", "Common.uss"));
            foreach (string c in new[] { "t-hero", "t-title", "t-section", "t-value", "t-body", "t-secondary" })
                StringAssert.Contains("." + c + " {", css);
        }

        [Test]
        public void CodeNamesASizeFromTheScale_NeverAPixelLiteral()
        {
            string ui = Path.Combine(Root(), "Scripts", "Presentation");
            foreach (string f in Directory.GetFiles(ui, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(f) == "UiType.cs") continue;
                foreach (string line in File.ReadAllLines(f))
                    if (Regex.IsMatch(line, @"style\.fontSize = \d") && !line.Contains("GUIStyle"))
                        Assert.Fail(Path.GetFileName(f) + ": " + line.Trim());
            }
        }

        [Test]
        public void TheUiScaleStaysInItsRange()
        {
            string code = File.ReadAllText(Path.Combine(Root(), "Scripts", "Presentation", "UI", "UiScale.cs"));
            StringAssert.Contains("Min = 0.8f, Max = 1.5f", code);
        }
    }
}
