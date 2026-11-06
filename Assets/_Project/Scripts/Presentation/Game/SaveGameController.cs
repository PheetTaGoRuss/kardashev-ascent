using System;
using System.IO;
using KardashevAscent.Simulation.Game;
using UnityEngine;

namespace KardashevAscent.Presentation.Game
{
    /// <summary>F5 saves the game to a JSON file in the persistent data folder, F9 loads it.</summary>
    public sealed class SaveGameController : MonoBehaviour
    {
        const string FileName = "kardashev-save.json";

        [SerializeField] SimulationRunner runner;
        [SerializeField] KeyCode saveKey = KeyCode.F5;
        [SerializeField] KeyCode loadKey = KeyCode.F9;

        /// <summary>Raised after a save was loaded into the simulation.</summary>
        public event Action Loaded;

        public string Message { get; private set; }
        public float MessageTime { get; private set; }

        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        void Update()
        {
            if (Input.GetKeyDown(saveKey))
                Save();
            else if (Input.GetKeyDown(loadKey))
                Load();
        }

        void Show(string message)
        {
            Message = message;
            MessageTime = Time.unscaledTime;
        }

        public void Save()
        {
            if (runner == null || runner.Simulation == null)
                return;
            try
            {
                SimulationState state = runner.Simulation.CaptureState();
                File.WriteAllText(FilePath, JsonUtility.ToJson(state, true));
                Show($"Saved (day {(int)(state.TimeHours / 24) + 1})");
            }
            catch (Exception e)
            {
                Show("Could not save: " + e.Message);
            }
        }

        public void Load()
        {
            if (runner == null || runner.Simulation == null)
                return;
            try
            {
                if (!File.Exists(FilePath))
                {
                    Show("No saved game yet (F5 saves)");
                    return;
                }
                SimulationState state = JsonUtility.FromJson<SimulationState>(File.ReadAllText(FilePath));
                // A save from another planet: land there first (version 3 saves name their planet).
                if (state != null && !string.IsNullOrEmpty(state.PlanetId) && runner.Record != null && state.PlanetId != runner.Record.id)
                {
                    int index = runner.IndexOf(state.PlanetId);
                    if (index < 0 || !runner.ChoosePlanet(index))
                    {
                        Show($"That save is from {state.PlanetId}, which this game doesn't have");
                        return;
                    }
                }
                if (state == null || !runner.Simulation.RestoreState(state))
                {
                    Show("That save is from a different planet");
                    return;
                }
                Loaded?.Invoke();
                Show($"Loaded (day {(int)(state.TimeHours / 24) + 1})");
            }
            catch (Exception e)
            {
                Show("Could not load: " + e.Message);
            }
        }
    }
}
