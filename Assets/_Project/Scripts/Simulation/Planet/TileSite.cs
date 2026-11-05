using System;
using System.Collections.Generic;
using System.Numerics;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// The building slots inside one tile: whole small hexagons that fill the tile exactly, with no half or corner
    /// pieces. The slots start as a regular hex grid of R rings (<c>3R(R+1)+1</c> slots on a hexagonal tile).
    /// That grid's outline is jagged, so it is stretched outward until its outline lies on the tile's edges: inside,
    /// the slots stay regular hexagons; the outer ring leans out a little so its outer sides lie flat along the edge.
    /// The result is then fitted onto the real tile one centre–corner–corner triangle at a time, because the tiles of
    /// a Goldberg sphere are not quite regular. The 12 pentagon tiles get the whole hexagons that fit inside a
    /// pentagon, stretched the same way.
    /// Work is done in the tile's tangent plane (gnomonic projection keeps the tile's straight edges straight).
    /// Slot 0 is the centre; ids grow ring by ring. Every slot shares its tile's sunlight (latitude/longitude).
    /// </summary>
    public sealed class TileSite
    {
        public const int DefaultRings = 4;

        /// <summary>How sharply the stretch fades toward the centre: higher keeps inner slots more regular.</summary>
        const double StretchFalloff = 6.0;
        /// <summary>Points per hexagon side when bending slot outlines.</summary>
        const int EdgeSteps = 4;

        static readonly double Sqrt3 = Math.Sqrt(3.0);

        readonly Vector3 east, north;
        readonly Vector2[] reference;                     // regular reference polygon, grid units (slot spacing 1)
        readonly (Vector2 A, Vector2 B)[] gridOutline;     // outline of the unstretched hex grid
        readonly Dictionary<(int Q, int R), int> slotAt;
        readonly Vector2[][] outlines;
        readonly Vector2[] centroids;
        readonly double[] areaShares;

        TileSite(int tile, int rings, Vector3 centre, Vector3 east, Vector3 north, Vector2[] reference,
                 (Vector2, Vector2)[] gridOutline, double spacing, Vector2[] tileOutline,
                 Dictionary<(int, int), int> slotAt, Vector2[][] outlines, Vector2[] centroids, double[] areaShares)
        {
            Tile = tile;
            Rings = rings;
            Centre = centre;
            this.east = east;
            this.north = north;
            this.reference = reference;
            this.gridOutline = gridOutline;
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

        /// <summary>A slot's size compared with a regular slot of the grid: 1 inside, a little more on the outer ring.</summary>
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

            // Reference polygon in grid units, a corner on the x axis and one every 360°/k. Its inradius reaches the
            // outermost points of ring R, so R rings of whole hexagons just fit inside a hexagonal one.
            double refInradius = rings * Sqrt3 / 2.0 + 1.0 / Sqrt3;
            double refCircumradius = refInradius / Math.Cos(Math.PI / k);
            var reference = new Vector2[k];
            for (int i = 0; i < k; i++)
            {
                double a = 2.0 * Math.PI * i / k;
                reference[i] = new Vector2((float)(refCircumradius * Math.Cos(a)), (float)(refCircumradius * Math.Sin(a)));
            }

            // Whole hexagons that fit inside the reference polygon, ring by ring.
            var cells = new List<(int Q, int R, Vector2[] Hex)>();
            for (int ring = 0; ring <= rings + 1; ring++)
                for (int q = -ring; q <= ring; q++)
                    for (int r = -ring; r <= ring; r++)
                    {
                        if (HexDistance(q, r) != ring)
                            continue;
                        Vector2[] hex = Hexagon(q, r);
                        if (AllInside(hex, reference, 1e-4))
                            cells.Add((q, r, hex));
                    }
            var gridOutline = OuterEdges(cells);

            double inradius = 0.0;
            for (int i = 0; i < k; i++)
                inradius += DistanceToLine(Vector2.Zero, polygon[i], polygon[(i + 1) % k]);
            double spacing = inradius / k / refInradius;          // one grid unit on the real tile
            double wholeSlot = Sqrt3 / 2.0;                        // area of one regular slot, grid units

            var slotAt = new Dictionary<(int, int), int>();
            var outlines = new List<Vector2[]>();
            var centroids = new List<Vector2>();
            var shares = new List<double>();
            foreach (var cell in cells)
            {
                Vector2[] dense = Densify(cell.Hex, EdgeSteps);
                var stretched = new Vector2[dense.Length];
                for (int i = 0; i < dense.Length; i++)
                    stretched[i] = Stretch(dense[i], gridOutline, reference);
                Vector2[] onTile = WarpPolygon(stretched, reference, polygon);
                slotAt[(cell.Q, cell.R)] = outlines.Count;
                outlines.Add(onTile);
                centroids.Add(Centroid(onTile));
                shares.Add(SignedArea(stretched) / wholeSlot);
            }
            return new TileSite(tile, rings, c, e, n, reference, gridOutline, spacing, polygon, slotAt,
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
            if (!Inside(p, TileOutline, 1e-12))
                return -1;

            // Back onto the reference polygon, undo the stretch, then round to the nearest hexagon (cube rounding).
            Vector2 g = Unstretch(WarpPoint(p, TileOutline, reference), gridOutline, reference);
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

        // ---- Stretching the jagged hex grid onto the polygon ----

        /// <summary>
        /// Pushes a point of the hex grid outward along its direction from the centre so the grid's jagged outline
        /// lands on the polygon's edges. The push grows as (r / outline)⁶, so inner slots barely move.
        /// </summary>
        static Vector2 Stretch(Vector2 p, (Vector2 A, Vector2 B)[] grid, Vector2[] polygon)
        {
            float r = p.Length();
            if (r < 1e-9f)
                return p;
            Vector2 d = p / r;
            double inner = FarthestHit(d, grid), outer = FarthestHit(d, polygon);
            double t = Math.Min(1.0, r / inner);
            double scale = 1.0 + (outer / inner - 1.0) * Math.Pow(t, StretchFalloff);
            return p * (float)scale;
        }

        /// <summary>Inverse of <see cref="Stretch"/> (the stretch only grows along each ray, so bisection finds it).</summary>
        static Vector2 Unstretch(Vector2 p, (Vector2 A, Vector2 B)[] grid, Vector2[] polygon)
        {
            float target = p.Length();
            if (target < 1e-9f)
                return p;
            Vector2 d = p / target;
            double inner = FarthestHit(d, grid), outer = FarthestHit(d, polygon);
            if (target >= outer)
                return d * (float)inner;
            double lo = 0.0, hi = inner;
            for (int i = 0; i < 40; i++)
            {
                double mid = 0.5 * (lo + hi);
                double stretched = mid * (1.0 + (outer / inner - 1.0) * Math.Pow(mid / inner, StretchFalloff));
                if (stretched < target) lo = mid; else hi = mid;
            }
            return d * (float)(0.5 * (lo + hi));
        }

        /// <summary>Farthest crossing of the ray from the origin along <paramref name="d"/> with a set of edges.</summary>
        static double FarthestHit(Vector2 d, (Vector2 A, Vector2 B)[] edges)
        {
            double best = 0.0;
            foreach (var (a, b) in edges)
            {
                Vector2 ab = b - a;
                double denom = Cross(d, ab);
                if (Math.Abs(denom) < 1e-12)
                    continue;
                double t = Cross(a, ab) / denom;           // distance along the ray
                double u = Cross(a, d) / denom;            // position along the edge
                if (t > 0.0 && u >= -1e-9 && u <= 1.0 + 1e-9 && t > best)
                    best = t;
            }
            return best;
        }

        static double FarthestHit(Vector2 d, Vector2[] polygon)
        {
            var edges = new (Vector2, Vector2)[polygon.Length];
            for (int i = 0; i < polygon.Length; i++)
                edges[i] = (polygon[i], polygon[(i + 1) % polygon.Length]);
            return FarthestHit(d, edges);
        }

        /// <summary>Edges that belong to only one cell: the outline of the whole grid.</summary>
        static (Vector2, Vector2)[] OuterEdges(List<(int Q, int R, Vector2[] Hex)> cells)
        {
            var seen = new Dictionary<(long, long, long, long), (Vector2 A, Vector2 B, int Count)>();
            foreach (var cell in cells)
            {
                for (int i = 0; i < 6; i++)
                {
                    Vector2 a = cell.Hex[i], b = cell.Hex[(i + 1) % 6];
                    var ka = Key(a);
                    var kb = Key(b);
                    var key = ka.CompareTo(kb) < 0 ? (ka.Item1, ka.Item2, kb.Item1, kb.Item2)
                                                   : (kb.Item1, kb.Item2, ka.Item1, ka.Item2);
                    seen[key] = seen.TryGetValue(key, out var entry) ? (entry.A, entry.B, entry.Count + 1) : (a, b, 1);
                }
            }
            var outline = new List<(Vector2, Vector2)>();
            foreach (var entry in seen.Values)
                if (entry.Count == 1)
                    outline.Add((entry.A, entry.B));
            return outline.ToArray();
        }

        static (long, long) Key(Vector2 p) => ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4));

        static Vector2[] Densify(Vector2[] polygon, int steps)
        {
            var result = new Vector2[polygon.Length * steps];
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                for (int j = 0; j < steps; j++)
                    result[i * steps + j] = a + (b - a) * (j / (float)steps);
            }
            return result;
        }

        // ---- Fitting the regular polygon onto the real tile ----

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

        // ---- Geometry helpers ----

        static double Side(Vector2 a, Vector2 b, Vector2 p) => Cross(b - a, p - a);

        static bool Inside(Vector2 p, Vector2[] convex, double tolerance)
        {
            for (int i = 0; i < convex.Length; i++)
                if (Side(convex[i], convex[(i + 1) % convex.Length], p) < -tolerance)
                    return false;
            return true;
        }

        static bool AllInside(Vector2[] points, Vector2[] convex, double tolerance)
        {
            foreach (Vector2 p in points)
                if (!Inside(p, convex, tolerance))
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
