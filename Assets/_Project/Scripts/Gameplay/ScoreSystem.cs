using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// Points, precision bonus and the combo multiplier. Listens to placements; knows nothing about
    /// how blocks got there.
    /// </summary>
    public sealed class ScoreSystem : MonoBehaviour
    {
        [SerializeField] private GameTuning tuning;

        private float _lastPlacementTime;
        private bool _hasPlacement;
        private bool _comboEnabled;

        public int Score { get; private set; }
        /// <summary>0 = no combo, 1..n = index into the multiplier list.</summary>
        public int ComboStep { get; private set; }
        public float Multiplier => tuning.ComboMultiplier(ComboStep);

        /// <summary>The combo only runs once the learn phase is over.</summary>
        public bool ComboEnabled
        {
            get => _comboEnabled;
            set
            {
                _comboEnabled = value;
                if (!value)
                {
                    SetCombo(0);
                }
            }
        }

        public void BeginRun()
        {
            Score = 0;
            ComboStep = 0;
            _hasPlacement = false;
            GameEvents.RaiseScoreChanged(Score);
            GameEvents.RaiseComboChanged(1f, 0);
        }

        /// <summary>End-of-run bonus for finishing a target with time to spare. Returns the points added.</summary>
        public int AddTimeBonus(float secondsRemaining)
        {
            int bonus = Mathf.FloorToInt(Mathf.Max(0f, secondsRemaining)) * tuning.timeBonusPerSecond;
            if (bonus > 0)
            {
                Score += bonus;
                GameEvents.RaiseScoreChanged(Score);
            }

            return bonus;
        }

        private void OnEnable()
        {
            GameEvents.BlockPlaced += HandlePlaced;
            GameEvents.BlockLost += HandleLost;
            GameEvents.WobbleChanged += HandleWobble;
        }

        private void OnDisable()
        {
            GameEvents.BlockPlaced -= HandlePlaced;
            GameEvents.BlockLost -= HandleLost;
            GameEvents.WobbleChanged -= HandleWobble;
        }

        private void Update()
        {
            // Let the HUD see the combo run out instead of only finding out at the next placement.
            if (ComboStep > 0 && Time.time - _lastPlacementTime > tuning.comboWindow)
            {
                SetCombo(0);
            }
        }

        private void HandlePlaced(PlacementInfo info)
        {
            float now = Time.time;
            bool quick = _hasPlacement && now - _lastPlacementTime <= tuning.comboWindow;
            _lastPlacementTime = now;
            _hasPlacement = true;

            SetCombo(ComboEnabled && quick ? ComboStep + 1 : 0);
            float multiplier = Multiplier;

            BlockDefinition definition = info.Block.Definition;
            int blockPoints = definition.scoreValue;
            int heightPoints = Mathf.Max(0,
                Centimetres(info.MaxHeight) - Centimetres(info.PreviousMaxHeight)) * tuning.pointsPerCentimetre;
            int precisionPoints = info.IsPrecise ? tuning.precisionBonus : 0;
            int bonusPoints = definition.bonusPoints;

            int total = Mathf.RoundToInt((blockPoints + heightPoints + precisionPoints) * multiplier) + bonusPoints;
            Score += total;

            GameEvents.RaiseScoreAwarded(new ScoreAward(total, blockPoints, heightPoints, precisionPoints,
                bonusPoints, multiplier, info.Block.Position));
            GameEvents.RaiseScoreChanged(Score);
        }

        private void HandleLost(StackBlock block)
        {
            SetCombo(0);
        }

        private void HandleWobble(bool wobbling)
        {
            if (wobbling)
            {
                SetCombo(0);
            }
        }

        private void SetCombo(int step)
        {
            int max = tuning.comboMultipliers != null ? tuning.comboMultipliers.Length : 0;
            step = Mathf.Clamp(step, 0, max);
            if (step == ComboStep)
            {
                return;
            }

            ComboStep = step;
            GameEvents.RaiseComboChanged(Multiplier, ComboStep);
        }

        private static int Centimetres(float metres)
        {
            return Mathf.FloorToInt(metres * 100f + 0.0001f);
        }
    }
}
