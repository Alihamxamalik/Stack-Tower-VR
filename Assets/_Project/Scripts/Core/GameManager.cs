using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// Runs one run: Intro -> Playing -> FinalRush -> Ended. Owns the timer and the pacing phases and
    /// tells the other systems when a run starts and stops. Everything else reacts through
    /// <see cref="GameEvents"/>.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField] private LevelConfig level;
        [SerializeField] private StageLayout stage;
        [SerializeField] private BlockSpawner spawner;
        [SerializeField] private TowerMonitor tower;
        [SerializeField] private ScoreSystem score;
        [SerializeField] private PlacementAssist assist;

        [Header("Start")]
        [SerializeField] private bool autoStart = true;
        [Tooltip("Wait at least this long after the scene loads before starting (s).")]
        [SerializeField] private float autoStartDelay = 0.5f;
        [Tooltip("Start anyway if the headset hasn't reported a pose by then (s).")]
        [SerializeField] private float autoStartTimeout = 2.5f;

        private float _sinceLoad;
        private bool _firstBlockSpawned;
        private int _lastCountdown;

        public GameState State { get; private set; } = GameState.Idle;
        public RunPhase Phase { get; private set; } = RunPhase.Hook;
        public LevelConfig Level => level;
        /// <summary>Seconds since the run started. Stops when the run ends.</summary>
        public float Elapsed { get; private set; }
        public float Remaining => level != null && level.HasTimer ? Mathf.Max(0f, level.duration - Elapsed) : 0f;
        public bool IsRunning => State == GameState.Intro || State == GameState.Playing || State == GameState.FinalRush;
        public RunResult LastResult { get; private set; }

        public void StartRun()
        {
            if (level == null)
            {
                Debug.LogError("[GameManager] No LevelConfig assigned.", this);
                return;
            }

            spawner.DespawnAll();
            stage.Calibrate();

            Elapsed = 0f;
            _firstBlockSpawned = false;
            _lastCountdown = -1;

            tower.BeginRun(level.canFail);
            score.BeginRun();
            spawner.BeginRun(level, tower.FallY);

            SetPhase(RunPhase.Hook, force: true);
            SetState(GameState.Intro);
            stage.PlayIntro(level.firstSpawnTime);
        }

        public void RestartRun()
        {
            StartRun();
        }

        public void ReturnToMenu()
        {
            spawner.StopSpawning();
            SceneFlow.LoadMenu();
        }

        private void Awake()
        {
            if (SceneFlow.PendingLevel != null)
            {
                level = SceneFlow.PendingLevel;
            }
        }

        private void OnEnable()
        {
            GameEvents.BlockPlaced += HandleBlockPlaced;
            GameEvents.BlockLost += HandleBlockLost;
            GameEvents.TowerFell += HandleTowerFell;
            SaveService.SettingsChanged += ApplyRotationSnap;
        }

        private void OnDisable()
        {
            GameEvents.BlockPlaced -= HandleBlockPlaced;
            GameEvents.BlockLost -= HandleBlockLost;
            GameEvents.TowerFell -= HandleTowerFell;
            SaveService.SettingsChanged -= ApplyRotationSnap;
        }

        private void Update()
        {
            if (State == GameState.Idle)
            {
                TryAutoStart();
                return;
            }

            if (!IsRunning)
            {
                return;
            }

            Elapsed += Time.deltaTime;
            spawner.RunTime = Elapsed;

            if (!_firstBlockSpawned && Elapsed >= level.firstSpawnTime)
            {
                _firstBlockSpawned = true;
                spawner.SpawnNow();
            }

            UpdatePhase();

            if (!level.HasTimer)
            {
                return;
            }

            int secondsLeft = Mathf.CeilToInt(Remaining);
            if (secondsLeft > 0 && secondsLeft <= level.countdownSeconds && secondsLeft != _lastCountdown)
            {
                _lastCountdown = secondsLeft;
                GameEvents.RaiseCountdownTick(secondsLeft);
            }

            if (Elapsed >= level.duration)
            {
                EndRun(RunEndReason.TimeUp);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            PauseService.Set(PauseService.Reason.ApplicationPaused, paused);
        }

        private void TryAutoStart()
        {
            if (!autoStart)
            {
                return;
            }

            // Calibration needs a real head pose, which can take a few frames to arrive.
            _sinceLoad += Time.unscaledDeltaTime;
            if (_sinceLoad < autoStartDelay)
            {
                return;
            }

            if (stage.HasTrackedHead || _sinceLoad >= autoStartTimeout)
            {
                StartRun();
            }
        }

        private void UpdatePhase()
        {
            RunPhase phase;
            if (Elapsed < level.introDuration)
            {
                phase = RunPhase.Hook;
            }
            else if (Elapsed < level.learnEnd)
            {
                phase = RunPhase.Learn;
            }
            else if (level.HasTimer && Elapsed >= level.finalRushStart)
            {
                phase = RunPhase.FinalRush;
            }
            else
            {
                phase = RunPhase.Ramp;
            }

            SetPhase(phase);

            GameState state = phase == RunPhase.Hook ? GameState.Intro
                : phase == RunPhase.FinalRush ? GameState.FinalRush
                : GameState.Playing;
            SetState(state);
        }

        private void SetPhase(RunPhase phase, bool force = false)
        {
            if (phase == Phase && !force)
            {
                return;
            }

            Phase = phase;
            spawner.Phase = phase;
            score.ComboEnabled = phase >= RunPhase.Ramp;
            ApplyRotationSnap();
            GameEvents.RaisePhaseChanged(phase);
        }

        private void SetState(GameState state)
        {
            if (state == State)
            {
                return;
            }

            State = state;
            GameEvents.RaiseStateChanged(state);
        }

        private void ApplyRotationSnap()
        {
            if (assist == null)
            {
                return;
            }

            RotationSnapMode mode = SaveService.Settings.rotationSnap;
            assist.RotationSnapEnabled = mode == RotationSnapMode.Always
                                         || (mode == RotationSnapMode.LearnPhaseOnly && Phase <= RunPhase.Learn);
        }

        private void HandleBlockPlaced(PlacementInfo info)
        {
            if (!IsRunning)
            {
                return;
            }

            if (level.targetHeight > 0f && info.MaxHeight >= level.targetHeight)
            {
                EndRun(RunEndReason.TargetReached);
                return;
            }

            spawner.QueueSpawn(level.SpawnDelay(Phase));
        }

        private void HandleBlockLost(StackBlock block)
        {
            if (IsRunning)
            {
                spawner.QueueSpawn(level.SpawnDelay(Phase));
            }
        }

        private void HandleTowerFell()
        {
            if (IsRunning && level.canFail)
            {
                EndRun(RunEndReason.TowerFell);
            }
        }

        private void EndRun(RunEndReason reason)
        {
            if (!IsRunning)
            {
                return;
            }

            SetState(GameState.Ended);
            spawner.StopSpawning();
            spawner.LockAllBlocks();
            tower.EndRun();

            // Time left is only worth points when the timer isn't what ended the run.
            int timeBonus = reason == RunEndReason.TargetReached ? score.AddTimeBonus(Remaining) : 0;
            float height = tower.MaxHeight;

            SaveService.SubmitRun(level.mode, score.Score, height, out bool newBestScore, out bool newBestHeight);
            ModeRecord record = SaveService.GetRecord(level.mode);

            LastResult = new RunResult(level.mode, reason, score.Score, timeBonus, height, tower.PlacedCount,
                record.bestScore, record.bestHeight, newBestScore, newBestHeight);
            GameEvents.RaiseRunEnded(LastResult);
        }
    }
}
