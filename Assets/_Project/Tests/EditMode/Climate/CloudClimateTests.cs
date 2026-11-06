using System;
using KardashevAscent.Simulation.Climate;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Climate
{
    /// <summary>Cloud factor from NASA POWER (tools/nasa_power/fetch_cloud_factor.py).</summary>
    public class CloudClimateTests
    {
        const double Deg = Math.PI / 180.0;

        [Test]
        public void Factor_IsBetweenZeroAndOneEverywhere()
        {
            for (int lat = -90; lat <= 90; lat += 5)
                for (int m = 0; m < 48; m++)
                {
                    double f = CloudClimate.Factor(lat * Deg, m / 48.0);
                    Assert.That(f, Is.InRange(0.3, 1.0), $"lat {lat}, step {m}");
                }
        }

        [Test]
        public void SouthernOceanIsCloudierThanSubtropics()
        {
            Assert.Less(CloudClimate.AnnualMean(-60 * Deg), 0.6);
            Assert.Greater(CloudClimate.AnnualMean(20 * Deg), 0.8);
        }

        [Test]
        public void Monsoon_TenNorthIsCloudierInAugustThanFebruary()
        {
            Assert.Less(CloudClimate.Factor(10 * Deg, 7.5 / 12.0), CloudClimate.Factor(10 * Deg, 1.5 / 12.0) - 0.08);
        }

        [Test]
        public void Factor_AtMonthCentreMatchesTableRow()
        {
            // 0° row, March centre: 0.839 in the generated CloudClimateTable (internal to Simulation).
            Assert.AreEqual(0.839, CloudClimate.Factor(0.0, 2.5 / 12.0), 1e-12);
        }

        [Test]
        public void Factor_WrapsAcrossNewYear()
        {
            Assert.AreEqual(CloudClimate.Factor(45 * Deg, 0.999), CloudClimate.Factor(45 * Deg, -0.001), 1e-12);
            Assert.AreEqual(CloudClimate.Factor(45 * Deg, 0.0), CloudClimate.Factor(45 * Deg, 1.0), 1e-12);
        }

        [Test]
        public void BangkokAnnualIsNearPointLookup()
        {
            // Zonal mean at 13.75°N vs Bangkok's own all-sky/clear-sky 1911/2319 = 0.824: same ballpark.
            Assert.AreEqual(0.824, CloudClimate.AnnualMean(13.75 * Deg), 0.05);
        }

        [Test]
        public void YearFraction_PerihelionIsEarlyJanuary()
        {
            Assert.AreEqual(2.5 / 365.25, CloudClimate.YearFractionFromMeanAnomaly(0.0), 1e-12);
            Assert.AreEqual(0.5 + 2.5 / 365.25, CloudClimate.YearFractionFromMeanAnomaly(Math.PI), 1e-12);
        }
    }
}
