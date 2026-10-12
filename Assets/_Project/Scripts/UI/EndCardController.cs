using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackTower
{
    /// <summary>
    /// End card: score, height, best, and poke buttons to play again or go back to the menu.
    /// Shares the side panel with the HUD, so the buttons are where the player was already looking.
    /// </summary>
    public sealed class EndCardController : MonoBehaviour
    {
        [SerializeField] private GameManager game;
        [SerializeField] private GameObject content;

        [Header("Texts")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text heightText;
        [SerializeField] private TMP_Text bestText;
        [SerializeField] private TMP_Text newBestText;

        [Header("Buttons")]
        [SerializeField] private Button playAgainButton;
        [SerializeField] private Button menuButton;

        [Header("Timing")]
        [Tooltip("Let the last block land (or the tower finish falling) before the card appears (s).")]
        [SerializeField] private float showDelay = 1.2f;
        [Tooltip("Buttons ignore pokes this long after the card appears, so a hand that was mid-reach can't hit one (s).")]
        [SerializeField] private float armDelay = 0.6f;

        private RunResult _result;
        private float _showTimer = -1f;
        private float _armTimer = -1f;

        private void Awake()
        {
            if (content != null)
            {
                content.SetActive(false);
            }
        }

        private void OnEnable()
        {
            GameEvents.RunEnded += HandleRunEnded;
            GameEvents.StateChanged += HandleState;

            if (playAgainButton != null)
            {
                playAgainButton.onClick.AddListener(PlayAgain);
            }

            if (menuButton != null)
            {
                menuButton.onClick.AddListener(Menu);
            }
        }

        private void OnDisable()
        {
            GameEvents.RunEnded -= HandleRunEnded;
            GameEvents.StateChanged -= HandleState;

            if (playAgainButton != null)
            {
                playAgainButton.onClick.RemoveListener(PlayAgain);
            }

            if (menuButton != null)
            {
                menuButton.onClick.RemoveListener(Menu);
            }
        }

        private void Update()
        {
            if (_showTimer >= 0f)
            {
                _showTimer -= Time.deltaTime;
                if (_showTimer < 0f)
                {
                    Show();
                }
            }

            if (_armTimer >= 0f)
            {
                _armTimer -= Time.deltaTime;
                if (_armTimer < 0f)
                {
                    SetButtonsInteractable(true);
                }
            }
        }

        private void HandleRunEnded(RunResult result)
        {
            _result = result;
            _showTimer = showDelay;
        }

        private void HandleState(GameState state)
        {
            if (state != GameState.Ended)
            {
                _showTimer = -1f;
                _armTimer = -1f;
                if (content != null)
                {
                    content.SetActive(false);
                }
            }
        }

        private void Show()
        {
            if (titleText != null)
            {
                switch (_result.Reason)
                {
                    case RunEndReason.TowerFell:
                        titleText.text = "Tower fell!";
                        break;
                    case RunEndReason.TargetReached:
                        titleText.text = "Target reached!";
                        break;
                    default:
                        titleText.text = "Time's up!";
                        break;
                }
            }

            if (scoreText != null)
            {
                scoreText.SetText("{0}", _result.Score);
            }

            if (heightText != null)
            {
                heightText.SetText("{0:2} m", _result.Height);
            }

            if (bestText != null)
            {
                bestText.SetText("Best  {0}   {1:2} m", _result.BestScore, _result.BestHeight);
            }

            if (newBestText != null)
            {
                newBestText.gameObject.SetActive(_result.IsNewBestScore || _result.IsNewBestHeight);
            }

            SetButtonsInteractable(false);
            _armTimer = armDelay;

            if (content != null)
            {
                content.SetActive(true);
            }
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (playAgainButton != null)
            {
                playAgainButton.interactable = interactable;
            }

            if (menuButton != null)
            {
                menuButton.interactable = interactable;
            }
        }

        private void PlayAgain()
        {
            if (game != null)
            {
                game.RestartRun();
            }
        }

        private void Menu()
        {
            if (game != null)
            {
                game.ReturnToMenu();
            }
        }
    }
}
