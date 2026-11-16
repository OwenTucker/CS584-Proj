using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;

namespace T1DCGM.EditorTools
{
    public static partial class DemoSceneBuilder
    {
        const int k_UILayer = 5; // boards and the mirror screen: hidden from the mirror camera

        /// <summary>
        /// Scene 1, mixed reality: the learner's real room and arm through passthrough. Everything the learner uses sits on a
        /// "station" that moves in front of them when the scene starts (X recentres it), because the real room is unknown.
        /// </summary>
        static void BuildCgmScene()
        {
            var scene = NewScene();
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.65f, 0.65f, 0.65f);

            var rig = AddXRRig(Vector3.zero, 0f);
            var cam = rig.GetComponentInChildren<Camera>(true);
            ConfigurePassthroughCamera(cam);
            new GameObject("AR Session").AddComponent<ARSession>();

            // ---- station (moves in front of the user at start)
            var stationGo = new GameObject("Station (moves in front of you)");
            var station = stationGo.AddComponent<FollowStart>();
            station.head = cam.transform;
            var st = stationGo.transform;

            var board = BuildBoard("Board", st, new Vector3(0f, 1.45f, 0.95f), Quaternion.Euler(0f, 0f, 0f), new Vector2(0.95f, 0.62f));
            SetLayer(board.gameObject, k_UILayer);

            // Tray to the right with the wipe and the applicator.
            var tray = new GameObject("Supply Tray").transform;
            tray.SetParent(st, false);
            tray.localPosition = new Vector3(0.34f, 0.92f, 0.42f);
            tray.localRotation = Quaternion.Euler(0f, -20f, 0f);
            Box("Tray", tray, s_Mats["Teal"], new Vector3(0f, -0.01f, 0f), new Vector3(0.36f, 0.02f, 0.26f), false);
            var wipe = BuildWipe(tray, new Vector3(-0.1f, 0f, 0.03f));
            var applicator = BuildApplicator(tray, new Vector3(0.08f, 0f, 0.03f), out var applicatorSensor);

            var tierRoot = new GameObject("Try Again Buttons").transform;
            tierRoot.SetParent(tray, false);
            tierRoot.localPosition = new Vector3(0f, 0f, -0.09f);
            var guided = PokeButton("Guided Button", tierRoot, new Vector3(-0.11f, 0f, 0f), "GUIDED", s_Mats["Green"], out _);
            var haptics = PokeButton("Haptics Button", tierRoot, new Vector3(0f, 0f, 0f), "HAPTICS", s_Mats["Blue"], out _);
            var noHelp = PokeButton("No Help Button", tierRoot, new Vector3(0.11f, 0f, 0f), "NO HELP", s_Mats["Red"], out _);
            var tryLabel = Text3D("Try Again Label", tierRoot, "<b>TRY AGAIN</b>", 0.2f, new Color(1f, 0.84f, 0.25f), new Vector2(0.3f, 0.04f));
            tryLabel.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            tryLabel.gameObject.AddComponent<BillboardLabel>().height = 0.12f;

            // Mirror screen front-left, angled toward the user.
            var mirrorRoot = new GameObject("Mirror Screen").transform;
            mirrorRoot.SetParent(st, false);
            var mirrorPos = new Vector3(-0.5f, 1.35f, 0.62f);
            mirrorRoot.localPosition = mirrorPos;
            mirrorRoot.localRotation = Quaternion.LookRotation(new Vector3(mirrorPos.x, 0f, mirrorPos.z).normalized, Vector3.up);
            Box("Frame", mirrorRoot, s_Mats["Dark"], new Vector3(0f, 0f, 0.006f), new Vector3(0.44f, 0.44f, 0.01f), false);
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.transform.SetParent(mirrorRoot, false);
            screen.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
            screen.GetComponent<Renderer>().sharedMaterial = s_Mats["MirrorScreen"];
            var mirrorLabel = Text3D("Label", mirrorRoot, "<b>MIRROR</b>  back of your left arm", 0.14f, Color.white, new Vector2(0.44f, 0.03f));
            mirrorLabel.transform.localPosition = new Vector3(0f, 0.245f, 0f);
            SetLayer(mirrorRoot.gameObject, k_UILayer);

