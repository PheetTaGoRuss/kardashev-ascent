using System;
using System.Collections.Generic;
using System.Numerics;

namespace KardashevAscent.Simulation.Planet
{
    /// <summary>
    /// The building slots inside one tile: R rings of whole, regular hexagons (<c>3R(R+1)+1</c> slots on a hexagonal
    /// tile) with an even empty border between them and the tile's edge. The slots sit on a regular hex lattice with one
    /// more row (R + 1) placed a quarter row-step (√3/4 of the spacing) in from each edge; that outer row holds no slots
    /// and only shapes the border, so every slot is an exact hexagon (its Voronoi cell among the lattice points).
    /// The layout is made on a regular reference polygon and fitted onto the real tile one centre–corner–corner
    /// triangle at a time, because the tiles of a Goldberg sphere are not quite regular. The 12 pentagon tiles use a
    /// regular pentagon and keep the lattice points with all six neighbours inside it.
    /// Work is done in the tile's tangent plane (gnomonic projection keeps the tile's straight edges straight).
    /// Slot 0 is the centre; ids grow ring by ring. Every slot shares its tile's sunlight (latitude/longitude).
    /// </summary>
    public sealed class TileSite
    {
        public const int DefaultRings = 4;

        static readonly double Sqrt3 = Math.Sqrt(3.0);
        /// <summary>Gap from the border row of lattice points to the tile edge, in slot spacings: half a row-step.</summary>
        static readonly double EdgeInset = Sqrt3 / 4.0;

        readonly Vector3 east, north;
        readonly Vector2[] reference;                     // regular reference polygon, grid units (slot spacing 1)
        readonly Vector2[] centres;                       // every lattice point on the reference, grid units
        readonly int[] slotOf;                            // lattice point → slot id, −1 on the border row
        readonly Vector2[][] outlines;
        readonly Vector2[] centroids;
        readonly double[] areaShares;

        TileSite(int tile, int rings, Vector3 centre, Vector3 east, Vector3 north, Vector2[] reference, Vector2[] centres,
                 int[] slotOf, double spacing, Vector2[] tileOutline, Vector2[][] outlines, Vector2[] centroids,
                 double[] areaShares)
        {
            Tile = tile;
            Rings = rings;
            Centre = centre;
            this.east = east;
            this.north = north;
            this.reference = reference;
            this.centres = centres;
            this.slotOf = slotOf;
            Spacing = spacing;
            TileOutline = tileOutline;
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

        /// <summary>
        /// The <paramref name="count"/> slots nearest to <paramref name="anchor"/> (the anchor first, then by distance,
        /// ties by id): the footprint of a building that covers several slots (a compact patch inside this tile).
        /// </summary>
        public List<int> NearestSlots(int anchor, int count)
        {
            var list = new List<int>(centroids.Length);
            for (int s = 0; s < centroids.Length; s++) list.Add(s);
            Vector2 a = centroids[anchor];
            list.Sort((x, y) =>
            {
                int c = Vector2.DistanceSquared(centroids[x], a).CompareTo(Vector2.DistanceSquared(centroids[y], a));
                return c != 0 ? c : x.CompareTo(y);
            });
            return list.GetRange(0, Math.Max(1, Math.Min(count, list.Count)));
        }

        /// <summary>A slot's area compared with a regular slot (1: every slot is a whole regular hexagon).</summary>
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

            // Reference polygon in grid units (spacing 1), a corner on the x axis and one every 360°/k. Its edges run
            // parallel to rows of the lattice, EdgeInset beyond row R + 1: that outer row is the empty border.
            double refInradius = (rings + 1) * Sqrt3 / 2.0 + EdgeInset;
            double refCircumradius = refInradius / Math.Cos(Math.PI / k);
            var reference = new Vector2[k];
            for (int i = 0; i < k; i++)
            {
                double a = 2.0 * Math.PI * i / k;
                reference[i] = new Vector2((float)(refCircumradius * Math.Cos(a)), (float)(refCircumradius * Math.Sin(a)));
            }

            // Lattice points at least (about) EdgeInset inside every edge, ring by ring.
            var lattice = new List<(int Q, int R, Vector2 P)>();
            var present = new HashSet<(int, int)>();
            for (int ring = 0; ring <= rings + 2; ring++)
                for (int q = -ring; q <= ring; q++)
                    for (int r = -ring; r <= ring; r++)
                    {
                        if (HexDistance(q, r) != ring)
                            continue;
                        var point = new Vector2((float)(q + r / 2.0), (float)(r * Sqrt3 / 2.0));
                        if (Inside(point, reference, EdgeInset * 0.95))
                        {
                            lattice.Add((q, r, point));
                            present.Add((q, r));
                        }
                    }

            // Slots are the points whose six neighbours are all on the lattice: their cells are whole regular
            // hexagons. The outer points (next to an edge) are only there to shape the border and hold no slot.
            var centres = new Vector2[lattice.Count];
            var slotOf = new int[lattice.Count];
            var slotCentres = new List<Vector2>();
            for (int i = 0; i < lattice.Count; i++)
            {
                var (q, r, point) = lattice[i];
                centres[i] = point;
                bool inner = present.Contains((q + 1, r)) && present.Contains((q - 1, r)) &&
                             present.Contains((q, r + 1)) && present.Contains((q, r - 1)) &&
                             present.Contains((q + 1, r - 1)) && present.Contains((q - 1, r + 1));
                slotOf[i] = inner ? slotCentres.Count : -1;
                if (inner)
                    slotCentres.Add(point);
            }

            double inradius = 0.0;
            for (int i = 0; i < k; i++)
                inradius += DistanceToLine(Vector2.Zero, polygon[i], polygon[(i + 1) % k]);
            double spacing = inradius / k / refInradius;          // one grid unit on the real tile
            double wholeSlot = Sqrt3 / 2.0;                        // area of one regular slot, grid units

            var outlines = new Vector2[slotCentres.Count][];
            var centroids = new Vector2[slotCentres.Count];
            var shares = new double[slotCentres.Count];
            for (int s = 0; s < slotCentres.Count; s++)
            {
                // Voronoi cell: cut by the perpendicular bisector with every nearby lattice point (a regular hexagon).
                Vector2[] cell = reference;
                foreach (Vector2 other in centres)
                    if (other != slotCentres[s] && Vector2.DistanceSquared(slotCentres[s], other) < 4.5f)
                        cell = ClipCloserTo(cell, slotCentres[s], other);
                shares[s] = SignedArea(cell) / wholeSlot;
                outlines[s] = WarpPolygon(cell, reference, polygon);
                centroids[s] = Centroid(outlines[s]);
            }
            return new TileSite(tile, rings, c, e, n, reference, centres, slotOf, spacing, polygon,
                                outlines, centroids, shares);
        }

