using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR;
using InputDevice = UnityEngine.XR.InputDevice;

namespace T1DCGM
{
    /// <summary>
    /// Passthrough-over-Link prototype. Shows whether the OpenXR runtime exposes passthrough, lets the user toggle it,
    /// and drops markers where the controller tip touches the body, to check that virtual points line up with the real arm
    /// (the basis of the body calibration in DESIGN.md).
    ///
    /// Controls (right hand unless noted): A = drop marker, B = passthrough on/off, X (left) = bring panel in front,
    /// Y (left) = clear markers. Keyboard in the Editor: Space, P, R, C.
    /// </summary>
    public class PassthroughProbe : MonoBehaviour
    {
        const string k_PassthroughExtension = "XR_FB_passthrough";

        [Header("Scene references")]
        public ARCameraManager cameraManager;
        public Camera xrCamera;
        public Transform rightController;
        public Transform panel;
        public TMP_Text statusText;
        [Tooltip("Shown only while passthrough is off, so the user is not floating in a black void.")]
        public GameObject fallbackEnvironment;

        [Header("Markers")]
        [Tooltip("Where the touching point sits relative to the right controller's pose. Tune it so the cyan dot sits on the part you press to your skin.")]
        public Vector3 tipOffset = new Vector3(0f, -0.015f, 0.045f);
        public Material markerMaterial;
        public Material tipMaterial;
        public float markerDiameter = 0.02f;

        [Header("Feedback")]
        public AudioSource audioSource;

        InputAction m_Drop, m_Toggle, m_Recenter, m_Clear;
        readonly List<Transform> m_Markers = new List<Transform>();
        Transform m_Tip;
        bool m_PassthroughOn = true;
        bool m_PanelPlaced;
        float m_NextRefresh;
        string m_LastEvent = "";
        Color m_OpaqueBackground = new Color(0.10f, 0.11f, 0.13f, 1f);

        void Awake()
        {
            if (xrCamera == null) xrCamera = Camera.main;
            if (cameraManager == null && xrCamera != null) cameraManager = xrCamera.GetComponent<ARCameraManager>();
            if (rightController == null) rightController = FindByName("Right Controller");

            m_Drop = MakeAction("Drop marker", "<XRController>{RightHand}/primaryButton", "<Keyboard>/space");
            m_Toggle = MakeAction("Toggle passthrough", "<XRController>{RightHand}/secondaryButton", "<Keyboard>/p");
            m_Recenter = MakeAction("Recenter panel", "<XRController>{LeftHand}/primaryButton", "<Keyboard>/r");
            m_Clear = MakeAction("Clear markers", "<XRController>{LeftHand}/secondaryButton", "<Keyboard>/c");
        }

        void OnEnable()
        {
            m_Drop.Enable(); m_Toggle.Enable(); m_Recenter.Enable(); m_Clear.Enable();
            m_Drop.performed += OnDrop;
            m_Toggle.performed += OnToggle;
            m_Recenter.performed += OnRecenter;
            m_Clear.performed += OnClear;
        }

        void OnDisable()
        {
            m_Drop.performed -= OnDrop;
            m_Toggle.performed -= OnToggle;
            m_Recenter.performed -= OnRecenter;
            m_Clear.performed -= OnClear;
            m_Drop.Disable(); m_Toggle.Disable(); m_Recenter.Disable(); m_Clear.Disable();
        }

        void OnDestroy()
        {
            m_Drop?.Dispose(); m_Toggle?.Dispose(); m_Recenter?.Dispose(); m_Clear?.Dispose();
        }

        void Start()
        {
            if (rightController != null)
            {
                m_Tip = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
                m_Tip.name = "Controller Tip";
                Destroy(m_Tip.GetComponent<Collider>());
                m_Tip.SetParent(rightController, false);
                m_Tip.localPosition = tipOffset;
                m_Tip.localScale = Vector3.one * 0.008f;
                if (tipMaterial != null) m_Tip.GetComponent<Renderer>().sharedMaterial = tipMaterial;
            }
            SetPassthrough(true);
        }

        void Update()
        {
            if (m_Tip != null) m_Tip.localPosition = tipOffset; // live-tunable in the Inspector

            // The headset pose is not valid on the first frames; place the panel once it is.
            if (!m_PanelPlaced && xrCamera != null && Time.timeSinceLevelLoad > 0.75f && xrCamera.transform.position.y > 0.3f)
                PlacePanel();

            if (Time.unscaledTime >= m_NextRefresh)
            {
                m_NextRefresh = Time.unscaledTime + 0.25f;
                RefreshStatus();
            }
        }

        // ------------------------------------------------------------------ actions

        void OnDrop(InputAction.CallbackContext _)
        {
            if (m_Tip == null) return;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            marker.name = $"Marker {m_Markers.Count + 1}";
            Destroy(marker.GetComponent<Collider>());
            // Parent to the tracking space so markers stay fixed to the real room even if the rig moves.
            marker.SetParent(rightController.parent, true);
            marker.position = m_Tip.position;
            marker.localScale = Vector3.one * markerDiameter;
            if (markerMaterial != null) marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
            m_Markers.Add(marker);

            Haptic(XRNode.RightHand, 0.6f, 0.08f);
            PlayClip(ToneLibrary.Click());
            m_LastEvent = $"Dropped marker {m_Markers.Count}.";
            RefreshStatus();
        }

