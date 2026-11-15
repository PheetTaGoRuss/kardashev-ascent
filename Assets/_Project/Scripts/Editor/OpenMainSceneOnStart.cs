using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace KardashevAscent.EditorTools
{
    /// <summary>
    /// When the project opens on an empty untitled scene (a fresh checkout or worktree), open the game's scene,
    /// Assets/Scenes/Main.unity, so Play shows the planet. Once per editor session; a scene you chose is left alone.
    /// It also turns off the Game view's Gizmos toggle once per session (director 2026-11-15: the light's sun icon and
    /// the sound's speaker icon were drawn over the planet). Built games never show gizmos; turn it back on from the
    /// Game view toolbar when you need it.
    /// </summary>
    [InitializeOnLoad]
    static class OpenMainSceneOnStart
    {
        const string DoneKey = "KardashevAscent.OpenMainSceneOnStart.Done";

        static OpenMainSceneOnStart()
        {
            if (SessionState.GetBool(DoneKey, false))
                return;
            EditorApplication.delayCall += () =>
            {
                SessionState.SetBool(DoneKey, true);
                HideGameViewGizmos();
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                Scene active = SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(active.path) || active.isDirty || SceneManager.sceneCount > 1)
                    return;
                if (System.IO.File.Exists(BuildWindows.ScenePath))
                    EditorSceneManager.OpenScene(BuildWindows.ScenePath);
            };
        }

        /// <summary>The Game view's Gizmos toggle has no public API: set its internal flag (and do nothing if it moved).</summary>
        static void HideGameViewGizmos()
        {
            try
            {
                System.Type gameView = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
                if (gameView == null) return;
                const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                System.Reflection.PropertyInfo draw = gameView.GetProperty("drawGizmos", Any);
                System.Reflection.FieldInfo field = gameView.GetField("m_Gizmos", Any);
                foreach (UnityEngine.Object w in UnityEngine.Resources.FindObjectsOfTypeAll(gameView))
                {
                    if (draw != null && draw.CanWrite) draw.SetValue(w, false);
                    else field?.SetValue(w, false);
                    ((EditorWindow)w).Repaint();
                }
            }
            catch (System.Exception) { }   // an editor nicety only
        }
    }
}
