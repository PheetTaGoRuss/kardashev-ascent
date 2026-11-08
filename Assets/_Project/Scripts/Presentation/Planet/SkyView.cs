using System;
using System.Collections.Generic;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using UnityEngine;

namespace KardashevAscent.Presentation.Planet
{
    /// <summary>
    /// The sky view: zoom in on one panel and draw the local sky around it as a half dome (celestial sphere seen from
    /// the panel): horizon with N/E/S/W, zenith and meridian, the sun and the panel's normal; and, once the player has
    /// found them (<see cref="SkyNotebook"/>), the angle between them, the celestial pole tilted by the latitude, the
    /// celestial equator, today's sun path and the solstice paths (±ε).
    /// Opens with V on a selected slot, or by changing a single panel's angles; V or Esc closes it.
    /// </summary>
    [RequireComponent(typeof(PlanetView))]
    public sealed class SkyView : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] SiteView siteView;
        [SerializeField] PanelPlacer placer;
        [SerializeField] Material lineMaterial;
        [SerializeField] KeyCode toggleKey = KeyCode.V;
        [Tooltip("Dome radius in slot spacings.")]
        [Range(0.5f, 6f)] [SerializeField] float domeRadiusSlots = 1.3f;
        [Tooltip("Camera angle in the sky view, degrees from looking straight down.")]
        [Range(0f, 80f)] [SerializeField] float viewTilt = 66f;
        [Tooltip("Open the sky view when a single panel's angles change.")]
        [SerializeField] bool openOnAdjust = true;

        static readonly Color Horizon = new Color(1f, 1f, 1f, 0.75f);
        static readonly Color Faint = new Color(1f, 1f, 1f, 0.25f);
        static readonly Color SunColor = new Color(1f, 0.85f, 0.3f, 1f);
        static readonly Color SunPath = new Color(1f, 0.8f, 0.3f, 0.7f);
        static readonly Color Solstice = new Color(1f, 0.6f, 0.3f, 0.35f);
        static readonly Color NormalColor = new Color(0.4f, 0.9f, 1f, 1f);
        static readonly Color Incidence = new Color(1f, 0.55f, 0.2f, 1f);
        static readonly Color PoleColor = new Color(0.75f, 0.55f, 1f, 0.9f);
        static readonly Color EquatorColor = new Color(0.75f, 0.55f, 1f, 0.5f);

        PlanetView planet;
        GameObject root;
        readonly Dictionary<string, List<LineRenderer>> lines = new Dictionary<string, List<LineRenderer>>();
        float lastTilt = float.NaN, lastAzimuth = float.NaN;
        int lastSlot = -1;

        public bool Active { get; private set; }
        public int Tile { get; private set; } = -1;
        public int Slot { get; private set; } = -1;
        /// <summary>The colony's notebook (saved with the game); an empty one before the simulation exists.</summary>
        public SkyNotebook Notebook => runner != null && runner.Simulation != null ? runner.Simulation.Notebook : emptyNotebook;
        readonly SkyNotebook emptyNotebook = new SkyNotebook();
        public SkyReport Report { get; private set; }
        public KeyCode ToggleKey => toggleKey;

        /// <summary>The panel's tilt and azimuth the view is about (radians): the built panel's, or the planned ones.</summary>
        public double Tilt { get; private set; }
        public double Azimuth { get; private set; }
        public bool PanelBuilt { get; private set; }

        /// <summary>Points to label on screen this frame: text and position (planet-local).</summary>
        public readonly List<(string Text, Vector3 Local, Color Color)> Labels = new List<(string, Vector3, Color)>();

        void Awake()
        {
            planet = GetComponent<PlanetView>();
            root = new GameObject("Sky Dome") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(transform, false);
            root.SetActive(false);
        }

        void OnEnable()
        {
            if (runner != null)
                runner.SimulationCreated += OnSimulationCreated;
        }

        void OnDisable()
        {
            if (runner != null)
                runner.SimulationCreated -= OnSimulationCreated;
        }

        void OnDestroy()
        {
            if (root != null)
                Destroy(root);
        }

        void OnSimulationCreated()
        {
            Close();
        }

        public void Open(int slot)
        {
            if (siteView == null || siteView.Site == null || slot < 0)
                return;
            bool wasActive = Active;
            Active = true;
            Slot = lastSlot = slot;
            Tile = runner != null && runner.Simulation != null ? CurrentTile() : -1;
            root.SetActive(true);
            // Close in, low over the panel, so the dome reads as a half sphere; remember the site view to go back to.
            Vector2 before = siteView.FocusSlot(slot, DomeRadiusWorld() * 0.9f, viewTilt);
            if (!wasActive)
                siteViewBefore = before;
        }

        public void Close()
        {
            if (!Active)
                return;
            Active = false;
            root.SetActive(false);
            Labels.Clear();
            if (siteView != null && siteView.Site != null)
                siteView.FocusWholeSite(siteViewBefore);
        }

        Vector2 siteViewBefore;

        int CurrentTile()
        {
            var pickerTile = GetComponent<TilePicker>();
            return pickerTile != null ? pickerTile.SelectedTile : -1;
        }

        // ---- Player actions (called by the sky panel) ----

        public string WorkOutDeclination() => Notebook.WorkOutDeclination(Report);
        public string MeasureSun() => Notebook.MeasureSun(Tile, Report);
        public string MeasurePole() => Notebook.MeasurePole(Tile, Report);

        void Update()
        {
            bool siteOpen = siteView != null && siteView.Site != null;
            int selected = siteOpen && siteView.Selection.Count == 1 ? siteView.SelectedSlot : -1;

            if (!siteOpen || (Active && selected < 0))
            {
                Close();
                lastTilt = lastAzimuth = float.NaN;
                return;
            }
            if (Input.GetKeyDown(toggleKey) && selected >= 0)
            {
                if (Active) Close();
                else Open(selected);
            }
            else if (Active && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }

            // Changing a single panel's angles brings the sky view in (the player is aiming the panel at the sun).
            if (placer != null && selected >= 0)
            {
                bool changed = selected == lastSlot && !float.IsNaN(lastTilt)
                               && (Mathf.Abs(placer.TiltDeg - lastTilt) > 0.01f || Mathf.Abs(placer.AzimuthDeg - lastAzimuth) > 0.01f);
                if (changed && openOnAdjust && !Active)
                    Open(selected);
                lastTilt = placer.TiltDeg;
                lastAzimuth = placer.AzimuthDeg;
                lastSlot = selected;
            }
            if (Active && selected >= 0 && selected != Slot)
                Open(selected);   // follow the selection to another slot

            if (!Active || runner == null || runner.Simulation == null)
                return;

            if (placer != null && placer.TryGetSelectedPanel(out double t, out double a, out _, out _))
            {
                Tilt = t;
                Azimuth = a;
                PanelBuilt = true;
            }
            else if (placer != null)
            {
                Tilt = placer.TiltDeg * Mathf.Deg2Rad;
                Azimuth = placer.AzimuthDeg * Mathf.Deg2Rad;
                PanelBuilt = false;
            }
            Report = runner.Simulation.DescribeSky(Tile, Tilt, Azimuth);
            Draw();
        }

        float DomeRadiusLocal() =>
            siteView != null && siteView.Site != null ? (float)siteView.Site.Spacing * planet.Radius * domeRadiusSlots : 0.1f;

        float DomeRadiusWorld() => DomeRadiusLocal() * transform.lossyScale.x;

        // ---- Drawing ----

        Vector3 centre, east, north, up;
        float radius;

        void Draw()
        {
            foreach (var list in lines.Values)
                foreach (LineRenderer l in list)
                    l.enabled = false;
            Labels.Clear();
            if (!Report.Valid || siteView.Site == null)
                return;

            System.Numerics.Vector3 d = siteView.Site.SlotDirections[Slot];
            var dir = new Vector3(d.X, d.Y, d.Z);
            double lat = PlanetFrame.Latitude(d), lon = PlanetFrame.Longitude(d);
            up = ToUnity(PlanetFrame.FromLocal(new Double3(0, 0, 1), lat, lon));
            north = ToUnity(PlanetFrame.FromLocal(new Double3(0, 1, 0), lat, lon));
            east = ToUnity(PlanetFrame.FromLocal(new Double3(1, 0, 0), lat, lon));
            radius = DomeRadiusLocal();
            centre = dir.normalized * siteView.GroundRadius(siteView.Site, dir) + up * radius * 0.02f;
            float width = radius * 0.012f;

            // Always visible: the horizon, compass points, the meridian and the zenith.
            var horizon = new List<Vector3>();
            for (int i = 0; i <= 72; i++)
            {
                float a = i * 5f * Mathf.Deg2Rad;
                horizon.Add(P(new Double3(Mathf.Sin(a), Mathf.Cos(a), 0)));
            }
            Polyline("horizon", 0, horizon, Horizon, width);
            Arc("meridian", Dir(0, 0), Dir(0, 90), Faint, width * 0.7f, 0);
            Arc("meridian", Dir(0, 90), Dir(180, 0), Faint, width * 0.7f, 1);
            Arc("primeVertical", Dir(90, 0), Dir(0, 90), Faint, width * 0.5f, 0);
            Arc("primeVertical", Dir(0, 90), Dir(270, 0), Faint, width * 0.5f, 1);
            Label("N", Scale(Dir(0, 0), 1.08), Horizon);
            Label("E", Scale(Dir(90, 0), 1.08), Horizon);
            Label("S", Scale(Dir(180, 0), 1.08), Horizon);
            Label("W", Scale(Dir(270, 0), 1.08), Horizon);
            Label("Z", Scale(Dir(0, 90), 1.06), Faint);

            // The panel and its normal: the player sets these.
            Double3 normal = PanelModel.Normal(Tilt, Azimuth);
            Segment("normal", 0, Double3Zero, Scale(normal, 0.85), NormalColor, width * 1.6f);
            Label(PanelBuilt ? "panel normal" : "planned normal", Scale(normal, 0.92), NormalColor);

            // The sun: you can always see it; its numbers need a measurement.
            Double3 sun = Dir(Report.SunAzimuthDeg, Report.SunAltitudeDeg);
            bool sunUp = Report.SunUp;
            Color sunColor = sunUp ? SunColor : new Color(SunColor.r, SunColor.g, SunColor.b, 0.25f);
            Segment("sunRay", 0, Double3Zero, sun, sunColor, width);
            Ring("sunDisc", sun, 0.06f, sunColor, width * 1.2f);
            Label(sunUp ? "sun" : "sun (below horizon)", Scale(sun, 1.1), sunColor);

            SkyNotebook book = Notebook;
            if (book.TracksSun && sunUp)
            {
                Arc("incidence", Scale(normal, 0.45), Scale(sun, 0.45), Incidence, width * 1.3f, 0);
                double theta = Math.Acos(Math.Max(-1.0, Math.Min(1.0, Report.CosIncidence))) * 180.0 / Math.PI;
                Label($"θ {theta:0}°", Scale(Normalize(Add(normal, sun)), 0.52), Incidence);
            }

            if (book.KnowsLatitude(Tile))
            {
                double phi = book.LatitudeDeg(Tile, Report.LatitudeDeg) * Math.PI / 180.0;
                var pole = new Double3(0, Math.Cos(phi), Math.Sin(phi));   // the north celestial pole
                Segment("poleAxis", 0, Scale(pole, -1.05), Scale(pole, 1.05), PoleColor, width);
                Label("NCP", Scale(pole, 1.12), PoleColor);
                Label("SCP", Scale(pole, -1.12), PoleColor);
                DeclinationCircle("equator", phi, 0.0, EquatorColor, width * 0.8f, aboveOnly: false);
                Label("celestial equator", DeclinationPoint(phi, 0.0, 0.0), EquatorColor);

                if (book.KnowsDeclination)
                {
                    double dec = Report.DeclinationDeg * Math.PI / 180.0;
                    DeclinationCircle("sunPath", phi, dec, SunPath, width, aboveOnly: true);
                    double eps = Report.ObliquityDeg * Math.PI / 180.0;
                    if (eps > 0.5 * Math.PI / 180.0)
                    {
                        DeclinationCircle("summer", phi, eps, Solstice, width * 0.6f, aboveOnly: true);
                        DeclinationCircle("winter", phi, -eps, Solstice, width * 0.6f, aboveOnly: true);
                        Label("+ε", DeclinationPoint(phi, eps, 0.0), Solstice);
                        Label("−ε", DeclinationPoint(phi, -eps, 0.0), Solstice);
                    }
                }
            }
        }

        static readonly Double3 Double3Zero = new Double3(0, 0, 0);

        /// <summary>Direction on the dome from azimuth (clockwise from north) and altitude, degrees.</summary>
        static Double3 Dir(double azimuthDeg, double altitudeDeg)
        {
            double az = azimuthDeg * Math.PI / 180.0, alt = altitudeDeg * Math.PI / 180.0;
            return new Double3(Math.Cos(alt) * Math.Sin(az), Math.Cos(alt) * Math.Cos(az), Math.Sin(alt));
        }

        static Double3 Scale(Double3 v, double k) => new Double3(v.East * k, v.North * k, v.Up * k);
        static Double3 Add(Double3 a, Double3 b) => new Double3(a.East + b.East, a.North + b.North, a.Up + b.Up);

        static Double3 Normalize(Double3 v)
        {
            double n = Math.Sqrt(v.East * v.East + v.North * v.North + v.Up * v.Up);
            return n > 1e-9 ? Scale(v, 1.0 / n) : new Double3(0, 0, 1);
        }

        static Double3 DeclinationPoint(double phi, double dec, double hourAngle) =>
            SunPosition.Direction(phi, dec, hourAngle);

        /// <summary>Planet-local position of a dome direction (unit vector times the dome radius).</summary>
        Vector3 P(Double3 v) => centre + (east * (float)v.East + north * (float)v.North + up * (float)v.Up) * radius;

        void DeclinationCircle(string key, double phi, double dec, Color color, float width, bool aboveOnly)
        {
            var run = new List<Vector3>();
            int index = 0;
            for (int i = 0; i <= 144; i++)
            {
                double h = -Math.PI + i * (2.0 * Math.PI / 144.0);
                Double3 v = SunPosition.Direction(phi, dec, h);
                if (aboveOnly && v.Up < 0.0)
                {
                    if (run.Count > 1) Polyline(key, index++, run, color, width);
                    run = new List<Vector3>();
                    continue;
                }
                run.Add(P(v));
            }
            if (run.Count > 1) Polyline(key, index, run, color, width);
        }

        void Arc(string key, Double3 from, Double3 to, Color color, float width, int index)
        {
            var points = new List<Vector3>();
            for (int i = 0; i <= 24; i++)
            {
                float t = i / 24f;
                Double3 v = Normalize(Add(Scale(from, 1 - t), Scale(to, t)));
                double k = Math.Sqrt(from.East * from.East + from.North * from.North + from.Up * from.Up);
                points.Add(P(Scale(v, k)));
            }
            Polyline(key, index, points, color, width);
        }

        void Segment(string key, int index, Double3 a, Double3 b, Color color, float width) =>
            Polyline(key, index, new List<Vector3> { P(a), P(b) }, color, width);

        void Ring(string key, Double3 at, float size, Color color, float width)
        {
            Double3 n = Normalize(at);
            Double3 u = Math.Abs(n.Up) < 0.9 ? new Double3(0, 0, 1) : new Double3(1, 0, 0);
            Double3 a = Normalize(Cross(n, u)), b = Cross(n, a);
            var points = new List<Vector3>();
            for (int i = 0; i <= 24; i++)
            {
                double t = i * 2.0 * Math.PI / 24.0;
                points.Add(P(Add(at, Add(Scale(a, size * Math.Cos(t)), Scale(b, size * Math.Sin(t))))));
            }
            Polyline(key, 0, points, color, width);
        }

        static Double3 Cross(Double3 a, Double3 b) =>
            new Double3(a.North * b.Up - a.Up * b.North, a.Up * b.East - a.East * b.Up, a.East * b.North - a.North * b.East);

        void Label(string text, Double3 at, Color color) => Labels.Add((text, P(at), color));

        void Polyline(string key, int index, List<Vector3> points, Color color, float width)
        {
            if (!lines.TryGetValue(key, out var list))
                lines[key] = list = new List<LineRenderer>();
            while (list.Count <= index)
            {
                var go = new GameObject($"{key} {list.Count}") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(root.transform, false);
                var l = go.AddComponent<LineRenderer>();
                l.useWorldSpace = false;
                l.sharedMaterial = lineMaterial;
                l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                l.receiveShadows = false;
                l.numCapVertices = 2;
                l.alignment = LineAlignment.View;
                list.Add(l);
            }
            LineRenderer line = list[index];
            line.enabled = true;
            line.positionCount = points.Count;
            line.SetPositions(points.ToArray());
            line.startColor = line.endColor = color;
            line.widthMultiplier = width * transform.lossyScale.x;
        }

        static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
