using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR;

namespace T1DCGM
{
    /// <summary>
    /// If passthrough does not start (headset without colour passthrough, or the "Passthrough over Meta Quest Link"
    /// developer setting is off), switches to an opaque virtual room instead of leaving the learner in black.
    /// The calibration still gives real touch on the real arm; the virtual sleeve stands in for seeing it.
    /// Left Y (P on the keyboard) switches between passthrough and the virtual room by hand.
    /// </summary>
    public class PassthroughFallback : MonoBehaviour
    {
        const string k_Extension = "XR_FB_passthrough";

        public Camera xrCamera;
        public ARCameraManager cameraManager;
        public GameObject fallbackRoom;
        public CGMApplicationTrainer trainer;
        [Tooltip("Seconds to wait for passthrough before falling back.")]
        public float timeout = 2.5f;
        public Color roomBackground = new Color(0.55f, 0.62f, 0.7f, 1f);

        bool m_Decided;
        bool m_PassthroughOn = true;
        InputAction m_Toggle;

        void Awake()
        {
            m_Toggle = XRInput.Button("Toggle passthrough", "<XRController>{LeftHand}/secondaryButton", "<Keyboard>/p");
        }

        void OnEnable() { m_Toggle.Enable(); m_Toggle.performed += OnToggle; }
        void OnDisable() { m_Toggle.performed -= OnToggle; m_Toggle.Disable(); }
        void OnDestroy() => m_Toggle?.Dispose();

        void Start()
        {
            if (fallbackRoom != null) fallbackRoom.SetActive(false);
        }

        void Update()
        {
            if (m_Decided)
                return;

            bool runtimeUp = !string.IsNullOrEmpty(OpenXRRuntime.name);
            bool extension = runtimeUp && OpenXRRuntime.IsExtensionEnabled(k_Extension);
            bool running = cameraManager != null && cameraManager.subsystem != null && cameraManager.subsystem.running;

            if (extension && running)
            {
                m_Decided = true;
                Debug.Log($"[T1D] Passthrough running ({OpenXRRuntime.name} {OpenXRRuntime.version}). Left Y toggles the virtual room.");
                return;
            }
            // The runtime reports its extensions as soon as it starts, so a missing extension is final.
            if ((runtimeUp && !extension) || Time.timeSinceLevelLoad > timeout)
            {
                m_Decided = true;
                SetPassthrough(false, runtimeUp && !extension
                    ? "Passthrough is not available from the headset runtime."
                    : "Passthrough did not start.");
            }
        }

        void OnToggle(InputAction.CallbackContext _)
        {
            m_Decided = true;
            SetPassthrough(!m_PassthroughOn, "Passthrough switched off (left Y switches it back on).");
        }

        void SetPassthrough(bool on, string reason)
        {
            m_PassthroughOn = on;
            if (cameraManager != null) cameraManager.enabled = on;
            if (xrCamera != null)
            {
                xrCamera.clearFlags = CameraClearFlags.SolidColor;
                // Alpha 0 lets the passthrough layer (composited behind the app) show through.
                xrCamera.backgroundColor = on ? new Color(0f, 0f, 0f, 0f) : roomBackground;
            }
            if (fallbackRoom != null) fallbackRoom.SetActive(!on);
            Debug.Log(on ? "[T1D] Passthrough on." : $"[T1D] {reason} Using the virtual room. Runtime: '{OpenXRRuntime.name}'.");
            if (trainer != null)
                trainer.SetPassthroughAvailable(on, on ? "" :
                    $"<size=80%><color=#fc6>{reason} You are in a virtual room; your arm is shown as a blue sleeve. " +
                    "For your real room: Meta Quest Link app > Settings > Beta: turn on Developer Runtime Features and " +
                    "Passthrough over Meta Quest Link, then restart Play mode (Quest 2 shows it in black and white).</color></size>");
        }
    }
}
