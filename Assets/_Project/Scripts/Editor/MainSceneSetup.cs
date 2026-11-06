using KardashevAscent.Presentation.Game;
using KardashevAscent.Presentation.Planet;
using KardashevAscent.Presentation.Star;
using UnityEditor;
using UnityEditor.SceneManagement;
using KardashevAscent.Presentation.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace KardashevAscent.EditorTools
{
    /// <summary>
    /// Wires the open scene for play: a Simulation object running the planet, the directional light
    /// following the star, the light-potential map, building sites and panel placement on the planet. Safe to run again; it only adds what is missing.
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
            SetUpPlanets(runner);

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

            var picker = planet.GetComponent<TilePicker>();
            var site = planet.GetComponent<SiteView>();
            if (site == null)
                site = Undo.AddComponent<SiteView>(planet.gameObject);
            SetReference(site, "runner", runner);
            SetReference(site, "picker", picker);
            var mainCamera = Camera.main;
            SetReference(site, "orbitCamera", mainCamera != null ? mainCamera.GetComponent<KardashevAscent.Presentation.CameraControl.OrbitCamera>() : null);
            SetReference(site, "padMaterial", AssetDatabase.LoadAssetAtPath<Material>(PlanetMaterial));
            if (picker != null)
            {
                var pickerObject = new SerializedObject(picker);
                SetReference(site, "hoverMaterial", pickerObject.FindProperty("hoverMaterial").objectReferenceValue);
                SetReference(site, "selectedMaterial", pickerObject.FindProperty("selectedMaterial").objectReferenceValue);
            }

            var placer = planet.GetComponent<PanelPlacer>();
            if (placer == null)
                placer = Undo.AddComponent<PanelPlacer>(planet.gameObject);
            SetReference(placer, "runner", runner);
            SetReference(placer, "siteView", site);
            SetReference(placer, "frameMaterial", LoadOrCreateMaterial(PanelFrameMaterial, "Standard", m =>
            {
                m.color = new Color(0.55f, 0.59f, 0.64f);
                m.SetFloat("_Glossiness", 0.4f);
            }));
            SetReference(placer, "cellMaterial", LoadOrCreateMaterial(PanelCellMaterial, "Standard", m =>
            {
                m.color = new Color(0.10f, 0.16f, 0.30f);
                m.SetFloat("_Metallic", 0.3f);
                m.SetFloat("_Glossiness", 0.85f);
            }));
            SetReference(placer, "ghostMaterial", LoadOrCreateMaterial(PanelGhostMaterial, "KardashevAscent/GhostTint"));

            SetUpHud(runner);
            var saveController = runner.GetComponent<SaveGameController>();
            if (saveController == null)
                saveController = Undo.AddComponent<SaveGameController>(runner.gameObject);
            SetReference(saveController, "runner", runner);
            SetReference(placer, "saveGame", saveController);
            var hudView = Object.FindAnyObjectByType<HudView>();
            if (hudView != null)
                SetReference(hudView, "saveGame", saveController);
            SetUpTileInfo(runner, planet, placer);
            SetUpPanels(planet, placer, site);
            SetUpColony(runner);
            SetUpPlanetChooser(runner);
            SetUpCursorLight(planet, picker);

            EditorSceneManager.MarkSceneDirty(planet.gameObject.scene);
            Debug.Log("Scene set up: Simulation runner, planets, star light, light-potential map, building sites, panel placement, HUD, tile info, side panels, colony overview, planet chooser, cursor flashlight. Save the scene to keep it.");
        }

        /// <summary>Import planets.json and give the runner every planet in file order, starting on the first playable one.</summary>
        static void SetUpPlanets(SimulationRunner runner)
        {
            var planets = PlanetImporter.Import();
            var so = new SerializedObject(runner);
            SerializedProperty list = so.FindProperty("planets");
            list.arraySize = planets.Count;
            int start = -1;
            for (int i = 0; i < planets.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = planets[i];
                if (start < 0 && planets[i].Record.IsPlayable)
                    start = i;
            }
            so.FindProperty("startPlanet").intValue = Mathf.Max(0, start);
            so.ApplyModifiedProperties();
        }

        /// <summary>The flashlight that follows the mouse over the planet (F toggles it).</summary>
        static void SetUpCursorLight(PlanetView planet, TilePicker picker)
        {
            var cursorLight = Object.FindAnyObjectByType<KardashevAscent.Presentation.CameraControl.CursorLight>(FindObjectsInactive.Include);
            if (cursorLight == null)
            {
                var go = new GameObject("Cursor Light");
                Undo.RegisterCreatedObjectUndo(go, "Add Cursor Light");
                cursorLight = Undo.AddComponent<KardashevAscent.Presentation.CameraControl.CursorLight>(go);
            }
            // CursorLight makes its own hidden light at runtime; a Light here would only add an editor icon.
            var oldLight = cursorLight.GetComponent<Light>();
            if (oldLight != null)
                Undo.DestroyObjectImmediate(oldLight);
            SetReference(cursorLight, "picker", picker);
        }

        const string MapMaterial = "Assets/_Project/Materials/PlanetTilesUnlit.mat";
        const string PlanetMaterial = "Assets/_Project/Materials/PlanetTiles.mat";
        const string PanelFrameMaterial = "Assets/_Project/Materials/PanelFrame.mat";
        const string PanelCellMaterial = "Assets/_Project/Materials/PanelCells.mat";
        const string PanelGhostMaterial = "Assets/_Project/Materials/PanelGhost.mat";

        static Material LoadOrCreateMaterial(string path, string shaderName, System.Action<Material> configure = null)
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
            configure?.Invoke(material);
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            return material;
        }

        const string ThemePath = "Assets/_Project/UI/KardashevTheme.tss";
        const string PanelSettingsPath = "Assets/_Project/UI/GamePanelSettings.asset";
        const string HudLayoutPath = "Assets/_Project/UI/Hud/Hud.uxml";

        /// <summary>The UI Toolkit HUD: one PanelSettings (shared theme) for the whole game, and a HUD document.</summary>
        static void SetUpHud(SimulationRunner runner)
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
                panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
                panelSettings.scale = 1f;
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
                AssetDatabase.SaveAssets();
            }

            var hud = Object.FindAnyObjectByType<HudView>();
            GameObject go;
            if (hud == null)
            {
                go = new GameObject("HUD");
                Undo.RegisterCreatedObjectUndo(go, "Add HUD");
                var document = Undo.AddComponent<UIDocument>(go);
                document.panelSettings = panelSettings;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudLayoutPath);
                hud = Undo.AddComponent<HudView>(go);
            }
            SetReference(hud, "runner", runner);
        }

        const string TileInfoLayoutPath = "Assets/_Project/UI/TileInfo/TileInfo.uxml";

        /// <summary>The tile information panel (#13), on the same PanelSettings as the HUD.</summary>
        static void SetUpTileInfo(SimulationRunner runner, PlanetView planet, PanelPlacer placer)
        {
            var view = Object.FindAnyObjectByType<TileInfoView>();
            if (view == null)
            {
                var go = new GameObject("Tile Info");
                Undo.RegisterCreatedObjectUndo(go, "Add Tile Info");
                var document = Undo.AddComponent<UIDocument>(go);
                document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TileInfoLayoutPath);
                view = Undo.AddComponent<TileInfoView>(go);
            }
            SetReference(view, "runner", runner);
            SetReference(view, "picker", planet.GetComponent<TilePicker>());
            SetReference(view, "placer", placer);
            SetReference(view, "lightMap", planet.GetComponent<LightPotentialOverlay>());
        }

        const string PanelsLayoutPath = "Assets/_Project/UI/Panels/Panels.uxml";

        /// <summary>The side panels (tile under the mouse, site and panel planner, light-map legend, help).</summary>
        static void SetUpPanels(PlanetView planet, PanelPlacer placer, SiteView site)
        {
            var view = Object.FindAnyObjectByType<PanelsView>();
            if (view == null)
            {
                var go = new GameObject("Panels");
                Undo.RegisterCreatedObjectUndo(go, "Add Panels");
                var document = Undo.AddComponent<UIDocument>(go);
                document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PanelsLayoutPath);
                view = Undo.AddComponent<PanelsView>(go);
            }
            SetReference(view, "picker", planet.GetComponent<TilePicker>());
            SetReference(view, "siteView", site);
            SetReference(view, "placer", placer);
            SetReference(view, "lightMap", planet.GetComponent<LightPotentialOverlay>());
            var mainCamera = Camera.main;
            SetReference(view, "orbitCamera", mainCamera != null ? mainCamera.GetComponent<KardashevAscent.Presentation.CameraControl.OrbitCamera>() : null);
        }

        const string ColonyLayoutPath = "Assets/_Project/UI/Colony/Colony.uxml";

        /// <summary>Draw order of the colony overview: above the HUD, tile info and side panels (all 0).</summary>
        const float ColonySortingOrder = 10f;

        /// <summary>The colony overview (C), on the same PanelSettings as the HUD.</summary>
        static void SetUpColony(SimulationRunner runner)
        {
            var view = Object.FindAnyObjectByType<ColonyView>();
            if (view == null)
            {
                var go = new GameObject("Colony");
                Undo.RegisterCreatedObjectUndo(go, "Add Colony");
                var document = Undo.AddComponent<UIDocument>(go);
                document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ColonyLayoutPath);
                view = Undo.AddComponent<ColonyView>(go);
            }
            SetReference(view, "runner", runner);

            // The overview opens over the other panels; with equal orders their stacking is arbitrary.
            var colonyDocument = view.GetComponent<UIDocument>();
            if (colonyDocument.sortingOrder != ColonySortingOrder)
            {
                Undo.RecordObject(colonyDocument, "Colony draw order");
                colonyDocument.sortingOrder = ColonySortingOrder;
            }
        }

        const string PlanetsLayoutPath = "Assets/_Project/UI/Planets/Planets.uxml";

        /// <summary>Draw order of the planet chooser: above everything, the colony overview included.</summary>
        const float PlanetsSortingOrder = 20f;

        /// <summary>The planet chooser (L, and at start), on the same PanelSettings as the HUD.</summary>
        static void SetUpPlanetChooser(SimulationRunner runner)
        {
            var view = Object.FindAnyObjectByType<PlanetChooserView>();
            if (view == null)
            {
                var go = new GameObject("Planets");
                Undo.RegisterCreatedObjectUndo(go, "Add Planet Chooser");
                var document = Undo.AddComponent<UIDocument>(go);
                document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PlanetsLayoutPath);
                view = Undo.AddComponent<PlanetChooserView>(go);
            }
            SetReference(view, "runner", runner);
            var planetsDocument = view.GetComponent<UIDocument>();
            if (planetsDocument.sortingOrder != PlanetsSortingOrder)
            {
                Undo.RecordObject(planetsDocument, "Planet chooser draw order");
                planetsDocument.sortingOrder = PlanetsSortingOrder;
            }
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
