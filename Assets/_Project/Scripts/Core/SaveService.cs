using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace StackTower
{
    [Serializable]
    public sealed class SettingsData
    {
        public bool leftHanded;
        public bool seated;
        public bool mixedReality = true;
        public RotationSnapMode rotationSnap = RotationSnapMode.LearnPhaseOnly;
        [Range(0f, 1f)] public float volume = 1f;

        public DominantHand DominantHand => leftHanded ? DominantHand.Left : DominantHand.Right;
    }

    [Serializable]
    public sealed class ModeRecord
    {
        public GameMode mode;
        public int bestScore;
        public float bestHeight;
        public int runs;
    }

    [Serializable]
    public sealed class SaveData
    {
        public int version = 1;
        public SettingsData settings = new SettingsData();
        public List<ModeRecord> records = new List<ModeRecord>();
        /// <summary>Sum of every run's height, in metres. Drives cosmetic unlocks.</summary>
        public float totalHeightStacked;
        public List<string> unlockedSkins = new List<string>();
    }

    /// <summary>
    /// Best scores, unlocks and settings as one JSON file in Application.persistentDataPath.
    /// </summary>
    public static class SaveService
    {
        private const string FileName = "stacktower-save.json";

        private static SaveData _data;
        private static string _pathOverride;

        public static event Action SettingsChanged;

        public static SaveData Data => _data ??= Load();
        public static SettingsData Settings => Data.settings;

        private static string FilePath => _pathOverride ?? Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>
        /// Points the service at another file and forgets what was loaded. For automated tests, so they
        /// never read or overwrite a real save. Pass null to go back to the normal location.
        /// </summary>
        public static void UseFile(string path)
        {
            _pathOverride = path;
            _data = null;
        }

        public static SaveData Load()
        {
            SaveData loaded = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    loaded = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveService] Could not read {FilePath}, starting fresh. {e.Message}");
            }

            _data = loaded ?? new SaveData();
            _data.settings ??= new SettingsData();
            _data.records ??= new List<ModeRecord>();
            _data.unlockedSkins ??= new List<string>();
            return _data;
        }

        public static void Save()
        {
            try
            {
                // Write beside the real file first so a crash mid-write can't corrupt the save.
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(Data, true));
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }

                File.Move(temp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveService] Could not write {FilePath}. {e.Message}");
            }
        }

        public static ModeRecord GetRecord(GameMode mode)
        {
            List<ModeRecord> records = Data.records;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].mode == mode)
                {
                    return records[i];
                }
            }

            var record = new ModeRecord { mode = mode };
            records.Add(record);
            return record;
        }

        /// <summary>Records a finished run and saves. Reports which personal bests it beat.</summary>
        public static void SubmitRun(GameMode mode, int score, float height, out bool newBestScore,
            out bool newBestHeight)
        {
            ModeRecord record = GetRecord(mode);
            // A first run always sets the record; only call it a "new best" once there was one to beat.
            bool hadRecord = record.runs > 0;
            newBestScore = score > record.bestScore;
            newBestHeight = height > record.bestHeight;

            if (newBestScore)
            {
                record.bestScore = score;
            }

            if (newBestHeight)
            {
                record.bestHeight = height;
            }

            newBestScore &= hadRecord;
            newBestHeight &= hadRecord;
            record.runs++;
            Data.totalHeightStacked += Mathf.Max(0f, height);
            Save();
        }

        /// <summary>Call after changing anything in <see cref="Settings"/>.</summary>
        public static void NotifySettingsChanged()
        {
            Save();
            SettingsChanged?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _data = null;
            _pathOverride = null;
            SettingsChanged = null;
        }
    }
}
