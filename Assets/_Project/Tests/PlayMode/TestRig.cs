using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StackTower.Tests
{
    /// <summary>
    /// Shared plumbing for the play mode tests: boots the app the way a real launch does (Bootstrap
    /// scene, persistent rig), and separates genuine failures from the XR SDK's own noise.
    /// </summary>
    internal static class TestRig
    {
        private const string PersistentName = "[Persistent]";

        private static readonly List<string> Problems = new List<string>();

        /// <summary>
        /// With no headset attached the XR SDK logs errors of its own (no OpenXR runtime, no device).
        /// Those would fail every test, so only exceptions and failed assertions count.
        /// </summary>
        public static void BeginCapture()
        {
            Problems.Clear();
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += Capture;
        }

        public static void EndCapture()
        {
            Application.logMessageReceived -= Capture;
            LogAssert.ignoreFailingMessages = false;
        }

        public static void AssertNoProblems()
        {
            Assert.IsEmpty(Problems, "Exceptions or failed assertions were logged:\n" + string.Join("\n", Problems));
        }

        /// <summary>Loads Bootstrap and waits for it to hand over to the menu, unless the app is already up.</summary>
        public static IEnumerator EnsureBooted()
        {
            if (GameObject.Find(PersistentName) != null)
            {
                yield break;
            }

            yield return SceneManager.LoadSceneAsync(SceneFlow.Bootstrap);
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == SceneFlow.Menu, 20f, "the menu to load");
            yield return null;
        }

        /// <summary>Tears the persistent rig down so the next boot starts from nothing.</summary>
        public static IEnumerator Shutdown()
        {
            GameObject persistent = GameObject.Find(PersistentName);
            if (persistent != null)
            {
                Object.Destroy(persistent);
                yield return null;
            }
        }

        public static IEnumerator WaitUntil(System.Func<bool> condition, float timeout, string what)
        {
            float waited = 0f;
            while (!condition())
            {
                waited += Time.unscaledDeltaTime;
                if (waited > timeout)
                {
                    Assert.Fail($"Timed out after {timeout:0.#} s waiting for {what}.");
                }

                yield return null;
            }
        }

        private static void Capture(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Assert)
            {
                Problems.Add(condition + "\n" + stackTrace);
            }
        }
    }
}
