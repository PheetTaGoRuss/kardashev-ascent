using System.Linq;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Resources;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>The guide book (director 2026-11-15): it must say what the game actually does.</summary>
    public class GuideBookTests
    {
        static readonly HexSphere Sphere = HexSphere.Create(12);
        static readonly PlanetSurface Wet = PlanetSurface.Generate(Sphere, new SurfaceSettings(7));

        [Test]
        public void EveryPage_HasATitleAndASummary_AndEveryCategoryHasPages()
        {
            var pages = GuideBook.All();
            Assert.IsNotEmpty(pages);
            foreach (GuidePage p in pages)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(p.Title));
                Assert.IsFalse(string.IsNullOrWhiteSpace(p.Summary), p.Title);
                Assert.Contains(p.Category, GuideBook.Categories);
            }
            foreach (string c in GuideBook.Categories)
                Assert.IsTrue(pages.Any(p => p.Category == c), c);
            foreach (Material m in Materials.All)
                Assert.IsTrue(pages.Any(p => p.Material == m), $"a page for {m}");
        }

        [Test]
        public void WhereMaterialsAreFound_MatchesTheDepositGenerator()
        {
            // Every deposit the generator places sits on a ground the guide names for it (shore water aside).
            for (int seed = 1; seed <= 6; seed++)
            {
                var map = new DepositMap(Sphere, Wet, PlanetChemistry.EarthLike, seed);
                for (int t = 0; t < Sphere.TileCount; t++)
                {
                    TerrainType ground = Wet.Terrain[t];
                    foreach (Deposit d in map.At(t))
                    {
                        bool shoreWater = d.Material == Material.Water && Sphere.Neighbors[t].Any(n => Terrains.IsWater(Wet.Terrain[n]));
                        if (shoreWater) continue;
                        Assert.Contains(ground, GuideBook.GroundsWith(d.Material), $"{d.Material} on {ground} (seed {seed}, tile {t})");
                    }
                }
            }
        }

        [Test]
        public void TheDirectorsExamples_ReadRight()
        {
            CollectionAssert.Contains(GuideBook.GroundsWith(Material.Copper), TerrainType.Highland, "copper in highland");
            CollectionAssert.Contains(GuideBook.GroundsWith(Material.Lithium), TerrainType.Desert, "lithium in desert");
            CollectionAssert.DoesNotContain(GuideBook.GroundsWith(Material.Copper), TerrainType.Desert);
            CollectionAssert.DoesNotContain(GuideBook.GroundsWith(Material.Lithium), TerrainType.Mountain);
            Assert.IsEmpty(GuideBook.GroundsWith(Material.Aluminium), "made, not mined");

            GuidePage copper = GuideBook.All().First(p => p.Material == Material.Copper);
            Assert.IsTrue(copper.Mentions("highland"));
            GuidePage desert = GuideBook.All().First(p => p.Category == GuideBook.Ground && p.Title == Terrains.Name(TerrainType.Desert));
            Assert.IsTrue(desert.Mentions("lithium"), "the desert page says lithium can be there");
        }

        [Test]
        public void UsedFor_ComesFromTheBuildingBills()
        {
            CollectionAssert.Contains(GuideBook.BuildingsUsing(Material.Copper), "Robot Workshop");
            CollectionAssert.Contains(GuideBook.BuildingsUsing(Material.Lithium), "Battery yard");
            CollectionAssert.Contains(GuideBook.BuildingsUsing(Material.Rock), "Solar panel");
            Assert.IsEmpty(GuideBook.BuildingsUsing(Material.Slag));
        }

        [Test]
        public void Search_FindsPagesByAnyWord()
        {
            var pages = GuideBook.All();
            Assert.IsTrue(pages.Where(p => p.Mentions("boiling")).Any(p => p.Category == GuideBook.Research));
            Assert.IsTrue(pages.All(p => p.Mentions("")), "an empty search shows everything");
            Assert.IsFalse(pages.Any(p => p.Mentions("zzqx")));
        }
    }
}
