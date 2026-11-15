using KardashevAscent.Simulation.Game;
using NUnit.Framework;

namespace KardashevAscent.Simulation.Tests.Game
{
    /// <summary>The in-game science calculator: order of operations, degrees, the puzzles' own sums, and errors.</summary>
    public class ScienceCalculatorTests
    {
        static double Eval(string s)
        {
            Assert.IsTrue(ScienceCalculator.TryEvaluate(s, out double v, out string error), $"{s}: {error}");
            return v;
        }

        [TestCase("1+2*3", 7.0)]
        [TestCase("(1+2)*3", 9.0)]
        [TestCase("10-4-3", 3.0)]
        [TestCase("12/4/3", 1.0)]
        [TestCase("2^3^2", 512.0)]
        [TestCase("-2^2", -4.0)]
        [TestCase("3×4", 12.0)]
        [TestCase("3÷4", 0.75)]
        [TestCase("5−7", -2.0)]
        [TestCase("1.5e3", 1500.0)]
        [TestCase("2*(3+4", 14.0)]
        [TestCase("sqrt(16)", 4.0)]
        [TestCase("log(1000)", 3.0)]
        public void Arithmetic(string s, double expected) => Assert.AreEqual(expected, Eval(s), 1e-12);

        [Test]
        public void AnglesAreInDegrees()
        {
            Assert.AreEqual(0.5, Eval("sin(30)"), 1e-12);
            Assert.AreEqual(0.5, Eval("cos(60)"), 1e-12);
            Assert.AreEqual(1.0, Eval("tan(45)"), 1e-12);
            Assert.AreEqual(30.0, Eval("asin(0.5)"), 1e-12);
            Assert.AreEqual(45.0, Eval("arctan(1)"), 1e-12);
            Assert.AreEqual(180.0, Eval("pi") * 180.0 / System.Math.PI, 1e-12);
            Assert.AreEqual(System.Math.E, Eval("e"), 1e-15);
        }

        [Test]
        public void ThePuzzlesWorkedExamples()
        {
            Assert.AreEqual(0.303, Eval("ln(1361/742)/2"), 5e-4);                  // air haze τ
            Assert.AreEqual(55.0, Eval("atan(1/0.7)"), 0.05);                      // stick shadow h
            Assert.AreEqual(20.15, Eval("asin(sin(23.44)*sin(60))"), 0.01);         // declination δ
            Assert.AreEqual(3622.5, Eval("2898/0.8"), 0.01);                       // Wien
            Assert.AreEqual(20.43, Eval("atan(1/2.684)"), 0.01);                   // the director's own reading
        }

        [Test]
        public void Ans_IsThePreviousResult()
        {
            Assert.IsTrue(ScienceCalculator.TryEvaluate("ans*2", 20.5, out double v, out _));
            Assert.AreEqual(41.0, v, 1e-12);
            Assert.IsTrue(ScienceCalculator.TryEvaluate("atan(1/ans)", 2.684, out double h, out _));
            Assert.AreEqual(20.43, h, 0.01);
            Assert.IsFalse(ScienceCalculator.TryEvaluate("ans+1", out _, out string error), "no answer yet");
            StringAssert.Contains("ans", error);
        }

        [TestCase("")]
        [TestCase("1/0")]
        [TestCase("asin(2)")]
        [TestCase("2+")]
        [TestCase("sin 30")]
        [TestCase("foo(1)")]
        [TestCase("3 $ 4")]
        public void NonsenseGivesAReasonNotANumber(string s)
        {
            Assert.IsFalse(ScienceCalculator.TryEvaluate(s, out _, out string error));
            Assert.IsFalse(string.IsNullOrEmpty(error));
        }

        [Test]
        public void ShowsNumbersPlainly()
        {
            Assert.AreEqual("20.434299", ScienceCalculator.Format(20.43429876));
            Assert.AreEqual("4", ScienceCalculator.Format(4.0));
            Assert.AreEqual("-0.5", ScienceCalculator.Format(-0.5));
            Assert.AreEqual("1.5E+9", ScienceCalculator.Format(1.5e9));
        }
    }
}