            var lunchPad = ScenePad(st, "Lunch Pad", new Vector3(0f, 0f, 1.4f), 0f, "LUNCH", k_LunchSceneName);

            // ---- world-space parts (the calibrated arm lives in tracking space)
            var ghostGo = new GameObject("Ghost Arm");
            var ghost = ghostGo.AddComponent<GhostArm>();
            ghost.sleeveMaterial = s_Mats["Sleeve"];
            ghost.zoneMaterial = s_Mats["Zone"];
            ghost.oldSiteMaterial = s_Mats["OldSite"];
            ghost.outlineMaterial = s_Mats["Line"];
            ghost.sensorMaterial = s_Mats["Sensor"];

            var mirrorCamGo = new GameObject("Mirror Camera");
            var mirrorCam = mirrorCamGo.AddComponent<Camera>();
            mirrorCam.fieldOfView = 45f;
            mirrorCam.nearClipPlane = 0.05f;
            mirrorCam.farClipPlane = 5f;
            mirrorCam.cullingMask = ~(1 << k_UILayer);
            mirrorCam.enabled = false;
            var mirrorUrp = mirrorCam.GetUniversalAdditionalCameraData();
            if (mirrorUrp != null) mirrorUrp.renderPostProcessing = false;
            var mirror = mirrorCamGo.AddComponent<ArmMirror>();
            mirror.mirrorCamera = mirrorCam;
            mirror.display = screen.GetComponent<Renderer>();

            // ---- trainer
            var trainerGo = new GameObject("CGM Application Trainer");
            var trainer = trainerGo.AddComponent<CGMApplicationTrainer>();
            trainer.board = board;
            trainer.station = station;
            trainer.xrCamera = cam;
            trainer.rightController = FindChild(rig, "Right Controller");
            trainer.ghost = ghost;
            trainer.mirror = mirror;
            trainer.wipe = wipe;
            trainer.applicator = applicator;
            trainer.applicatorSensor = applicatorSensor;
            trainer.sensorMesh = SensorMesh();
            trainer.lunchPad = lunchPad;
            trainer.tierButtons = tierRoot.gameObject;
            trainer.guidedButton = guided;
            trainer.hapticsButton = haptics;
            trainer.noHelpButton = noHelp;
            trainer.tipMaterial = s_Mats["Tip"];
            trainer.audioSource = AddAudio(trainerGo);
            if (trainer.rightController == null)
                Debug.LogWarning("[T1D] 'Right Controller' not found under the XR Origin; the trainer will search at runtime.");

            // Fallback (DESIGN.md): no passthrough -> simple virtual room instead of black.
            var fallback = trainerGo.AddComponent<PassthroughFallback>();
            fallback.xrCamera = cam;
            fallback.cameraManager = cam.GetComponent<ARCameraManager>();
            fallback.fallbackRoom = BuildFallbackRoom();
            fallback.trainer = trainer;

            EditorSceneManager.SaveScene(scene, k_CgmScenePath);
        }

        /// <summary>Plain room around the start point, shown only when passthrough is unavailable.</summary>
        static GameObject BuildFallbackRoom()
        {
            var root = new GameObject("Virtual Room (no passthrough)");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
            floor.GetComponent<Renderer>().sharedMaterial = s_Mats["Floor"];
            const float h = 2.7f, t = 0.1f, half = 3.5f;
            var wall = s_Mats["Wall"];
            Box("Wall N", root.transform, wall, new Vector3(0f, h / 2f, half), new Vector3(2f * half, h, t), false);
            Box("Wall S", root.transform, wall, new Vector3(0f, h / 2f, -half), new Vector3(2f * half, h, t), false);
            Box("Wall E", root.transform, wall, new Vector3(half, h / 2f, 0f), new Vector3(t, h, 2f * half), false);
            Box("Wall W", root.transform, wall, new Vector3(-half, h / 2f, 0f), new Vector3(t, h, 2f * half), false);
            root.SetActive(false);
            return root;
        }

