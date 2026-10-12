using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace StackTower.Tests
{
    /// <summary>
    /// Walks the real scene flow with the real rig: Bootstrap -> Menu -> Play -> Game. There is no
    /// headset in a test run, so this can't check interaction, but it does catch broken wiring:
    /// missing references, components that throw on start, a rig that doesn't survive scene changes.
    /// </summary>
    public sealed class BootFlowTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            TestSave.Begin();
            TestRig.BeginCapture();
            // Start cold, even if an earlier test left the app running.
            yield return TestRig.Shutdown();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            TestRig.EndCapture();
            TestSave.End();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Bootstrap_LeadsToMenu_AndPlayStartsARun()
        {
            yield return TestRig.EnsureBooted();

            Assert.AreEqual(SceneFlow.Menu, SceneManager.GetActiveScene().name);
            Assert.IsNotNull(Camera.main, "The camera rig should still be there after the scene change.");

            GameObject playObject = GameObject.Find("PlayButton");
            Assert.IsNotNull(playObject, "Menu has no PlayButton.");

            // Flip a setting and flip it back, the way a player would, to exercise the settings path.
            bool leftHanded = SaveService.Settings.leftHanded;
            Button hand = GameObject.Find("HandButton").GetComponent<Button>();
            hand.onClick.Invoke();
            Assert.AreNotEqual(leftHanded, SaveService.Settings.leftHanded);
            hand.onClick.Invoke();
            Assert.AreEqual(leftHanded, SaveService.Settings.leftHanded);

            playObject.GetComponent<Button>().onClick.Invoke();
            yield return TestRig.WaitUntil(() => SceneManager.GetActiveScene().name == SceneFlow.Game, 20f,
                "the game to load");
            yield return null;

            Assert.IsNotNull(Camera.main, "The camera rig should still be there in the game scene.");

            var game = Object.FindFirstObjectByType<GameManager>();
            var spawner = Object.FindFirstObjectByType<BlockSpawner>();
            Assert.IsNotNull(game);
            yield return TestRig.WaitUntil(() => game.IsRunning, 10f, "the run to start");
            yield return TestRig.WaitUntil(() => spawner.Waiting != null, 10f, "the first block");

            // Give every component a few frames to fall over if it is going to.
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            TestRig.AssertNoProblems();
        }
    }
}
