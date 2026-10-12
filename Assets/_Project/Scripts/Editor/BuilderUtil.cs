using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace StackTower.EditorTools
{
    /// <summary>
    /// Sets private serialized fields by name, and fails loudly when a name is wrong. The builders
    /// wire everything through this so a renamed field can't silently leave a reference empty.
    /// </summary>
    internal sealed class Wire : IDisposable
    {
        private readonly SerializedObject _target;

        public Wire(Object target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            _target = new SerializedObject(target);
        }

        public Wire Ref(string field, Object value)
        {
            Find(field).objectReferenceValue = value;
            return this;
        }

        public Wire Refs<T>(string field, IReadOnlyList<T> values) where T : Object
        {
            SerializedProperty array = Find(field);
            array.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            return this;
        }

        public Wire Float(string field, float value)
        {
            Find(field).floatValue = value;
            return this;
        }

        public Wire Bool(string field, bool value)
        {
            Find(field).boolValue = value;
            return this;
        }

        public Wire Mask(string field, LayerMask value)
        {
            Find(field).intValue = value.value;
            return this;
        }

        public void Dispose()
        {
            _target.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(_target.targetObject);
        }

        private SerializedProperty Find(string field)
        {
            SerializedProperty property = _target.FindProperty(field);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"{_target.targetObject.GetType().Name} has no serialized field named '{field}'.");
            }

            return property;
        }
    }

    internal static class BuilderUtil
    {
        public const string Root = "Assets/_Project";

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        /// <summary>Loads the asset at a path, or creates it. Existing assets are never overwritten.</summary>
        public static T LoadOrCreate<T>(string path, Func<T> create) where T : Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
            T created = create();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        public static GameObject Child(string name, Transform parent, params Type[] components)
        {
            var child = new GameObject(name, components);
            child.transform.SetParent(parent, false);
            return child;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == name)
                {
                    return candidate;
                }
            }

            return null;
        }

        // ---- Materials -------------------------------------------------------------------------

        public static Material LitMaterial(string path, Color color, float smoothness, Color? emission = null,
            bool transparent = false)
        {
            return LoadOrCreate(path, () =>
            {
                var material = new Material(RequireShader("Universal Render Pipeline/Simple Lit"))
                {
                    enableInstancing = true
                };
                material.SetColor("_BaseColor", color);
                material.SetColor("_SpecColor", new Color(0.18f, 0.18f, 0.18f, 1f));
                material.SetFloat("_Smoothness", smoothness);

                if (emission.HasValue)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", emission.Value);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }

                if (transparent)
                {
                    MakeTransparent(material);
                }

                return material;
            });
        }

        public static Material UnlitMaterial(string path, Color color, bool transparent)
        {
            return LoadOrCreate(path, () =>
            {
                var material = new Material(RequireShader("Universal Render Pipeline/Unlit"))
                {
                    enableInstancing = true
                };
                material.SetColor("_BaseColor", color);
                if (transparent)
                {
                    MakeTransparent(material);
                }

                return material;
            });
        }

        /// <summary>For LineRenderers: the particle shader multiplies by vertex colour, which is how lines are tinted.</summary>
        public static Material LineMaterial(string path)
        {
            return LoadOrCreate(path, () =>
            {
                var material = new Material(RequireShader("Universal Render Pipeline/Particles/Unlit"));
                material.SetColor("_BaseColor", Color.white);
                return material;
            });
        }

        /// <summary>Soft round sprites for particle bursts, tinted per particle.</summary>
        public static Material ParticleMaterial(string path)
        {
            return LoadOrCreate(path, () =>
            {
                var material = new Material(RequireShader("Universal Render Pipeline/Particles/Unlit"));
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
                MakeTransparent(material);
                return material;
            });
        }

        /// <summary>
        /// A particle system that never emits on its own; PlacementEffects asks it for a burst at a point.
        /// </summary>
        public static ParticleSystem BurstParticles(string name, Transform parent, Material material, Color color,
            float size, float speed, float lifetime, float radius, float gravity)
        {
            GameObject go = Child(name, parent, typeof(ParticleSystem));
            var particles = go.GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.maxParticles = 96;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.4f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
            colour.enabled = true;
            colour.color = fade;

            ParticleSystem.SizeOverLifetimeModule shrink = particles.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }

        private static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            // Alpha accumulates properly, so transparent objects composite over passthrough.
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("SHADOWCASTER", false);
        }

        private static Shader RequireShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null)
            {
                throw new InvalidOperationException($"Shader '{name}' not found. Is the Universal RP package installed?");
            }

            return shader;
        }

        // ---- Meshes ----------------------------------------------------------------------------

        /// <summary>
        /// A ramp inside a box of the given size: flat bottom, upright back face at -Z, slope running
        /// from the top of the back down to the front edge at +Z. Flat shaded, 8 triangles.
        /// </summary>
        public static Mesh WedgeMesh(Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3 a = new Vector3(-h.x, -h.y, -h.z);
            Vector3 b = new Vector3(h.x, -h.y, -h.z);
            Vector3 c = new Vector3(h.x, -h.y, h.z);
            Vector3 d = new Vector3(-h.x, -h.y, h.z);
            Vector3 e = new Vector3(-h.x, h.y, -h.z);
            Vector3 f = new Vector3(h.x, h.y, -h.z);

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            AddTriangle(vertices, triangles, a, b, c); // bottom
            AddTriangle(vertices, triangles, a, c, d);
            AddTriangle(vertices, triangles, a, f, b); // back
            AddTriangle(vertices, triangles, f, a, e);
            AddTriangle(vertices, triangles, e, c, f); // slope
            AddTriangle(vertices, triangles, e, d, c);
            AddTriangle(vertices, triangles, a, d, e); // left
            AddTriangle(vertices, triangles, b, f, c); // right

            var mesh = new Mesh { name = "Wedge" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>An upright cylinder (axis along Y) filling a box of the given size.</summary>
        public static Mesh CylinderMesh(Vector3 size, int segments = 16)
        {
            Vector3 h = size * 0.5f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();

            // Side: smooth normals, one extra column to close the seam.
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices.Add(new Vector3(outward.x * h.x, -h.y, outward.z * h.z));
                vertices.Add(new Vector3(outward.x * h.x, h.y, outward.z * h.z));
                normals.Add(outward);
                normals.Add(outward);
            }

            for (int i = 0; i < segments; i++)
            {
                int bottom0 = i * 2;
                int top0 = bottom0 + 1;
                int bottom1 = bottom0 + 2;
                int top1 = bottom0 + 3;
                triangles.AddRange(new[] { bottom0, top0, bottom1, top0, top1, bottom1 });
            }

            // Caps: their own vertices so the rim stays a hard edge.
            foreach (float sign in new[] { 1f, -1f })
            {
                int centre = vertices.Count;
                vertices.Add(new Vector3(0f, sign * h.y, 0f));
                normals.Add(new Vector3(0f, sign, 0f));

                for (int i = 0; i < segments; i++)
                {
                    float angle = i * Mathf.PI * 2f / segments;
                    vertices.Add(new Vector3(Mathf.Cos(angle) * h.x, sign * h.y, Mathf.Sin(angle) * h.z));
                    normals.Add(new Vector3(0f, sign, 0f));
                }

                for (int i = 0; i < segments; i++)
                {
                    int current = centre + 1 + i;
                    int next = centre + 1 + (i + 1) % segments;
                    triangles.AddRange(sign > 0f ? new[] { centre, next, current } : new[] { centre, current, next });
                }
            }

            var mesh = new Mesh { name = "Cylinder" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 p0, Vector3 p1, Vector3 p2)
        {
            int start = vertices.Count;
            vertices.Add(p0);
            vertices.Add(p1);
            vertices.Add(p2);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        public static Vector3[] Circle(float radius, int segments)
        {
            var points = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                points[i] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }

            return points;
        }

        // ---- World-space UI --------------------------------------------------------------------
        // Canvases are authored in millimetres and scaled by 0.001, so a font size of 24 is 24 mm tall.

        public const float CanvasScale = 0.001f;

        public static Canvas WorldCanvas(string name, Transform parent, Vector2 sizeMillimetres)
        {
            GameObject go = Child(name, parent, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = sizeMillimetres;
            rect.localScale = Vector3.one * CanvasScale;
            return canvas;
        }

        /// <summary>Places a rect by its top-left corner, in canvas units measured from the parent's top-left.</summary>
        public static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static GameObject AddGroup(string name, Transform parent)
        {
            GameObject go = Child(name, parent, typeof(RectTransform));
            Stretch((RectTransform)go.transform);
            return go;
        }

        public static Image AddPanel(string name, Transform parent, Color color)
        {
            GameObject go = Child(name, parent, typeof(RectTransform), typeof(Image));
            Stretch((RectTransform)go.transform);
            var image = go.GetComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        public static TextMeshProUGUI AddText(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment, float x, float y, float width, float height, bool wrap = false)
        {
            GameObject go = Child(name, parent, typeof(RectTransform));
            Place((RectTransform)go.transform, x, y, width, height);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = RequireFont();
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            return label;
        }

        public static Button AddButton(string name, Transform parent, string label, float fontSize, float x, float y,
            float width, float height, out TextMeshProUGUI labelText)
        {
            GameObject go = Child(name, parent, typeof(RectTransform), typeof(Image), typeof(Button));
            Place((RectTransform)go.transform, x, y, width, height);

            var image = go.GetComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = new ColorBlock
            {
                normalColor = new Color(0.22f, 0.5f, 0.95f, 1f),
                highlightedColor = new Color(0.35f, 0.62f, 1f, 1f),
                pressedColor = new Color(0.12f, 0.36f, 0.8f, 1f),
                selectedColor = new Color(0.22f, 0.5f, 0.95f, 1f),
                disabledColor = new Color(0.35f, 0.38f, 0.44f, 0.7f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };

            labelText = AddText("Label", go.transform, label, fontSize, Color.white, TextAlignmentOptions.Center,
                0f, 0f, width, height);
            Stretch(labelText.rectTransform);
            return button;
        }

        /// <summary>A free-standing label in the world (not on a canvas), sized in millimetres like the canvases.</summary>
        public static TextMeshPro WorldText(string name, Transform parent, float sizeMillimetres, Color color,
            TextAlignmentOptions alignment, Vector2 boxMillimetres)
        {
            GameObject go = Child(name, parent);
            var label = go.AddComponent<TextMeshPro>();
            label.font = RequireFont();
            // World-space TMP: font size 10 is one unit tall. At a scale of 0.01, size N reads as N millimetres.
            label.rectTransform.sizeDelta = boxMillimetres * 0.1f;
            label.transform.localScale = Vector3.one * 0.01f;
            label.fontSize = sizeMillimetres;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.text = string.Empty;
            return label;
        }

        private static TMP_FontAsset RequireFont()
        {
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                throw new InvalidOperationException(
                    "TextMeshPro has no default font. Import the TMP Essential Resources first " +
                    "(Window > TextMeshPro > Import TMP Essential Resources).");
            }

            return font;
        }
    }
}
