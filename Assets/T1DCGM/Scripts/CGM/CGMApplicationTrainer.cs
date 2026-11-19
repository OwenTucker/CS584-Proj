using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace T1DCGM
{
    /// <summary>Guidance tiers from DESIGN.md (fading scaffolding). Explicit values: serialized in scenes.</summary>
    public enum GuidanceTier { Guided = 0, HapticsOnly = 1, NoHelp = 2 }

    /// <summary>Steps of the CGM application. Explicit values: serialized in scenes.</summary>
    public enum CgmStep
    {
        Intro = 0, CalibShoulder = 1, CalibElbow = 2, CalibBack = 3, Clean = 4, PickUp = 5, Place = 6, Hold = 7, Remove = 8, Result = 9,
    }

    /// <summary>
    /// Runs the mixed-reality CGM application: intro, three-touch arm calibration, cleaning the site, applying a
    /// one-piece (press-down) applicator to the back of the real upper arm, then a score. Guidance fades over attempts:
    /// Guided (sleeve, zone, mirror, haptics), Haptics only, No help.
    /// </summary>
    public class CGMApplicationTrainer : MonoBehaviour, ISceneGate
    {
        [Header("Scene")]
        public ProgressBoard board;
        public FollowStart station;
        public Camera xrCamera;
        public Transform rightController;
        [Tooltip("Second hand that can turn the applicator (hold left GRIP near it).")]
        public Transform leftController;
        [Tooltip("How close (metres) the left controller must be to the applicator to take hold of it.")]
        public float twistReach = 0.25f;
        public GhostArm ghost;
        public ArmMirror mirror;
        public HeldTool wipe;
        public HeldTool applicator;
        [Tooltip("Figure that shows where to touch during calibration.")]
        public CalibrationGuide calibrationGuide;
        [Tooltip("Sensor shown inside the applicator until it is applied.")]
        public GameObject applicatorSensor;
        public Mesh sensorMesh;
        public SceneTeleportPad lunchPad;
        public GameObject tierButtons;
        public PressButton3D guidedButton, hapticsButton, noHelpButton;
        public AudioSource audioSource;

        [Header("Controller tip")]
        [Tooltip("Point on the right controller that touches the skin. Tune so the cyan dot sits on the part you press against your arm.")]
        public Vector3 tipOffset = new Vector3(0f, -0.015f, 0.045f);
        public Material tipMaterial;

        [Header("Rules")]
        public float maxTiltDegrees = 20f;
        public float cleanSeconds = 2.5f;
        [Tooltip("New sites should be at least this far (metres) from recent ones.")]
        public float minSiteSpacing = 0.025f;

        [Header("State")]
        public GuidanceTier tier = GuidanceTier.Guided;
        public CgmStep step = CgmStep.Intro;

        readonly ArmModel m_Arm = new ArmModel();
        InputAction m_Confirm, m_Grip, m_LeftGrip, m_Trigger, m_Recenter, m_Recalibrate;
        Transform m_Tip;
        HeldTool m_Held;
        float m_CleanTime, m_NextBuzz;
        Vector3 m_LastToolPos;
        Vector3 m_PressPoint;
        Placement m_PressPlacement;
        float m_PressTilt;
        bool m_WasGood;
        int m_Attempts;
        GameObject m_AppliedSensor;
        List<Vector2> m_OldSites = new List<Vector2>();
        bool m_Passthrough = true;
        string m_PassthroughNote = "";

        static readonly Color k_Yellow = new Color(1f, 0.84f, 0.25f);

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (xrCamera == null) xrCamera = Camera.main;
            if (rightController == null) rightController = XRInput.FindByName("Right Controller");
            if (leftController == null) leftController = XRInput.FindByName("Left Controller");

            m_Confirm = XRInput.Button("Confirm", "<XRController>{RightHand}/primaryButton", "<Keyboard>/space");
            m_Grip = XRInput.Button("Grip", "<XRController>{RightHand}/gripButton", "<Keyboard>/g");
            m_LeftGrip = XRInput.Button("Left grip", "<XRController>{LeftHand}/gripButton", "<Keyboard>/h");
            m_Trigger = XRInput.Button("Press", "<XRController>{RightHand}/triggerButton", "<Keyboard>/t");
            m_Recenter = XRInput.Button("Recenter", "<XRController>{LeftHand}/primaryButton", "<Keyboard>/r");
            m_Recalibrate = XRInput.Button("Recalibrate", "<XRController>{RightHand}/secondaryButton", "<Keyboard>/backspace");

            if (guidedButton != null) guidedButton.onPressed.AddListener(() => Retry(GuidanceTier.Guided));
            if (hapticsButton != null) hapticsButton.onPressed.AddListener(() => Retry(GuidanceTier.HapticsOnly));
            if (noHelpButton != null) noHelpButton.onPressed.AddListener(() => Retry(GuidanceTier.NoHelp));
        }

        void OnEnable()
        {
            foreach (var a in Actions) a.Enable();
            m_Confirm.performed += OnConfirm;
            m_Grip.performed += OnGripDown;
            m_Grip.canceled += OnGripUp;
            m_LeftGrip.performed += OnLeftGripDown;
            m_LeftGrip.canceled += OnLeftGripUp;
            m_Trigger.performed += OnTrigger;
            m_Recenter.performed += OnRecenter;
            m_Recalibrate.performed += OnRecalibrate;
        }

        void OnDisable()
        {
            m_Confirm.performed -= OnConfirm;
            m_Grip.performed -= OnGripDown;
            m_Grip.canceled -= OnGripUp;
            m_LeftGrip.performed -= OnLeftGripDown;
            m_LeftGrip.canceled -= OnLeftGripUp;
            m_Trigger.performed -= OnTrigger;
            m_Recenter.performed -= OnRecenter;
            m_Recalibrate.performed -= OnRecalibrate;
            foreach (var a in Actions) a.Disable();
        }

        void OnDestroy()
        {
            foreach (var a in Actions) a?.Dispose();
        }

        IEnumerable<InputAction> Actions => new[] { m_Confirm, m_Grip, m_LeftGrip, m_Trigger, m_Recenter, m_Recalibrate };

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
            m_OldSites = SiteHistory.Load();
            if (lunchPad != null) lunchPad.gate = this;
            if (tierButtons != null) tierButtons.SetActive(false);
            Enter(CgmStep.Intro);
        }

        Vector3 TipPosition => m_Tip != null ? m_Tip.position : (rightController != null ? rightController.TransformPoint(tipOffset) : Vector3.zero);

        // ------------------------------------------------------------------ input

        void OnConfirm(InputAction.CallbackContext _)
        {
            switch (step)
            {
                case CgmStep.Intro:
                    Enter(CgmStep.CalibShoulder);
                    break;
                case CgmStep.CalibShoulder:
                    m_Arm.shoulder = TipPosition;
                    Touched();
                    Enter(CgmStep.CalibElbow);
                    break;
                case CgmStep.CalibElbow:
                    m_Arm.elbow = TipPosition;
                    Touched();
                    Enter(CgmStep.CalibBack);
                    break;
                case CgmStep.CalibBack:
                    m_Arm.back = TipPosition;
                    Touched();
                    FinishCalibration();
                    break;
            }
        }

        void Touched()
        {
            XRInput.Haptic(XRNode.RightHand, 0.6f, 0.08f);
            Play(ToneLibrary.Click());
        }

        void OnGripDown(InputAction.CallbackContext _)
        {
            if (m_Held != null)
                return;
            var tip = TipPosition;
            if (step == CgmStep.Clean && wipe != null && wipe.gameObject.activeInHierarchy && wipe.InReach(tip))
                Hold(wipe);
            else if ((step == CgmStep.PickUp || step == CgmStep.Place) && applicator != null && applicator.InReach(tip))
                Hold(applicator);
        }

        void Hold(HeldTool tool)
        {
            m_Held = tool;
            tool.Grab(rightController, tipOffset);
            m_LastToolPos = tool.FacePosition;
            XRInput.Haptic(XRNode.RightHand, 0.3f, 0.05f);
            Play(ToneLibrary.Click());
            if (tool == applicator && step == CgmStep.PickUp)
                Enter(CgmStep.Place);
        }

        void OnGripUp(InputAction.CallbackContext _)
        {
            if (m_Held == null)
                return;
            var tool = m_Held;
            m_Held = null;
            tool.Release();
            if (tool == applicator && step == CgmStep.Place)
            {
                board.ShowFeedback("Dropped. GRIP to pick it up again.", FeedbackKind.Info);
                Enter(CgmStep.PickUp);
            }
            else if (tool == applicator && step == CgmStep.Remove)
            {
                // Letting go also lifts it off the arm.
                FinishApplication();
            }
        }

        // Two hands: while the right hand holds the applicator, left GRIP near it lets the left hand turn it.
        void OnLeftGripDown(InputAction.CallbackContext _)
        {
            if (m_Held != applicator || step != CgmStep.Place || leftController == null)
                return;
            if (Vector3.Distance(leftController.position, applicator.Centre) > twistReach)
                return;
            applicator.BeginTwist(leftController);
            XRInput.Haptic(XRNode.LeftHand, 0.3f, 0.05f);
        }

        void OnLeftGripUp(InputAction.CallbackContext _)
        {
            if (applicator != null && applicator.twisting)
                applicator.EndTwist();
        }

        void OnTrigger(InputAction.CallbackContext _)
        {
            if (step != CgmStep.Place || m_Held != applicator)
                return;

            var p = m_Arm.Locate(applicator.FacePosition);
            if (!p.touching)
            {
                board.ShowFeedback("Not touching your arm yet.", FeedbackKind.Error);
                Play(ToneLibrary.Error());
                return;
            }
            m_PressPlacement = p;
            m_PressTilt = Vector3.Angle(applicator.FaceDirection, -p.normal);
            m_PressPoint = applicator.FacePosition;
            // One click and the sensor is in, like the real one-piece applicators: no waiting.
            XRInput.Haptic(XRNode.RightHand, 1f, 0.15f);
            Play(ToneLibrary.Click());
            board.ShowFeedback("Click! The sensor is in.", FeedbackKind.Success);
            Enter(CgmStep.Remove);
        }

        void OnRecenter(InputAction.CallbackContext _)
        {
            if (station != null) station.Recenter();
        }

        void OnRecalibrate(InputAction.CallbackContext _)
        {
            if (step == CgmStep.Intro)
                return;
            ReleaseHeld();
            board.ShowFeedback("Calibrating again.", FeedbackKind.Info);
            Enter(CgmStep.CalibShoulder);
        }

        void ReleaseHeld()
        {
            if (m_Held == null) return;
            m_Held.Release();
            m_Held = null;
        }

        // ------------------------------------------------------------------ steps

        void FinishCalibration()
        {
            var head = xrCamera != null ? xrCamera.transform : transform;
            var outward = -Vector3.ProjectOnPlane(head.right, Vector3.up).normalized; // left arm: away from the body is to the left
            var error = m_Arm.Build(outward);
            if (error != null)
            {
                board.ShowFeedback(error, FeedbackKind.Error);
                Play(ToneLibrary.Error());
                Enter(CgmStep.CalibShoulder);
                return;
            }
            board.ShowFeedback($"Arm calibrated: shoulder to elbow {m_Arm.length * 100f:0} cm.", FeedbackKind.Success);
            Play(ToneLibrary.Success());
            StartAttempt();
        }

        void StartAttempt()
        {
            m_Attempts++;
            if (m_AppliedSensor != null) Destroy(m_AppliedSensor);
            ghost.Build(m_Arm, m_OldSites);
            if (mirror != null) mirror.Aim(m_Arm);
            if (wipe != null) { wipe.gameObject.SetActive(true); wipe.Release(); }
            if (applicator != null) { applicator.gameObject.SetActive(true); applicator.Release(); }
            if (applicatorSensor != null) applicatorSensor.SetActive(true);
            if (tierButtons != null) tierButtons.SetActive(false);
            if (lunchPad != null) lunchPad.SetAvailable(false);
            m_CleanTime = 0f;
            Enter(CgmStep.Clean);
        }

        void Retry(GuidanceTier newTier)
        {
            if (step != CgmStep.Result)
                return;
            tier = newTier;
            board.ShowFeedback($"New attempt: {TierName(tier)}.", FeedbackKind.Info);
            StartAttempt();
        }

        void Enter(CgmStep next)
        {
            step = next;
            bool calibrated = m_Arm.valid && next >= CgmStep.Clean;
            bool guided = tier == GuidanceTier.Guided;
            ghost.SetVisible(calibrated && (guided || !m_Passthrough), calibrated && guided);
            if (mirror != null) mirror.SetActive(calibrated && guided && next < CgmStep.Result);
            if (next != CgmStep.Place) ghost.ShowOutline(default, false, false);
            if (next != CgmStep.Place && applicator != null && applicator.twisting) applicator.EndTwist();
            if (calibrationGuide != null)
                calibrationGuide.Show(next switch
                {
                    CgmStep.CalibShoulder => 0,
                    CgmStep.CalibElbow => 1,
                    CgmStep.CalibBack => 2,
                    _ => -1,
                });
            m_WasGood = false;
            ShowStep();
        }

        void ShowStep()
        {
            string counter = step switch
            {
                CgmStep.Intro => "INTRO",
                CgmStep.CalibShoulder or CgmStep.CalibElbow or CgmStep.CalibBack => $"CALIBRATE  {(int)step} OF 3",
                CgmStep.Result => "RESULT",
                _ => $"APPLY  {(step == CgmStep.Remove ? 4 : (int)step - 3)} OF 4   ({TierName(tier).ToUpperInvariant()})",
            };
            float progress = (int)step / 9f;
            const string figureHint = "\n\n<color=#ffd640>Yellow dot on the figure = where.</color>";
            const string calibControls = "A: record      B: start over";
            bool guided = tier == GuidanceTier.Guided;

            switch (step)
            {
                case CgmStep.Intro:
                    board.Show(counter, "Continuous Glucose Monitor", k_Yellow,
                        "A CGM reads your glucose <b>every 5 minutes</b>, day and night.\n\n" +
                        "Today: put one on <b>your own arm</b>, then use it at lunch." +
                        (string.IsNullOrEmpty(m_PassthroughNote) ? "" : "\n\n" + m_PassthroughNote),
                        "A: continue      X: move board", progress);
                    break;
                case CgmStep.CalibShoulder:
                    board.Show(counter, "Top of your shoulder", Color.white,
                        "Left arm relaxed at your side.\nTouch the <b>cyan dot</b> on your right controller to the <b>top of your left shoulder</b>." + figureHint,
                        calibControls, progress);
                    break;
                case CgmStep.CalibElbow:
                    board.Show(counter, "Point of your elbow", Color.white,
                        "Keep still. Touch the <b>point of your left elbow</b>." + figureHint,
                        calibControls, progress);
                    break;
                case CgmStep.CalibBack:
                    board.Show(counter, "Back of your upper arm", Color.white,
                        "Touch the <b>back of your upper arm</b>, halfway down. Find it by feel." + figureHint,
                        calibControls, progress);
                    break;
                case CgmStep.Clean:
                    board.Show(counter, "Clean the site", Color.white,
                        "GRIP the <b>alcohol wipe</b> and rub the back of your upper arm" + (guided ? " (green area)" : "") + ".",
                        "GRIP: pick up      B: recalibrate", progress);
                    break;
                case CgmStep.PickUp:
                    board.Show(counter, "Pick up the applicator", Color.white,
                        "Let the skin dry.\n\nGRIP the <b>applicator</b> at any angle and keep holding.",
                        "GRIP: pick up      B: recalibrate", progress);
                    break;
                case CgmStep.Place:
                    board.Show(counter, "Place and click", Color.white,
                        "Hold it <b>flat on the back of your arm</b>" + (guided ? ", in the green area" : "") + ", then pull the <b>TRIGGER</b>." +
                        "\n\n<size=85%>To turn it: hold <b>left GRIP</b> near it and twist your left hand.</size>" +
                        (tier == GuidanceTier.HapticsOnly ? "\n<size=85%>A buzz means wrong spot.</size>" : "") +
                        (tier == GuidanceTier.NoHelp ? "\n<size=85%>No help this time.</size>" : ""),
                        "GRIP: hold      TRIGGER: click in      Left GRIP: turn", progress);
                    break;
                case CgmStep.Remove:
                    board.Show(counter, "Pull away", Color.white,
                        "Pull the applicator <b>straight off</b>. The sensor stays on.",
                        "Move your hand away", progress);
                    break;
            }
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            board.RefreshLive();
            if (m_Tip != null) m_Tip.localPosition = tipOffset;

            switch (step)
            {
                case CgmStep.Clean: UpdateClean(); break;
                case CgmStep.Place: UpdatePlace(); break;
                case CgmStep.Remove: UpdateRemove(); break;
            }
        }

        void UpdateClean()
        {
            if (m_Held != wipe)
                return;
            var pos = wipe.FacePosition;
            float speed = (pos - m_LastToolPos).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            m_LastToolPos = pos;
            var p = m_Arm.Locate(pos);
            if (!p.touching)
                return;

            if (!p.inZone)
            {
                Buzz(0.35f);
                if (tier == GuidanceTier.Guided)
                    board.ShowFeedback("Clean the back of the upper arm.", FeedbackKind.Info);
                return;
            }

            if (speed > 0.02f)
            {
                m_CleanTime += Time.deltaTime;
                if (Time.time >= m_NextBuzz)
                {
                    XRInput.Haptic(XRNode.RightHand, 0.12f, 0.03f);
                    m_NextBuzz = Time.time + 0.1f;
                }
            }

            if (m_CleanTime >= cleanSeconds)
            {
                ReleaseHeld();
                wipe.gameObject.SetActive(false);
                board.ShowFeedback("Site cleaned.", FeedbackKind.Success);
                Play(ToneLibrary.Success());
                Enter(CgmStep.PickUp);
            }
            else if (tier != GuidanceTier.NoHelp)
            {
                board.ShowFeedback($"Cleaning... {Mathf.RoundToInt(100f * m_CleanTime / cleanSeconds)}%", FeedbackKind.Info);
            }
        }

        void UpdatePlace()
        {
            if (m_Held != applicator)
                return;
            var p = m_Arm.Locate(applicator.FacePosition);
            float tilt = Vector3.Angle(applicator.FaceDirection, -p.normal);
            bool flat = tilt <= maxTiltDegrees;
            bool good = p.touching && p.inZone && flat;
            bool nearSkin = p.surfaceGap < 0.08f && p.t > -0.2f && p.t < 1.2f;

            if (tier == GuidanceTier.Guided)
                ghost.ShowOutline(p, good, nearSkin);

            if (tier != GuidanceTier.NoHelp && p.touching)
            {
                if (!p.inZone) Buzz(0.5f);
                else if (!flat) Buzz(0.15f);
                else if (!m_WasGood) XRInput.Haptic(XRNode.RightHand, 0.25f, 0.04f);
            }
            m_WasGood = good;

            if (tier == GuidanceTier.Guided && nearSkin)
            {
                string where = p.inZone ? "<color=#7f7>in the zone</color>" : "<color=#f77>outside the zone</color>";
                string flatText = flat ? $"<color=#7f7>flat ({tilt:0} degrees)</color>" : $"<color=#f77>tilted {tilt:0} degrees, keep it under {maxTiltDegrees:0}</color>";
                string touch = p.touching ? "touching skin" : $"{Mathf.Max(0f, p.surfaceGap) * 100f:0} cm from skin";
                board.ShowFeedback($"{touch}, {where}, {flatText}", good ? FeedbackKind.Success : FeedbackKind.Info);
            }
        }

        void UpdateRemove()
        {
            if (Vector3.Distance(applicator.FacePosition, m_PressPoint) > 0.04f)
                FinishApplication();
        }

        void FinishApplication()
        {
            if (step != CgmStep.Remove)
                return;
            ReleaseHeld();
            if (applicator != null) applicator.gameObject.SetActive(false);
            if (applicatorSensor != null) applicatorSensor.SetActive(false);
            m_AppliedSensor = ghost.PlaceSensor(sensorMesh, m_PressPlacement.site);
            ShowResult();
        }

        // ------------------------------------------------------------------ result

        void ShowResult()
        {
            var site = m_PressPlacement.site;
            bool inZone = m_PressPlacement.inZone;
            bool flat = m_PressTilt <= maxTiltDegrees;
            float nearest = float.MaxValue;
            foreach (var old in m_OldSites)
                nearest = Mathf.Min(nearest, m_Arm.SkinDistance(site, old));
            bool newSite = nearest >= minSiteSpacing;

            SiteHistory.Add(site);
            m_OldSites = SiteHistory.Load();

            int passed = (inZone ? 1 : 0) + (flat ? 1 : 0) + (newSite ? 1 : 0);
            var sb = new StringBuilder();
            sb.AppendLine(Check(inZone, "Back of the upper arm", inZone ? "" : WhereText(m_PressPlacement)));
            sb.AppendLine(Check(flat, $"Flat ({m_PressTilt:0}° tilt)", flat ? "" : "Tilting can bend the filament."));
            sb.AppendLine(Check(newSite, "New spot",
                newSite ? "" : $"{nearest * 100f:0.0} cm from an old site. Keep 2.5 cm apart."));
            sb.AppendLine();
            sb.Append("Readings start in about an hour, then <b>every 5 minutes</b>.");

            step = CgmStep.Result;
            ghost.SetVisible(tier == GuidanceTier.Guided || !m_Passthrough, tier == GuidanceTier.Guided);
            ghost.ShowOutline(default, false, false);
            if (mirror != null) mirror.SetActive(false);
            board.Show($"RESULT   ({TierName(tier).ToUpperInvariant()})", passed == 3 ? "CGM applied!" : $"Applied: {passed} of 3 checks",
                passed == 3 ? new Color(0.45f, 0.95f, 0.55f) : k_Yellow, sb.ToString(),
                "Next: teleport to LUNCH      Retry: poke a tray button", 1f);
            Play(passed == 3 ? ToneLibrary.Finish() : ToneLibrary.Success());
            if (tierButtons != null) tierButtons.SetActive(true);
            if (lunchPad != null) lunchPad.SetAvailable(true);
            CgmSession.applied = true;
            CgmSession.attempts = m_Attempts;
        }

        static string Check(bool ok, string label, string why) =>
            (ok ? "<color=#7f7>PASS</color>  " : "<color=#f77>MISS</color>  ") + label + (string.IsNullOrEmpty(why) ? "" : $"\n        <size=80%>{why}</size>");

        static string WhereText(Placement p)
        {
            if (p.t < ArmModel.ZoneTMin) return "Too close to the shoulder.";
            if (p.t > ArmModel.ZoneTMax) return "Too close to the elbow.";
            return p.angle > 0f ? "Too far round to the outside of the arm." : "Too far round toward the inside of the arm.";
        }

        static string TierName(GuidanceTier t) => t switch
        {
            GuidanceTier.Guided => "Guided",
            GuidanceTier.HapticsOnly => "Haptics only",
            _ => "No help",
        };

        // ------------------------------------------------------------------ helpers

        void Buzz(float amplitude)
        {
            if (Time.time < m_NextBuzz) return;
            XRInput.Haptic(XRNode.RightHand, amplitude, 0.06f);
            m_NextBuzz = Time.time + 0.15f;
        }

        void Play(AudioClip clip)
        {
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        }

        /// <summary>Called by <see cref="PassthroughFallback"/>; without passthrough the sleeve is always shown.</summary>
        public void SetPassthroughAvailable(bool available, string note)
        {
            m_Passthrough = available;
            m_PassthroughNote = available ? "" : note;
            if (step == CgmStep.Result)
                ghost.SetVisible(tier == GuidanceTier.Guided || !m_Passthrough, tier == GuidanceTier.Guided);
            else
                Enter(step);
            if (!available)
                board.ShowFeedback("No passthrough: using a virtual room.", FeedbackKind.Info);
        }

        public bool TryLeave()
        {
            if (step == CgmStep.Result)
                return true;
            board.ShowFeedback("Finish applying your CGM first.", FeedbackKind.Error);
            Play(ToneLibrary.Error());
            return false;
        }
    }

    /// <summary>Carries a little state from the CGM scene to the lunch scene.</summary>
    public static class CgmSession
    {
        public static bool applied;
        public static int attempts;
    }
}
