using System;
using System.Collections.Generic;
using System.Numerics;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// The building slots inside one tile, covering the whole tile edge to edge.
    /// The slots are laid out on a perfect reference polygon (a regular hexagon, or a regular pentagon for the 12
    /// pentagon tiles) and then stretched onto the real tile one centre–corner–corner triangle at a time, because the
    /// tiles of a Goldberg sphere are not quite regular. On a hexagon the reference is sized so each edge runs through
    /// the centres of the outer row: <c>3R(R+1)+1</c> slots (R = rings), whole hexagons inside, halves along the edges
    /// and thirds in the corners. Together the slots fill the tile exactly.
    /// Work is done in the tile's tangent plane (gnomonic projection keeps the tile's straight edges straight).
    /// Slot 0 is the centre; ids grow ring by ring. Every slot shares its tile's sunlight (latitude/longitude).
    /// </summary>
    public sealed class TileSite
    {
        public const int DefaultRings = 4;

        /// <summary>Pieces smaller than this share of a whole slot are left out (slivers on the pentagons).</summary>
        public const double MinAreaShare = 0.05;

        static readonly double Sqrt3 = Math.Sqrt(3.0);

        readonly Vector3 east, north;
        readonly Vector2[] reference;                 // regular reference polygon, grid units (slot spacing 1)
        readonly Dictionary<(int Q, int R), int> slotAt;
        readonly Vector2[][] outlines;
        readonly Vector2[] centroids;
        readonly double[] areaShares;

        TileSite(int tile, int rings, Vector3 centre, Vector3 east, Vector3 north, Vector2[] reference,
                 double spacing, Vector2[] tileOutline, Dictionary<(int, int), int> slotAt,
                 Vector2[][] outlines, Vector2[] centroids, double[] areaShares)
        {
            Tile = tile;
            Rings = rings;
            Centre = centre;
            this.east = east;
            this.north = north;
            this.reference = reference;
            Spacing = spacing;
            TileOutline = tileOutline;
            this.slotAt = slotAt;
            this.outlines = outlines;
            this.centroids = centroids;
            this.areaShares = areaShares;
            SlotDirections = new Vector3[centroids.Length];
            for (int s = 0; s < centroids.Length; s++)
                SlotDirections[s] = DirectionAt(centroids[s].X, centroids[s].Y);
        }

        public int Tile { get; }
        public int Rings { get; }
        /// <summary>Unit direction to the tile's centre.</summary>
        public Vector3 Centre { get; }
        /// <summary>Typical distance between neighbouring slot centres, in tangent-plane units of the unit sphere (≈ radians).</summary>
        public double Spacing { get; }
        /// <summary>The tile's polygon in the tangent plane, (east, north), counter-clockwise.</summary>
        public Vector2[] TileOutline { get; }
        /// <summary>Unit direction to each slot's centre (the centre of its area).</summary>
        public Vector3[] SlotDirections { get; }
        public int SlotCount => centroids.Length;

        /// <summary>Slots on a hexagonal tile: 3R(R+1)+1.</summary>
        public static int HexagonSlotCount(int rings) => 3 * rings * (rings + 1) + 1;

        /// <summary>A slot's shape in the tangent plane, (east, north), counter-clockwise.</summary>
        public Vector2[] SlotOutline(int slot) => outlines[slot];

        /// <summary>Tangent-plane (east, north) centre of a slot's area.</summary>
        public Vector2 SlotOffset(int slot) => centroids[slot];

        /// <summary>A slot's size as a share of a whole slot (1 inside, ½ on edges, ⅓ in corners on a hexagon).</summary>
        public double SlotAreaShare(int slot) => areaShares[slot];

        public static TileSite Create(HexSphere sphere, int tile, int rings = DefaultRings)
        {
            if (sphere == null)
                throw new ArgumentNullException(nameof(sphere));
            if (tile < 0 || tile >= sphere.TileCount)
                throw new ArgumentOutOfRangeException(nameof(tile));
            if (rings < 1)
                throw new ArgumentOutOfRangeException(nameof(rings));

            Vector3 c = Vector3.Normalize(sphere.Centers[tile]);
            double lat = sphere.Latitude(tile), lon = sphere.Longitude(tile);
            Vector3 e = PlanetFrame.FromLocal(new Solar.Double3(1, 0, 0), lat, lon);
            Vector3 n = PlanetFrame.FromLocal(new Solar.Double3(0, 1, 0), lat, lon);

            // The tile's polygon in the tangent plane, counter-clockwise.
            Vector3[] corners = sphere.Corners[tile];
            int k = corners.Length;
            var polygon = new Vector2[k];
            for (int i = 0; i < k; i++)
            {
                Vector3 p = corners[i] / Vector3.Dot(corners[i], c) - c;
                polygon[i] = new Vector2(Vector3.Dot(p, e), Vector3.Dot(p, n));
            }
            if (SignedArea(polygon) < 0.0)
                Array.Reverse(polygon);

            // Reference polygon in grid units: a corner on the x axis, one every 360°/k, inradius R·√3/2 so each
            // hexagon edge runs through the centres of row R. Corner i of the reference becomes corner i of the tile.
            double refInradius = rings * Sqrt3 / 2.0;
            double refCircumradius = refInradius / Math.Cos(Math.PI / k);
            var reference = new Vector2[k];
            for (int i = 0; i < k; i++)
            {
                double a = 2.0 * Math.PI * i / k;
                reference[i] = new Vector2((float)(refCircumradius * Math.Cos(a)), (float)(refCircumradius * Math.Sin(a)));
            }

            double inradius = 0.0;
            for (int i = 0; i < k; i++)
                inradius += DistanceToLine(Vector2.Zero, polygon[i], polygon[(i + 1) % k]);
            double spacing = inradius / k / refInradius;
            double wholeSlot = Sqrt3 / 2.0;                        // area of one hexagonal slot, grid units

            var slotAt = new Dictionary<(int, int), int>();
            var outlines = new List<Vector2[]>();
            var centroids = new List<Vector2>();
            var shares = new List<double>();
            for (int ring = 0; ring <= rings + 1; ring++)
            {
                for (int q = -ring; q <= ring; q++)
                {
                    for (int r = -ring; r <= ring; r++)
                    {
                        if (HexDistance(q, r) != ring)
                            continue;
                        Vector2[] piece = Clip(Hexagon(q, r), reference);
                        if (piece.Length < 3)
                            continue;
                        double share = SignedArea(piece) / wholeSlot;
                        if (share < MinAreaShare)
                            continue;
                        Vector2[] onTile = WarpPolygon(piece, reference, polygon);
                        slotAt[(q, r)] = outlines.Count;
                        outlines.Add(onTile);
                        centroids.Add(Centroid(onTile));
                        shares.Add(share);
                    }
                }
            }
            return new TileSite(tile, rings, c, e, n, reference, spacing, polygon, slotAt,
                                outlines.ToArray(), centroids.ToArray(), shares.ToArray());
        }

        /// <summary>The slot under a direction from the planet's centre, or −1 if it is outside this tile.</summary>
        public int FindSlot(Vector3 direction)
        {
            float along = Vector3.Dot(direction, Centre);
            if (along <= 0f)
                return -1;
            Vector3 p3 = direction / along - Centre;
            var p = new Vector2(Vector3.Dot(p3, east), Vector3.Dot(p3, north));
            if (!Inside(p, TileOutline))
                return -1;

            // Back onto the reference polygon, then axial, then round to the nearest hexagon (cube rounding).
            Vector2 g = WarpPoint(p, TileOutline, reference);
            double rf = g.Y / (Sqrt3 / 2.0);
            double qf = g.X - rf / 2.0;
            double sf = -qf - rf;
            double q = Math.Round(qf), r = Math.Round(rf), s = Math.Round(sf);
            double dq = Math.Abs(q - qf), dr = Math.Abs(r - rf), ds = Math.Abs(s - sf);
            if (dq > dr && dq > ds) q = -r - s;
            else if (dr > ds) r = -q - s;
            return slotAt.TryGetValue(((int)q, (int)r), out int slot) ? slot : -1;
        }

        /// <summary>Tangent-plane (east, north) offset from the tile centre → unit direction.</summary>
        public Vector3 DirectionAt(double eastOffset, double northOffset) =>
            Vector3.Normalize(Centre + east * (float)eastOffset + north * (float)northOffset);

        /// <summary>Pointy hexagon of the axial grid in grid units (spacing 1): centre (q + r/2, r√3/2).</summary>
        static Vector2[] Hexagon(int q, int r)
        {
            double cx = q + r / 2.0, cy = r * Sqrt3 / 2.0;
            double radius = 1.0 / Sqrt3;
            var points = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                double a = (60.0 * i + 30.0) * Math.PI / 180.0;
                points[i] = new Vector2((float)(cx + radius * Math.Cos(a)), (float)(cy + radius * Math.Sin(a)));
            }
            return points;
        }

        /// <summary>
        /// Moves a polygon from one fan of centre–corner–corner triangles (around the origin) to another with the same
        /// number of corners, triangle by triangle. Points are added where edges cross a spoke so the result is exact.
        /// </summary>
        static Vector2[] WarpPolygon(Vector2[] polygon, Vector2[] from, Vector2[] to)
        {
            var dense = new List<Vector2>(polygon.Length * 2);
            var crossings = new List<(double T, Vector2 P)>();
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                dense.Add(a);
                crossings.Clear();
                Vector2 d = b - a;
                foreach (Vector2 spoke in from)
                {
                    // a + t·d = u·spoke
                    double denom = Cross(d, spoke);
                    if (Math.Abs(denom) < 1e-12)
                        continue;
                    double t = Cross(spoke, a) / denom;
                    double u = Cross(d, a) / denom;
                    if (t > 1e-6 && t < 1.0 - 1e-6 && u > 0.0)
                        crossings.Add((t, a + d * (float)t));
                }
                crossings.Sort((x, y) => x.T.CompareTo(y.T));
                foreach (var c in crossings)
                    dense.Add(c.P);
            }
            var result = new Vector2[dense.Count];
            for (int i = 0; i < dense.Count; i++)
                result[i] = WarpPoint(dense[i], from, to);
            return result;
        }

        /// <summary>One point from the fan of <paramref name="from"/> to the fan of <paramref name="to"/>, barycentric.</summary>
        static Vector2 WarpPoint(Vector2 p, Vector2[] from, Vector2[] to)
        {
            int k = from.Length, best = 0;
            double bestWorst = double.NegativeInfinity, bestU = 0.0, bestV = 0.0;
            for (int i = 0; i < k; i++)
            {
                Vector2 a = from[i], b = from[(i + 1) % k];
                double det = Cross(a, b);
                double u = Cross(p, b) / det, v = Cross(a, p) / det;   // p = u·a + v·b, the centre is the origin
                double worst = Math.Min(Math.Min(u, v), 1.0 - u - v);
                if (worst > bestWorst)
                {
                    bestWorst = worst;
                    best = i;
                    bestU = u;
                    bestV = v;
                }
            }
            return to[best] * (float)bestU + to[(best + 1) % k] * (float)bestV;
        }

        /// <summary>Sutherland–Hodgman: a polygon clipped by a convex counter-clockwise polygon.</summary>
        static Vector2[] Clip(Vector2[] subject, Vector2[] convex)
        {
            var output = new List<Vector2>(subject);
            for (int i = 0; i < convex.Length && output.Count > 0; i++)
            {
                Vector2 a = convex[i], b = convex[(i + 1) % convex.Length];
                var input = output;
                output = new List<Vector2>(input.Count + 2);
                for (int j = 0; j < input.Count; j++)
                {
                    Vector2 p = input[j], prev = input[(j + input.Count - 1) % input.Count];
                    bool pIn = Side(a, b, p) >= -1e-9, prevIn = Side(a, b, prev) >= -1e-9;
                    if (pIn)
                    {
                        if (!prevIn) output.Add(Intersect(prev, p, a, b));
                        output.Add(p);
                    }
                    else if (prevIn)
                    {
                        output.Add(Intersect(prev, p, a, b));
                    }
                }
            }
            return output.ToArray();
        }

        static double Side(Vector2 a, Vector2 b, Vector2 p) => Cross(b - a, p - a);

        static Vector2 Intersect(Vector2 p, Vector2 q, Vector2 a, Vector2 b)
        {
            double sp = Side(a, b, p), sq = Side(a, b, q);
            return p + (q - p) * (float)(sp / (sp - sq));
        }

        static bool Inside(Vector2 p, Vector2[] convex)
        {
            for (int i = 0; i < convex.Length; i++)
                if (Side(convex[i], convex[(i + 1) % convex.Length], p) < -1e-12)
                    return false;
            return true;
        }

        static double Cross(Vector2 a, Vector2 b) => (double)a.X * b.Y - (double)a.Y * b.X;

        static double SignedArea(Vector2[] polygon)
        {
            double area = 0.0;
            for (int i = 0; i < polygon.Length; i++)
                area += Cross(polygon[i], polygon[(i + 1) % polygon.Length]);
            return area / 2.0;
        }

        static Vector2 Centroid(Vector2[] polygon)
        {
            double cx = 0.0, cy = 0.0, area = 0.0;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                double cross = Cross(a, b);
                area += cross;
                cx += (a.X + b.X) * cross;
                cy += (a.Y + b.Y) * cross;
            }
            area /= 2.0;
            return new Vector2((float)(cx / (6.0 * area)), (float)(cy / (6.0 * area)));
        }

        static int HexDistance(int q, int r) => Math.Max(Math.Abs(q), Math.Max(Math.Abs(r), Math.Abs(q + r)));

        static double DistanceToLine(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            Vector2 t = a + ab * (Vector2.Dot(point - a, ab) / ab.LengthSquared());
            return (point - t).Length();
        }
    }
}
