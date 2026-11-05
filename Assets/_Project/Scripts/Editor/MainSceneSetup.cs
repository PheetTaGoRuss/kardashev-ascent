using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.Planet;
using KardashevAscent.Presentation.Star;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KardashevAscent.EditorTools
{
    /// <summary>
    /// Wires the open scene for play: a Simulation object running the planet, the directional light
    /// following the star, and the light-potential map on the planet. Safe to run again; it only adds what is missing.
    /// Menu: Kardashev Ascent ▸ Set Up Scene.
    /// </summary>
    public static class MainSceneSetup
    {
        const string StarGlowMaterial = "Assets/_Project/Materials/StarGlow.mat";

        [MenuItem("Kardashev Ascent/Set Up Scene")]
        public static void SetUp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Leave Play mode first: scene changes made while playing are thrown away.");
                return;
            }

            var planet = Object.FindAnyObjectByType<PlanetView>();
            if (planet == null)
            {
                Debug.LogError("No PlanetView in the scene.");
                return;
            }

            var runner = Object.FindAnyObjectByType<SimulationRunner>();
            if (runner == null)
            {
                var go = new GameObject("Simulation");
                Undo.RegisterCreatedObjectUndo(go, "Add Simulation");
                runner = Undo.AddComponent<SimulationRunner>(go);
            }
            SetReference(runner, "planet", planet);

            Light sun = FindDirectionalLight();
            if (sun == null)
            {
                var go = new GameObject("Star Light");
                Undo.RegisterCreatedObjectUndo(go, "Add Star Light");
                sun = Undo.AddComponent<Light>(go);
                sun.type = LightType.Directional;
            }
            sun.shadows = LightShadows.Soft;
            var star = sun.GetComponent<StarView>();
            if (star == null)
                star = Undo.AddComponent<StarView>(sun.gameObject);
            SetReference(star, "runner", runner);
            SetReference(star, "glowMaterial", AssetDatabase.LoadAssetAtPath<Material>(StarGlowMaterial));

            var map = planet.GetComponent<LightPotentialOverlay>();
            if (map == null)
                map = Undo.AddComponent<LightPotentialOverlay>(planet.gameObject);
            SetReference(map, "runner", runner);
            SetReference(map, "picker", planet.GetComponent<TilePicker>());
            SetReference(map, "mapMaterial", LoadOrCreateMaterial(MapMaterial, "KardashevAscent/PlanetTilesUnlit"));

            EditorSceneManager.MarkSceneDirty(planet.gameObject.scene);
            Debug.Log("Scene set up: Simulation runner, star light, light-potential map. Save the scene to keep it.");
        }

        const string MapMaterial = "Assets/_Project/Materials/PlanetTilesUnlit.mat";

        static Material LoadOrCreateMaterial(string path, string shaderName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"Shader {shaderName} not found.");
                return null;
            }
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            return material;
        }

        static Light FindDirectionalLight()
        {
            foreach (var light in Object.FindObjectsByType<Light>())
                if (light.type == LightType.Directional)
                    return light;
            return null;
        }

        static void SetReference(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
