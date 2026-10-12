using UnityEngine;

namespace StackTower
{
    /// <summary>One block type: how it looks, how it behaves physically, and what it is worth.</summary>
    [CreateAssetMenu(fileName = "Block", menuName = "Stack Tower/Block Definition")]
    public sealed class BlockDefinition : ScriptableObject
    {
        public string id = "standard";
        public string displayName = "Standard";

        [Header("Shape")]
        public BlockShape shape = BlockShape.Box;
        [Tooltip("Bounding size in metres. Cylinders stand along Y; wedges slope down toward +Z.")]
        public Vector3 size = new Vector3(0.18f, 0.18f, 0.18f);

        [Header("Look")]
        public Mesh mesh;
        public Material material;

        [Header("Physics")]
        public float mass = 1f;
        public PhysicsMaterial physicsMaterial;

        [Header("Scoring")]
        [Tooltip("Points for placing this block. Multiplied by the combo.")]
        public int scoreValue = 10;
        [Tooltip("Flat bonus on top (golden block). Not multiplied.")]
        public int bonusPoints;
        public bool isGolden;

        [Header("Spawning")]
        [Tooltip("Relative chance among the blocks a level allows.")]
        public float spawnWeight = 1f;
        public StackBlock prefab;
        [Tooltip("Instances created up front so nothing is instantiated mid-run.")]
        public int poolSize = 8;

        public Vector3 HalfSize => size * 0.5f;
    }
}
