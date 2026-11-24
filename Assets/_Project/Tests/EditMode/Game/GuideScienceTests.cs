using System.Linq;
using KardashevAscent.Simulation.Game;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>UI information design, stage 3: the new guide categories and the page template.</summary>
    public class GuideScienceTests
    {
        [Test]
        public void TheGuideHasTheTwoNewCategories_WithPages()
        {
            var all = GuideBook.All();
            Assert.Contains(GuideScience.Energy, GuideBook.Categories);
            Assert.Contains(GuideScience.Science, GuideBook.Categories);
            Assert.GreaterOrEqual(all.Count(p => p.Category == GuideScience.Energy), 4);
            Assert.GreaterOrEqual(all.Count(p => p.Category == GuideScience.Science), 4);
        }

        [Test]
        public void EveryNewPageFollowsTheTemplate_AndItsRelatedLinksPointAtRealPages()
        {
            var all = GuideBook.All();
            var titles = all.Select(p => p.Title).ToHashSet();
            foreach (GuidePage p in GuideScience.Pages())
            {
                Assert.IsNotEmpty(p.Summary, p.Title);
                string[] order = p.Sections.Select(s => s.Heading).ToArray();
                CollectionAssert.IsSubsetOf(order, new[] { "In play", "How to use", "Real science", "Related" }, p.Title);
                Assert.Contains("In play", order, p.Title);
                Assert.Contains("Real science", order, p.Title);
                foreach (string rel in p.Sections.First(s => s.Heading == "Related").Lines)
                    Assert.IsTrue(titles.Contains(rel), p.Title + " links to a page that does not exist: " + rel);
                foreach (var s in p.Sections) Assert.IsNotEmpty(s.Lines, p.Title + " / " + s.Heading);
            }
        }

        [Test]
        public void PageTitlesAreUnique_SoALinkNamesOnePage()
        {
            var titles = GuideBook.All().Select(p => p.Title).ToList();
            CollectionAssert.AreEqual(titles.Distinct().ToList(), titles.Where((t, i) => titles.IndexOf(t) == i).ToList());
        }

        [Test]
        public void TheNumbersOnThePagesComeFromTheCodesConstants()
        {
            string cables = string.Join(" ", GuideScience.Pages().First(p => p.Title == "Cables and ratings").Sections.SelectMany(s => s.Lines));
            StringAssert.Contains(PlanetSimulation.PlainLineRatingA.ToString("0"), cables);
            StringAssert.Contains("112.5", cables);
            string swing = string.Join(" ", GuideScience.Pages().First(p => p.Title == "Temperature and the day-night swing").Sections.SelectMany(s => s.Lines));
            StringAssert.Contains("REMS", swing);
        }
    }
}
