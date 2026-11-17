using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace T1DCGM.EditorTools
{
    /// <summary>
    /// Builds the two demo scenes: CGM application (mixed reality, scene 1) and Lunch (virtual cafeteria, scene 2).
    /// Running it again regenerates both scenes (hand edits are lost) and updates generated meshes and materials in place.
    /// </summary>
    public static partial class DemoSceneBuilder
    {
        const string k_Root = "Assets/T1DCGM";
        const string k_CgmScenePath = k_Root + "/Scenes/CGMApplication.unity";
        const string k_LunchScenePath = k_Root + "/Scenes/Lunch.unity";
        const string k_CgmSceneName = "CGMApplication";
        const string k_LunchSceneName = "Lunch";
        const string k_XROriginPrefab = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Variant.prefab";
        const string k_InterFont = "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset";
        const string k_InterTtf = "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular.ttf";

        static readonly Dictionary<string, Material> s_Mats = new Dictionary<string, Material>();
        static TMP_FontAsset s_Font;

        // ------------------------------------------------------------------ menu

        [MenuItem("T1D CGM/Build Demo Scenes", priority = -10)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[T1D] Build cancelled (the 'save scene' prompt was cancelled).");
                return;
            }
            try
            {
                PrepareAssets();
                PassthroughPrototypeBuilder.EnableFeaturesMenu();
                BuildLunchScene();
                BuildCgmScene(); // last, so it is the scene left open
                SetBuildScenes(k_CgmScenePath, k_LunchScenePath);
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("T1D CGM",
                    "Built:\n" + k_CgmScenePath + " (opened, starts the demo)\n" + k_LunchScenePath +
                    "\n\nScene 1 uses passthrough: run it over Quest Link with Passthrough over Meta Quest Link enabled.", "OK");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("T1D CGM", "Building the demo failed:\n\n" + e.Message + "\n\nSee the Console.", "OK");
            }
        }

        [MenuItem("T1D CGM/Clear Saved CGM Sites", priority = 30)]
        public static void ClearSites()
        {
            SiteHistory.Clear();
            Debug.Log("[T1D] Saved CGM sites cleared.");
        }

        static void PrepareAssets()
        {
            foreach (var dir in new[] { "Scenes", "Meshes", "Materials", "Textures", "Fonts" })
                if (!AssetDatabase.IsValidFolder($"{k_Root}/{dir}"))
                    AssetDatabase.CreateFolder(k_Root, dir);
            s_Font = CrispFont();
            if (s_Font == null) s_Font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_InterFont);
            if (s_Font == null) s_Font = TMP_Settings.defaultFontAsset;
            CreateMaterials();
        }

        // ------------------------------------------------------------------ scene basics

        static UnityEngine.SceneManagement.Scene NewScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                Object.DestroyImmediate(cam.gameObject); // the XR rig brings its own camera
            var light = Object.FindFirstObjectByType<Light>();
            if (light != null)
            {
                light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
                light.intensity = 1.2f;
                light.shadows = LightShadows.Soft;
            }
            return scene;
        }

        static GameObject AddXRRig(Vector3 position, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_XROriginPrefab);
            if (prefab == null)
                throw new System.Exception("XR Origin prefab not found: " + k_XROriginPrefab);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Affordance Callouts"))
                    t.gameObject.SetActive(false);
            // The template's gaze controller keeps poking the hidden callouts ("Coroutine couldn't be started ... inactive").
            foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == "CalloutGazeController")
                    mb.enabled = false;
            // A is "jump" in the template; the demo uses A to continue / record touches.
            foreach (var jump in rig.GetComponentsInChildren<JumpProvider>(true))
                jump.enabled = false;

            var simulatorPath = AssetDatabase.FindAssets("XR Interaction Simulator t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "XR Interaction Simulator");
            if (simulatorPath != null)
            {
                var sim = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(simulatorPath));
                sim.SetActive(false); // tick on in the Hierarchy to test without a headset
            }
            return rig;
        }

        static Transform FindChild(GameObject root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        static AudioSource AddAudio(GameObject go)
        {
            var audio = go.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            return audio;
        }

        // ------------------------------------------------------------------ boards

        /// <summary>World-space board facing -Z (readable from the -Z side), centred on <paramref name="centre"/>.</summary>
        static ProgressBoard BuildBoard(string name, Transform parent, Vector3 localCentre, Quaternion localRotation, Vector2 size)
        {
            const float pixelsWide = 1400f;
            float pixelsHigh = pixelsWide * size.y / size.x;

            var canvasGo = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            var rt = (RectTransform)canvasGo.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(pixelsWide, pixelsHigh);
            rt.localScale = Vector3.one * (size.x / pixelsWide);
            rt.localPosition = localCentre;
            rt.localRotation = localRotation;

            var bg = UIImage("Background", rt, new Color(0.07f, 0.09f, 0.12f, 0.96f));
            Anchor(bg.rectTransform, 0f, 0f, 1f, 1f);

            var board = canvasGo.AddComponent<ProgressBoard>();
            var muted = new Color(0.62f, 0.7f, 0.8f);
            board.stepCounter = UIText("Counter", rt, 0.04f, 0.04f, 0.5f, 0.11f, 30, muted, FontStyles.Bold);
            board.stepCounter.characterSpacing = 4f;
            var barBg = UIImage("Progress Bar", rt, new Color(1f, 1f, 1f, 0.12f));
            Anchor(barBg.rectTransform, 0.52f, 0.06f, 0.96f, 0.085f);
            var barFill = UIImage("Fill", barBg.rectTransform, new Color(0.35f, 0.85f, 0.5f));
            barFill.rectTransform.anchorMin = Vector2.zero;
            barFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            barFill.rectTransform.offsetMin = barFill.rectTransform.offsetMax = Vector2.zero;
            board.progressFill = barFill.rectTransform;
            board.headerText = UIText("Header", rt, 0.04f, 0.12f, 0.96f, 0.24f, 60, Color.white, FontStyles.Bold);
            board.headerText.enableAutoSizing = true;
            board.headerText.fontSizeMin = 30f;
            board.headerText.fontSizeMax = 60f;

            board.instructionText = UIText("Instruction", rt, 0.04f, 0.26f, 0.96f, 0.72f, 40, Color.white, FontStyles.Normal);
            board.instructionText.lineSpacing = 8f;
            board.instructionText.enableAutoSizing = true;
            board.instructionText.fontSizeMin = 20f;
            board.instructionText.fontSizeMax = 40f;
            board.feedbackText = UIText("Feedback", rt, 0.04f, 0.73f, 0.96f, 0.84f, 34, Color.white, FontStyles.Bold);
            board.feedbackText.enableAutoSizing = true;
            board.feedbackText.fontSizeMin = 18f;
            board.feedbackText.fontSizeMax = 34f;

            var bar = UIImage("Controls Bar", rt, new Color(0.15f, 0.3f, 0.5f, 0.6f));
            Anchor(bar.rectTransform, 0.02f, 0.86f, 0.98f, 0.97f);
            board.controlsText = UIText("Controls", rt, 0.04f, 0.87f, 0.96f, 0.96f, 30, new Color(0.85f, 0.92f, 1f), FontStyles.Bold, TextAlignmentOptions.Left);
            board.controlsText.enableAutoSizing = true;
            board.controlsText.fontSizeMin = 16f;
            board.controlsText.fontSizeMax = 30f;
            return board;
        }

        // ------------------------------------------------------------------ interaction pieces

        /// <summary>Poke button lying on a surface; returns the button and its label.</summary>
        static PressButton3D PokeButton(string name, Transform parent, Vector3 localPosition, string label, Material capMaterial, out TextMeshPro labelText)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            Box("Base", root.transform, s_Mats["Dark"], new Vector3(0f, 0.01f, 0f), new Vector3(0.09f, 0.02f, 0.07f), false);
            var cap = Part("Cap", root.transform, SaveMesh("Button_Cap", MeshFactory.Cylinder("Button_Cap", 0.026f, 0.02f, 0.032f, 32, MeshFactory.Axis.Y)), capMaterial);
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.02f, 0f);
            col.size = new Vector3(0.09f, 0.04f, 0.07f);
            var button = root.AddComponent<PressButton3D>();
            button.cap = cap.transform;
            button.pressDepth = 0.006f;
            button.colliders.Add(col);
            labelText = Text3D("Label", root.transform, $"<b>{label}</b>", 0.16f, Color.white, new Vector2(0.16f, 0.03f));
            labelText.transform.localPosition = new Vector3(0f, 0.07f, 0f);
            labelText.gameObject.AddComponent<BillboardLabel>().height = 0.07f;
            return button;
        }

        static XRGrabInteractable Grab(GameObject go, Collider collider, float mass, bool gravity = true)
        {
            // Not "??": Unity returns a fake-null object for a missing component, which "??" treats as present.
            if (!go.TryGetComponent<Rigidbody>(out var rb))
                rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.useGravity = gravity;
            rb.isKinematic = !gravity;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.colliders.Clear();
            grab.colliders.Add(collider);
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            return grab;
        }

        static TeleportationAnchor TeleportPad(Transform parent, string name, Vector3 localPosition, float yaw, Material material, string label)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Pad";
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(root.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.004f, 0f);
            disc.transform.localScale = new Vector3(0.6f, 0.004f, 0.6f);
            disc.GetComponent<Renderer>().sharedMaterial = material;

            var pointer = Box("Facing", root.transform, s_Mats["White"], Vector3.zero, new Vector3(0.05f, 0.004f, 0.22f), false);
            pointer.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            pointer.transform.localPosition = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0.01f, 0.12f);

            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(0.6f, 0.02f, 0.6f);
            col.center = new Vector3(0f, 0.01f, 0f);

            var arrival = new GameObject("Arrival").transform;
            arrival.SetParent(root.transform, false);
            arrival.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var anchor = root.AddComponent<TeleportationAnchor>();
            anchor.colliders.Clear();
            anchor.colliders.Add(col);
            anchor.teleportAnchorTransform = arrival;
            anchor.matchOrientation = MatchOrientation.TargetUpAndForward;
            anchor.interactionLayers = InteractionLayerMask.GetMask("Teleport");

            var labelRoot = new GameObject("Label").transform;
            labelRoot.SetParent(root.transform, false);
            var text = Text3D("Text", labelRoot, $"<b>{label}</b>", 0.18f, Color.white, new Vector2(0.8f, 0.06f));
            labelRoot.gameObject.AddComponent<BillboardLabel>().height = 0.35f;
            return anchor;
        }

        static SceneTeleportPad ScenePad(Transform parent, string name, Vector3 localPosition, float yaw, string label, string sceneName)
        {
            var anchor = TeleportPad(parent, name, localPosition, yaw, s_Mats["Grey"], label);
            var pad = anchor.gameObject.AddComponent<SceneTeleportPad>();
            pad.sceneName = sceneName;
            pad.padRenderer = anchor.transform.Find("Pad").GetComponent<Renderer>();
            return pad;
        }

        static GuideArrow BuildGuideArrow()
        {
            var root = new GameObject("Guide Arrow");
            var arrow = root.AddComponent<GuideArrow>();
            var visual = Part("Visual", root.transform, SaveMesh("Guide_Arrow", MeshFactory.Lathe("Guide_Arrow", new[]
            {
                V(0f, 0f), V(0.024f, 0.044f), V(0.01f, 0.044f), V(0.01f, 0.1f), V(0f, 0.1f),
            }, 24, MeshFactory.Axis.Y)), s_Mats["Arrow"]);
            visual.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            arrow.visual = visual;
            arrow.height = 0.18f;
            return arrow;
        }

        // ------------------------------------------------------------------ geometry helpers

        static Vector2 V(float r, float h) => new Vector2(r, h);

        static GameObject Part(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static GameObject Box(string name, Transform parent, Material material, Vector3 localPosition, Vector3 size, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!keepCollider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static TextMeshPro Text3D(string name, Transform parent, string text, float fontSize, Color color, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            if (s_Font != null) tmp.font = s_Font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = size;
            tmp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return tmp;
        }

        static TextMeshProUGUI UIText(string name, RectTransform parent, float x0, float y0, float x1, float y1, float size, Color color,
            FontStyles style, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (s_Font != null) tmp.font = s_Font;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            tmp.text = "";
            Anchor(tmp.rectTransform, x0, y0, x1, y1);
            return tmp;
        }

        static Image UIImage(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Stretches a rect between normalised corners, measured from the top-left.</summary>
        static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, 1f - y1);
            rt.anchorMax = new Vector2(x1, 1f - y0);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static Bounds RendererBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(root.position, Vector3.zero);
            var b = renderers[0].bounds;
            foreach (var r in renderers)
                b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------------ assets

        /// <summary>Saves a generated mesh as an asset, updating an existing one in place so references survive a rebuild.</summary>
        static Mesh SaveMesh(string name, Mesh generated)
        {
            string path = $"{k_Root}/Meshes/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            EditorUtility.CopySerialized(generated, existing);
            existing.name = name;
            Object.DestroyImmediate(generated);
            return existing;
        }

        static Texture2D SaveTexture(string name, Texture2D generated)
        {
            string path = $"{k_Root}/Textures/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }
            EditorUtility.CopySerialized(generated, existing);
            existing.name = name;
            Object.DestroyImmediate(generated);
            return existing;
        }

        /// <summary>High-resolution dynamic font from the template's Inter TTF (the template's own atlas looks soft on boards).</summary>
        static TMP_FontAsset CrispFont()
        {
            string path = $"{k_Root}/Fonts/Inter Crisp SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
                return existing;
            var ttf = AssetDatabase.LoadAssetAtPath<Font>(k_InterTtf);
            if (ttf == null)
                return null;
            try
            {
                var font = TMP_FontAsset.CreateFontAsset(ttf, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                    2048, 2048, AtlasPopulationMode.Dynamic, true);
                font.name = "Inter Crisp SDF";
                AssetDatabase.CreateAsset(font, path);
                font.material.name = "Inter Crisp SDF Material";
                AssetDatabase.AddObjectToAsset(font.material, font);
                for (int i = 0; i < font.atlasTextures.Length; i++)
                {
                    font.atlasTextures[i].name = $"Inter Crisp SDF Atlas {i}";
                    AssetDatabase.AddObjectToAsset(font.atlasTextures[i], font);
                }
                AssetDatabase.SaveAssets();
                return font;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[T1D] Could not create the crisp font, using the template font instead: " + e.Message);
                return null;
            }
        }

        /// <summary>Puts the given scenes first in the build list, in order (the first one is the starting scene).</summary>
        static void SetBuildScenes(params string[] paths)
        {
            var others = EditorBuildSettings.scenes.Where(s => !paths.Contains(s.path));
            EditorBuildSettings.scenes = paths.Select(p => new EditorBuildSettingsScene(p, true)).Concat(others).ToArray();
        }

        static void CreateMaterials()
        {
            s_Mats.Clear();
            Lit("Floor", new Color(0.55f, 0.53f, 0.5f), 0.25f);
            Lit("Wall", new Color(0.86f, 0.84f, 0.8f), 0.2f);
            Lit("Dark", new Color(0.13f, 0.14f, 0.16f), 0.4f);
            Lit("Grey", new Color(0.42f, 0.44f, 0.48f), 0.4f);
            Lit("White", new Color(0.94f, 0.94f, 0.95f), 0.5f);
            Lit("Steel", new Color(0.78f, 0.8f, 0.83f), 0.85f, 1f);
            Lit("Wood", new Color(0.55f, 0.38f, 0.22f), 0.35f);
            Lit("Blue", new Color(0.15f, 0.5f, 0.95f), 0.5f);
            Lit("Green", new Color(0.2f, 0.8f, 0.35f), 0.5f);
            Lit("Red", new Color(0.88f, 0.2f, 0.2f), 0.5f);
            Lit("Teal", new Color(0.1f, 0.6f, 0.62f), 0.55f);
            Lit("Applicator", new Color(0.92f, 0.92f, 0.9f), 0.6f);
            Lit("ApplicatorAccent", new Color(0.95f, 0.75f, 0.15f), 0.6f);
            Lit("Sensor", new Color(0.96f, 0.96f, 0.96f), 0.7f);
            Lit("Wipe", new Color(0.95f, 0.97f, 1f), 0.15f);
            Lit("WipePacket", new Color(0.25f, 0.5f, 0.9f), 0.5f);
            Lit("Crust", new Color(0.66f, 0.42f, 0.2f), 0.2f);
            Lit("Bagel", new Color(0.8f, 0.55f, 0.28f), 0.4f);
            Lit("CupBall", new Color(0.95f, 0.85f, 0.2f, 0.45f), 0.6f, 0f, true);
            Lit("Sleeve", new Color(0.55f, 0.75f, 1f, 0.18f), 0.4f, 0f, true);
            Lit("Zone", new Color(0.3f, 1f, 0.45f, 0.45f), 0.4f, 0f, true);
            Lit("OldSite", new Color(1f, 0.2f, 0.2f, 0.85f), 0.4f, 0f, true);

            // Lunch room.
            var planks = SaveTexture("Wood Planks", FoodModels.PlankTexture());
            Lit("WoodFloor", Color.white, 0.35f, 0f, false, planks, new Vector2(8f, 3f));
            Lit("Wainscot", new Color(0.62f, 0.5f, 0.42f), 0.3f, 0f, false, planks, new Vector2(3f, 1f));
            Lit("Trim", new Color(0.95f, 0.94f, 0.9f), 0.4f);
            Lit("Ceiling", new Color(0.95f, 0.95f, 0.93f), 0.1f);
            Lit("Tile", Color.white, 0.75f, 0f, false, SaveTexture("Subway Tile", FoodModels.TileTexture()), new Vector2(14f, 1.5f));
            Lit("LogoPlate", new Color(0.07f, 0.09f, 0.14f), 0.75f);
            Lit("LogoDrop", new Color(0.86f, 0.1f, 0.17f), 0.92f);
            Lit("Glass", new Color(0.85f, 0.93f, 1f, 0.14f), 0.95f, 0f, true);
            Lit("Pad", new Color(0.35f, 0.65f, 0.95f, 0.3f), 0.5f, 0f, true);
            Lit("LampShade", new Color(0.13f, 0.28f, 0.22f), 0.65f, 0.3f);
            Lit("Terracotta", new Color(0.7f, 0.37f, 0.24f), 0.2f);
            Lit("Leaf", new Color(0.17f, 0.42f, 0.18f), 0.45f);
            Lit("Ceramic", new Color(0.97f, 0.96f, 0.94f), 0.85f);
            Lit("BowlBand", new Color(0.2f, 0.33f, 0.68f), 0.85f);

            // Food.
            VertexLit("RiceGrain", new Color(0.99f, 0.98f, 0.95f), 0.55f);
            VertexLit("Penne", new Color(0.97f, 0.81f, 0.47f), 0.45f);
            Lit("RiceCore", new Color(0.8f, 0.78f, 0.72f), 0.3f, 0f, false, SaveTexture("Rice Grains", GrainTexture()), Vector2.one);
            Lit("Crumb", Color.white, 0.1f, 0f, false, SaveTexture("Bread Crumb", FoodModels.CrumbTexture()), Vector2.one);
            Lit("Apple", Color.white, 0.72f, 0f, false, SaveTexture("Apple Skin", FoodModels.AppleTexture()), Vector2.one);
            Lit("Banana", Color.white, 0.4f, 0f, false, SaveTexture("Banana Skin", FoodModels.BananaTexture()), Vector2.one);
            Lit("Orange", Color.white, 0.45f, 0f, false, SaveTexture("Orange Peel", FoodModels.OrangeTexture()), new Vector2(2f, 2f));
            Lit("Stem", new Color(0.33f, 0.24f, 0.12f), 0.2f);
            Lit("Watermelon", new Color(0.93f, 0.24f, 0.28f), 0.55f);
            Lit("RindWhite", new Color(0.86f, 0.92f, 0.72f), 0.4f);
            Lit("RindGreen", new Color(0.1f, 0.33f, 0.12f), 0.55f);
            Lit("Seed", new Color(0.07f, 0.05f, 0.04f), 0.75f);

            Unlit("Arrow", new Color(1f, 0.84f, 0.1f));
            Unlit("Tip", new Color(0.1f, 0.9f, 1f));
            Unlit("MirrorScreen", Color.white);
            Unlit("PhoneScreen", new Color(0.05f, 0.06f, 0.08f));
            Unlit("TabletScreen", new Color(0.06f, 0.08f, 0.11f));
            Unlit("Bulb", new Color(1f, 0.93f, 0.76f));
            Unlit("LogoEdge", new Color(0.35f, 0.8f, 1f));
            Unlit("WindowView", Color.white);
            s_Mats["WindowView"].SetTexture("_BaseMap", SaveTexture("Window View", FoodModels.WindowViewTexture()));

            var line = GetOrCreateMaterial("Line", Shader.Find("Sprites/Default"));
            line.color = Color.white;
            s_Mats["Line"] = line;
        }

        /// <summary>Cooked-rice look: cream base with many small lighter and darker grains.</summary>
        static Texture2D GrainTexture()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Rice Grains", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[size * size];
            var baseColor = new Color(0.9f, 0.88f, 0.82f);
            for (int i = 0; i < px.Length; i++) px[i] = baseColor;
            var rng = new System.Random(7);
            for (int g = 0; g < 900; g++)
            {
                float cx = rng.Next(size), cy = rng.Next(size);
                float ang = (float)rng.NextDouble() * Mathf.PI;
                float len = 6f + (float)rng.NextDouble() * 4f, wid = 2.2f + (float)rng.NextDouble();
                float shade = 0.9f + (float)rng.NextDouble() * 0.12f;
                var c = new Color(shade, shade * 0.99f, shade * 0.95f);
                for (int y = -8; y <= 8; y++)
                    for (int x = -8; x <= 8; x++)
                    {
                        float u = x * Mathf.Cos(ang) + y * Mathf.Sin(ang), v = -x * Mathf.Sin(ang) + y * Mathf.Cos(ang);
                        float d = (u * u) / (len * len * 0.25f) + (v * v) / (wid * wid * 0.25f);
                        if (d > 1f) continue;
                        int px0 = ((int)cx + x + size) % size, py0 = ((int)cy + y + size) % size;
                        px[py0 * size + px0] = Color.Lerp(c, c * 0.82f, d * d); // darker rim between grains
                    }
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        static void Lit(string name, Color color, float smoothness, float metallic = 0f, bool transparent = false, Texture2D texture = null, Vector2 tiling = default)
        {
            var m = GetOrCreateMaterial(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BaseMap", texture);
            m.SetTextureScale("_BaseMap", texture != null && tiling != Vector2.zero ? tiling : Vector2.one);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetShaderPassEnabled("DepthOnly", false);
                m.SetShaderPassEnabled("ShadowCaster", false);
            }
            EditorUtility.SetDirty(m);
            s_Mats[name] = m;
        }

        /// <summary>Lit material that multiplies by vertex colour (baked shading between rice grains and pasta pieces).</summary>
        static void VertexLit(string name, Color color, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Lit");
            if (shader == null)
            {
                Lit(name, color, smoothness);
                return;
            }
            var m = GetOrCreateMaterial(name, shader);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.renderQueue = (int)RenderQueue.Geometry;
            EditorUtility.SetDirty(m);
            s_Mats[name] = m;
        }

        static void Unlit(string name, Color color)
        {
            var m = GetOrCreateMaterial(name, Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            s_Mats[name] = m;
        }

        static Material GetOrCreateMaterial(string name, Shader shader)
        {
            string path = $"{k_Root}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                m.shader = shader;
            }
            return m;
        }
    }
}
