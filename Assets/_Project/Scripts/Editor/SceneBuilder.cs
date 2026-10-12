using System.Collections.Generic;
using System.IO;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.OVR.Editor.QuickActions;
using StackTower.XR;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static StackTower.EditorTools.BuilderUtil;

namespace StackTower.EditorTools
{
    /// <summary>
    /// Builds the three scenes from code so they can be recreated at any time:
    ///   Bootstrap  camera rig, interaction rig, passthrough and app services (kept alive all session)
    ///   Menu       main menu panel
    ///   Game       pedestal, spawn shelf, HUD / end card and the gameplay systems
    /// </summary>
    internal static class SceneBuilder
    {
        public const string ScenesDir = Root + "/Scenes";
        public const string BootstrapPath = ScenesDir + "/Bootstrap.unity";
        public const string MenuPath = ScenesDir + "/Menu.unity";
        public const string GamePath = ScenesDir + "/Game.unity";

        private const string SfxDir = Root + "/Audio/SFX";

        private static readonly Color PanelColor = new Color(0.09f, 0.11f, 0.15f, 0.9f);
        private static readonly Color TextColor = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color LabelColor = new Color(0.62f, 0.68f, 0.78f, 1f);
        private static readonly Color GoldColor = new Color(1f, 0.85f, 0.3f, 1f);
        private static readonly Color SkyColor = new Color(0.69f, 0.82f, 0.93f, 1f);

        // Parts of the SDK's all-in-one rig this game must not have: the player stays in place.
        private static readonly string[] LocomotionObjects =
        {
            "Locomotor",
            "TeleportMicrogestureInteractor", "TurnerMicrogesture", "StepMicrogesture",
            "ControllerSlideInteractor", "ControllerLocomotionSlideActions",
            "LocomotionTeleportAudio"
        };

        /// <param name="overwrite">Rebuild scenes that already exist. Otherwise only missing ones are created.</param>
        public static void BuildAll(bool overwrite)
        {
            EnsureFolder(ScenesDir);

            if (overwrite || !File.Exists(BootstrapPath))
            {
                BuildBootstrap();
            }

            if (overwrite || !File.Exists(MenuPath))
            {
                BuildMenu();
            }

            if (overwrite || !File.Exists(GamePath))
            {
                BuildGame();
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapPath, true),
                new EditorBuildSettingsScene(MenuPath, true),
                new EditorBuildSettingsScene(GamePath, true)
            };

            EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Single);
        }

        // ---- Bootstrap -------------------------------------------------------------------------

        private static void BuildBootstrap()
        {
            Scene scene = NewScene();

            // The SDK's quick action: OVRCameraRig (floor-level tracking) + the comprehensive interaction
            // rig (hand grab, poke, ray, synthetic hands; controllers take over when picked up).
            OVRQuickActionsAPI.AddOVRInteractionRig(false);
            var cameraRig = Object.FindFirstObjectByType<OVRCameraRig>();
            var manager = Object.FindFirstObjectByType<OVRManager>();
            if (cameraRig == null || manager == null)
            {
                throw new System.InvalidOperationException("The Interaction SDK did not create a camera rig.");
            }

            var persistent = new GameObject("[Persistent]");
            cameraRig.transform.SetParent(persistent.transform, false);

            RemoveLocomotion(cameraRig.transform);

            Transform centerEye = cameraRig.centerEyeAnchor != null
                ? cameraRig.centerEyeAnchor
                : FindDeep(cameraRig.transform, "CenterEyeAnchor");
            Camera eye = centerEye.GetComponent<Camera>();
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = SkyColor;
            // Hands and held blocks come close to the face; the default near plane would clip them.
            eye.nearClipPlane = 0.05f;
            eye.farClipPlane = 60f;
            EditorUtility.SetDirty(eye);

            // Passthrough is switched on at runtime by MRModeController when mixed reality is selected.
            var passthrough = cameraRig.gameObject.AddComponent<OVRPassthroughLayer>();
            manager.isInsightPassthroughEnabled = false;
            EditorUtility.SetDirty(manager);

            // World-space canvases get their poke / ray clicks through this module.
            Child("EventSystem", persistent.transform, typeof(EventSystem), typeof(PointableCanvasModule));

            GameObject light = Child("Directional Light", persistent.transform, typeof(Light),
                typeof(UniversalAdditionalLightData));
            light.transform.rotation = Quaternion.Euler(52f, -32f, 0f);
            var sun = light.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.None;

            GameObject services = Child("Services", persistent.transform, typeof(XRDisplaySetup),
                typeof(InputModeWatcher), typeof(XRHaptics));
            var mrMode = services.AddComponent<MRModeController>();
            using (var wire = new Wire(mrMode))
            {
                wire.Ref("manager", manager).Ref("passthroughLayer", passthrough).Ref("eyeCamera", eye);
            }

            var desktop = services.AddComponent<EditorDesktopControls>();
            using (var wire = new Wire(desktop))
            {
                wire.Ref("rigRoot", cameraRig.transform);
            }

            GameObject overlay = Child("PerfOverlay", persistent.transform);
            TextMeshPro overlayText = WorldText("Text", overlay.transform, 9f, new Color(0.6f, 1f, 0.6f, 0.9f),
                TextAlignmentOptions.TopLeft, new Vector2(300f, 40f));
            using (var wire = new Wire(overlay.AddComponent<PerfOverlay>()))
            {
                wire.Ref("text", overlayText);
            }

            using (var wire = new Wire(persistent.AddComponent<AppBootstrap>()))
            {
                wire.Ref("persistentRoot", persistent);
            }

            ApplyLighting();
            Save(scene, BootstrapPath);
        }

        private static void RemoveLocomotion(Transform rig)
        {
            foreach (Transform candidate in rig.GetComponentsInChildren<Transform>(true))
            {
                if (System.Array.IndexOf(LocomotionObjects, candidate.name) < 0 || !candidate.gameObject.activeSelf)
                {
                    continue;
                }

                candidate.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(candidate.gameObject);
            }

            if (FindDeep(rig, "Locomotor") == null)
            {
                Debug.LogWarning("[Stack Tower] The interaction rig has no 'Locomotor' object. The SDK's rig layout " +
                                 "may have changed; check that thumbstick and microgesture locomotion are off.");
            }
        }

        // ---- Menu ------------------------------------------------------------------------------

        private static void BuildMenu()
        {
            Scene scene = NewScene();
            LevelConfig timedTower = RequireTimedTower();
            BuildEnvironment(withFloorCollider: false);

            const float width = 320f;
            Canvas canvas = WorldCanvas("MenuPanel", null, new Vector2(width, 432f));
            canvas.gameObject.AddComponent<PlayerAnchoredPanel>();
            canvas.transform.position = new Vector3(0f, 1.2f, 0.55f);
            Transform panel = canvas.transform;
            AddPanel("Background", panel, PanelColor);

            AddText("Title", panel, "STACK TOWER", 40f, TextColor, TextAlignmentOptions.Center, 0f, 18f, width, 46f);
            AddText("Subtitle", panel, "Timed Tower  -  90 seconds", 18f, LabelColor, TextAlignmentOptions.Center,
                0f, 66f, width, 24f);

            Button play = AddButton("PlayButton", panel, "Play", 30f, 60f, 100f, 200f, 62f, out _);
            TextMeshProUGUI best = AddText("Best", panel, "Best  0   0.00 m", 18f, GoldColor, TextAlignmentOptions.Center,
                0f, 168f, width, 24f);

            AddText("SettingsHeader", panel, "SETTINGS", 14f, LabelColor, TextAlignmentOptions.Center, 0f, 202f, width, 18f);

            Button hand = AddButton("HandButton", panel, "Hand: Right", 18f, 30f, 226f, 260f, 38f,
                out TextMeshProUGUI handLabel);
            Button posture = AddButton("PostureButton", panel, "Posture: Standing", 18f, 30f, 268f, 260f, 38f,
                out TextMeshProUGUI postureLabel);
            Button view = AddButton("ViewButton", panel, "View: Mixed reality", 18f, 30f, 310f, 260f, 38f,
                out TextMeshProUGUI viewLabel);
            Button snap = AddButton("SnapButton", panel, "Rotation snap: Learn phase", 18f, 30f, 352f, 260f, 38f,
                out TextMeshProUGUI snapLabel);

            Button volumeDown = AddButton("VolumeDownButton", panel, "-", 24f, 30f, 394f, 56f, 30f, out _);
            TextMeshProUGUI volume = AddText("VolumeLabel", panel, "Volume 100%", 18f, TextColor,
                TextAlignmentOptions.Center, 86f, 394f, 148f, 30f);
            Button volumeUp = AddButton("VolumeUpButton", panel, "+", 24f, 234f, 394f, 56f, 30f, out _);

            using (var wire = new Wire(canvas.gameObject.AddComponent<MenuController>()))
            {
                wire.Ref("timedTower", timedTower)
                    .Ref("playButton", play)
                    .Ref("bestText", best)
                    .Ref("handButton", hand).Ref("handLabel", handLabel)
                    .Ref("postureButton", posture).Ref("postureLabel", postureLabel)
                    .Ref("viewButton", view).Ref("viewLabel", viewLabel)
                    .Ref("snapButton", snap).Ref("snapLabel", snapLabel)
                    .Ref("volumeDownButton", volumeDown)
                    .Ref("volumeUpButton", volumeUp)
                    .Ref("volumeLabel", volume);
            }

            MakeInteractable(canvas);
            ApplyLighting();
            Save(scene, MenuPath);
        }

        // ---- Game ------------------------------------------------------------------------------

        private static void BuildGame()
        {
            Scene scene = NewScene();
            LevelConfig timedTower = RequireTimedTower();
            GameTuning tuning = BlockAssetBuilder.Tuning();
            BuildEnvironment(withFloorCollider: true);

            Material lineMaterial = LineMaterial(BlockAssetBuilder.MaterialsDir + "/Line.mat");

            // Stage: everything that is laid out around the player at the start of a run.
            var stageObject = new GameObject("Stage");
            Transform stage = stageObject.transform;

            Pedestal pedestal = BuildPedestal(stage);
            pedestal.transform.localPosition = new Vector3(0f, 0f, 0.55f);

            GameObject shelf = Child("SpawnShelf", stage);
            shelf.transform.localPosition = new Vector3(0.36f, 1.15f, 0.4f);
            GameObject spawnPoint = Child("SpawnPoint", shelf.transform);
            GameObject shelfRing = Child("Ring", shelf.transform, typeof(LineRenderer));
            shelfRing.transform.localPosition = new Vector3(0f, -0.14f, 0f);
            SetUpRing(shelfRing.GetComponent<LineRenderer>(), lineMaterial, 0.1f, 0.005f,
                new Color(0.45f, 0.85f, 1f, 1f));

            Canvas sidePanel = BuildSidePanel(stage, out HUDController hud, out EndCardController endCard);
            sidePanel.transform.localPosition = new Vector3(-0.4f, 1.18f, 0.52f);

            // Free-standing helpers.
            GameObject ghost = new GameObject("Ghost", typeof(MeshFilter), typeof(MeshRenderer));
            var ghostRenderer = ghost.GetComponent<MeshRenderer>();
            ghostRenderer.sharedMaterial = UnlitMaterial(BlockAssetBuilder.MaterialsDir + "/Ghost.mat",
                tuning.ghostColor, transparent: true);
            ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ghostRenderer.receiveShadows = false;

            GameObject marker = new GameObject("HeightMarker", typeof(LineRenderer));
            SetUpRing(marker.GetComponent<LineRenderer>(), lineMaterial, 0.36f, 0.006f, new Color(0.45f, 0.85f, 1f, 0.9f));

            var popupsObject = new GameObject("RewardPopups");
            var popupLabels = new List<TMP_Text>();
            for (int i = 0; i < 4; i++)
            {
                popupLabels.Add(WorldText("Popup" + i, popupsObject.transform, 34f, Color.white,
                    TextAlignmentOptions.Center, new Vector2(400f, 50f)));
            }

            var effects = new GameObject("Effects");
            Material particleMaterial = ParticleMaterial(BlockAssetBuilder.MaterialsDir + "/Particle.mat");
            ParticleSystem puff = BurstParticles("Puff", effects.transform, particleMaterial,
                new Color(1f, 1f, 1f, 0.7f), 0.035f, 0.35f, 0.45f, 0.07f, -0.05f);
            ParticleSystem goldBurst = BurstParticles("GoldBurst", effects.transform, particleMaterial,
                new Color(1f, 0.82f, 0.25f, 1f), 0.03f, 0.9f, 0.6f, 0.05f, 0.25f);
            using (var wire = new Wire(effects.AddComponent<PlacementEffects>()))
            {
                wire.Ref("puff", puff).Ref("goldBurst", goldBurst);
            }

            var poolRoot = new GameObject("BlockPool");

            // Systems.
            var systems = new GameObject("Systems");
            var stageLayout = stageObject.AddComponent<StageLayout>();
            var spawner = systems.AddComponent<BlockSpawner>();
            var tower = systems.AddComponent<TowerMonitor>();
            var score = systems.AddComponent<ScoreSystem>();
            var assist = systems.AddComponent<PlacementAssist>();
            var game = systems.AddComponent<GameManager>();

            using (var wire = new Wire(stageLayout))
            {
                wire.Ref("pedestal", pedestal).Ref("spawnShelf", shelf.transform).Ref("sidePanel", sidePanel.transform);
            }

            using (var wire = new Wire(spawner))
            {
                wire.Ref("spawnPoint", spawnPoint.transform).Ref("poolRoot", poolRoot.transform);
            }

            using (var wire = new Wire(tower))
            {
                wire.Ref("tuning", tuning).Ref("pedestal", pedestal).Ref("spawner", spawner);
            }

            using (var wire = new Wire(score))
            {
                wire.Ref("tuning", tuning);
            }

            using (var wire = new Wire(assist))
            {
                wire.Ref("tuning", tuning).Ref("spawner", spawner).Ref("pedestal", pedestal)
                    .Ref("ghost", ghost.transform)
                    .Ref("ghostFilter", ghost.GetComponent<MeshFilter>())
                    .Ref("ghostRenderer", ghostRenderer);
            }

            using (var wire = new Wire(game))
            {
                wire.Ref("level", timedTower).Ref("stage", stageLayout).Ref("spawner", spawner).Ref("tower", tower)
                    .Ref("score", score).Ref("assist", assist);
            }

            using (var wire = new Wire(hud))
            {
                wire.Ref("game", game);
            }

            using (var wire = new Wire(endCard))
            {
                wire.Ref("game", game);
            }

            using (var wire = new Wire(marker.AddComponent<HeightMarker>()))
            {
                wire.Ref("game", game).Ref("pedestal", pedestal);
            }

            using (var wire = new Wire(popupsObject.AddComponent<RewardPopups>()))
            {
                wire.Refs("labels", popupLabels);
            }

            BuildAudio(systems.transform);

            ApplyLighting();
            Save(scene, GamePath);
        }

        private static Pedestal BuildPedestal(Transform stage)
        {
            Material material = LitMaterial(BlockAssetBuilder.MaterialsDir + "/Pedestal.mat",
                new Color(0.9f, 0.9f, 0.88f), 0.3f);
            PhysicsMaterial grip = LoadOrCreate(BlockAssetBuilder.DataDir + "/Physics/Pedestal.physicsMaterial",
                () => new PhysicsMaterial("Pedestal")
                {
                    dynamicFriction = 0.8f, staticFriction = 0.9f, bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum
                });

            GameObject root = Child("Pedestal", stage);
            GameObject body = Child("Body", root.transform);
            GameObject slab = Part("Slab", body.transform, material, grip);
            GameObject column = Part("Column", body.transform, material, grip);

            var pedestal = root.AddComponent<Pedestal>();
            using (var wire = new Wire(pedestal))
            {
                wire.Ref("body", body.transform).Ref("slab", slab.transform).Ref("column", column.transform)
                    .Refs("colliders", new[] { slab.GetComponent<Collider>(), column.GetComponent<Collider>() });
            }

            pedestal.SetHeight(0.72f);
            return pedestal;
        }

        private static GameObject Part(string name, Transform parent, Material material, PhysicsMaterial physics)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.GetComponent<Collider>().sharedMaterial = physics;
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part;
        }

        // One panel beside the tower: the HUD during a run, the end card after it.
        private static Canvas BuildSidePanel(Transform stage, out HUDController hud, out EndCardController endCard)
        {
            const float width = 300f;
            Canvas canvas = WorldCanvas("SidePanel", stage, new Vector2(width, 268f));
            Transform panel = canvas.transform;
            AddPanel("Background", panel, PanelColor);

            GameObject hudContent = AddGroup("HUD", panel);
            Transform h = hudContent.transform;
            AddText("TimeLabel", h, "TIME", 14f, LabelColor, TextAlignmentOptions.TopLeft, 22f, 16f, 110f, 18f);
            TextMeshProUGUI timer = AddText("Timer", h, "90", 72f, TextColor, TextAlignmentOptions.TopLeft,
                20f, 30f, 130f, 78f);
            AddText("ScoreLabel", h, "SCORE", 14f, LabelColor, TextAlignmentOptions.TopLeft, 162f, 16f, 110f, 18f);
            TextMeshProUGUI scoreText = AddText("Score", h, "0", 48f, TextColor, TextAlignmentOptions.TopLeft,
                160f, 36f, 130f, 56f);
            AddText("HeightLabel", h, "HEIGHT", 14f, LabelColor, TextAlignmentOptions.TopLeft, 22f, 124f, 110f, 18f);
            TextMeshProUGUI height = AddText("Height", h, "0.00 m", 40f, TextColor, TextAlignmentOptions.TopLeft,
                20f, 140f, 150f, 48f);
            TextMeshProUGUI combo = AddText("Combo", h, "Combo x1.5", 21f, GoldColor, TextAlignmentOptions.TopLeft,
                162f, 150f, 130f, 30f);
            TextMeshProUGUI hint = AddText("Hint", h, "Pinch the block to pick it up", 20f, TextColor,
                TextAlignmentOptions.TopLeft, 22f, 204f, 256f, 52f, wrap: true);

            hud = canvas.gameObject.AddComponent<HUDController>();
            using (var wire = new Wire(hud))
            {
                wire.Ref("content", hudContent).Ref("timerText", timer).Ref("scoreText", scoreText)
                    .Ref("heightText", height).Ref("comboText", combo).Ref("hintText", hint);
            }

            GameObject cardContent = AddGroup("EndCard", panel);
            Transform c = cardContent.transform;
            TextMeshProUGUI title = AddText("Title", c, "Time's up!", 34f, TextColor, TextAlignmentOptions.Center,
                0f, 14f, width, 42f);
            TextMeshProUGUI cardScore = AddText("Score", c, "0", 60f, TextColor, TextAlignmentOptions.Center,
                0f, 56f, width, 66f);
            TextMeshProUGUI cardHeight = AddText("Height", c, "0.00 m", 28f, TextColor, TextAlignmentOptions.Center,
                0f, 124f, width, 34f);
            TextMeshProUGUI best = AddText("Best", c, "Best  0   0.00 m", 17f, LabelColor, TextAlignmentOptions.Center,
                0f, 160f, width, 22f);
            TextMeshProUGUI newBest = AddText("NewBest", c, "New best!", 20f, GoldColor, TextAlignmentOptions.Center,
                0f, 182f, width, 24f);
            Button playAgain = AddButton("PlayAgainButton", c, "Play again", 22f, 20f, 212f, 162f, 44f, out _);
            Button menu = AddButton("MenuButton", c, "Menu", 22f, 192f, 212f, 88f, 44f, out _);

            endCard = canvas.gameObject.AddComponent<EndCardController>();
            using (var wire = new Wire(endCard))
            {
                wire.Ref("content", cardContent).Ref("titleText", title).Ref("scoreText", cardScore)
                    .Ref("heightText", cardHeight).Ref("bestText", best).Ref("newBestText", newBest)
                    .Ref("playAgainButton", playAgain).Ref("menuButton", menu);
            }

            cardContent.SetActive(false);
            MakeInteractable(canvas);
            return canvas;
        }

        private static void BuildAudio(Transform parent)
        {
            GameObject audio = Child("Audio", parent);

            var voices = new List<AudioSource>();
            for (int i = 0; i < 6; i++)
            {
                var voice = Child("Voice" + i, audio.transform).AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 1f;
                voice.dopplerLevel = 0f;
                voice.rolloffMode = AudioRolloffMode.Logarithmic;
                voice.minDistance = 0.4f;
                voice.maxDistance = 8f;
                voices.Add(voice);
            }

            var ui = Child("UI", audio.transform).AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.spatialBlend = 0f;

            var music = Child("Music", audio.transform).AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.spatialBlend = 0f;
            music.loop = true;
            music.volume = 0.35f;

            using (var wire = new Wire(audio.AddComponent<AudioDirector>()))
            {
                wire.Refs("voices", voices).Ref("uiSource", ui).Ref("musicSource", music)
                    .Ref("spawnClip", Clip("spawn")).Ref("grabClip", Clip("grab")).Ref("placeClip", Clip("place"))
                    .Ref("rewardClip", Clip("reward")).Ref("precisionClip", Clip("precision"))
                    .Ref("goldenClip", Clip("golden")).Ref("lostClip", Clip("lost"))
                    .Ref("wobbleClip", Clip("wobble")).Ref("collapseClip", Clip("collapse"))
                    .Ref("tickClip", Clip("tick")).Ref("timeUpClip", Clip("timeup")).Ref("newBestClip", Clip("newbest"));
            }
        }

        private static AudioClip Clip(string name)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxDir}/{name}.wav");
            if (clip == null)
            {
                Debug.LogWarning($"[Stack Tower] Sound '{name}.wav' not found in {SfxDir}; that sound will be silent.");
            }

            return clip;
        }

        // ---- Shared ----------------------------------------------------------------------------

        // The virtual room. Hidden in mixed reality, where the real room takes its place. The floor
        // collider stays in both modes so dropped blocks land on the (real or virtual) floor.
        private static void BuildEnvironment(bool withFloorCollider)
        {
            var environment = new GameObject("Environment");
            GameObject content = Child("VirtualRoom", environment.transform);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(content.transform, false);
            floor.transform.localScale = new Vector3(3f, 1f, 3f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            var renderer = floor.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = LitMaterial(BlockAssetBuilder.MaterialsDir + "/Floor.mat",
                new Color(0.86f, 0.88f, 0.9f), 0.1f);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            using (var wire = new Wire(environment.AddComponent<VirtualEnvironment>()))
            {
                wire.Ref("content", content);
            }

            if (withFloorCollider)
            {
                var collider = new GameObject("FloorCollider").AddComponent<BoxCollider>();
                collider.size = new Vector3(30f, 0.2f, 30f);
                collider.center = new Vector3(0f, -0.1f, 0f);
            }
        }

        // Poke for direct touch, ray for pinch-at-a-distance. The event system lives in Bootstrap.
        private static void MakeInteractable(Canvas canvas)
        {
            QuickActionsAPI.AddPokeCanvasInteraction(canvas.gameObject, false);
            QuickActionsAPI.AddRayCanvasInteraction(canvas.gameObject, false);
        }

        private static void SetUpRing(LineRenderer line, Material material, float radius, float width, Color color)
        {
            Vector3[] points = Circle(radius, 48);
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.widthMultiplier = width;
            line.sharedMaterial = material;
            line.startColor = color;
            line.endColor = color;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View;
        }

        // Opening a scene unloads assets that nothing in a scene refers to, so every builder loads the
        // assets it needs after calling this, never before.
        private static Scene NewScene()
        {
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static LevelConfig RequireTimedTower()
        {
            LevelConfig level = BlockAssetBuilder.TimedTower();
            if (level == null)
            {
                throw new System.InvalidOperationException(
                    "Timed Tower level not found at " + BlockAssetBuilder.TimedTowerPath + ". Build the data assets first.");
            }

            return level;
        }

        // No skybox and no baked lighting yet: a three-colour ambient plus the one directional light.
        private static void ApplyLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.84f, 0.89f, 0.97f);
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.74f, 0.78f);
            RenderSettings.ambientGroundColor = new Color(0.46f, 0.44f, 0.42f);
            RenderSettings.fog = false;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        }

        private static void Save(Scene scene, string path)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new System.InvalidOperationException("Could not save scene to " + path);
            }
        }
    }
}
