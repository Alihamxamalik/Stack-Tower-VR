using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// Shows its content only in the fully virtual room. In mixed reality the real room replaces it.
    /// Put this on an always-active parent and assign the set dressing as <see cref="content"/>.
    /// </summary>
    public sealed class VirtualEnvironment : MonoBehaviour
    {
        [SerializeField] private GameObject content;

        private void OnEnable()
        {
            MixedReality.Changed += Apply;
            Apply(MixedReality.PassthroughActive);
        }

        private void OnDisable()
        {
            MixedReality.Changed -= Apply;
        }

        private void Apply(bool passthroughActive)
        {
            if (content != null)
            {
                content.SetActive(!passthroughActive);
            }
        }
    }
}
