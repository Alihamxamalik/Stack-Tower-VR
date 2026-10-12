using TMPro;
using Unity.Profiling;
using UnityEngine;

namespace StackTower
{
    /// <summary>
    /// In-headset frame rate, frame time and draw calls. Development builds and the editor only;
    /// it switches itself off everywhere else.
    /// </summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;
        [Tooltip("Where the readout sits relative to the head (m).")]
        [SerializeField] private Vector3 headOffset = new Vector3(-0.17f, -0.15f, 0.6f);
        [SerializeField] private float refreshInterval = 0.25f;

        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _batches;
        private ProfilerRecorder _triangles;
        private Transform _head;
        private float _elapsed;
        private float _worstFrame;
        private int _frames;

        private void Awake()
        {
            if (!Debug.isDebugBuild)
            {
                gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }

        private void OnDisable()
        {
            _drawCalls.Dispose();
            _batches.Dispose();
            _triangles.Dispose();
        }

        private void Update()
        {
            if (_head == null)
            {
                Camera main = Camera.main;
                if (main == null)
                {
                    return;
                }

                // Parented to the head so it inherits the headset's own late pose update and stays steady.
                _head = main.transform;
                transform.SetParent(_head, false);
                transform.localPosition = headOffset;
                transform.localRotation = Quaternion.identity;
            }

            float frame = Time.unscaledDeltaTime;
            _elapsed += frame;
            _frames++;
            if (frame > _worstFrame)
            {
                _worstFrame = frame;
            }

            if (_elapsed < refreshInterval || text == null)
            {
                return;
            }

            float fps = _frames / _elapsed;
            text.SetText("{0:0} fps  {1:1} ms (worst {2:1})\n{3:0} draws  {4:0} batches  {5:0}k tris",
                fps,
                1000f / Mathf.Max(fps, 0.01f),
                _worstFrame * 1000f,
                Count(_drawCalls),
                Count(_batches),
                Count(_triangles) / 1000f);

            _elapsed = 0f;
            _worstFrame = 0f;
            _frames = 0;
        }

        private static float Count(ProfilerRecorder recorder)
        {
            return recorder.Valid ? recorder.LastValue : 0f;
        }
    }
}
