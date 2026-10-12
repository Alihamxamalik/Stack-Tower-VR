using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// Every number worth tuning lives here, so feel can be adjusted in the inspector (even in play
    /// mode) without touching code.
    /// </summary>
    [CreateAssetMenu(fileName = "GameTuning", menuName = "Stack Tower/Game Tuning")]
    public sealed class GameTuning : ScriptableObject
    {
        [Header("Settling")]
        [Tooltip("A released block counts as settled once it stays slower than this (m/s)...")]
        public float settleSpeed = 0.05f;
        [Tooltip("...and rotates slower than this (deg/s)...")]
        public float settleAngularSpeed = 12f;
        [Tooltip("...for this long (s).")]
        public float settleTime = 0.5f;

        [Header("Release")]
        [Tooltip("Fraction of the hand's velocity a block keeps on release. Low values stop careless releases flinging it.")]
        [Range(0f, 1f)] public float releaseVelocityScale = 0.3f;
        [Tooltip("Cap on the release speed after scaling (m/s).")]
        public float maxReleaseSpeed = 1.5f;
        [Tooltip("Cap on the release spin after scaling (deg/s).")]
        public float maxReleaseAngularSpeed = 180f;
        [Tooltip("Limits how hard physics pushes overlapping blocks apart (m/s). Keeps a block released inside the tower from exploding it.")]
        public float maxDepenetrationSpeed = 1.5f;

        [Header("Solver (per block)")]
        [Tooltip("Position iterations for blocks. Set on the blocks rather than project-wide, so stacks get the extra stability without raising the cost of everything else. Raise only if towers jitter.")]
        public int solverIterations = 10;
        [Tooltip("Velocity iterations for blocks.")]
        public int solverVelocityIterations = 4;

        [Header("Soft alignment")]
        public bool softAlignment = true;
        [Tooltip("Pull into alignment when released within this distance of the centre of the block below (m).")]
        public float alignRadius = 0.03f;
        [Tooltip("...and within this angle of its rotation (deg).")]
        public float alignAngle = 10f;
        [Tooltip("How long the pull takes (s).")]
        public float alignDuration = 0.12f;
        [Tooltip("If the block is released this close above its resting place, it is set down as well as aligned (m).")]
        public float alignSnapDownDistance = 0.06f;
        [Tooltip("Also centre the first block on the pedestal.")]
        public bool alignToPedestal = true;

        [Header("Settle damping")]
        [Tooltip("Extra angular damping right after release so tiny tracking errors don't topple the block.")]
        public float settleAngularDamping = 12f;
        public float settleDampingDuration = 0.3f;

        [Header("Lost tracking")]
        [Tooltip("If the hand is lost while holding a block, keep the block frozen in place this long before dropping it gently (s).")]
        public float trackingLostHoldTime = 0.5f;

        [Header("Rotation snap")]
        [Tooltip("Step for rotation snapping while held (deg).")]
        public float rotationSnapStep = 15f;

        [Header("Ghost preview")]
        [Tooltip("How far below the held block to look for the tower (m).")]
        public float ghostMaxDrop = 2.5f;
        public Color ghostColor = new Color(1f, 1f, 1f, 0.28f);
        public Color ghostAlignedColor = new Color(0.45f, 1f, 0.6f, 0.4f);

        [Header("Scoring")]
        [Tooltip("Points per centimetre of new max height.")]
        public int pointsPerCentimetre = 1;
        [Tooltip("Bonus when a block lands within Precision Radius of the centre of the block below.")]
        public int precisionBonus = 5;
        public float precisionRadius = 0.02f;
        [Tooltip("Placing within this long of the previous block raises the combo (s).")]
        public float comboWindow = 3f;
        public float[] comboMultipliers = { 1.5f, 2f, 3f };
        [Tooltip("Points per remaining second when a run ends by reaching its target height.")]
        public int timeBonusPerSecond = 5;

        [Header("Tower")]
        [Tooltip("The tower counts as wobbling when its top moves sideways faster than this (m/s)...")]
        public float wobbleSpeed = 0.1f;
        [Tooltip("...for at least this long (s).")]
        public float wobbleMinDuration = 0.2f;
        [Tooltip("Wobble ends after the tower has been calm this long (s).")]
        public float wobbleCalmTime = 0.4f;
        public Color wobbleTint = new Color(1f, 0.55f, 0.15f, 1f);
        [Tooltip("The run fails when the tower top drops by more than this fraction of its max height.")]
        [Range(0.05f, 1f)] public float failTopDropFraction = 0.5f;
        [Tooltip("The top-drop rule only applies once the tower has been at least this tall (m).")]
        public float failMinHeight = 0.25f;
        [Tooltip("A block has fallen once its centre is this far below the pedestal surface (m).")]
        public float fallMargin = 0.03f;
        [Tooltip("How long a dropped, never-placed block stays visible before it is recycled (s).")]
        public float lostBlockLinger = 0.7f;

        [Header("Freezing deep blocks")]
        [Tooltip("Settled blocks more than this many layers below the top become kinematic.")]
        public int freezeLayers = 5;
        [Tooltip("An impact faster than this unfreezes the whole tower (m/s).")]
        public float bigHitSpeed = 2.5f;
        [Tooltip("After a big hit, wait this long before freezing again (s).")]
        public float refreezeDelay = 1.5f;

        public float SettleAngularSpeedRad => settleAngularSpeed * Mathf.Deg2Rad;
        public float MaxReleaseAngularSpeedRad => maxReleaseAngularSpeed * Mathf.Deg2Rad;

        public float ComboMultiplier(int step)
        {
            if (step <= 0 || comboMultipliers == null || comboMultipliers.Length == 0)
            {
                return 1f;
            }

            return comboMultipliers[Mathf.Min(step, comboMultipliers.Length) - 1];
        }
    }
}
