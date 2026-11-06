using System;
using System.Collections.Generic;
using KardashevAscent.Presentation.Planet;
using KardashevAscent.Simulation.Game;
using KardashevAscent.Simulation.Planet;
using KardashevAscent.Simulation.Solar;
using UnityEngine;

namespace KardashevAscent.Presentation.Game
{
    /// <summary>
    /// Owns the planet's simulation and its clock: builds a <see cref="StubPlanetSimulation"/> for the chosen
    /// <see cref="PlanetDNA"/> (real NASA planets or hand-made ones, #11), then ticks it with game time every frame.
    /// Keys: Space pause/resume, 1–5 speed (pause, real time, 1 hour/s, 1 day/s, 1 year/min).
    /// </summary>
    public sealed class SimulationRunner : MonoBehaviour
    {
        [SerializeField] PlanetView planet;

        [Header("Planets")]
        [Tooltip("Planets the colony can land on, in the order the chooser lists them (Set Up Scene fills this).")]
        [SerializeField] PlanetDNA[] planets = Array.Empty<PlanetDNA>();
        [Tooltip("Index into Planets of the planet loaded at start.")]
        [Min(0)] [SerializeField] int startPlanet;

        [Header("Time")]
        [SerializeField] ClockSpeed startSpeed = ClockSpeed.HourPerSecond;

        ClockSpeed speedBeforePause = ClockSpeed.HourPerSecond;
        PlanetRecord record;

        public IPlanetSimulation Simulation { get; private set; }
        public SimClock Clock { get; private set; }
        public PlanetSolarParams SolarParams { get; private set; }
        public PlanetView Planet => planet;

        /// <summary>The planet being played (its NASA, derived and assumed values).</summary>
        public PlanetRecord Record => record;
        public double StarTemperatureK => record != null ? record.star.teffK : 5772.0;
        public IReadOnlyList<PlanetDNA> Planets => planets;
        /// <summary>Index of the current planet in <see cref="Planets"/>, or −1 for the built-in fallback.</summary>
        public int CurrentIndex { get; private set; } = -1;

        /// <summary>Raised when a new simulation is built (at start, on a new planet, and whenever the tiles are rebuilt).</summary>
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
            // Pick the planet before building the tiles: EnsureBuilt raises Rebuilt, which creates the simulation.
            if (record == null)
                PickRecord(startPlanet);
            planet.SetSurface(record.ToSurfaceSettings());
            planet.EnsureBuilt();
            if (Simulation == null)
                CreateSimulation();
        }

        /// <summary>Start a new colony on planet <paramref name="index"/> of <see cref="Planets"/>. False if it cannot be landed on.</summary>
        public bool ChoosePlanet(int index)
        {
            if (index < 0 || index >= planets.Length || planets[index] == null || !planets[index].Record.IsPlayable)
                return false;
            SelectPlanet(index);
            return true;
        }

        /// <summary>Index of the planet with this id, or −1.</summary>
        public int IndexOf(string planetId)
        {
            for (int i = 0; i < planets.Length; i++)
                if (planets[i] != null && planets[i].Id == planetId)
                    return i;
            return -1;
        }

        void SelectPlanet(int index)
        {
            PickRecord(index);
            if (planet == null)
                return;
            // A new surface rebuilds the tiles, and Rebuilt creates the simulation; otherwise create it here.
            if (planet.SetSurface(record.ToSurfaceSettings()))
                planet.EnsureBuilt();
            else if (planet.Sphere != null)
                CreateSimulation();
        }

        void PickRecord(int index)
        {
            if (index >= 0 && index < planets.Length && planets[index] != null && planets[index].Record.IsPlayable)
            {
                CurrentIndex = index;
                record = planets[index].Record;
            }
            else
            {
                // No planet assets yet (run Set Up Scene): an Earth twin so the scene still plays.
                CurrentIndex = -1;
                record = Fallback();
            }
        }

        static PlanetRecord Fallback()
        {
            var r = new PlanetRecord { id = "viridia", displayName = "Viridia", source = "built-in fallback" };
            r.solar.eccentricity = 0.0167;
            r.solar.obliquityDeg = 23.44;
            r.solar.perihelionLongitudeDeg = 282.94;
            r.solar.airTemperatureC = 25.0;
            r.flags.clouds = "earth";
            return r;
        }

        void CreateSimulation()
        {
            if (record == null)
                PickRecord(startPlanet);
            Simulation = new StubPlanetSimulation(planet.Sphere, planet.Surface, record);
            SolarParams = record.ToSolarParams();
            ClockSpeed speed = Clock?.Speed ?? startSpeed;
            double yearHours = SolarParams.YearLengthHours;
            Clock = new SimClock(SolarParams.DayLengthHours, yearHours) { Speed = speed };
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