        /// <summary>The slot under a direction from the planet's centre, or −1 if it is outside this tile.</summary>
        public int FindSlot(Vector3 direction)
        {
            float along = Vector3.Dot(direction, Centre);
            if (along <= 0f)
                return -1;
            Vector3 p3 = direction / along - Centre;
            var p = new Vector2(Vector3.Dot(p3, east), Vector3.Dot(p3, north));
            if (!Inside(p, TileOutline, -1e-12))
                return -1;

            // Back onto the reference polygon; the nearest lattice point owns the spot (a Voronoi cell). Points of the
            // border row own no slot.
            Vector2 g = WarpPoint(p, TileOutline, reference);
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < centres.Length; i++)
            {
                float d = Vector2.DistanceSquared(g, centres[i]);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return best >= 0 ? slotOf[best] : -1;
        }

        /// <summary>Tangent-plane (east, north) offset from the tile centre → unit direction.</summary>
        public Vector3 DirectionAt(double eastOffset, double northOffset) =>
            Vector3.Normalize(Centre + east * (float)eastOffset + north * (float)northOffset);

        /// <summary>The part of a convex polygon closer to <paramref name="mine"/> than to <paramref name="other"/>.</summary>
        static Vector2[] ClipCloserTo(Vector2[] polygon, Vector2 mine, Vector2 other)
        {
            Vector2 mid = (mine + other) * 0.5f, away = other - mine;
            double Value(Vector2 p) => Vector2.Dot(p - mid, away);    // ≤ 0 on my side

            var output = new List<Vector2>(polygon.Length + 1);
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                double va = Value(a), vb = Value(b);
                if (va <= 0.0)
                    output.Add(a);
                if ((va < 0.0 && vb > 0.0) || (va > 0.0 && vb < 0.0))
                    output.Add(a + (b - a) * (float)(va / (va - vb)));
            }
            return output.ToArray();
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
                foreach (var cr in crossings)
                    dense.Add(cr.P);
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

        /// <summary>True if <paramref name="p"/> is at least <paramref name="margin"/> inside every edge of a convex CCW polygon.</summary>
        static bool Inside(Vector2 p, Vector2[] convex, double margin)
        {
            for (int i = 0; i < convex.Length; i++)
            {
                Vector2 a = convex[i], b = convex[(i + 1) % convex.Length];
                double distance = Cross(b - a, p - a) / (b - a).Length();
                if (distance < margin)
                    return false;
            }
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
