using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// The small puff when a block settles and the gold burst for a precise or golden placement.
    /// Two particle systems emit on demand at the placement point, so nothing is spawned per effect.
    /// </summary>
    public sealed class PlacementEffects : MonoBehaviour
    {
        [SerializeField] private ParticleSystem puff;
        [SerializeField] private ParticleSystem goldBurst;
        [SerializeField] private int puffCount = 10;
        [SerializeField] private int goldCount = 18;

        private void OnEnable()
        {
            GameEvents.BlockPlaced += HandlePlaced;
        }

        private void OnDisable()
        {
            GameEvents.BlockPlaced -= HandlePlaced;
        }

        private void HandlePlaced(PlacementInfo info)
        {
            StackBlock block = info.Block;
            Vector3 centre = block.Position;

            // The puff comes out from under the block, where it touched down.
            Emit(puff, new Vector3(centre.x, block.BottomY, centre.z), puffCount);

            if (info.IsPrecise || block.Definition.isGolden)
            {
                Emit(goldBurst, centre, goldCount);
            }
        }

        private static void Emit(ParticleSystem system, Vector3 position, int count)
        {
            if (system == null || count <= 0)
            {
                return;
            }

            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                applyShapeToPosition = true
            };
            system.Emit(emit, count);
        }
    }
}
