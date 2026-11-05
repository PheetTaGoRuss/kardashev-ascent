using System;
using System.Numerics;
using KardashevAscent.Simulation.Solar;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// The planet-fixed frame every system shares (also the PlanetView's local space in Unity):
    /// unit sphere, +Y = north pole, longitude 0 toward +Z, longitude increases toward +X (east).
    /// Converts between this frame, latitude/longitude, and a tile's local (east, north, up) frame
    /// used by the solar model.
    /// </summary>
    public static class PlanetFrame
    {
        /// <summary>Unit vector for a latitude/longitude (radians).</summary>
        public static Vector3 FromLatLon(double latitude, double longitude)
        {
            double cl = Math.Cos(latitude);
            return new Vector3((float)(cl * Math.Sin(longitude)), (float)Math.Sin(latitude), (float)(cl * Math.Cos(longitude)));
        }

        /// <summary>Latitude (radians, + north) of a direction in the planet frame.</summary>
        public static double Latitude(Vector3 direction)
        {
            double len = direction.Length();
            return Math.Asin(Math.Max(-1.0, Math.Min(1.0, direction.Y / len)));
        }

        /// <summary>Longitude (radians, −π..π, + east) of a direction in the planet frame.</summary>
        public static double Longitude(Vector3 direction) => Math.Atan2(direction.X, direction.Z);

        /// <summary>A planet-frame direction expressed in the (east, north, up) frame at a latitude/longitude.</summary>
        public static Double3 ToLocal(Vector3 direction, double latitude, double longitude)
        {
            double sl = Math.Sin(latitude), cl = Math.Cos(latitude);
            double so = Math.Sin(longitude), co = Math.Cos(longitude);
            double x = direction.X, y = direction.Y, z = direction.Z;
            double east = x * co - z * so;
            double north = -x * sl * so + y * cl - z * sl * co;
            double up = x * cl * so + y * sl + z * cl * co;
            return new Double3(east, north, up);
        }

        /// <summary>An (east, north, up) direction at a latitude/longitude, expressed in the planet frame.</summary>
        public static Vector3 FromLocal(Double3 local, double latitude, double longitude)
        {
            double sl = Math.Sin(latitude), cl = Math.Cos(latitude);
            double so = Math.Sin(longitude), co = Math.Cos(longitude);
            double x = local.East * co - local.North * sl * so + local.Up * cl * so;
            double y = local.North * cl + local.Up * sl;
            double z = -local.East * so - local.North * sl * co + local.Up * cl * co;
            return new Vector3((float)x, (float)y, (float)z);
        }

        /// <summary>
        /// Direction to the star in the planet frame, from the sub-stellar point
        /// (where the star is overhead): latitude = declination δ, longitude = sub-stellar longitude.
        /// A tile at longitude λ then sees hour angle h = λ − subStellarLongitude.
        /// </summary>
        public static Vector3 StarDirection(double declination, double subStellarLongitude) =>
            FromLatLon(declination, subStellarLongitude);
    }
}
