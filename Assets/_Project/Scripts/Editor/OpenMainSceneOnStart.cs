using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace KardashevAscent.EditorTools
{
    /// <summary>
    /// When the project opens on an empty untitled scene (a fresh checkout or worktree), open the game's scene,
    /// Assets/Scenes/Main.unity, so Play shows the planet. Once per editor session; a scene you chose is left alone.
    /// It also hides the editor's gizmo icons for lights and sound (director 2026-11-15: the sun and speaker icons
    /// drawn over the planet); built games never show them.
    /// </summary>
    [InitializeOnLoad]
    static class OpenMainSceneOnStart
    {
        const string DoneKey = "KardashevAscent.OpenMainSceneOnStart.Done";

        static OpenMainSceneOnStart()
        {
            HideGizmoIcons();
            if (SessionState.GetBool(DoneKey, false))
                return;
            EditorApplication.delayCall += () =>
            {
                SessionState.SetBool(DoneKey, true);
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;
                Scene active = SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(active.path) || active.isDirty || SceneManager.sceneCount > 1)
                    return;
                if (System.IO.File.Exists(BuildWindows.ScenePath))
                    EditorSceneManager.OpenScene(BuildWindows.ScenePath);
            };
        }

        static void HideGizmoIcons()
        {
            foreach (System.Type t in new[] { typeof(UnityEngine.Light), typeof(UnityEngine.AudioSource), typeof(UnityEngine.AudioListener) })
                GizmoUtility.SetIconEnabled(t, false);
        }
    }
}
