using System.Collections;
using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// First thing that runs. Keeps the camera rig and app services alive across scene changes
    /// (so tracking never re-initialises), loads the save file, then hands over to the menu.
    /// </summary>
    public sealed class AppBootstrap : MonoBehaviour
    {
        /// <summary>
        /// SessionState key the editor uses to send Play straight to the scene you were editing.
        /// </summary>
        public const string EditorStartSceneKey = "StackTower.EditorStartScene";

        private static bool _booted;
        private bool _isPrimary;

        [Tooltip("Root that holds the camera rig, event system and app services. Kept for the whole session.")]
        [SerializeField] private GameObject persistentRoot;
        [SerializeField] private string firstScene = SceneFlow.Menu;

        private void Awake()
        {
            if (_booted)
            {
                // Bootstrap got loaded a second time; the first rig is still alive.
                Destroy(persistentRoot != null ? persistentRoot : gameObject);
                return;
            }

            _booted = true;
            _isPrimary = true;
            DontDestroyOnLoad(persistentRoot != null ? persistentRoot : gameObject);

            SaveService.Load();
            AudioListener.volume = SaveService.Settings.volume;
        }

        private void OnDestroy()
        {
            if (_isPrimary)
            {
                _booted = false;
            }
        }

        private IEnumerator Start()
        {
            if (!_isPrimary)
            {
                yield break;
            }

            // Give the rig one frame to bring tracking up before content appears.
            yield return null;

            string target = firstScene;
#if UNITY_EDITOR
            string requested = UnityEditor.SessionState.GetString(EditorStartSceneKey, string.Empty);
            if (!string.IsNullOrEmpty(requested))
            {
                target = requested;
            }
#endif
            if (target == SceneFlow.Game)
            {
                SceneFlow.LoadGame();
            }
            else
            {
                SceneFlow.LoadMenu();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _booted = false;
        }
    }
}
