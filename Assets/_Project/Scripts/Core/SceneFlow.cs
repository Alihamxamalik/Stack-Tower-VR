using UnityEngine;
using UnityEngine.SceneManagement;

namespace StackTower
{
    /// <summary>
    /// Scene changes for the three-scene layout: Bootstrap stays alive (rig and services live in
    /// DontDestroyOnLoad), Menu and Game are swapped underneath it.
    /// </summary>
    public static class SceneFlow
    {
        public const string Bootstrap = "Bootstrap";
        public const string Menu = "Menu";
        public const string Game = "Game";

        /// <summary>Level the Game scene should run next. Null keeps the scene's own default.</summary>
        public static LevelConfig PendingLevel { get; private set; }

        public static AsyncOperation LoadMenu()
        {
            return SceneManager.LoadSceneAsync(Menu);
        }

        public static AsyncOperation LoadGame(LevelConfig level = null)
        {
            PendingLevel = level;
            return SceneManager.LoadSceneAsync(Game);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PendingLevel = null;
        }
    }
}
