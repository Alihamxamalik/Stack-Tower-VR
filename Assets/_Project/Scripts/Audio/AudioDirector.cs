using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// Sound stands in for haptics when playing with bare hands: a tock per placement that climbs in
    /// pitch with the combo, a creak when the tower wobbles, a crash when it goes. Positional sounds
    /// come from a small pool of spatial sources; UI sounds are plain 2D.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        [Header("Sources")]
        [Tooltip("Spatial one-shot sources, used round-robin.")]
        [SerializeField] private AudioSource[] voices;
        [Tooltip("Non-spatial source for ticks and fanfares.")]
        [SerializeField] private AudioSource uiSource;
        [SerializeField] private AudioSource musicSource;

        [Header("Blocks")]
        [SerializeField] private AudioClip spawnClip;
        [SerializeField] private AudioClip grabClip;
        [SerializeField] private AudioClip placeClip;
        [SerializeField] private AudioClip rewardClip;
        [SerializeField] private AudioClip precisionClip;
        [SerializeField] private AudioClip goldenClip;
        [SerializeField] private AudioClip lostClip;

        [Header("Tower")]
        [SerializeField] private AudioClip wobbleClip;
        [SerializeField] private AudioClip collapseClip;

        [Header("Run")]
        [SerializeField] private AudioClip tickClip;
        [SerializeField] private AudioClip timeUpClip;
        [SerializeField] private AudioClip newBestClip;

        [Header("Music")]
        [SerializeField] private AudioClip musicLoop;
        [SerializeField] private float finalRushMusicPitch = 1.15f;

        [Header("Feel")]
        [Tooltip("Each combo step raises the placement pitch by this much.")]
        [SerializeField] private float comboPitchStep = 0.09f;
        [Tooltip("Impact speed that plays the placement sound at full volume (m/s).")]
        [SerializeField] private float fullVolumeImpactSpeed = 1.2f;

        private int _nextVoice;
        private int _comboStep;
        private Vector3 _towerPosition;

        private void OnEnable()
        {
            GameEvents.BlockSpawned += HandleSpawned;
            GameEvents.BlockGrabbed += HandleGrabbed;
            GameEvents.BlockImpact += HandleImpact;
            GameEvents.BlockPlaced += HandlePlaced;
            GameEvents.BlockLost += HandleLost;
            GameEvents.ScoreAwarded += HandleAward;
            GameEvents.ComboChanged += HandleCombo;
            GameEvents.WobbleChanged += HandleWobble;
            GameEvents.TowerFell += HandleTowerFell;
            GameEvents.CountdownTick += HandleTick;
            GameEvents.StateChanged += HandleState;
            GameEvents.RunEnded += HandleRunEnded;
        }

        private void OnDisable()
        {
            GameEvents.BlockSpawned -= HandleSpawned;
            GameEvents.BlockGrabbed -= HandleGrabbed;
            GameEvents.BlockImpact -= HandleImpact;
            GameEvents.BlockPlaced -= HandlePlaced;
            GameEvents.BlockLost -= HandleLost;
            GameEvents.ScoreAwarded -= HandleAward;
            GameEvents.ComboChanged -= HandleCombo;
            GameEvents.WobbleChanged -= HandleWobble;
            GameEvents.TowerFell -= HandleTowerFell;
            GameEvents.CountdownTick -= HandleTick;
            GameEvents.StateChanged -= HandleState;
            GameEvents.RunEnded -= HandleRunEnded;
        }

        private void HandleSpawned(StackBlock block)
        {
            PlayAt(spawnClip, block.Position, 0.5f, 1f);
        }

        private void HandleGrabbed(StackBlock block)
        {
            PlayAt(grabClip, block.Position, 0.6f, 1f);
        }

        // The tock lands with the block, not half a second later when it counts as settled.
        private void HandleImpact(StackBlock block, float speed)
        {
            float volume = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(speed / fullVolumeImpactSpeed));
            PlayAt(placeClip, block.Position, volume, 1f + comboPitchStep * _comboStep);
        }

        private void HandlePlaced(PlacementInfo info)
        {
            _towerPosition = info.Block.Position;
        }

        private void HandleAward(ScoreAward award)
        {
            float pitch = 1f + comboPitchStep * _comboStep;
            if (award.BonusPoints > 0)
            {
                PlayAt(goldenClip, award.WorldPosition, 0.8f, 1f);
            }
            else if (award.PrecisionPoints > 0)
            {
                PlayAt(precisionClip, award.WorldPosition, 0.7f, pitch);
            }
            else
            {
                PlayAt(rewardClip, award.WorldPosition, 0.45f, pitch);
            }
        }

        private void HandleLost(StackBlock block)
        {
            PlayAt(lostClip, block.Position, 0.6f, 1f);
        }

        private void HandleCombo(float multiplier, int step)
        {
            _comboStep = step;
        }

        private void HandleWobble(bool wobbling)
        {
            if (wobbling)
            {
                PlayAt(wobbleClip, _towerPosition, 0.8f, Random.Range(0.92f, 1.08f));
            }
        }

        private void HandleTowerFell()
        {
            PlayAt(collapseClip, _towerPosition, 1f, 1f);
        }

        private void HandleTick(int secondsLeft)
        {
            // The last three ticks step up in pitch.
            PlayUi(tickClip, 0.6f, secondsLeft <= 3 ? 1.25f : 1f);
        }

        private void HandleState(GameState state)
        {
            if (musicSource == null || musicLoop == null)
            {
                return;
            }

            switch (state)
            {
                case GameState.Intro:
                    musicSource.clip = musicLoop;
                    musicSource.loop = true;
                    musicSource.pitch = 1f;
                    musicSource.Play();
                    break;
                case GameState.FinalRush:
                    musicSource.pitch = finalRushMusicPitch;
                    break;
                case GameState.Ended:
                    musicSource.Stop();
                    break;
            }
        }

        private void HandleRunEnded(RunResult result)
        {
            if (result.IsNewBestScore || result.IsNewBestHeight)
            {
                PlayUi(newBestClip, 0.9f, 1f);
            }
            else if (result.Reason != RunEndReason.TowerFell)
            {
                PlayUi(timeUpClip, 0.8f, 1f);
            }
        }

        private void PlayAt(AudioClip clip, Vector3 position, float volume, float pitch)
        {
            if (clip == null || voices == null || voices.Length == 0)
            {
                return;
            }

            AudioSource voice = voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % voices.Length;
            if (voice == null)
            {
                return;
            }

            voice.transform.position = position;
            voice.pitch = pitch;
            voice.PlayOneShot(clip, volume);
        }

        private void PlayUi(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || uiSource == null)
            {
                return;
            }

            uiSource.pitch = pitch;
            uiSource.PlayOneShot(clip, volume);
        }
    }
}
