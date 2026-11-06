using System.Collections.Generic;
using System.IO;
using KardashevAscent.Presentation.Game;
using KardashevAscent.Simulation.Planet;
using UnityEditor;
using UnityEngine;

namespace KardashevAscent.EditorTools
{
    /// <summary>
    /// Kardashev Ascent ▸ Import Planets: Assets/_Project/Data/Planets/planets.json (written by
    /// tools/exoplanets/fetch_exoplanets.py) → one <see cref="PlanetDNA"/> asset per planet next to it.
    /// Existing assets are updated in place, so scene references survive a re-import.
    /// </summary>
    public static class PlanetImporter
    {
        public const string JsonPath = "Assets/_Project/Data/Planets/planets.json";
        public const string Folder = "Assets/_Project/Data/Planets";

        [MenuItem("Kardashev Ascent/Import Planets")]
        public static void ImportMenu()
        {
            List<PlanetDNA> planets = Import();
            Debug.Log($"Imported {planets.Count} planets from {JsonPath}.");
        }

        /// <summary>Import every planet in planets.json, in file order. Returns the assets.</summary>
        public static List<PlanetDNA> Import()
        {
            var result = new List<PlanetDNA>();
            if (!File.Exists(JsonPath))
            {
                Debug.LogError($"{JsonPath} not found. Run: python tools/exoplanets/fetch_exoplanets.py");
                return result;
            }
            PlanetCatalogRecord catalog = JsonUtility.FromJson<PlanetCatalogRecord>(File.ReadAllText(JsonPath));
            if (catalog?.planets == null)
            {
                Debug.LogError($"{JsonPath} has no planets.");
                return result;
            }
            foreach (PlanetRecord record in catalog.planets)
            {
                if (record == null || string.IsNullOrEmpty(record.id))
                    continue;
                string path = $"{Folder}/{record.id}.asset";
                var dna = AssetDatabase.LoadAssetAtPath<PlanetDNA>(path);
                if (dna == null)
                {
                    dna = ScriptableObject.CreateInstance<PlanetDNA>();
                    dna.SetRecord(record);
                    AssetDatabase.CreateAsset(dna, path);
                }
                else
                {
                    dna.SetRecord(record);
                    EditorUtility.SetDirty(dna);
                }
                result.Add(dna);
            }
            AssetDatabase.SaveAssets();
            return result;
        }
    }
}
