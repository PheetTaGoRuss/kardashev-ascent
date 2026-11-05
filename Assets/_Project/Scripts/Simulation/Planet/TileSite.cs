using System;
using System.Numerics;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// The building slots inside one tile: a small hex grid of <c>3R(R+1)+1</c> slots (R = rings) laid out in the
    /// tile's tangent plane and pushed back onto the unit sphere. The grid fits inside the tile's inscribed circle,
    /// so hexagons and the 12 pentagons get the same number of slots.
    /// Slot 0 is the centre; slot ids grow ring by ring. Every slot shares its tile's sunlight (latitude/longitude).
    /// </summary>
    public sealed class TileSite
    {
        public const int DefaultRings = 4;

        readonly Vector3 east, north;
        readonly int[,] slotAt; // axial (q + R, r + R) → slot id, −1 outside the grid

        TileSite(int tile, int rings, Vector3 centre, Vector3 east, Vector3 north, double spacing,
                 Vector3[] directions, int[] q, int[] r, int[,] slotAt)
        {
            Tile = tile;
            Rings = rings;
            Centre = centre;
            this.east = east;
            this.north = north;
            Spacing = spacing;
            SlotDirections = directions;
            AxialQ = q;
            AxialR = r;
            this.slotAt = slotAt;
        }

        public int Tile { get; }
        public int Rings { get; }
        /// <summary>Unit direction to the tile's centre.</summary>
        public Vector3 Centre { get; }
        /// <summary>Distance between neighbouring slot centres, in tangent-plane units of the unit sphere (≈ radians).</summary>
        public double Spacing { get; }
        /// <summary>Unit direction to each slot's centre.</summary>
        public Vector3[] SlotDirections { get; }
        public int[] AxialQ { get; }
        public int[] AxialR { get; }
        public int SlotCount => SlotDirections.Length;

        public static int SlotCountFor(int rings) => 3 * rings * (rings + 1) + 1;

        public static TileSite Create(HexSphere sphere, int tile, int rings = DefaultRings)
        {
            if (sphere == null)
                throw new ArgumentNullException(nameof(sphere));
            if (tile < 0 || tile >= sphere.TileCount)
                throw new ArgumentOutOfRangeException(nameof(tile));
            if (rings < 0)
                throw new ArgumentOutOfRangeException(nameof(rings));

            Vector3 c = Vector3.Normalize(sphere.Centers[tile]);
            double lat = sphere.Latitude(tile), lon = sphere.Longitude(tile);
            Vector3 e = PlanetFrame.FromLocal(new Solar.Double3(1, 0, 0), lat, lon);
            Vector3 n = PlanetFrame.FromLocal(new Solar.Double3(0, 1, 0), lat, lon);

            // Inscribed circle of the tile polygon in the tangent plane (gnomonic projection keeps edges straight).
            Vector3[] corners = sphere.Corners[tile];
            double inradius = double.MaxValue;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 a = Project(corners[i], c), b = Project(corners[(i + 1) % corners.Length], c);
                inradius = Math.Min(inradius, DistanceToLine(Vector3.Zero, a, b));
            }

            // The outer ring's slot pads (circumradius spacing/√3) must stay inside that circle.
            double spacing = inradius / (rings + 1.0 / Math.Sqrt(3.0) + 0.15);
            int count = SlotCountFor(rings);
            var directions = new Vector3[count];
            var qs = new int[count];
            var rs = new int[count];
            var slotAt = new int[2 * rings + 1, 2 * rings + 1];
            for (int i = 0; i <= 2 * rings; i++)
                for (int j = 0; j <= 2 * rings; j++)
                    slotAt[i, j] = -1;

            int id = 0;
            for (int ring = 0; ring <= rings; ring++)
            {
                for (int q = -ring; q <= ring; q++)
                {
                    for (int r = -ring; r <= ring; r++)
                    {
                        if (HexDistance(q, r) != ring)
                            continue;
                        Vector2 p = AxialToPlane(q, r, spacing);
                        directions[id] = Vector3.Normalize(c + e * p.X + n * p.Y);
                        qs[id] = q;
                        rs[id] = r;
                        slotAt[q + rings, r + rings] = id;
                        id++;
                    }
                }
            }
            return new TileSite(tile, rings, c, e, n, spacing, directions, qs, rs, slotAt);
        }

        /// <summary>The slot under a direction from the planet's centre, or −1 if it is outside this site's grid.</summary>
        public int FindSlot(Vector3 direction)
        {
            float along = Vector3.Dot(direction, Centre);
            if (along <= 0f)
                return -1;
            Vector3 p = direction / along - Centre;
            double x = Vector3.Dot(p, east), y = Vector3.Dot(p, north);

            // Inverse of AxialToPlane, then round to the nearest hex (cube rounding).
            double rf = y / (Spacing * Math.Sqrt(3.0) / 2.0);
            double qf = x / Spacing - rf / 2.0;
            double sf = -qf - rf;
            double q = Math.Round(qf), r = Math.Round(rf), s = Math.Round(sf);
            double dq = Math.Abs(q - qf), dr = Math.Abs(r - rf), ds = Math.Abs(s - sf);
            if (dq > dr && dq > ds) q = -r - s;
            else if (dr > ds) r = -q - s;

            int qi = (int)q, ri = (int)r;
            if (HexDistance(qi, ri) > Rings)
                return -1;
            return slotAt[qi + Rings, ri + Rings];
        }

        /// <summary>Position of a point in the tangent plane at the site centre: (east, north) offset → unit direction.</summary>
        public Vector3 DirectionAt(double eastOffset, double northOffset) =>
            Vector3.Normalize(Centre + east * (float)eastOffset + north * (float)northOffset);

        /// <summary>Tangent-plane (east, north) offset of a slot centre.</summary>
        public Vector2 SlotOffset(int slot) => AxialToPlane(AxialQ[slot], AxialR[slot], Spacing);

        static Vector2 AxialToPlane(int q, int r, double spacing) =>
            new Vector2((float)(spacing * (q + r / 2.0)), (float)(spacing * r * Math.Sqrt(3.0) / 2.0));

        static int HexDistance(int q, int r) => Math.Max(Math.Abs(q), Math.Max(Math.Abs(r), Math.Abs(q + r)));

        static Vector3 Project(Vector3 direction, Vector3 centre) =>
            direction / Vector3.Dot(direction, centre) - centre;

        static double DistanceToLine(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            Vector3 t = a + ab * (Vector3.Dot(point - a, ab) / ab.LengthSquared());
            return (point - t).Length();
        }
    }
}
