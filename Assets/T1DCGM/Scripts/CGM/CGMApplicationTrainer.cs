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
        public GhostArm ghost;
        public ArmMirror mirror;
        public HeldTool wipe;
        public HeldTool applicator;
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
        public float holdSeconds = 3f;
        public float maxHoldMovement = 0.015f;
        [Tooltip("New sites should be at least this far (metres) from recent ones.")]
        public float minSiteSpacing = 0.025f;

        [Header("State")]
        public GuidanceTier tier = GuidanceTier.Guided;
        public CgmStep step = CgmStep.Intro;

        readonly ArmModel m_Arm = new ArmModel();
        InputAction m_Confirm, m_Grip, m_Trigger, m_Recenter, m_Recalibrate;
        Transform m_Tip;
        HeldTool m_Held;
        float m_CleanTime, m_HoldTime, m_MaxHoldMove, m_NextBuzz;
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

            m_Confirm = XRInput.Button("Confirm", "<XRController>{RightHand}/primaryButton", "<Keyboard>/space");
            m_Grip = XRInput.Button("Grip", "<XRController>{RightHand}/gripButton", "<Keyboard>/g");
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
            m_Trigger.performed += OnTrigger;
            m_Recenter.performed += OnRecenter;
            m_Recalibrate.performed += OnRecalibrate;
        }

        void OnDisable()
        {
            m_Confirm.performed -= OnConfirm;
            m_Grip.performed -= OnGripDown;
            m_Grip.canceled -= OnGripUp;
            m_Trigger.performed -= OnTrigger;
            m_Recenter.performed -= OnRecenter;
            m_Recalibrate.performed -= OnRecalibrate;
            foreach (var a in Actions) a.Disable();
        }

        void OnDestroy()
        {
            foreach (var a in Actions) a?.Dispose();
        }

        IEnumerable<InputAction> Actions => new[] { m_Confirm, m_Grip, m_Trigger, m_Recenter, m_Recalibrate };

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
            if (tool == applicator && (step == CgmStep.Place || step == CgmStep.Hold))
            {
                board.ShowFeedback("You let go of the applicator. Pick it up again with GRIP.", FeedbackKind.Error);
                Enter(CgmStep.PickUp);
            }
            else if (tool == applicator && step == CgmStep.Remove)
            {
                // Letting go also lifts it off the arm.
                FinishApplication();
            }
        }

        void OnTrigger(InputAction.CallbackContext _)
        {
            if (step != CgmStep.Place || m_Held != applicator)
                return;

            var p = m_Arm.Locate(applicator.FacePosition);
            if (!p.touching)
            {
                board.ShowFeedback("The applicator is not touching your arm. Rest it flat on the skin first, then squeeze.", FeedbackKind.Error);
                Play(ToneLibrary.Error());
                return;
            }
            m_PressPlacement = p;
            m_PressTilt = Vector3.Angle(applicator.FaceDirection, -p.normal);
            m_PressPoint = applicator.FacePosition;
            m_HoldTime = 0f;
            m_MaxHoldMove = 0f;
            XRInput.Haptic(XRNode.RightHand, 1f, 0.15f);
            Play(ToneLibrary.Click());
            Enter(CgmStep.Hold);
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
                _ => $"APPLY  {(int)step - 3} OF 5   ({TierName(tier).ToUpperInvariant()})",
            };
            float progress = (int)step / 9f;
            const string tipHint = "the cyan dot on your right controller";
            bool guided = tier == GuidanceTier.Guided;

            switch (step)
            {
                case CgmStep.Intro:
                    board.Show(counter, "Continuous Glucose Monitor (CGM)", k_Yellow,
                        "To make informed decisions about your insulin doses, a CGM gives you a glucose reading <b>every 5 minutes</b>, day and night.\n\n" +
                        "A small sensor sits on the back of your upper arm with a thin filament just under the skin. Most sensors last 10 to 15 days, then you apply a new one at a new spot.\n\n" +
                        "Today: apply a CGM to <b>your own arm</b>, then use it at lunch." +
                        (string.IsNullOrEmpty(m_PassthroughNote) ? "" : "\n\n" + m_PassthroughNote),
                        "A: continue      X (left): move this board in front of you", progress);
                    break;
                case CgmStep.CalibShoulder:
                    board.Show(counter, "Calibrate your arm", Color.white,
                        "Stand still with your <b>left arm relaxed at your side</b>.\n\n" +
                        $"Touch {tipHint} to the <b>top of your left shoulder</b>, then press A.",
                        "A: record touch      B: start calibration again", progress);
                    break;
                case CgmStep.CalibElbow:
                    board.Show(counter, "Calibrate your arm", Color.white,
                        $"Keep the arm where it is. Touch {tipHint} to the <b>point of your left elbow</b>, then press A.",
                        "A: record touch      B: start calibration again", progress);
                    break;
                case CgmStep.CalibBack:
                    board.Show(counter, "Calibrate your arm", Color.white,
                        $"Now touch the <b>back of your left upper arm</b>, halfway between shoulder and elbow, then press A.\n\nYou cannot see this spot. Find it by feel.",
                        "A: record touch      B: start calibration again", progress);
                    break;
                case CgmStep.Clean:
                    board.Show(counter, "Clean the site", Color.white,
                        "Pick up the <b>alcohol wipe</b> from the tray with GRIP and rub it on the back of your upper arm for a few seconds" +
                        (guided ? " (the green area in the mirror)." : ".") +
                        "\n\nSkin oils and lotion stop the adhesive from sticking.",
                        "Hold GRIP: pick up      B: recalibrate", progress);
                    break;
                case CgmStep.PickUp:
                    board.Show(counter, "Pick up the applicator", Color.white,
                        "Let the skin air dry.\n\nPick up the <b>CGM applicator</b> from the tray with GRIP and keep holding GRIP.",
                        "Hold GRIP: pick up      B: recalibrate", progress);
                    break;
                case CgmStep.Place:
                    board.Show(counter, "Place and press", Color.white,
                        "Rest the applicator <b>flat against the back of your upper arm</b>" + (guided ? ", inside the green area" : "") + ".\n\n" +
                        "Then squeeze the <b>TRIGGER</b> to push it down firmly." +
                        (tier == GuidanceTier.HapticsOnly ? "\n\nThe controller buzzes when you are outside the right area." : "") +
                        (tier == GuidanceTier.NoHelp ? "\n\nNo help this time. You will see your score afterwards." : ""),
                        "Hold GRIP: hold applicator      TRIGGER: press down", progress);
                    break;
                case CgmStep.Hold:
                    board.Show(counter, "Hold it there", Color.white,
                        "Keep the applicator still against your arm while the filament goes in.",
                        "Keep holding GRIP", progress);
                    break;
                case CgmStep.Remove:
                    board.Show(counter, "Pull the applicator away", Color.white,
                        "Pull the applicator <b>straight away</b> from your arm. The sensor stays on your skin.",
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
                case CgmStep.Hold: UpdateHold(); break;
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
                    board.ShowFeedback("Clean the spot where the sensor will go: the back of the upper arm.", FeedbackKind.Info);
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

        void UpdateHold()
        {
            float moved = Vector3.Distance(applicator.FacePosition, m_PressPoint);
            m_MaxHoldMove = Mathf.Max(m_MaxHoldMove, moved);
            m_HoldTime += Time.deltaTime;
            if (tier != GuidanceTier.NoHelp && moved > maxHoldMovement)
                Buzz(0.4f);
            board.ShowFeedback($"Holding... {Mathf.Max(0f, holdSeconds - m_HoldTime):0.0} s", FeedbackKind.Info);
            if (m_HoldTime >= holdSeconds)
            {
                XRInput.Haptic(XRNode.RightHand, 0.3f, 0.05f);
                Enter(CgmStep.Remove);
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
            bool steady = m_MaxHoldMove <= maxHoldMovement;
            float nearest = float.MaxValue;
            foreach (var old in m_OldSites)
                nearest = Mathf.Min(nearest, m_Arm.SkinDistance(site, old));
            bool newSite = nearest >= minSiteSpacing;

            SiteHistory.Add(site);
            m_OldSites = SiteHistory.Load();

            int passed = (inZone ? 1 : 0) + (flat ? 1 : 0) + (steady ? 1 : 0) + (newSite ? 1 : 0);
            var sb = new StringBuilder();
            sb.AppendLine(Check(inZone, "Site: back of the upper arm", inZone ? "" : WhereText(m_PressPlacement)));
            sb.AppendLine(Check(flat, $"Flat against the skin ({m_PressTilt:0} degrees tilt)", flat ? "" : "A tilted applicator can bend the filament."));
            sb.AppendLine(Check(steady, $"Held still for {holdSeconds:0} seconds", steady ? "" : "Moving while it inserts can loosen the sensor."));
            sb.AppendLine(Check(newSite, "New spot",
                newSite ? "" : $"Only {nearest * 100f:0.0} cm from a recent site. Rotate sites at least 2.5 cm (1 inch) apart."));
            sb.AppendLine();
            sb.AppendLine("Your CGM is warming up. In about an hour it will send a reading to your phone <b>every 5 minutes</b>.");
            sb.Append("<size=80%>Tip: avoid spots you lie on at night. Pressure on the sensor can cause false low readings.</size>");

            step = CgmStep.Result;
            ghost.SetVisible(tier == GuidanceTier.Guided || !m_Passthrough, tier == GuidanceTier.Guided);
            ghost.ShowOutline(default, false, false);
            if (mirror != null) mirror.SetActive(false);
            board.Show($"RESULT   ({TierName(tier).ToUpperInvariant()})", passed == 4 ? "CGM applied!" : $"Sensor applied: {passed} of 4 checks passed",
                passed == 4 ? new Color(0.45f, 0.95f, 0.55f) : k_Yellow, sb.ToString(),
                "Teleport to the green LUNCH pad to continue.   Poke a button on the tray to try again.", 1f);
            Play(passed == 4 ? ToneLibrary.Finish() : ToneLibrary.Success());
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
