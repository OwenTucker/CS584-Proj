using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;

namespace T1DCGM.EditorTools
{
    /// <summary>
    /// Builds the passthrough-over-Link prototype scene and switches on the OpenXR features it needs.
    /// Running it again regenerates the scene (hand edits are lost).
    /// </summary>
    public static class PassthroughPrototypeBuilder
    {
        const string k_Root = "Assets/T1DCGM";
        const string k_ScenePath = k_Root + "/Scenes/PassthroughPrototype.unity";
        const string k_MaterialDir = k_Root + "/Materials";
        const string k_XROriginPrefab = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Variant.prefab";
        const string k_InterFont = "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset";

        // Meta OpenXR feature ids (UnityEngine.XR.OpenXR.Features.Meta.ARSessionFeature / ARCameraFeature).
        const string k_MetaSession = "com.unity.openxr.feature.arfoundation-meta-session";
        const string k_MetaCamera = "com.unity.openxr.feature.arfoundation-meta-camera";

        [MenuItem("T1D CGM/Build Passthrough Prototype", priority = 0)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[T1D] Build cancelled (the 'save scene' prompt was cancelled).");
                return;
            }

            try
            {
                var changed = EnablePassthroughFeatures();
                BuildScene();
                AddToBuildScenes(k_ScenePath);
                AssetDatabase.SaveAssets();

                EditorUtility.DisplayDialog("T1D CGM",
                    "Built " + k_ScenePath + " (opened).\n\n" +
                    (changed.Count > 0 ? "Enabled OpenXR features:\n- " + string.Join("\n- ", changed) + "\n\n" : "OpenXR passthrough features were already on.\n\n") +
                    "Before pressing Play:\n" +
                    "1. Meta Quest Link PC app > Settings > General: set Meta Quest Link as the active OpenXR runtime.\n" +
                    "2. Settings > Beta (or Developer): turn on Developer Runtime Features, then Passthrough over Meta Quest Link.\n" +
                    "3. Connect the Quest 3 with Link, then press Play.\n\n" +
                    "Passthrough only shows in the headset, never in the Game view.",
                    "OK");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("T1D CGM", "Building the prototype failed:\n\n" + e.Message + "\n\nSee the Console.", "OK");
            }
        }

        [MenuItem("T1D CGM/Enable Passthrough OpenXR Features", priority = 10)]
        public static void EnableFeaturesMenu()
        {
            var changed = EnablePassthroughFeatures();
            AssetDatabase.SaveAssets();
            Debug.Log(changed.Count > 0
                ? "[T1D] Enabled: " + string.Join(", ", changed)
                : "[T1D] Passthrough OpenXR features were already enabled.");
        }

        // ------------------------------------------------------------------ project settings

