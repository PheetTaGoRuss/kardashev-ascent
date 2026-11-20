using System.Linq;
using KardashevAscent.Simulation.Electrical;
using NUnit.Framework;

namespace KardashevAscent.Tests.Electrical
{
    public class CableEconomicsTests
    {
        const double TileM = 300_000.0;
        static CableSpec Cu(double mm2, double rating = 1e9) => CableSpec.FromSquareMillimetres("Cu " + mm2, ConductorMaterial.Copper, mm2, 1, rating);
        static CableSpec Al(double mm2, double rating = 1e9) => CableSpec.FromSquareMillimetres("Al " + mm2, ConductorMaterial.Aluminium, mm2, 1, rating);

        [Test]
        public void MassIsDensityLengthAreaAndTheBillIsOnePercentOfIt()
        {
            double mass = CableEconomics.ConductorMassKg(Cu(500), TileM);
            Assert.AreEqual(8890.0 * TileM * 500e-6, mass, 1.0);
            Assert.AreEqual(mass * 0.01, CableEconomics.ConductorBillKg(Cu(500), TileM), 1e-6);
            Assert.AreEqual(mass * 0.002, CableEconomics.ConductorBillKg(Cu(500), TileM, 0.002), 1e-6);
        }

        [Test]
        public void BillScalesWithLengthAndArea()
        {
            double one = CableEconomics.ConductorBillKg(Cu(100), 1000.0);
            Assert.AreEqual(2.0 * one, CableEconomics.ConductorBillKg(Cu(100), 2000.0), 1e-9);
            Assert.AreEqual(3.0 * one, CableEconomics.ConductorBillKg(Cu(300), 1000.0), 1e-9);
        }

        [Test]
        public void AluminiumForTheSameResistanceWeighsAboutHalf()
        {
            double rCu = Cu(500).ResistanceOhm(TileM);
            double areaAl = ConductorMaterial.Aluminium.Rho20 / ConductorMaterial.Copper.Rho20 * 500.0;   // same resistance
            Assert.AreEqual(rCu, Al(areaAl).ResistanceOhm(TileM), 1e-9);
            double ratio = CableEconomics.ConductorMassKg(Al(areaAl), TileM) / CableEconomics.ConductorMassKg(Cu(500), TileM);
            Assert.AreEqual(0.50, ratio, 0.02);
        }

        [Test]
        public void SalvageNeverReturnsMoreThanWasSpent()
        {
            Assert.AreEqual(60.0, CableEconomics.SalvageKg(100.0, 0.6), 1e-12);
            Assert.AreEqual(100.0, CableEconomics.SalvageKg(100.0, 7.0), 1e-12);
            Assert.AreEqual(0.0, CableEconomics.SalvageKg(100.0, -1.0), 1e-12);
        }

        [Test]
        public void KelvinsLawPicksTheSizeWhereMetalAndLossBalance()
        {
            // Cost = a A + b / A has its minimum at A* = sqrt(b / a). Build a ladder around the analytic optimum and check the pick.
            double volts = 33e3, power = 5e6, length = TileM, hours = 6000.0, years = 20.0, metal = 1.0, energy = 0.5;
            double rho = ConductorMaterial.Copper.Rho20, dens = ConductorMaterial.Copper.Density;
            double i = power / volts;
            double a = dens * length * metal;                                   // metal cost per m2 of area
            double b = i * i * rho * length * hours / 1000.0 * years * energy;  // loss cost times area
            double optimum = System.Math.Sqrt(b / a);                           // m2
            var ladder = new[] { 0.25, 0.5, 0.8, 1.0, 1.25, 2.0, 4.0 }.Select(f => Cu(f * optimum * 1e6)).ToList();
            var choices = CableEconomics.Compare(ladder, length, power, volts, hours, years, metal, energy);
            var pick = CableEconomics.Economic(choices);
            Assert.AreEqual(optimum * 1e6, pick.Spec.AreaM2 * 1e6, 1e-6 * optimum * 1e6);
            Assert.AreEqual(1.0, pick.Spec.AreaM2 / optimum, 1e-9);
            foreach (var c in choices) Assert.GreaterOrEqual(c.TotalCost, pick.TotalCost - 1e-6);
        }

        [Test]
        public void AUnderRatedCableIsNeverTheEconomicChoice()
        {
            var choices = CableEconomics.Compare(new[] { Cu(10, rating: 50.0), Cu(200, rating: 1000.0) }, 1000.0, 5e6, 33e3, 6000, 20, 1.0, 0.5);
            Assert.IsTrue(choices[0].OverRated);   // 151 A through a 50 A cable
            Assert.AreEqual(200.0, CableEconomics.Economic(choices).Spec.AreaM2 * 1e6, 1e-9);
            Assert.IsNull(CableEconomics.Economic(CableEconomics.Compare(new[] { Cu(10, 50.0) }, 1000.0, 5e6, 33e3, 6000, 20, 1.0, 0.5)));
        }

        [Test]
        public void LossGrowsWithTheSquareOfCurrentAndFallsWithVoltage()
        {
            var low = CableEconomics.Compare(new[] { Cu(500) }, TileM, 10e6, 33e3, 6000, 1, 1, 1)[0];
            var high = CableEconomics.Compare(new[] { Cu(500) }, TileM, 10e6, 66e3, 6000, 1, 1, 1)[0];
            Assert.AreEqual(4.0, low.LossW / high.LossW, 1e-9);
            var twice = CableEconomics.Compare(new[] { Cu(500) }, TileM, 20e6, 33e3, 6000, 1, 1, 1)[0];
            Assert.AreEqual(4.0, twice.LossW / low.LossW, 1e-9);
        }

        [Test]
        public void TheGapBetweenTodaysFlatBillAndTheRealMassIsLarge()
        {
            // Documented gap for gate G0(d): one 500 mm2 copper conductor over one 300 km tile.
            double real = CableEconomics.ConductorBillKg(Cu(500), TileM);
            Assert.AreEqual(13_335.0, real, 5.0);
            Assert.Greater(real / 15.0, 800.0, "today's bill is 15 Cu");
        }
    }
}
