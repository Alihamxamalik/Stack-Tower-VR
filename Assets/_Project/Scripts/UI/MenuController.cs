using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StackTower
{
    /// <summary>
    /// Main menu: start a run and change the comfort settings. Every control is a plain uGUI button
    /// so it works with a poke, a pinch ray or a controller.
    /// </summary>
    public sealed class MenuController : MonoBehaviour
    {
        [SerializeField] private LevelConfig timedTower;

        [Header("Play")]
        [SerializeField] private Button playButton;
        [SerializeField] private TMP_Text bestText;

        [Header("Settings")]
        [SerializeField] private Button handButton;
        [SerializeField] private TMP_Text handLabel;
        [SerializeField] private Button postureButton;
        [SerializeField] private TMP_Text postureLabel;
        [SerializeField] private Button viewButton;
        [SerializeField] private TMP_Text viewLabel;
        [SerializeField] private Button snapButton;
        [SerializeField] private TMP_Text snapLabel;
        [SerializeField] private Button volumeDownButton;
        [SerializeField] private Button volumeUpButton;
        [SerializeField] private TMP_Text volumeLabel;

        private bool _loading;

        private void OnEnable()
        {
            Bind(playButton, Play);
            Bind(handButton, ToggleHand);
            Bind(postureButton, TogglePosture);
            Bind(viewButton, ToggleView);
            Bind(snapButton, CycleSnap);
            Bind(volumeDownButton, VolumeDown);
            Bind(volumeUpButton, VolumeUp);
            Refresh();
        }

        private void OnDisable()
        {
            Unbind(playButton, Play);
            Unbind(handButton, ToggleHand);
            Unbind(postureButton, TogglePosture);
            Unbind(viewButton, ToggleView);
            Unbind(snapButton, CycleSnap);
            Unbind(volumeDownButton, VolumeDown);
            Unbind(volumeUpButton, VolumeUp);
        }

        private void Play()
        {
            if (_loading)
            {
                return;
            }

            _loading = true;
            SceneFlow.LoadGame(timedTower);
        }

        private void ToggleHand()
        {
            SaveService.Settings.leftHanded = !SaveService.Settings.leftHanded;
            Commit();
        }

        private void TogglePosture()
        {
            SaveService.Settings.seated = !SaveService.Settings.seated;
            Commit();
        }

        private void ToggleView()
        {
            SaveService.Settings.mixedReality = !SaveService.Settings.mixedReality;
            Commit();
        }

        private void CycleSnap()
        {
            SettingsData settings = SaveService.Settings;
            settings.rotationSnap = (RotationSnapMode)(((int)settings.rotationSnap + 1) % 3);
            Commit();
        }

        private void VolumeDown()
        {
            SetVolume(SaveService.Settings.volume - 0.1f);
        }

        private void VolumeUp()
        {
            SetVolume(SaveService.Settings.volume + 0.1f);
        }

        private void SetVolume(float volume)
        {
            SaveService.Settings.volume = Mathf.Round(Mathf.Clamp01(volume) * 10f) / 10f;
            AudioListener.volume = SaveService.Settings.volume;
            Commit();
        }

        private void Commit()
        {
            SaveService.NotifySettingsChanged();
            Refresh();
        }

        private void Refresh()
        {
            SettingsData settings = SaveService.Settings;

            SetLabel(handLabel, settings.leftHanded ? "Hand: Left" : "Hand: Right");
            SetLabel(postureLabel, settings.seated ? "Posture: Seated" : "Posture: Standing");
            SetLabel(viewLabel, settings.mixedReality ? "View: Mixed reality" : "View: Virtual room");
            SetLabel(volumeLabel, $"Volume {Mathf.RoundToInt(settings.volume * 100f)}%");

            switch (settings.rotationSnap)
            {
                case RotationSnapMode.Always:
                    SetLabel(snapLabel, "Rotation snap: Always");
                    break;
                case RotationSnapMode.Off:
                    SetLabel(snapLabel, "Rotation snap: Off");
                    break;
                default:
                    SetLabel(snapLabel, "Rotation snap: Learn phase");
                    break;
            }

            if (bestText != null)
            {
                ModeRecord record = SaveService.GetRecord(GameMode.TimedTower);
                bestText.gameObject.SetActive(record.runs > 0);
                if (record.runs > 0)
                {
                    bestText.SetText("Best  {0}   {1:2} m", record.bestScore, record.bestHeight);
                }
            }
        }

        private static void SetLabel(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void Unbind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }
    }
}