        /// <summary>
        /// Turns on Meta Quest: Session and Meta Quest: Camera (Passthrough) for Windows (Link in Play mode) and Android
        /// (on-device builds). Only these two: the rest of the Meta feature group (planes, meshes, ...) needs the
        /// scene permission and is not used yet.
        /// </summary>
        static List<string> EnablePassthroughFeatures()
        {
            var changed = new List<string>();
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                foreach (var id in new[] { k_MetaSession, k_MetaCamera })
                {
                    var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, id);
                    if (feature == null)
                    {
                        Debug.LogWarning($"[T1D] OpenXR feature {id} not found for {group}. Is com.unity.xr.meta-openxr installed?");
                        continue;
                    }
                    if (!feature.enabled)
                    {
                        feature.enabled = true;
                        EditorUtility.SetDirty(feature);
                        changed.Add(id == k_MetaCamera ? $"Meta Quest: Camera (Passthrough) ({group})" : $"Meta Quest: Session ({group})");
                    }
                }
            }
            return changed;
        }

        static void AddToBuildScenes(string path)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true)); // starting scene first
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ------------------------------------------------------------------ scene

        static void BuildScene()
        {
            foreach (var dir in new[] { "Scenes", "Materials" })
                if (!AssetDatabase.IsValidFolder($"{k_Root}/{dir}"))
                    AssetDatabase.CreateFolder(k_Root, dir);

            var mats = new Dictionary<string, Material>
            {
                ["Panel"] = UnlitMaterial("Proto Panel", new Color(0.06f, 0.07f, 0.09f)),
                ["Marker"] = UnlitMaterial("Proto Marker", new Color(0.95f, 0.15f, 0.15f)),
                ["Tip"] = UnlitMaterial("Proto Tip", new Color(0.1f, 0.9f, 1f)),
                ["Floor"] = UnlitMaterial("Proto Fallback Floor", new Color(0.22f, 0.24f, 0.27f)),
                ["Wall"] = UnlitMaterial("Proto Fallback Wall", new Color(0.32f, 0.34f, 0.38f)),
            };

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                Object.DestroyImmediate(cam.gameObject); // the XR rig brings its own camera

            // Passthrough replaces the sky; no skybox lighting or fog to fight with.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);

            var rig = AddXRRig();
            var xrCamera = rig != null ? rig.GetComponentInChildren<Camera>(true) : null;
            if (xrCamera == null)
                throw new System.Exception("No camera found under the XR Origin prefab " + k_XROriginPrefab);
            ConfigurePassthroughCamera(xrCamera);

            var session = new GameObject("AR Session");
            session.AddComponent<ARSession>();

            var fallback = BuildFallbackRoom(mats);
            var (panel, text) = BuildPanel(mats["Panel"]);

            var probeGo = new GameObject("Passthrough Probe");
            var probe = probeGo.AddComponent<PassthroughProbe>();
            var audio = probeGo.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            probe.audioSource = audio;
            probe.xrCamera = xrCamera;
            probe.cameraManager = xrCamera.GetComponent<ARCameraManager>();
            probe.rightController = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Right Controller");
            probe.panel = panel;
            probe.statusText = text;
            probe.fallbackEnvironment = fallback;
            probe.markerMaterial = mats["Marker"];
            probe.tipMaterial = mats["Tip"];
            if (probe.rightController == null)
                Debug.LogWarning("[T1D] 'Right Controller' not found under the XR Origin; the probe will search at runtime.");

            EditorSceneManager.SaveScene(scene, k_ScenePath);
        }

        static GameObject AddXRRig()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_XROriginPrefab);
            if (prefab == null)
                throw new System.Exception("XR Origin prefab not found: " + k_XROriginPrefab);

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Affordance Callouts"))
                    t.gameObject.SetActive(false);

            // A is "jump" in the template; the prototype uses A to drop markers.
            foreach (var jump in rig.GetComponentsInChildren<JumpProvider>(true))
                jump.enabled = false;

            return rig;
        }

        /// <summary>Transparent solid-colour background plus an ARCameraManager: the two things Meta passthrough needs.</summary>
        static void ConfigurePassthroughCamera(Camera cam)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;

            var urp = cam.GetUniversalAdditionalCameraData();
            if (urp != null)
                urp.renderPostProcessing = false; // post-processing can overwrite alpha and hide passthrough

            if (cam.GetComponent<ARCameraManager>() == null)
                cam.gameObject.AddComponent<ARCameraManager>();
            // Not needed on Quest (the runtime composites passthrough itself) and would only add cost.
            var bg = cam.GetComponent<ARCameraBackground>();
            if (bg != null) Object.DestroyImmediate(bg, true);
        }

        /// <summary>Simple grey room shown only when passthrough is off (the fallback in DESIGN.md).</summary>
        static GameObject BuildFallbackRoom(Dictionary<string, Material> mats)
        {
            var root = new GameObject("Fallback Room (passthrough off)");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform);
            floor.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
            floor.GetComponent<Renderer>().sharedMaterial = mats["Floor"];

            void Wall(string name, Vector3 pos, Vector3 scale)
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w.name = name;
                w.transform.SetParent(root.transform);
                w.transform.localPosition = pos;
                w.transform.localScale = scale;
                w.GetComponent<Renderer>().sharedMaterial = mats["Wall"];
            }
            Wall("Wall N", new Vector3(0f, 1.25f, 3f), new Vector3(6f, 2.5f, 0.05f));
            Wall("Wall S", new Vector3(0f, 1.25f, -3f), new Vector3(6f, 2.5f, 0.05f));
            Wall("Wall E", new Vector3(3f, 1.25f, 0f), new Vector3(0.05f, 2.5f, 6f));
            Wall("Wall W", new Vector3(-3f, 1.25f, 0f), new Vector3(0.05f, 2.5f, 6f));

            root.SetActive(false);
            return root;
        }

        static (Transform panel, TMP_Text text) BuildPanel(Material backMat)
        {
            var panel = new GameObject("Status Panel").transform;
            panel.position = new Vector3(0f, 1.35f, 0.9f); // moved in front of the head at runtime

            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Backing";
            Object.DestroyImmediate(back.GetComponent<Collider>());
            back.transform.SetParent(panel, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            back.transform.localScale = new Vector3(0.78f, 0.52f, 0.01f);
            back.GetComponent<Renderer>().sharedMaterial = backMat;

            var textGo = new GameObject("Status Text");
            textGo.transform.SetParent(panel, false);
            var text = textGo.AddComponent<TextMeshPro>();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_InterFont);
            if (font != null) text.font = font;
            text.rectTransform.sizeDelta = new Vector2(0.72f, 0.46f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.08f;
            text.fontSizeMax = 0.3f;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.color = Color.white;
            text.richText = true;
            text.text = "Starting...";
            return (panel, text);
        }

        static Material UnlitMaterial(string name, Color color)
        {
            var path = $"{k_MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.color = color;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
