using System;
using System.Globalization;

namespace KardashevAscent.Simulation.Game
{
    /// <summary>
    /// The colony's pocket science calculator (director 2026-11-14): works out a typed expression such as
    /// <c>atan(1/2.684)</c> or <c>ln(1361/742)/2</c>. Angles are in degrees, like the puzzles. Game tool, not physics.
    /// <para>Grammar: + − × / ^ and brackets; functions sin cos tan asin acos atan ln log sqrt; constants π (pi) and e;
    /// <c>ans</c> for the previous result.
    /// Missing closing brackets at the end are closed for you, the way pocket calculators do.</para>
    /// </summary>
    public static class ScienceCalculator
    {
        const double Deg = Math.PI / 180.0;

        /// <summary>The expression's value, or false with a short reason when it can't be worked out.</summary>
        public static bool TryEvaluate(string expression, out double value, out string error) =>
            TryEvaluate(expression, double.NaN, out value, out error);

        /// <summary>The same, with <c>ans</c> standing for <paramref name="previousAnswer"/> (the last result, NaN when none).</summary>
        public static bool TryEvaluate(string expression, double previousAnswer, out double value, out string error)
        {
            value = double.NaN;
            error = null;
            if (string.IsNullOrWhiteSpace(expression))
            {
                error = "Type or click a number";
                return false;
            }
            var p = new Parser(Normalise(expression)) { Ans = previousAnswer };
            try
            {
                value = p.Expression();
                p.SkipSpaces();
                while (p.Pos < p.Text.Length && p.Text[p.Pos] == ')') { p.Pos++; p.SkipSpaces(); }   // extra ')' at the end
                if (p.Pos < p.Text.Length)
                    throw new FormatException($"Can't read '{p.Text.Substring(p.Pos)}'");
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    error = "No answer (e.g. dividing by 0, or asin of more than 1)";
                    return false;
                }
                return true;
            }
            catch (FormatException e)
            {
                error = e.Message;
                value = double.NaN;
                return false;
            }
        }

        /// <summary>A number as the calculator shows it: up to 6 decimals, no trailing zeros.</summary>
        public static string Format(double v)
        {
            if (double.IsNaN(v)) return "?";
            double a = Math.Abs(v);
            if (a != 0.0 && (a >= 1e9 || a < 1e-6))
                return v.ToString("0.#####E+0", CultureInfo.InvariantCulture);
            return Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture);
        }

        static string Normalise(string s) =>
            s.Replace('×', '*').Replace('÷', '/').Replace('−', '-').Replace(',', '.').Replace("π", "pi").ToLowerInvariant();

        sealed class Parser
        {
            public readonly string Text;
            public int Pos;
            public double Ans = double.NaN;

            public Parser(string text) { Text = text; }

            public void SkipSpaces()
            {
                while (Pos < Text.Length && char.IsWhiteSpace(Text[Pos])) Pos++;
            }

            bool Eat(char c)
            {
                SkipSpaces();
                if (Pos < Text.Length && Text[Pos] == c) { Pos++; return true; }
                return false;
            }

            public double Expression()
            {
                double v = Term();
                while (true)
                {
                    if (Eat('+')) v += Term();
                    else if (Eat('-')) v -= Term();
                    else return v;
                }
            }

            double Term()
            {
                double v = Unary();
                while (true)
                {
                    if (Eat('*')) v *= Unary();
                    else if (Eat('/')) v /= Unary();
                    else return v;
                }
            }

            // -2^2 = -(2^2), as on a scientific calculator.
            double Unary()
            {
                if (Eat('-')) return -Unary();
                if (Eat('+')) return Unary();
                return Power();
            }

            double Power()
            {
                double b = Primary();
                if (Eat('^')) return Math.Pow(b, Unary());   // right-associative: 2^3^2 = 2^9
                return b;
            }

            double Primary()
            {
                SkipSpaces();
                if (Pos >= Text.Length)
                    throw new FormatException("Something is missing at the end");
                if (Eat('('))
                    return Closed(Expression());
                char c = Text[Pos];
                if (char.IsDigit(c) || c == '.')
                {
                    int start = Pos;
                    while (Pos < Text.Length && (char.IsDigit(Text[Pos]) || Text[Pos] == '.')) Pos++;
                    if (Pos < Text.Length && Text[Pos] == 'e' && Pos + 1 < Text.Length &&
                        (char.IsDigit(Text[Pos + 1]) || ((Text[Pos + 1] == '-' || Text[Pos + 1] == '+') && Pos + 2 < Text.Length && char.IsDigit(Text[Pos + 2]))))
                    {
                        Pos += 2;
                        while (Pos < Text.Length && char.IsDigit(Text[Pos])) Pos++;
                    }
                    string number = Text.Substring(start, Pos - start);
                    if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                        throw new FormatException($"'{number}' isn't a number");
                    return n;
                }
                if (char.IsLetter(c))
                {
                    int start = Pos;
                    while (Pos < Text.Length && char.IsLetter(Text[Pos])) Pos++;
                    string name = Text.Substring(start, Pos - start);
                    if (name == "pi") return Math.PI;
                    if (name == "ans")
                    {
                        if (double.IsNaN(Ans)) throw new FormatException("No answer yet to use as ans");
                        return Ans;
                    }
                    if (name == "e") return Math.E;
                    if (!Eat('('))
                        throw new FormatException($"{name} needs brackets: {name}( … )");
                    double x = Closed(Expression());
                    switch (name)
                    {
                        case "sin": return Math.Sin(x * Deg);
                        case "cos": return Math.Cos(x * Deg);
                        case "tan": return Math.Tan(x * Deg);
                        case "asin": case "arcsin": return Math.Asin(x) / Deg;
                        case "acos": case "arccos": return Math.Acos(x) / Deg;
                        case "atan": case "arctan": return Math.Atan(x) / Deg;
                        case "ln": return Math.Log(x);
                        case "log": return Math.Log10(x);
                        case "sqrt": return Math.Sqrt(x);
                        default: throw new FormatException($"Unknown function '{name}'");
                    }
                }
                throw new FormatException($"Can't read '{c}'");
            }

            // A ')' closes the bracket; at the very end it may be left out.
            double Closed(double v)
            {
                if (Eat(')')) return v;
                SkipSpaces();
                if (Pos >= Text.Length) return v;
                throw new FormatException("A bracket ')' is missing");
            }
        }
    }
}
