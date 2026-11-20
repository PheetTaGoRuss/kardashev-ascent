using KardashevAscent.Simulation.Logistics;
using KardashevAscent.Simulation.Resources;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Logistics
{
    /// <summary>Stock per place: every kilogram is in exactly one place (MO-02).</summary>
    public class InventoryBookTests
    {
        static readonly PlaceId A = PlaceId.Depot(1), B = PlaceId.Depot(2);

        [Test]
        public void Move_TakesFromOnePlaceAndGivesToTheOther_NothingIsLost()
        {
            var book = new InventoryBook();
            book.Add(A, Material.Iron, 100.0);
            double moved = book.Move(A, B, Material.Iron, 30.0);
            Assert.AreEqual(30.0, moved, 1e-12);
            Assert.AreEqual(70.0, book.StockOf(A)[Material.Iron], 1e-12);
            Assert.AreEqual(30.0, book.StockOf(B)[Material.Iron], 1e-12);
            Assert.AreEqual(100.0, book.Total(Material.Iron), 1e-12);
        }

        [Test]
        public void Move_NeverMovesMoreThanTheSourceHolds()
        {
            var book = new InventoryBook();
            book.Add(A, Material.Copper, 10.0);
            Assert.AreEqual(10.0, book.Move(A, B, Material.Copper, 50.0), 1e-12);
            Assert.AreEqual(0.0, book.Move(A, B, Material.Copper, 50.0), 1e-12);
            Assert.AreEqual(10.0, book.Total(Material.Copper), 1e-12);
        }

        [Test]
        public void ACapacity_StopsWhatGoesIn_AndMoveRespectsIt()
        {
            var book = new InventoryBook { Capacity = p => p.Equals(B) ? 40.0 : (double?)null };
            book.Add(A, Material.Rock, 100.0);
            Assert.AreEqual(40.0, book.Move(A, B, Material.Rock, 100.0), 1e-12);
            Assert.AreEqual(0.0, book.FreeAt(B), 1e-12);
            Assert.AreEqual(0.0, book.Move(A, B, Material.Rock, 10.0), 1e-12, "full");
            Assert.AreEqual(5.0, book.Add(PlaceId.Depot(3), Material.Rock, 5.0), 1e-12, "no limit elsewhere");
            Assert.AreEqual(105.0 - 5.0 + 5.0, book.Total(Material.Rock), 1e-9);
        }

        [Test]
        public void ThePlacesAreDistinct_ByKindAndId()
        {
            var book = new InventoryBook();
            book.Add(PlaceId.Depot(7), Material.Iron, 1.0);
            book.Add(PlaceId.Cargo(7), Material.Iron, 2.0);
            book.Add(new PlaceId(PlaceKind.BufferIn, 7), Material.Iron, 4.0);
            Assert.AreEqual(1.0, book.StockOf(PlaceId.Depot(7))[Material.Iron], 1e-12);
            Assert.AreEqual(7.0, book.Total(Material.Iron), 1e-12);
            Assert.AreEqual(7.0, book.Totals()[Material.Iron], 1e-12);
        }

        [Test]
        public void AnAdoptedStock_IsThePlace()
        {
            var old = new MaterialStock();
            old.Add(Material.Iron, 12.0);
            var book = new InventoryBook();
            book.Adopt(A, old);
            old.Add(Material.Iron, 3.0);
            Assert.AreEqual(15.0, book.Total(Material.Iron), 1e-12);
            book.ClearExcept(B);
            Assert.AreEqual(0.0, book.Total(Material.Iron), 1e-12);
        }

        [Test]
        public void TheLedger_AddsUpKgAndKgKm()
        {
            var ledger = new LogisticsLedger();
            ledger.RecordMove(Material.Iron, 100.0, 3.0);
            ledger.RecordMove(Material.Iron, 50.0, 2.0);
            ledger.RecordEnergy(12.5);
            Assert.AreEqual(150.0, ledger.KgMoved(Material.Iron), 1e-12);
            Assert.AreEqual(400.0, ledger.KgKm(Material.Iron), 1e-12);
            Assert.AreEqual(12.5, ledger.HaulWh, 1e-12);
        }
    }
}
