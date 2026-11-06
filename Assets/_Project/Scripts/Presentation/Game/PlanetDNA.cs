using KardashevAscent.Simulation.Planet;
using UnityEngine;

namespace KardashevAscent.Presentation.Game
{
    /// <summary>
    /// One planet the colony can land on (#11): a planets.json record (NASA Exoplanet Archive values, derived values
    /// and game assumptions, each labelled with where it came from) as an asset.
    /// Made by Kardashev Ascent ▸ Import Planets from Assets/_Project/Data/Planets/planets.json; to change a planet,
    /// edit tools/exoplanets/planets.json, run the fetcher and import again (edits here are overwritten).
    /// </summary>
    [CreateAssetMenu(menuName = "Kardashev Ascent/Planet DNA", fileName = "Planet")]
    public sealed class PlanetDNA : ScriptableObject
    {
        [SerializeField] PlanetRecord record = new PlanetRecord();

        public PlanetRecord Record => record;
        public string Id => record.id;
        public string DisplayName => string.IsNullOrEmpty(record.displayName) ? name : record.displayName;

        /// <summary>Replace the record (used by the importer).</summary>
        public void SetRecord(PlanetRecord value) => record = value ?? new PlanetRecord();
    }
}
