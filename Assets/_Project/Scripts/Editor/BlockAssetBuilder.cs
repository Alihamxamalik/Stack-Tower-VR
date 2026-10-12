using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.HandGrab;
using StackTower.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace StackTower.EditorTools
{
    /// <summary>
    /// Creates the data the game runs on: tuning, materials, meshes, the seven block definitions with
    /// their prefabs, and the Timed Tower level. Existing assets are kept, so values tuned in the
    /// inspector survive a re-run; only what is missing gets created.
    /// </summary>
    internal static class BlockAssetBuilder
    {
        public const string DataDir = BuilderUtil.Root + "/Data";
        public const string MaterialsDir = BuilderUtil.Root + "/Art/Materials";
        public const string ModelsDir = BuilderUtil.Root + "/Art/Models";
        public const string PrefabsDir = BuilderUtil.Root + "/Prefabs/Blocks";
        public const string TuningPath = DataDir + "/GameTuning.asset";
        public const string TimedTowerPath = DataDir + "/LevelConfigs/TimedTower.asset";

        private struct Spec
        {
            public string Id;
            public string Name;
            public BlockShape Shape;
            public Vector3 Size;
            public Color Color;
            public float Smoothness;
            public Color? Emission;
            public bool Transparent;
            public float Mass;
            public bool Slippery;
            public int Bonus;
            public bool Golden;
            public float Weight;
            public int Pool;
            /// <summary>Seconds into a Timed Tower run before the block can appear.</summary>
            public float UnlockTime;
        }

        // Sizes, behaviour and order of appearance follow the block table in the design document.
        private static readonly Spec[] Specs =
        {
            new Spec
            {
                Id = "standard", Name = "Standard", Shape = BlockShape.Box, Size = new Vector3(0.18f, 0.18f, 0.18f),
                Color = new Color(0.95f, 0.91f, 0.82f), Smoothness = 0.2f, Mass = 1f, Weight = 1f, Pool = 14,
                UnlockTime = 0f
            },
            new Spec
            {
                Id = "plank", Name = "Plank", Shape = BlockShape.Box, Size = new Vector3(0.36f, 0.09f, 0.18f),
                Color = new Color(0.79f, 0.6f, 0.42f), Smoothness = 0.2f, Mass = 1f, Weight = 0.8f, Pool = 8,
                UnlockTime = 20f
            },
            new Spec
            {
                Id = "wedge", Name = "Wedge", Shape = BlockShape.Wedge, Size = new Vector3(0.18f, 0.18f, 0.18f),
                Color = new Color(0.66f, 0.85f, 0.73f), Smoothness = 0.2f, Mass = 0.5f, Weight = 0.5f, Pool = 5,
                UnlockTime = 32f
            },
            new Spec
            {
                Id = "cylinder", Name = "Cylinder", Shape = BlockShape.Cylinder, Size = new Vector3(0.16f, 0.12f, 0.16f),
                Color = new Color(0.96f, 0.65f, 0.65f), Smoothness = 0.25f, Mass = 0.7f, Weight = 0.5f, Pool = 5,
                UnlockTime = 38f
            },
            new Spec
            {
                Id = "heavy", Name = "Heavy", Shape = BlockShape.Box, Size = new Vector3(0.18f, 0.18f, 0.18f),
                Color = new Color(0.29f, 0.31f, 0.34f), Smoothness = 0.1f, Mass = 3f, Weight = 0.35f, Pool = 4,
                UnlockTime = 46f
            },
            new Spec
            {
                Id = "slippery", Name = "Slippery", Shape = BlockShape.Box, Size = new Vector3(0.18f, 0.18f, 0.18f),
                Color = new Color(0.62f, 0.84f, 0.97f, 0.6f), Smoothness = 0.85f, Transparent = true, Mass = 1f,
                Slippery = true, Weight = 0.35f, Pool = 4, UnlockTime = 52f
            },
            new Spec
            {
                Id = "golden", Name = "Golden", Shape = BlockShape.Box, Size = new Vector3(0.18f, 0.18f, 0.18f),
                Color = new Color(1f, 0.8f, 0.25f), Smoothness = 0.7f, Emission = new Color(0.55f, 0.36f, 0.05f),
                Mass = 1f, Bonus = 50, Golden = true, Weight = 0.06f, Pool = 3, UnlockTime = 20f
            }
        };

        public static GameTuning Tuning()
        {
            return BuilderUtil.LoadOrCreate(TuningPath, ScriptableObject.CreateInstance<GameTuning>);
        }

        public static LevelConfig TimedTower()
        {
            return AssetDatabase.LoadAssetAtPath<LevelConfig>(TimedTowerPath);
        }

        /// <param name="rebuildPrefabs">Recreate block prefabs even when they already exist.</param>
        public static void Build(bool rebuildPrefabs)
        {
            // One empty scene for the whole job. Opening a scene makes Unity unload assets nothing in a scene
            // refers to, so no scene may be opened while the assets below are being created and linked.
            // It is also where the prefabs are assembled, with no rig for the SDK's quick action to modify.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (string folder in new[]
                     {
                         DataDir + "/BlockDefinitions", DataDir + "/LevelConfigs", DataDir + "/Physics",
                         MaterialsDir, ModelsDir, PrefabsDir
                     })
            {
                BuilderUtil.EnsureFolder(folder);
            }

            GameTuning tuning = Tuning();
            PhysicsMaterial blockPhysics = PhysicsMaterialAsset("Block", 0.6f, 0.75f, PhysicsMaterialCombine.Average);
            // Minimum: ice stays slippery whatever it touches.
            PhysicsMaterial icePhysics = PhysicsMaterialAsset("Ice", 0.04f, 0.05f, PhysicsMaterialCombine.Minimum);

            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var definitions = new List<BlockDefinition>();

            foreach (Spec spec in Specs)
            {
                Material material = BuilderUtil.LitMaterial($"{MaterialsDir}/Block_{spec.Name}.mat", spec.Color,
                    spec.Smoothness, spec.Emission, spec.Transparent);

                // Render meshes are unit sized and scaled by the visual child, so one cube serves every box.
                Mesh mesh = cube;
                Mesh colliderMesh = null;
                if (spec.Shape == BlockShape.Wedge)
                {
                    mesh = BuilderUtil.LoadOrCreate($"{ModelsDir}/Wedge.asset", () => BuilderUtil.WedgeMesh(Vector3.one));
                    colliderMesh = BuilderUtil.LoadOrCreate($"{ModelsDir}/WedgeCollider.asset",
                        () => BuilderUtil.WedgeMesh(spec.Size));
                }
                else if (spec.Shape == BlockShape.Cylinder)
                {
                    mesh = BuilderUtil.LoadOrCreate($"{ModelsDir}/Cylinder.asset",
                        () => BuilderUtil.CylinderMesh(Vector3.one));
                    colliderMesh = BuilderUtil.LoadOrCreate($"{ModelsDir}/CylinderCollider.asset",
                        () => BuilderUtil.CylinderMesh(spec.Size));
                }

                string definitionPath = $"{DataDir}/BlockDefinitions/{spec.Name}.asset";
                BlockDefinition definition = BuilderUtil.LoadOrCreate(definitionPath, () =>
                {
                    var created = ScriptableObject.CreateInstance<BlockDefinition>();
                    created.id = spec.Id;
                    created.displayName = spec.Name;
                    created.shape = spec.Shape;
                    created.size = spec.Size;
                    created.mesh = mesh;
                    created.material = material;
                    created.mass = spec.Mass;
                    created.physicsMaterial = spec.Slippery ? icePhysics : blockPhysics;
                    created.scoreValue = 10;
                    created.bonusPoints = spec.Bonus;
                    created.isGolden = spec.Golden;
                    created.spawnWeight = spec.Weight;
                    created.poolSize = spec.Pool;
                    return created;
                });

                string prefabPath = $"{PrefabsDir}/Block_{spec.Name}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null || rebuildPrefabs)
                {
                    prefab = BuildPrefab(definition, tuning, colliderMesh, prefabPath);
                }

                StackBlock block = prefab.GetComponent<StackBlock>();
                if (definition.prefab != block)
                {
                    definition.prefab = block;
                    EditorUtility.SetDirty(definition);
                }

                definitions.Add(definition);
            }

            BuilderUtil.LoadOrCreate(TimedTowerPath, () =>
            {
                var created = ScriptableObject.CreateInstance<LevelConfig>();
                created.mode = GameMode.TimedTower;
                created.displayName = "Timed Tower";
                created.blocks = new LevelConfig.BlockEntry[Specs.Length];
                for (int i = 0; i < Specs.Length; i++)
                {
                    created.blocks[i] = new LevelConfig.BlockEntry
                    {
                        definition = definitions[i], unlockTime = Specs[i].UnlockTime, weightScale = 1f
                    };
                }

                return created;
            });

            AssetDatabase.SaveAssets();
        }

        private static PhysicsMaterial PhysicsMaterialAsset(string name, float dynamicFriction, float staticFriction,
            PhysicsMaterialCombine frictionCombine)
        {
            return BuilderUtil.LoadOrCreate($"{DataDir}/Physics/{name}.physicsMaterial", () => new PhysicsMaterial(name)
            {
                dynamicFriction = dynamicFriction,
                staticFriction = staticFriction,
                bounciness = 0f,
                frictionCombine = frictionCombine,
                bounceCombine = PhysicsMaterialCombine.Minimum
            });
        }

        // Root: Rigidbody + collider + StackBlock + the SDK's Grabbable. Children: the visible mesh, and the
        // SDK's hand/controller grab interactables.
        private static GameObject BuildPrefab(BlockDefinition definition, GameTuning tuning, Mesh colliderMesh,
            string path)
        {
            var root = new GameObject("Block_" + definition.displayName);

            var body = root.AddComponent<Rigidbody>();
            body.mass = definition.mass;
            body.useGravity = true;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;

            if (definition.shape == BlockShape.Box)
            {
                var box = root.AddComponent<BoxCollider>();
                box.size = definition.size;
                box.sharedMaterial = definition.physicsMaterial;
            }
            else
            {
                var hull = root.AddComponent<MeshCollider>();
                hull.sharedMesh = colliderMesh;
                hull.convex = true;
                hull.sharedMaterial = definition.physicsMaterial;
            }

            GameObject visual = BuilderUtil.Child("Visual", root.transform, typeof(MeshFilter), typeof(MeshRenderer));
            visual.transform.localScale = definition.size;
            visual.GetComponent<MeshFilter>().sharedMesh = definition.mesh;
            var renderer = visual.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = definition.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var block = root.AddComponent<StackBlock>();
            using (var wire = new Wire(block))
            {
                wire.Ref("definition", definition)
                    .Ref("tuning", tuning)
                    .Ref("visualRoot", visual.transform)
                    .Refs("renderers", new Renderer[] { renderer });
            }

            // Pinch + palm grab for hands, grip grab for controllers.
            List<GameObject> created = QuickActionsAPI.AddGrabInteraction(root);
            var grabbable = root.GetComponent<Grabbable>();
            if (grabbable == null || created.Count == 0)
            {
                throw new System.InvalidOperationException(
                    "The Interaction SDK did not add its grab components to " + root.name + ".");
            }

            // StackBlock owns the Rigidbody; the SDK only moves the transform.
            grabbable.InjectOptionalKinematicWhileSelected(false);
            grabbable.InjectOptionalThrowWhenUnselected(false);

            // Add what the SDK would otherwise add at runtime, so nothing is created on first grab.
            var transformer = root.AddComponent<GrabFreeTransformer>();
            grabbable.InjectOptionalOneGrabTransformer(transformer);
            grabbable.InjectOptionalTwoGrabTransformer(transformer);
            EditorUtility.SetDirty(grabbable);

            GameObject interactables = created[0];
            var handGrab = interactables.GetComponent<HandGrabInteractable>();
            if (handGrab != null)
            {
                handGrab.InjectOptionalMovementProvider(interactables.AddComponent<MoveTowardsTargetProvider>());
                EditorUtility.SetDirty(handGrab);
            }

            var bridge = root.AddComponent<InteractionGrabBridge>();
            using (var wire = new Wire(bridge))
            {
                wire.Ref("block", block).Ref("grabbable", grabbable).Ref("interactables", interactables);
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
