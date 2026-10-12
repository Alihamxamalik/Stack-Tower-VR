using TMPro;
using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// The "+15" that pops off a block when it is placed. A small fixed pool of world-space labels,
    /// reused round-robin; no allocation per popup.
    /// </summary>
    public sealed class RewardPopups : MonoBehaviour
    {
        [SerializeField] private TMP_Text[] labels;
        [SerializeField] private float lifetime = 0.9f;
        [SerializeField] private float rise = 0.14f;
        [Tooltip("How far above the block's centre the label starts (m).")]
        [SerializeField] private float startHeight = 0.14f;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color preciseColor = new Color(0.5f, 1f, 0.65f, 1f);
        [SerializeField] private Color goldenColor = new Color(1f, 0.85f, 0.3f, 1f);

        private float[] _ages;
        private Vector3[] _origins;
        private int _next;
        private Transform _head;

        private void Awake()
        {
            int count = labels != null ? labels.Length : 0;
            _ages = new float[count];
            _origins = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                _ages[i] = -1f;
                if (labels[i] != null)
                {
                    labels[i].gameObject.SetActive(false);
                }
            }
        }

        private void OnEnable()
        {
            GameEvents.ScoreAwarded += Show;
        }

        private void OnDisable()
        {
            GameEvents.ScoreAwarded -= Show;
        }

        private void Show(ScoreAward award)
        {
            if (labels == null || labels.Length == 0)
            {
                return;
            }

            int index = _next;
            _next = (_next + 1) % labels.Length;
            TMP_Text label = labels[index];
            if (label == null)
            {
                return;
            }

            if (award.PrecisionPoints > 0)
            {
                label.SetText("+{0}  Precise!", award.Total);
            }
            else
            {
                label.SetText("+{0}", award.Total);
            }

            label.color = award.BonusPoints > 0 ? goldenColor : award.PrecisionPoints > 0 ? preciseColor : normalColor;

            _origins[index] = award.WorldPosition + Vector3.up * startHeight;
            _ages[index] = 0f;

            Transform labelTransform = label.transform;
            labelTransform.position = _origins[index];
            FaceHead(labelTransform);
            label.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (labels == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            for (int i = 0; i < labels.Length; i++)
            {
                if (_ages[i] < 0f)
                {
                    continue;
                }

                _ages[i] += dt;
                TMP_Text label = labels[i];
                float t = Mathf.Clamp01(_ages[i] / lifetime);

                if (t >= 1f)
                {
                    _ages[i] = -1f;
                    label.gameObject.SetActive(false);
                    continue;
                }

                // Rise fast then slow; stay solid for the first half, fade over the second.
                float eased = 1f - (1f - t) * (1f - t);
                label.transform.position = _origins[i] + Vector3.up * (rise * eased);
                label.alpha = 1f - Mathf.Clamp01((t - 0.5f) * 2f);
            }
        }

        private void FaceHead(Transform label)
        {
            if (_head == null)
            {
                Camera main = Camera.main;
                _head = main != null ? main.transform : null;
            }

            if (_head == null)
            {
                return;
            }

            Vector3 away = label.position - _head.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
            {
                label.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            }
        }
    }
}
