using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace StackTower.EditorTools
{
    /// <summary>
    /// Applies the project settings from the design document (player, physics, URP quality, XR
    /// provider, Meta project config). Every method is safe to run again: it only sets values.
    /// Meta's own Project Setup Tool is run afterwards by <see cref="SetupPipeline"/> and has the
    /// final say on anything the store requires.
    /// </summary>
    public static class ProjectConfigurator
    {
        public const string CompanyName = "Ali Hamza";
        public const string ProductName = "Stack Tower VR";
        public const string PackageName = "com.alihamza.stacktower";
        public const string Version = "0.1.0";

        // 90 Hz physics to match the display, so held and falling blocks don't stutter.
        private const float FixedTimestep = 1f / 90f;
        // Project-wide defaults stay at what Meta's setup tool accepts (8 or fewer position iterations).
        // The blocks themselves ask for 10 / 4 through GameTuning, which is where stacking stability matters.
        private const int SolverIterations = 8;
        private const int SolverVelocityIterations = 4;

        private const string OpenXRLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        private const string MetaFeatureSetId = "com.meta.openxr.featureset.metaxr";

        private static readonly string[] RequiredOpenXRFeatures =
        {
            "com.meta.openxr.feature.metaxr",
            "com.meta.openxr.feature.foveation",
            "com.meta.openxr.feature.subsampledLayout"
        };

        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            NamedBuildTarget android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, PackageName);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetMobileMTRendering(android, true);

            if (PlayerSettings.Android.bundleVersionCode < 1)
            {
                PlayerSettings.Android.bundleVersionCode = 1;
            }

            // Android 10 or higher. Never lower a value something else (Meta's setup tool) has raised.
            // The target API level is left alone: Meta's tool pins it to what the Quest store accepts.
            if ((int)PlayerSettings.Android.minSdkVersion < 29)
            {
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            }

            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        }

        public static void ApplyPhysicsAndTime()
        {
            // The time manager doesn't expose its timestep as a plain float property; the scripting API is
            // the supported way to set it, and outside play mode it changes the project setting.
            Time.fixedDeltaTime = FixedTimestep;
            EditorUtility.SetDirty(LoadProjectSettings("TimeManager.asset"));
            SetProjectSetting("DynamicsManager.asset", "m_DefaultSolverIterations", p => p.intValue = SolverIterations);
            SetProjectSetting("DynamicsManager.asset", "m_DefaultSolverVelocityIterations",
                p => p.intValue = SolverVelocityIterations);
        }

        public static void ApplyQualityAndUrp()
        {
            int level = AndroidQualityLevel();

            var quality = new SerializedObject(LoadProjectSettings("QualitySettings.asset"));
            SerializedProperty levels = quality.FindProperty("m_QualitySettings");
            if (levels != null && level < levels.arraySize)
            {
                SerializedProperty entry = levels.GetArrayElementAtIndex(level);
                SetRelative(entry, "anisotropicTextures", p => p.intValue = 1); // Per Texture
                SetRelative(entry, "globalTextureMipmapLimit", p => p.intValue = 0); // Full resolution
                SetRelative(entry, "shadows", p => p.intValue = 0);
                SetRelative(entry, "pixelLightCount", p => p.intValue = 1);
                SetRelative(entry, "vSyncCount", p => p.intValue = 0);
                quality.ApplyModifiedPropertiesWithoutUndo();
            }

            var urp = QualitySettings.GetRenderPipelineAssetAt(level) as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            }

            if (urp == null)
            {
                Debug.LogWarning("[Stack Tower] No URP asset found for the Android quality level; skipped URP settings.");
                return;
            }

            // MSAA has to be set by hand; nothing turns it on for you.
            urp.msaaSampleCount = 4;
            urp.supportsHDR = false;
            urp.renderScale = 1f;
            urp.supportsCameraDepthTexture = false;
            urp.supportsCameraOpaqueTexture = false;

            // One directional light, no real-time shadows.
            var asset = new SerializedObject(urp);
            SetProperty(asset, "m_MainLightShadowsSupported", p => p.boolValue = false);
            SetProperty(asset, "m_AdditionalLightShadowsSupported", p => p.boolValue = false);
            SetProperty(asset, "m_AdditionalLightsRenderingMode", p => p.intValue = 0);
            SetProperty(asset, "m_SupportsLightCookies", p => p.boolValue = false);
            asset.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
        }

        /// <summary>OpenXR as the XR provider, with the Meta XR feature group, on Android and desktop.</summary>
        public static void ApplyXR()
        {
            foreach (BuildTargetGroup group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                XRGeneralSettings settings = GetOrCreateXRSettings(group);
                if (settings == null || settings.Manager == null)
                {
                    Debug.LogWarning($"[Stack Tower] Could not create XR settings for {group}.");
                    continue;
                }

                if (!HasLoader(settings.Manager, OpenXRLoaderType))
                {
                    XRPackageMetadataStore.AssignLoader(settings.Manager, OpenXRLoaderType, group);
                }

                settings.InitManagerOnStart = true;
                EditorUtility.SetDirty(settings);

                OpenXRFeatureSetManager.FeatureSet featureSet =
                    OpenXRFeatureSetManager.GetFeatureSetWithId(group, MetaFeatureSetId);
                if (featureSet != null && !featureSet.isEnabled)
                {
                    featureSet.isEnabled = true;
                    OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(group);
                }

                foreach (string featureId in RequiredOpenXRFeatures)
                {
                    OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, featureId);
                    if (feature != null && !feature.enabled)
                    {
                        feature.enabled = true;
                        EditorUtility.SetDirty(feature);
                    }
                }

                OpenXRSettings openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (openXR != null && group == BuildTargetGroup.Android)
                {
                    // Multiview: both eyes in one pass.
                    openXR.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                    EditorUtility.SetDirty(openXR);
                }
            }
        }

        /// <summary>The settings that end up in the Android manifest via the Meta XR SDK.</summary>
        public static void ApplyMetaProjectConfig()
        {
            OVRProjectConfig config = OVRProjectConfig.CachedProjectConfig;
            if (config == null)
            {
                Debug.LogWarning("[Stack Tower] OVRProjectConfig isn't available yet; skipped Meta project config.");
                return;
            }

            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            config.handTrackingFrequency = OVRProjectConfig.HandTrackingFrequency.HIGH;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.boundaryVisibilitySupport = OVRProjectConfig.FeatureSupport.Supported;

            // The headsets this is built and tuned for. Listing them makes the headset report its real model
            // instead of running in compatibility mode. Add Quest 2 / Pro here if you start testing on them.
            config.targetDeviceTypes = new List<OVRProjectConfig.DeviceType>
            {
                OVRProjectConfig.DeviceType.Quest3,
                OVRProjectConfig.DeviceType.Quest3S
            };

            OVRProjectConfig.CommitProjectConfig(config);
        }

        public static void ApplyAll()
        {
            ApplyPlayerSettings();
            ApplyPhysicsAndTime();
            ApplyQualityAndUrp();
            ApplyXR();
            ApplyMetaProjectConfig();
            AssetDatabase.SaveAssets();
        }

        // Index of the quality level Android builds use by default.
        private static int AndroidQualityLevel()
        {
            var quality = new SerializedObject(LoadProjectSettings("QualitySettings.asset"));
            SerializedProperty perPlatform = quality.FindProperty("m_PerPlatformDefaultQuality");
            if (perPlatform != null)
            {
                for (int i = 0; i < perPlatform.arraySize; i++)
                {
                    SerializedProperty pair = perPlatform.GetArrayElementAtIndex(i);
                    SerializedProperty key = pair.FindPropertyRelative("first");
                    SerializedProperty value = pair.FindPropertyRelative("second");
                    if (key != null && value != null && key.stringValue == "Android")
                    {
                        return value.intValue;
                    }
                }
            }

            return QualitySettings.GetQualityLevel();
        }

        // XR Plug-in Management keeps this creation helper internal, so do what it does.
        private static XRGeneralSettings GetOrCreateXRSettings(BuildTargetGroup group)
        {
            XRGeneralSettings settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            if (settings != null && settings.Manager != null)
            {
                return settings;
            }

            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget perBuildTarget);
            if (perBuildTarget == null)
            {
                perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                if (!AssetDatabase.IsValidFolder("Assets/XR"))
                {
                    AssetDatabase.CreateFolder("Assets", "XR");
                }

                AssetDatabase.CreateAsset(perBuildTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                AssetDatabase.SaveAssets();
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuildTarget, true);
            }

            if (!perBuildTarget.HasSettingsForBuildTarget(group))
            {
                perBuildTarget.CreateDefaultSettingsForBuildTarget(group);
            }

            if (!perBuildTarget.HasManagerSettingsForBuildTarget(group))
            {
                perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            }

            EditorUtility.SetDirty(perBuildTarget);
            return XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
        }

        private static bool HasLoader(XRManagerSettings manager, string loaderTypeName)
        {
            foreach (XRLoader loader in manager.activeLoaders)
            {
                if (loader != null && loader.GetType().FullName == loaderTypeName)
                {
                    return true;
                }
            }

            return false;
        }

        private static Object LoadProjectSettings(string file)
        {
            return AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/" + file)[0];
        }

        private static void SetProjectSetting(string file, string property, System.Action<SerializedProperty> set)
        {
            var settings = new SerializedObject(LoadProjectSettings(file));
            SetProperty(settings, property, set);
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetProperty(SerializedObject target, string property, System.Action<SerializedProperty> set)
        {
            SerializedProperty found = target.FindProperty(property);
            if (found == null)
            {
                Debug.LogWarning($"[Stack Tower] Setting '{property}' not found on {target.targetObject.name}; skipped.");
                return;
            }

            set(found);
        }

        private static void SetRelative(SerializedProperty parent, string property, System.Action<SerializedProperty> set)
        {
            SerializedProperty found = parent.FindPropertyRelative(property);
            if (found != null)
            {
                set(found);
            }
        }
    }
}