        void OnToggle(InputAction.CallbackContext _)
        {
            SetPassthrough(!m_PassthroughOn);
            PlayClip(ToneLibrary.Click());
            RefreshStatus();
        }

        void OnRecenter(InputAction.CallbackContext _)
        {
            PlacePanel();
            PlayClip(ToneLibrary.Click());
        }

        void OnClear(InputAction.CallbackContext _)
        {
            foreach (var m in m_Markers)
                if (m != null) Destroy(m.gameObject);
            m_Markers.Clear();
            m_LastEvent = "Markers cleared.";
            PlayClip(ToneLibrary.Click());
            RefreshStatus();
        }

        // ------------------------------------------------------------------ passthrough

        void SetPassthrough(bool on)
        {
            m_PassthroughOn = on;
            if (cameraManager != null) cameraManager.enabled = on;
            if (xrCamera != null)
            {
                xrCamera.clearFlags = CameraClearFlags.SolidColor;
                // Alpha 0 lets the passthrough layer (composited behind the app) show through.
                xrCamera.backgroundColor = on ? new Color(0f, 0f, 0f, 0f) : m_OpaqueBackground;
            }
            if (fallbackEnvironment != null) fallbackEnvironment.SetActive(!on);
            m_LastEvent = on ? "Passthrough requested ON." : "Passthrough OFF (fallback room shown).";
        }

        void PlacePanel()
        {
            if (panel == null || xrCamera == null) return;
            var head = xrCamera.transform;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            panel.position = head.position + forward * 0.9f + Vector3.down * 0.15f;
            panel.rotation = Quaternion.LookRotation(forward, Vector3.up);
            m_PanelPlaced = true;
        }

        // ------------------------------------------------------------------ status

        void RefreshStatus()
        {
            if (statusText == null) return;

            string runtime = string.IsNullOrEmpty(OpenXRRuntime.name) ? "none (headset not connected?)" : $"{OpenXRRuntime.name} {OpenXRRuntime.version}";
            bool extension = OpenXRRuntime.IsExtensionEnabled(k_PassthroughExtension);
            bool cameraRunning = cameraManager != null && cameraManager.subsystem != null && cameraManager.subsystem.running;

            var sb = new StringBuilder();
            sb.AppendLine("<b>PASSTHROUGH OVER LINK: PROTOTYPE</b>");
            sb.AppendLine($"OpenXR runtime: {runtime}");
            sb.AppendLine($"{k_PassthroughExtension}: {YesNo(extension)}   AR session: {ARSession.state}");
            sb.AppendLine($"Camera (passthrough) subsystem: {(cameraRunning ? "<color=#5f5>running</color>" : "<color=#f77>not running</color>")}");
            sb.AppendLine($"Passthrough: <b>{(m_PassthroughOn ? "ON" : "OFF")}</b>  (B to toggle)");
            sb.AppendLine();

            if (m_PassthroughOn)
            {
                if (!extension && !string.IsNullOrEmpty(OpenXRRuntime.name))
                    sb.AppendLine("<color=#fc6>The runtime did not enable passthrough. In the Meta Quest Link PC app: Settings > Beta (or Developer) > turn on Developer Runtime Features and Passthrough over Meta Quest Link. Then stop and restart Play mode.</color>");
                else
                    sb.AppendLine("If you see your real room around this panel, passthrough over Link works. Black background means it does not.");
                sb.AppendLine();
            }

            sb.AppendLine("<b>Touch test:</b> press the cyan dot on the controller against your arm and press <b>A</b>. Check the red marker sits on your skin.");
            sb.AppendLine("X: move this panel in front of you.  Y: clear markers.");
            sb.AppendLine();
            sb.Append($"Markers: {m_Markers.Count}");
            if (m_Markers.Count >= 2)
            {
                var a = m_Markers[m_Markers.Count - 2].position;
                var b = m_Markers[m_Markers.Count - 1].position;
                sb.Append($"   last two are <b>{Vector3.Distance(a, b) * 100f:0.0} cm</b> apart (check with a ruler)");
            }
            sb.AppendLine();
            if (!string.IsNullOrEmpty(m_LastEvent)) sb.Append($"<color=#aaa>{m_LastEvent}</color>");

            statusText.text = sb.ToString();
        }

        static string YesNo(bool v) => v ? "<color=#5f5>enabled</color>" : "<color=#f77>not enabled</color>";

        // ------------------------------------------------------------------ helpers

        static InputAction MakeAction(string name, params string[] bindings)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var b in bindings) action.AddBinding(b);
            return action;
        }

        static void Haptic(XRNode node, float amplitude, float seconds)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid) device.SendHapticImpulse(0u, amplitude, seconds);
        }

        void PlayClip(AudioClip clip)
        {
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        }

        static Transform FindByName(string name)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (t.name == name) return t;
            return null;
        }
    }
}
