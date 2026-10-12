using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StackTower.EditorTools
{
    /// <summary>
    /// One-click project setup, in the order the design document lists it:
    /// switch to Android, import TextMeshPro's resources, apply project settings, run Meta's Project
    /// Setup Tool ("Fix All" and "Apply All"), then build the data assets, block prefabs and scenes.
    ///
    /// Several of those steps make Unity recompile and reload scripts, which wipes static state. The
    /// current step is therefore kept in SessionState and the pipeline picks itself up again after
    /// each reload. Every step is safe to repeat.
    /// </summary>
    public static class SetupPipeline
    {
        private const string StepKey = "StackTower.Setup.Step";
        private const string OverwriteKey = "StackTower.Setup.Overwrite";
        private const string FixPassesKey = "StackTower.Setup.FixPasses";
        private const string TmpRequestedKey = "StackTower.Setup.TmpRequested";
        private const string OfferedKey = "StackTower.Setup.Offered";
        private const string TmpPackage = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
        private const string TmpSettings = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        private enum Step
        {
            Idle = 0,
            SwitchPlatform = 1,
            ImportTextMeshPro = 2,
            ApplySettings = 3,
            MetaFixAll = 4,
            BuildContent = 5,
            Finish = 6
        }

        private static MetaProjectSetup.Run _metaFix;
        private static double _stepStartedAt;

        private static Step Current
        {
            get => (Step)SessionState.GetInt(StepKey, (int)Step.Idle);
            set
            {
                SessionState.SetInt(StepKey, (int)value);
                _stepStartedAt = EditorApplication.timeSinceStartup;
            }
        }

        [MenuItem("Stack Tower/Setup/Run Full Setup", priority = 0)]
        public static void RunFromMenu()
        {
            if (!CanTouchScenes())
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("Stack Tower VR setup",
                    "This will switch the project to Android, apply the project settings from the design document, " +
                    "run Meta's Project Setup Tool, and create any missing data assets, block prefabs and scenes.\n\n" +
                    "Unity will reimport assets and recompile a few times; it can take several minutes.",
                    "Run setup", "Cancel"))
            {
                return;
            }

            Begin(overwrite: false);
        }

        [MenuItem("Stack Tower/Setup/Apply Project Settings Only", priority = 20)]
        public static void ApplySettingsFromMenu()
        {
            ProjectConfigurator.ApplyAll();
            Debug.Log("[Stack Tower] Project settings applied.");
        }

        [MenuItem("Stack Tower/Setup/Rebuild Scenes and Block Prefabs (overwrites)", priority = 21)]
        public static void RebuildFromMenu()
        {
            if (!EditorUtility.DisplayDialog("Rebuild scenes and prefabs",
                    "Bootstrap, Menu and Game scenes and the seven block prefabs will be recreated from code. " +
                    "Manual changes to them will be lost. Data assets (tuning, block definitions, levels, " +
                    "materials) are kept.",
                    "Rebuild", "Cancel"))
            {
                return;
            }

            if (!CanTouchScenes())
            {
                return;
            }

            BuildContent(overwrite: true);
        }

        // The builders open and replace scenes, so leave play mode and deal with unsaved work first.
        private static bool CanTouchScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Stack Tower VR", "Exit play mode first.", "OK");
                return false;
            }

            return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        /// <summary>Command line entry point: Unity -batchmode -executeMethod StackTower.EditorTools.SetupPipeline.RunBatch</summary>
        public static void RunBatch()
        {
            Begin(overwrite: true);
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            if (Current != Step.Idle)
            {
                _stepStartedAt = EditorApplication.timeSinceStartup;
                EditorApplication.delayCall += Advance;
            }
            else if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += OfferFirstRun;
            }
        }

        // A project that has the scripts but no scenes yet hasn't been set up. Offer once per editor session.
        private static void OfferFirstRun()
        {
            if (Current != Step.Idle || File.Exists(SceneBuilder.GamePath) || SessionState.GetBool(OfferedKey, false))
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                                              || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += OfferFirstRun;
                return;
            }

            SessionState.SetBool(OfferedKey, true);
            if (EditorUtility.DisplayDialog("Stack Tower VR",
                    "This project hasn't been set up yet: the scenes and block prefabs don't exist.\n\n" +
                    "Setup switches to Android, applies the project settings, runs Meta's Project Setup Tool and " +
                    "builds the scenes. It takes a few minutes.\n\n" +
                    "You can also start it later from Stack Tower > Setup > Run Full Setup.",
                    "Run setup", "Not now") && CanTouchScenes())
            {
                Begin(overwrite: false);
            }
        }

        private static void Begin(bool overwrite)
        {
            SessionState.SetBool(OverwriteKey, overwrite);
            SessionState.SetInt(FixPassesKey, 0);
            SessionState.EraseBool(TmpRequestedKey);
            _metaFix = null;
            Current = Step.SwitchPlatform;
            Advance();
        }

        // Runs steps until one has to wait (for a compile, an import or an async fix), then reschedules itself.
        private static void Advance()
        {
            try
            {
                while (Current != Step.Idle)
                {
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    {
                        EditorApplication.delayCall += Advance;
                        return;
                    }

                    if (!RunStep(Current))
                    {
                        EditorApplication.delayCall += Advance;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Current = Step.Idle;
                Debug.LogError("[Stack Tower] Setup stopped: " + e);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        /// <returns>False when the step is still waiting and should be polled again.</returns>
        private static bool RunStep(Step step)
        {
            switch (step)
            {
                case Step.SwitchPlatform:
                    Current = Step.ImportTextMeshPro;
                    if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                    {
                        Debug.Log("[Stack Tower] Switching build target to Android...");
                        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android,
                                BuildTarget.Android))
                        {
                            throw new InvalidOperationException(
                                "Could not switch to Android. Is the Android Build Support module installed?");
                        }
                    }

                    return true;

                case Step.ImportTextMeshPro:
                    if (File.Exists(TmpSettings))
                    {
                        Current = Step.ApplySettings;
                        return true;
                    }

                    if (!SessionState.GetBool(TmpRequestedKey, false))
                    {
                        SessionState.SetBool(TmpRequestedKey, true);
                        Debug.Log("[Stack Tower] Importing TextMeshPro essential resources...");
                        AssetDatabase.ImportPackage(TmpPackage, false);
                    }
                    else if (WaitedLongerThan(120))
                    {
                        throw new TimeoutException("TextMeshPro resources did not finish importing. Import them from " +
                                                   "Window > TextMeshPro > Import TMP Essential Resources and run setup again.");
                    }

                    return false;

                case Step.ApplySettings:
                    Debug.Log("[Stack Tower] Applying project settings...");
                    ProjectConfigurator.ApplyAll();
                    Current = Step.MetaFixAll;
                    return true;

                case Step.MetaFixAll:
                    if (_metaFix != null && !_metaFix.Completed && !WaitedLongerThan(180))
                    {
                        return false;
                    }

                    _metaFix = null;

                    // Two passes: a fix can trigger a script reload that cuts the first pass short (the handle
                    // is lost with it), and some fixes only become possible after others have landed.
                    int passes = SessionState.GetInt(FixPassesKey, 0);
                    if (passes < 2)
                    {
                        SessionState.SetInt(FixPassesKey, passes + 1);
                        Debug.Log($"[Stack Tower] Meta Project Setup Tool: Fix All + Apply All (Android), pass {passes + 1}...");
                        _metaFix = MetaProjectSetup.FixRequiredAndRecommended(BuildTargetGroup.Android);
                        _stepStartedAt = EditorApplication.timeSinceStartup;
                        if (_metaFix != null)
                        {
                            return false;
                        }

                        SessionState.SetInt(FixPassesKey, 2);
                        Debug.LogWarning("[Stack Tower] Meta's Project Setup Tool could not be run automatically with " +
                                         "this SDK version. Open Edit > Project Settings > Meta XR and press Fix All, " +
                                         "then Apply All, on the Android tab.");
                    }

                    // Keeping the manifest in step with the settings is only an optional item in Meta's tool, so
                    // the passes above don't do it. A stale manifest would list the wrong headsets.
                    if (OVRManifestPreprocessor.IsManifestOutdated(out _))
                    {
                        OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(true);
                    }

                    AssetDatabase.SaveAssets();
                    Current = Step.BuildContent;
                    return true;

                case Step.BuildContent:
                    BuildContent(SessionState.GetBool(OverwriteKey, false));
                    Current = Step.Finish;
                    return true;

                case Step.Finish:
                    Current = Step.Idle;
                    SessionState.EraseBool(TmpRequestedKey);
                    SessionState.EraseInt(FixPassesKey);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[Stack Tower] Setup finished. Open Assets/_Project/Scenes/Game.unity and press Play, " +
                              "or File > Build And Run with the headset connected.");
                    if (Application.isBatchMode)
                    {
                        EditorApplication.Exit(0);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Stack Tower VR", "Setup finished.\n\nThe Game scene is open: press " +
                            "Play to try it with mouse and keyboard, or use Build And Run with the headset connected.\n\n" +
                            "Meta's Project Setup Tool is under Edit > Project Settings > Meta XR if you want to review it.",
                            "OK");
                    }

                    return true;

                default:
                    Current = Step.Idle;
                    return true;
            }
        }

        private static void BuildContent(bool overwrite)
        {
            Debug.Log("[Stack Tower] Building data assets, block prefabs and scenes...");
            BlockAssetBuilder.Build(rebuildPrefabs: overwrite);
            SceneBuilder.BuildAll(overwrite);
            AssetDatabase.SaveAssets();
        }

        private static bool WaitedLongerThan(double seconds)
        {
            return EditorApplication.timeSinceStartup - _stepStartedAt > seconds;
        }
    }
}
