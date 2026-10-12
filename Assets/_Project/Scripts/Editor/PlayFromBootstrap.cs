using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace StackTower.EditorTools
{
    /// <summary>
    /// The camera rig and app services live in the Bootstrap scene. This makes Play start there
    /// whichever project scene is open, then tells Bootstrap to go straight on to the scene you were
    /// editing, so pressing Play in Game.unity drops you into a run.
    /// Scenes outside Assets/_Project/Scenes (test scenes, samples) are left alone.
    /// </summary>
    [InitializeOnLoad]
    internal static class PlayFromBootstrap
    {
        private const string OwnsStartSceneKey = "StackTower.OwnsPlayModeStartScene";

        static PlayFromBootstrap()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeChange;
        }

        private static void HandlePlayModeChange(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                Scene active = SceneManager.GetActiveScene();
                var bootstrap = AssetDatabase.LoadAssetAtPath<SceneAsset>(SceneBuilder.BootstrapPath);
                bool ours = active.path.StartsWith(SceneBuilder.ScenesDir + "/");

                if (ours && bootstrap != null && active.path != SceneBuilder.BootstrapPath)
                {
                    EditorSceneManager.playModeStartScene = bootstrap;
                    SessionState.SetBool(OwnsStartSceneKey, true);
                    SessionState.SetString(AppBootstrap.EditorStartSceneKey, active.name);
                }
                else
                {
                    SessionState.EraseString(AppBootstrap.EditorStartSceneKey);
                }
            }
            else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(OwnsStartSceneKey, false))
            {
                // Only undo what we set, so other tools that use the start scene keep working.
                EditorSceneManager.playModeStartScene = null;
                SessionState.EraseBool(OwnsStartSceneKey);
                SessionState.EraseString(AppBootstrap.EditorStartSceneKey);
            }
        }
    }
}
