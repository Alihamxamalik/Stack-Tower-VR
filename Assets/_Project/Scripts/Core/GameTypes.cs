using UnityEngine;

namespace StackTower
{
    public enum GameMode
    {
        TimedTower = 0,
        TargetHeight = 1,
        Zen = 2,
        DailyChallenge = 3
    }

    /// <summary>Run state machine owned by <see cref="GameManager"/>.</summary>
    public enum GameState
    {
        Idle,
        Intro,
        Playing,
        FinalRush,
        Ended
    }

    /// <summary>Pacing phases inside one run (hook, learn, ramp, final rush).</summary>
    public enum RunPhase
    {
        Hook,
        Learn,
        Ramp,
        FinalRush
    }

    public enum RunEndReason
    {
        TimeUp,
        TowerFell,
        TargetReached
    }

    public enum InputMode
    {
        Hands,
        Controllers
    }

    public enum DominantHand
    {
        Right,
        Left
    }

    public enum RotationSnapMode
    {
        /// <summary>Snap while the player is learning, free rotation afterwards.</summary>
        LearnPhaseOnly = 0,
        Always = 1,
        Off = 2
    }

    public enum BlockShape
    {
        Box,
        Wedge,
        Cylinder
    }

    public enum HoldEndReason
    {
        Released,
        TrackingLost
    }

    /// <summary>Everything the rest of the game needs to know about one counted placement.</summary>
    public readonly struct PlacementInfo
    {
        public readonly StackBlock Block;
        /// <summary>Block directly underneath, or null when resting on the pedestal.</summary>
        public readonly StackBlock Below;
        /// <summary>Horizontal distance between this block's centre and its support's centre, in metres.</summary>
        public readonly float CentreOffset;
        public readonly bool IsPrecise;
        public readonly float PreviousMaxHeight;
        public readonly float MaxHeight;
        public readonly float CurrentHeight;

        public PlacementInfo(StackBlock block, StackBlock below, float centreOffset, bool isPrecise,
            float previousMaxHeight, float maxHeight, float currentHeight)
        {
            Block = block;
            Below = below;
            CentreOffset = centreOffset;
            IsPrecise = isPrecise;
            PreviousMaxHeight = previousMaxHeight;
            MaxHeight = maxHeight;
            CurrentHeight = currentHeight;
        }
    }

    /// <summary>Points handed out for one placement, already multiplied.</summary>
    public readonly struct ScoreAward
    {
        public readonly int Total;
        public readonly int BlockPoints;
        public readonly int HeightPoints;
        public readonly int PrecisionPoints;
        public readonly int BonusPoints;
        public readonly float Multiplier;
        public readonly Vector3 WorldPosition;

        public ScoreAward(int total, int blockPoints, int heightPoints, int precisionPoints, int bonusPoints,
            float multiplier, Vector3 worldPosition)
        {
            Total = total;
            BlockPoints = blockPoints;
            HeightPoints = heightPoints;
            PrecisionPoints = precisionPoints;
            BonusPoints = bonusPoints;
            Multiplier = multiplier;
            WorldPosition = worldPosition;
        }
    }

    public readonly struct RunResult
    {
        public readonly GameMode Mode;
        public readonly RunEndReason Reason;
        public readonly int Score;
        public readonly int TimeBonus;
        /// <summary>Highest tower reached during the run, in metres.</summary>
        public readonly float Height;
        public readonly int BlocksPlaced;
        public readonly int BestScore;
        public readonly float BestHeight;
        public readonly bool IsNewBestScore;
        public readonly bool IsNewBestHeight;

        public RunResult(GameMode mode, RunEndReason reason, int score, int timeBonus, float height, int blocksPlaced,
            int bestScore, float bestHeight, bool isNewBestScore, bool isNewBestHeight)
        {
            Mode = mode;
            Reason = reason;
            Score = score;
            TimeBonus = timeBonus;
            Height = height;
            BlocksPlaced = blocksPlaced;
            BestScore = bestScore;
            BestHeight = bestHeight;
            IsNewBestScore = isNewBestScore;
            IsNewBestHeight = isNewBestHeight;
        }
    }
}