        static void ConfigurePassthroughCamera(Camera cam)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // alpha 0 lets passthrough show behind the app
            cam.allowHDR = false;
            var urp = cam.GetUniversalAdditionalCameraData();
            if (urp != null) urp.renderPostProcessing = false;
            if (cam.GetComponent<ARCameraManager>() == null)
                cam.gameObject.AddComponent<ARCameraManager>();
            var bg = cam.GetComponent<ARCameraBackground>();
            if (bg != null) Object.DestroyImmediate(bg, true);
        }

        static void SetLayer(GameObject root, int layer)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }

        static Mesh SensorMesh() => SaveMesh("CGM_Sensor", MeshFactory.Lathe("CGM_Sensor", new[]
        {
            V(0f, 0f), V(0.0175f, 0f), V(0.0175f, 0.0028f), V(0.0155f, 0.0048f), V(0.006f, 0.0052f), V(0f, 0.0052f),
        }, 40, MeshFactory.Axis.Y));

        /// <summary>One-piece, press-down applicator (generic, no branding). Pivot and face at the bottom centre.</summary>
        static HeldTool BuildApplicator(Transform parent, Vector3 localPosition, out GameObject sensorInside)
        {
            var root = new GameObject("CGM Applicator");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;

            Part("Body", root.transform, SaveMesh("Applicator_Body", MeshFactory.Lathe("Applicator_Body", new[]
            {
                V(0.0215f, 0.0f), V(0.0245f, 0.002f), V(0.0255f, 0.035f), V(0.024f, 0.048f), V(0.02f, 0.052f), V(0f, 0.053f),
            }, 40, MeshFactory.Axis.Y)), s_Mats["Applicator"]);
            Part("Grip Ring", root.transform, SaveMesh("Applicator_Ring", MeshFactory.Lathe("Applicator_Ring", new[]
            {
                V(0.0257f, 0.03f), V(0.0262f, 0.031f), V(0.0262f, 0.04f), V(0.0255f, 0.041f),
            }, 40, MeshFactory.Axis.Y)), s_Mats["ApplicatorAccent"]);

            sensorInside = Part("Sensor (inside)", root.transform, SensorMesh(), s_Mats["Sensor"]);
            sensorInside.transform.localPosition = new Vector3(0f, 0.001f, 0f);

            var face = new GameObject("Face").transform; // -Y points out of the face
            face.SetParent(root.transform, false);

            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, 0.026f, 0f);
            col.size = new Vector3(0.052f, 0.053f, 0.052f);

            var tool = root.AddComponent<HeldTool>();
            tool.face = face;
            tool.displayName = "applicator";
            var label = Text3D("Label", root.transform, "<b>CGM applicator</b>", 0.12f, Color.white, new Vector2(0.2f, 0.03f));
            label.gameObject.AddComponent<BillboardLabel>().height = 0.09f;
            return tool;
        }

        static HeldTool BuildWipe(Transform parent, Vector3 localPosition)
        {
            var packet = Box("Wipe Packet", parent, s_Mats["WipePacket"], localPosition + new Vector3(0f, 0.002f, 0.05f), new Vector3(0.06f, 0.004f, 0.06f), false);
            var packetLabel = Text3D("Label", packet.transform.parent, "<b>alcohol wipe</b>", 0.12f, Color.white, new Vector2(0.2f, 0.03f));
            packetLabel.transform.localPosition = localPosition + new Vector3(0f, 0.08f, 0.02f);

            var root = new GameObject("Alcohol Wipe");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition + new Vector3(0f, 0.006f, -0.02f);
            var pad = Part("Pad", root.transform, SaveMesh("Wipe_Pad", MeshFactory.RoundedBox("Wipe_Pad", new Vector3(0.045f, 0.004f, 0.045f), 0.006f)), s_Mats["Wipe"]);
            pad.transform.localPosition = new Vector3(0f, 0.002f, 0f);
            var face = new GameObject("Face").transform;
            face.SetParent(root.transform, false);
            var col = root.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, 0.002f, 0f);
            col.size = new Vector3(0.05f, 0.01f, 0.05f);
            var tool = root.AddComponent<HeldTool>();
            tool.face = face;
            tool.displayName = "alcohol wipe";
            return tool;
        }
    }
}
