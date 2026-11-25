using System;

namespace KardashevAscent.Simulation.Climate
{
    /// <summary>NASA POWER relative humidity by latitude and month (see <see cref="HumidityTable"/>), interpolated.</summary>
    public static class HumidityClimate
    {
        public static (double Mean, double Sd) At(double latRad, double yearFraction)
        {
            int month = (int)Math.Floor((yearFraction - Math.Floor(yearFraction)) * 12.0) % 12;
            double y = Math.Max(0.0, Math.Min(HumidityTable.Mean.Length - 1, (latRad * 180.0 / Math.PI + 80.0) / 10.0));
            int i0 = Math.Min((int)y, HumidityTable.Mean.Length - 2); double f = y - i0;
            double Lerp(double[][] t) => t[i0][month] + f * (t[i0 + 1][month] - t[i0][month]);
            return (Lerp(HumidityTable.Mean), Lerp(HumidityTable.Sd));
        }
    }
}
