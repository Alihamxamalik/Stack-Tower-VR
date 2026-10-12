using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// The glowing ring around the tower at your best height. It starts at the saved best for this
    /// mode and climbs with you once you pass it.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class HeightMarker : MonoBehaviour
    {
        private const int Segments = 48;

        [SerializeField] private GameManager game;
        [SerializeField] private Pedestal pedestal;
        [SerializeField] private float radius = 0.36f;
        [Tooltip("Don't show a ring for a best lower than this (m).")]
        [SerializeField] private float minimumHeight = 0.1f;
        [SerializeField] private Color bestColor = new Color(0.45f, 0.85f, 1f, 0.9f);
        [SerializeField] private Color beatenColor = new Color(1f, 0.85f, 0.3f, 1f);

        private LineRenderer _line;
        private float _savedBest;
        private float _shown;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = false;
            _line.loop = true;
            _line.positionCount = Segments;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                _line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            _line.enabled = false;
        }

        private void OnEnable()
        {
            GameEvents.StateChanged += HandleState;
            GameEvents.HeightChanged += HandleHeight;
        }

        private void OnDisable()
        {
            GameEvents.StateChanged -= HandleState;
            GameEvents.HeightChanged -= HandleHeight;
        }

        private void HandleState(GameState state)
        {
            if (state != GameState.Intro || game == null || game.Level == null)
            {
                return;
            }

            // The stage has just been calibrated, so the pedestal is where it will stay.
            _savedBest = SaveService.GetRecord(game.Level.mode).bestHeight;
            SetColor(bestColor);
            MoveTo(_savedBest);
        }

        private void HandleHeight(float current, float max)
        {
            if (max > _shown && max >= minimumHeight)
            {
                if (max > _savedBest)
                {
                    SetColor(beatenColor);
                }

                MoveTo(max);
            }
        }

        private void MoveTo(float height)
        {
            _shown = height;
            _line.enabled = height >= minimumHeight && pedestal != null;
            if (!_line.enabled)
            {
                return;
            }

            Vector3 centre = pedestal.SurfaceCentre;
            transform.SetPositionAndRotation(new Vector3(centre.x, centre.y + height, centre.z), Quaternion.identity);
        }

        private void SetColor(Color color)
        {
            _line.startColor = color;
            _line.endColor = color;
        }
    }
}
