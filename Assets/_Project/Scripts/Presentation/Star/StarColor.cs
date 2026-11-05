using UnityEngine;

namespace KardashevAscent.Presentation.Star
{
    /// <summary>
    /// Approximate colour of a blackbody at a temperature (Tanner Helland's fit to the CIE blackbody locus),
    /// for tinting the star's light: about white at 5800 K, orange for red dwarfs, blue-white above 7000 K.
    /// A visual aid only; panel physics uses the spectrum model, not this colour.
    /// </summary>
    public static class StarColor
    {
        public static Color FromTemperature(double kelvin)
        {
            double t = Mathf.Clamp((float)kelvin, 1000f, 40000f) / 100.0;
            double r, g, b;
            if (t <= 66.0)
            {
                r = 255.0;
                g = 99.4708025861 * System.Math.Log(t) - 161.1195681661;
            }
            else
            {
                r = 329.698727446 * System.Math.Pow(t - 60.0, -0.1332047592);
                g = 288.1221695283 * System.Math.Pow(t - 60.0, -0.0755148492);
            }
            if (t >= 66.0) b = 255.0;
            else if (t <= 19.0) b = 0.0;
            else b = 138.5177312231 * System.Math.Log(t - 10.0) - 305.0447927307;

            return new Color(Clamp01(r / 255.0), Clamp01(g / 255.0), Clamp01(b / 255.0), 1f);
        }

        static float Clamp01(double v) => Mathf.Clamp01((float)v);
    }
}
