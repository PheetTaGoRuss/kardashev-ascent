using System;
using KardashevAscent.Presentation.Planet;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using UnityEngine;

namespace KardashevAscent.Presentation.Game
{
    /// <summary>
    /// Owns the planet's simulation and its clock: builds a <see cref="StubPlanetSimulation"/> from the
    /// planet and star settings below, then ticks it with game time every frame.
    /// Keys: Space pause/resume, 1–5 speed (pause, real time, 1 hour/s, 1 day/s, 1 year/min).
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [SerializeField] PlanetView planet;

        [Header("Star")]
        [Tooltip("Luminosity in Suns.")]
        [Min(1e-6f)] [SerializeField] float luminositySun = 1f;
        [Tooltip("Effective temperature, K (sets the light colour and the star's size).")]
        [Range(2000f, 12000f)] [SerializeField] float starTemperatureK = 5772f;

        [Header("Orbit and spin")]
        [Min(0.001f)] [SerializeField] float semiMajorAxisAu = 1f;
        [Range(0f, 0.95f)] [SerializeField] float eccentricity = 0.0167f;
        [Range(0f, 90f)] [SerializeField] float obliquityDeg = 23.44f;
        [Tooltip("Star's longitude at periapsis seen from the planet (Earth 282.94°).")]
        [SerializeField] float perihelionLongitudeDeg = 282.94f;
        [Min(0.1f)] [SerializeField] float dayLengthHours = 24f;
        [Min(0.1f)] [SerializeField] float yearLengthDays = 365.256f;
        [SerializeField] bool tidallyLocked;

        [Header("Planet and atmosphere")]
        [Min(0.01f)] [SerializeField] float radiusEarth = 1f;
        [Range(0f, 6f)] [SerializeField] float opticalDepth = 0.32f;
        [Range(0f, 1f)] [SerializeField] float diffuseK = 0.3f;
        [SerializeField] float airTemperatureC = 15f;

        [Header("Time")]
        [SerializeField] ClockSpeed startSpeed = ClockSpeed.HourPerSecond;

        ClockSpeed speedBeforePause = ClockSpeed.HourPerSecond;

        public IPlanetSimulation Simulation { get; private set; }
        public SimClock Clock { get; private set; }
        public PlanetSolarParams SolarParams { get; private set; }
        public PlanetView Planet => planet;
        public double StarTemperatureK => starTemperatureK;

        /// <summary>Raised when a new simulation is built (at start and whenever the planet's tiles are rebuilt).</summary>
        public event Action SimulationCreated;

        void OnEnable()
        {
            if (planet != null)
                planet.Rebuilt += CreateSimulation;
        }

        void OnDisable()
        {
            if (planet != null)
                planet.Rebuilt -= CreateSimulation;
        }

        void Start()
        {
            if (planet == null)
            {
                Debug.LogError("SimulationRunner needs a PlanetView.", this);
                enabled = false;
                return;
            }
            planet.EnsureBuilt();
            if (Simulation == null)
                CreateSimulation();
        }

        void CreateSimulation()
        {
            const double deg = Math.PI / 180.0;
            double yearHours = yearLengthDays * (tidallyLocked ? 24.0 : dayLengthHours);
            SolarParams = new PlanetSolarParams(
                luminositySun * SolarConstants.SolarLuminosity,
                semiMajorAxisAu * SolarConstants.AstronomicalUnit,
                eccentricity, obliquityDeg * deg, perihelionLongitudeDeg * deg,
                tidallyLocked ? 0.0 : dayLengthHours, yearHours,
                opticalDepth, diffuseK, airTemperatureC, tidallyLocked);

            Simulation = new StubPlanetSimulation(planet.Sphere, planet.Surface, SolarParams, radiusEarth * 6.371e6);
            ClockSpeed speed = Clock?.Speed ?? startSpeed;
            Clock = new SimClock(SolarParams.DayLengthHours, SolarParams.YearLengthHours) { Speed = speed };
            SimulationCreated?.Invoke();
        }

        void Update()
        {
            if (Simulation == null)
                return;
            ReadKeys();
            double hours = Clock.Advance(Time.unscaledDeltaTime);
            if (hours > 0.0)
                Simulation.Tick(hours);
        }

        void ReadKeys()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                SetSpeed(Clock.Speed == ClockSpeed.Paused ? speedBeforePause : ClockSpeed.Paused);
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetSpeed(ClockSpeed.Paused);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetSpeed(ClockSpeed.RealTime);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetSpeed(ClockSpeed.HourPerSecond);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetSpeed(ClockSpeed.DayPerSecond);
            if (Input.GetKeyDown(KeyCode.Alpha5)) SetSpeed(ClockSpeed.YearPerMinute);
        }

        public void SetSpeed(ClockSpeed speed)
        {
            if (Clock == null)
                return;
            if (Clock.Speed != ClockSpeed.Paused)
                speedBeforePause = Clock.Speed;
            Clock.Speed = speed;
        }

        /// <summary>Local solar time (0–24 h) at a tile; NaN on a tidally locked planet.</summary>
        public double LocalTimeAt(int tileId)
        {
            if (Simulation == null || SolarParams.TidallyLocked)
                return double.NaN;
            double subStellar = PlanetFrame.Longitude(Simulation.StarDirection);
            return SimClock.LocalTime24(Simulation.Sphere.Longitude(tileId) - subStellar);
        }

        }
    }
}
