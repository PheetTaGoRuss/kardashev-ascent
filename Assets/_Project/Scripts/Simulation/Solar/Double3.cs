namespace KardashevAscent.Simulation.Solar
{
    /// <summary>Double-precision vector in the local (east, north, up) frame.</summary>
    public readonly struct Double3
    {
        public readonly double East;
        public readonly double North;
        public readonly double Up;

        public Double3(double east, double north, double up)
        {
            East = east;
            North = north;
            Up = up;
        }

        public double Dot(Double3 other) => East * other.East + North * other.North + Up * other.Up;

        public override string ToString() => $"({East:F6}, {North:F6}, {Up:F6})";
    }
}
