using TMPro;
using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// In-run HUD on the panel beside the tower: timer, score, current height, combo and a short
    /// onboarding hint. World-space only; nothing is locked to the head.
    /// </summary>
    public sealed class HUDController : MonoBehaviour
    {
        [SerializeField] private GameManager game;
        [Tooltip("Everything the HUD shows. Hidden while the end card is up.")]
        [SerializeField] private GameObject content;

        [Header("Texts")]
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text heightText;
        [SerializeField] private TMP_Text comboText;
        [SerializeField] private TMP_Text hintText;

        [Header("Timer colours")]
        [Tooltip("Learn phase: the clock is there but doesn't shout.")]
        [SerializeField] private Color timerCalm = new Color(1f, 1f, 1f, 0.45f);
        [SerializeField] private Color timerNormal = Color.white;
        [SerializeField] private Color timerUrgent = new Color(1f, 0.35f, 0.3f, 1f);

        [Header("Hints")]
        [SerializeField] private string grabHintHands = "Pinch the block to pick it up";
        [SerializeField] private string grabHintControllers = "Squeeze the grip to pick it up";
        [SerializeField] private string placeHint = "Set it down on the pedestal";

        private enum Hint
        {
            None,
            Grab,
            Place
        }

        private int _shownSeconds = -1;
        private Hint _hint = Hint.None;
        private bool _taughtGrab;
        private bool _taughtPlace;
        private bool _urgent;
        private float _pulse;

        private void OnEnable()
        {
            GameEvents.StateChanged += HandleState;
            GameEvents.PhaseChanged += HandlePhase;
            GameEvents.ScoreChanged += HandleScore;
            GameEvents.HeightChanged += HandleHeight;
            GameEvents.ComboChanged += HandleCombo;
            GameEvents.CountdownTick += HandleCountdown;
            GameEvents.BlockSpawned += HandleSpawned;
            GameEvents.BlockGrabbed += HandleGrabbed;
            GameEvents.BlockPlaced += HandlePlaced;
            GameEvents.InputModeChanged += HandleInputMode;

            HandleScore(0);
            HandleHeight(0f, 0f);
            HandleCombo(1f, 0);
            SetHint(Hint.None);
        }

        private void OnDisable()
        {
            GameEvents.StateChanged -= HandleState;
            GameEvents.PhaseChanged -= HandlePhase;
            GameEvents.ScoreChanged -= HandleScore;
            GameEvents.HeightChanged -= HandleHeight;
            GameEvents.ComboChanged -= HandleCombo;
            GameEvents.CountdownTick -= HandleCountdown;
            GameEvents.BlockSpawned -= HandleSpawned;
            GameEvents.BlockGrabbed -= HandleGrabbed;
            GameEvents.BlockPlaced -= HandlePlaced;
            GameEvents.InputModeChanged -= HandleInputMode;
        }

        private void Update()
        {
            if (game == null || !game.IsRunning)
            {
                return;
            }

            if (timerText != null && game.Level.HasTimer)
            {
                // Only touch the text when the number changes; SetText with numbers doesn't allocate.
                int seconds = Mathf.CeilToInt(game.Remaining);
                if (seconds != _shownSeconds)
                {
                    _shownSeconds = seconds;
                    timerText.SetText("{0}", seconds);
                }
            }

            if (_pulse > 0f && timerText != null)
            {
                _pulse = Mathf.Max(0f, _pulse - Time.deltaTime * 4f);
                timerText.rectTransform.localScale = Vector3.one * (1f + 0.25f * _pulse);
            }

            if (_hint != Hint.None && hintText != null)
            {
                hintText.alpha = 0.65f + 0.35f * Mathf.Sin(Time.time * 4f);
            }
        }

        private void HandleState(GameState state)
        {
            if (content != null)
            {
                content.SetActive(state != GameState.Ended && state != GameState.Idle);
            }

            if (state == GameState.Intro)
            {
                _shownSeconds = -1;
                _urgent = false;
                _taughtGrab = false;
                _taughtPlace = false;
                SetHint(Hint.None);

                if (timerText != null)
                {
                    timerText.gameObject.SetActive(game != null && game.Level.HasTimer);
                    timerText.rectTransform.localScale = Vector3.one;
                }
            }
        }

        private void HandlePhase(RunPhase phase)
        {
            if (timerText != null && !_urgent)
            {
                timerText.color = phase <= RunPhase.Learn ? timerCalm : timerNormal;
            }
        }

        private void HandleCountdown(int secondsLeft)
        {
            _urgent = true;
            _pulse = 1f;
            if (timerText != null)
            {
                timerText.color = timerUrgent;
            }
        }

        private void HandleScore(int score)
        {
            if (scoreText != null)
            {
                scoreText.SetText("{0}", score);
            }
        }

        private void HandleHeight(float current, float max)
        {
            if (heightText != null)
            {
                heightText.SetText("{0:2} m", current);
            }
        }

        private void HandleCombo(float multiplier, int step)
        {
            if (comboText == null)
            {
                return;
            }

            comboText.gameObject.SetActive(step > 0);
            if (step > 0)
            {
                comboText.SetText("Combo x{0:1}", multiplier);
            }
        }

        private void HandleSpawned(StackBlock block)
        {
            if (!_taughtGrab)
            {
                SetHint(Hint.Grab);
            }
        }

        private void HandleGrabbed(StackBlock block)
        {
            _taughtGrab = true;
            SetHint(_taughtPlace ? Hint.None : Hint.Place);
        }

        private void HandlePlaced(PlacementInfo info)
        {
            _taughtPlace = true;
            SetHint(Hint.None);
        }

        private void HandleInputMode(InputMode mode)
        {
            if (_hint == Hint.Grab)
            {
                SetHint(Hint.Grab);
            }
        }

        private void SetHint(Hint hint)
        {
            _hint = hint;
            if (hintText == null)
            {
                return;
            }

            hintText.gameObject.SetActive(hint != Hint.None);
            switch (hint)
            {
                case Hint.Grab:
                    hintText.text = GameEvents.CurrentInputMode == InputMode.Controllers
                        ? grabHintControllers
                        : grabHintHands;
                    break;
                case Hint.Place:
                    hintText.text = placeHint;
                    break;
            }
        }
    }
}
