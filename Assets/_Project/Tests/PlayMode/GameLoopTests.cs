using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StackTower.Tests
{
    /// <summary>
    /// Plays the real game without a headset. The app is booted the normal way (Bootstrap, then the
    /// Game scene); the tests then do what the XR layer does (begin a hold, move the block, end the
    /// hold) and check that the run reacts the way the design says it should.
    /// Run them from Window > General > Test Runner > PlayMode.
    /// </summary>
    public sealed class GameLoopTests
    {
        private const string TimedTowerPath = "Assets/_Project/Data/LevelConfigs/TimedTower.asset";
        private const float Cube = 0.18f;

        private GameManager _game;
        private BlockSpawner _spawner;
        private TowerMonitor _tower;
        private ScoreSystem _score;
        private Pedestal _pedestal;
        private LevelConfig _level;
        private readonly List<RunResult> _results = new List<RunResult>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _results.Clear();
            TestSave.Begin();
            TestRig.BeginCapture();
            // Fast, but each physics step is still a normal 1/90 s step.
            Time.timeScale = 4f;

            // A copy of the shipping level, so shortening it here never touches the asset.
            _level = Object.Instantiate(LoadTimedTower());
            _level.firstSpawnTime = 0.2f;
            _level.introDuration = 0.5f;
            // Cubes only, so the expected heights below are exact.
            _level.blocks = new[] { _level.blocks[0] };
            _level.openingSequence = new BlockDefinition[0];

            yield return TestRig.EnsureBooted();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameEvents.RunEnded -= RecordResult;
            TestRig.EndCapture();
            TestSave.End();
            Time.timeScale = 1f;
            if (_level != null)
            {
                Object.Destroy(_level);
            }

            yield return null;
            TestRig.AssertNoProblems();
        }

        [UnityTest]
        public IEnumerator FirstBlock_PlacedOnPedestal_CountsAndScores()
        {
            yield return LoadGame();
            yield return WaitForBlock();

            StackBlock block = _spawner.Waiting;
            Assert.AreEqual(StackBlock.BlockState.Spawned, block.State);
            Assert.IsTrue(block.IsGrabbable, "A freshly spawned block should be grabbable.");

            // Released a little above the pedestal and a little off centre: soft alignment should centre it.
            yield return Place(block, _pedestal.SurfaceCentre + new Vector3(0.015f, Cube * 0.5f + 0.03f, -0.01f));
            yield return WaitUntil(() => _tower.PlacedCount == 1, 5f, "the first block to count as placed");

            Assert.AreEqual(StackBlock.BlockState.Settled, block.State);
            Assert.IsFalse(block.IsGrabbable, "A placed block must not be grabbable again.");
            Assert.AreEqual(Cube, _tower.CurrentHeight, 0.01f, "Tower height should be one cube.");
            Assert.Less(PlacementMath.HorizontalDistance(block.Position, _pedestal.SurfaceCentre), 0.005f,
                "Soft alignment should have centred the block on the pedestal.");

            // 10 for the block, about 18 for the centimetres gained, 5 for precision. No combo in the learn phase.
            Assert.GreaterOrEqual(_score.Score, 31);
            Assert.LessOrEqual(_score.Score, 34);
        }

        [UnityTest]
        public IEnumerator Block_ReleasedSlightlyInsideTheSurface_IsSetDownNotPoppedOut()
        {
            yield return LoadGame();
            yield return WaitForBlock();

            StackBlock block = _spawner.Waiting;
            // Pushed a centimetre into the pedestal and well off centre, so alignment isn't what saves it.
            Vector3 inside = _pedestal.SurfaceCentre + new Vector3(0.08f, Cube * 0.5f - 0.01f, 0.05f);
            yield return Place(block, inside);

            float highest = block.Position.y;
            float fastest = 0f;
            for (int i = 0; i < 30 && !block.IsPlaced; i++)
            {
                yield return new WaitForFixedUpdate();
                highest = Mathf.Max(highest, block.Position.y);
                fastest = Mathf.Max(fastest, block.Body.linearVelocity.magnitude);
            }

            yield return WaitUntil(() => _tower.PlacedCount == 1, 5f, "the block to be placed");
            Assert.AreEqual(Cube, _tower.CurrentHeight, 0.01f);
            Assert.Less(highest, _pedestal.SurfaceY + Cube * 0.5f + 0.01f, "The block should not hop.");
            Assert.Less(fastest, 0.3f, "The block should not be thrown out of the surface.");
            Assert.AreEqual(0.08f, block.Position.x - _pedestal.SurfaceCentre.x, 0.01f,
                "Off centre stays off centre: this was a lift, not an alignment.");
        }

        [UnityTest]
        public IEnumerator FiveBlocks_StackedInAColumn_StayStanding()
        {
            yield return LoadGame();

            for (int i = 0; i < 5; i++)
            {
                yield return WaitForBlock();
                StackBlock block = _spawner.Waiting;
                float top = _pedestal.SurfaceY + _tower.CurrentHeight;
                Vector3 above = new Vector3(_pedestal.SurfaceCentre.x, top + block.WorldExtents.y + 0.02f,
                    _pedestal.SurfaceCentre.z);
                yield return Place(block, above);

                int expected = i + 1;
                yield return WaitUntil(() => _tower.PlacedCount == expected, 6f, $"block {expected} to be placed");
            }

            Assert.AreEqual(5 * Cube, _tower.CurrentHeight, 0.02f);
            Assert.IsFalse(_tower.HasFallen);

            // Left alone, the tower must not creep or topple.
            float before = _tower.CurrentHeight;
            yield return new WaitForSeconds(3f);
            Assert.AreEqual(before, _tower.CurrentHeight, 0.005f, "The tower moved while untouched.");
            Assert.IsFalse(_tower.HasFallen);
            Assert.IsTrue(_game.IsRunning);
        }

        [UnityTest]
        public IEnumerator DroppedBlock_IsRecycled_AndAnotherAppears()
        {
            yield return LoadGame();
            yield return WaitForBlock();

            StackBlock dropped = _spawner.Waiting;
            // Let go well away from the pedestal: it falls to the floor.
            yield return Place(dropped, _pedestal.SurfaceCentre + new Vector3(1f, 0.3f, 0f));

            yield return WaitUntil(() => _spawner.Waiting != null && _spawner.Waiting != dropped, 6f,
                "a replacement block after a miss");
            yield return WaitUntil(() => dropped.State == StackBlock.BlockState.Pooled, 4f,
                "the dropped block to return to the pool");

            Assert.AreEqual(0, _tower.PlacedCount);
            Assert.IsFalse(_tower.HasFallen, "Missing with an unplaced block must not end the run.");
            Assert.IsTrue(_game.IsRunning);
        }

        [UnityTest]
        public IEnumerator PlacedBlock_FallingOffThePedestal_EndsTheRun()
        {
            yield return LoadGame();
            yield return WaitForBlock();

            StackBlock block = _spawner.Waiting;
            yield return Place(block, _pedestal.SurfaceCentre + Vector3.up * (Cube * 0.5f + 0.02f));
            yield return WaitUntil(() => _tower.PlacedCount == 1, 5f, "the block to be placed");

            // Shove it off the edge.
            block.Body.linearVelocity = new Vector3(2.5f, 0.5f, 0f);

            yield return WaitUntil(() => _results.Count == 1, 6f, "the run to end");
            Assert.AreEqual(RunEndReason.TowerFell, _results[0].Reason);
            Assert.AreEqual(GameState.Ended, _game.State);
            Assert.IsTrue(_tower.HasFallen);
        }

        [UnityTest]
        public IEnumerator Timer_RunningOut_EndsTheRunWithTimeUp()
        {
            _level.learnEnd = 1f;
            _level.finalRushStart = 2f;
            _level.duration = 4f;
            _level.countdownSeconds = 2;

            var phases = new List<RunPhase>();
            GameEvents.PhaseChanged += phases.Add;

            yield return LoadGame();
            yield return WaitUntil(() => _results.Count == 1, 12f, "the timer to run out");
            GameEvents.PhaseChanged -= phases.Add;

            Assert.AreEqual(RunEndReason.TimeUp, _results[0].Reason);
            Assert.AreEqual(0, _results[0].TimeBonus, "Time bonus only applies when the timer didn't end the run.");
            CollectionAssert.AreEqual(
                new[] { RunPhase.Hook, RunPhase.Learn, RunPhase.Ramp, RunPhase.FinalRush }, phases);

            // Nothing can be picked up once the run is over.
            foreach (StackBlock block in _spawner.ActiveBlocks)
            {
                Assert.IsFalse(block.IsGrabbable);
            }
        }

        [UnityTest]
        public IEnumerator LostTracking_HoldsTheBlockInPlace_ThenDropsItGently()
        {
            yield return LoadGame();
            yield return WaitForBlock();

            StackBlock block = _spawner.Waiting;
            block.BeginHold();
            Vector3 held = _pedestal.SurfaceCentre + new Vector3(0.4f, 0.5f, 0f);
            block.transform.position = held;
            yield return null;

            block.EndHold(HoldEndReason.TrackingLost);
            Assert.IsTrue(block.IsWaitingForTracking);
            yield return new WaitForSeconds(0.25f);
            Assert.AreEqual(StackBlock.BlockState.Held, block.State, "Still held during the grace period.");
            Assert.Less(Vector3.Distance(block.Position, held), 0.001f, "Frozen in place while waiting.");

            yield return WaitUntil(() => block.State == StackBlock.BlockState.Free, 2f, "the block to be let go");
            Assert.Less(block.Body.linearVelocity.magnitude, 0.6f, "Dropped, not flung.");
        }

        [UnityTest]
        public IEnumerator HeldBlock_ShowsGhostOverTheTower_AndNotElsewhere()
        {
            yield return LoadGame();
            yield return WaitForBlock();

            var assist = Object.FindFirstObjectByType<PlacementAssist>();
            StackBlock block = _spawner.Waiting;
            block.BeginHold();

            // 30 cm above the pedestal, well off centre so soft alignment stays out of it.
            Vector3 over = _pedestal.SurfaceCentre + new Vector3(0.1f, 0.3f + Cube * 0.5f, 0f);
            block.transform.SetPositionAndRotation(over, Quaternion.identity);
            yield return null;
            yield return null;

            Assert.IsTrue(assist.Predict(block, block.Position, block.Rotation, out PlacementAssist.Landing landing));
            Assert.IsFalse(landing.Aligned);
            Assert.AreEqual(_pedestal.SurfaceY + Cube * 0.5f, landing.Position.y, 0.006f,
                "The ghost should rest on the pedestal surface.");
            Assert.AreEqual(over.x, landing.Position.x, 0.001f, "Projected straight down.");
            Assert.IsNotNull(GameObject.Find("Ghost"), "The ghost should be visible while held over the pedestal.");

            // Off to the side there is only floor underneath: no ghost.
            block.transform.position = _pedestal.SurfaceCentre + new Vector3(0.8f, 0.3f, 0f);
            yield return null;
            yield return null;
            Assert.IsFalse(assist.Predict(block, block.Position, block.Rotation, out _));
            Assert.IsNull(GameObject.Find("Ghost"), "No ghost when the block isn't over the tower.");

            block.EndHold();
        }

        [UnityTest]
        public IEnumerator RotationSnap_LevelsTheHeldBlock_DuringTheLearnPhase()
        {
            _level.introDuration = 0.1f;
            _level.learnEnd = 1000f;
            SaveService.Settings.rotationSnap = RotationSnapMode.LearnPhaseOnly;

            yield return LoadGame();
            yield return WaitForBlock();

            StackBlock block = _spawner.Waiting;
            block.BeginHold();
            // Held slightly crooked: tilted 5 degrees and turned to 37.
            Quaternion crooked = Quaternion.Euler(5f, 37f, 0f);
            block.transform.SetPositionAndRotation(_pedestal.SurfaceCentre + new Vector3(0.4f, 0.4f, 0f), crooked);
            yield return null;
            yield return null;

            Quaternion shown = block.VisualRotation;
            Assert.AreEqual(1f, (shown * Vector3.up).y, 0.0005f, "The visible block should be level.");
            Assert.AreEqual(30f, shown.eulerAngles.y, 0.1f, "Heading snaps to the nearest 15 degrees.");
            Assert.AreEqual(0f, Quaternion.Angle(crooked, block.Rotation), 0.01f,
                "Only the visual turns while held; the hand still drives the body.");

            block.EndHold();
            Assert.AreEqual(0f, Quaternion.Angle(shown, block.Rotation), 0.01f,
                "On release the body takes the rotation the player was looking at.");
        }

        [UnityTest]
        public IEnumerator QuickPlacements_AfterTheLearnPhase_BuildACombo()
        {
            _level.introDuration = 0.1f;
            _level.learnEnd = 0.3f;

            yield return LoadGame();

            for (int i = 0; i < 2; i++)
            {
                yield return WaitForBlock();
                StackBlock block = _spawner.Waiting;
                Vector3 above = new Vector3(_pedestal.SurfaceCentre.x,
                    _pedestal.SurfaceY + _tower.CurrentHeight + Cube * 0.5f + 0.02f, _pedestal.SurfaceCentre.z);
                yield return Place(block, above);

                int expected = i + 1;
                yield return WaitUntil(() => _tower.PlacedCount == expected, 6f, $"block {expected} to be placed");
            }

            Assert.AreEqual(RunPhase.Ramp, _game.Phase);
            Assert.AreEqual(1, _score.ComboStep, "Two placements inside the combo window should start a combo.");
            Assert.AreEqual(1.5f, _score.Multiplier, 0.001f);
        }

        [UnityTest]
        public IEnumerator DeepBlocks_AreFrozen_OnceTheTowerIsTallEnough()
        {
            yield return LoadGame();

            var placed = new List<StackBlock>();
            for (int i = 0; i < 7; i++)
            {
                yield return WaitForBlock();
                StackBlock block = _spawner.Waiting;
                Vector3 above = new Vector3(_pedestal.SurfaceCentre.x,
                    _pedestal.SurfaceY + _tower.CurrentHeight + Cube * 0.5f + 0.02f, _pedestal.SurfaceCentre.z);
                yield return Place(block, above);

                int expected = i + 1;
                yield return WaitUntil(() => _tower.PlacedCount == expected, 6f, $"block {expected} to be placed");
                placed.Add(block);
            }

            // Seven layers: only the bottom one is more than five layers below the top.
            yield return WaitUntil(() => placed[0].State == StackBlock.BlockState.Frozen, 3f,
                "the bottom block to freeze");
            Assert.AreEqual(StackBlock.BlockState.Settled, placed[1].State);
            Assert.AreEqual(7 * Cube, _tower.CurrentHeight, 0.03f);
            Assert.IsFalse(_tower.HasFallen);
        }

        // ---- helpers ---------------------------------------------------------------------------

        private IEnumerator LoadGame()
        {
            GameEvents.RunEnded += RecordResult;
            AsyncOperation load = SceneFlow.LoadGame(_level);
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;
            _game = Object.FindFirstObjectByType<GameManager>();
            _spawner = Object.FindFirstObjectByType<BlockSpawner>();
            _tower = Object.FindFirstObjectByType<TowerMonitor>();
            _score = Object.FindFirstObjectByType<ScoreSystem>();
            _pedestal = Object.FindFirstObjectByType<Pedestal>();

            Assert.IsNotNull(_game, "Game scene has no GameManager. Run Stack Tower > Setup > Run Full Setup.");
            Assert.AreEqual(SceneFlow.Game, SceneManager.GetActiveScene().name);

            // Without a headset the run starts once the auto-start timeout passes.
            yield return WaitUntil(() => _game.IsRunning, 8f, "the run to start");
        }

        private IEnumerator WaitForBlock()
        {
            yield return WaitUntil(() => _spawner.Waiting != null, 8f, "a block on the spawn shelf");
        }

        // What a hand does: pick the block up, carry it over a few frames, let go.
        private static IEnumerator Place(StackBlock block, Vector3 position)
        {
            block.BeginHold();
            Assert.AreEqual(StackBlock.BlockState.Held, block.State);

            Vector3 start = block.Position;
            const int frames = 12;
            for (int i = 1; i <= frames; i++)
            {
                block.transform.SetPositionAndRotation(Vector3.Lerp(start, position, i / (float)frames),
                    Quaternion.identity);
                yield return null;
            }

            // Hold still for a moment, as a careful player would, so the release carries no velocity.
            for (int i = 0; i < 8; i++)
            {
                block.transform.position = position;
                yield return null;
            }

            block.EndHold();
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, float timeout, string what)
        {
            return TestRig.WaitUntil(condition, timeout, what);
        }

        private void RecordResult(RunResult result)
        {
            _results.Add(result);
        }

        private static LevelConfig LoadTimedTower()
        {
#if UNITY_EDITOR
            var level = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelConfig>(TimedTowerPath);
            Assert.IsNotNull(level, "TimedTower level not found. Run Stack Tower > Setup > Run Full Setup.");
            return level;
#else
            Assert.Ignore("These tests load project assets and run in the editor only.");
            return null;
#endif
        }
    }
}
