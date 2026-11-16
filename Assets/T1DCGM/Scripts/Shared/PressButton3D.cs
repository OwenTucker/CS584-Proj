using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace T1DCGM
{
    /// <summary>
    /// Physical push button. Pressed by touching it with the front of a controller (no button needed),
    /// or by selecting it with GRIP (near or with the ray).
    /// </summary>
    public class PressButton3D : XRSimpleInteractable
    {
        [Header("Button")]
        public Transform cap;
        public float pressDepth = 0.003f;
        [Tooltip("Press when a controller tip touches the button, without needing GRIP.")]
        public bool pressOnTouch = true;
        [Tooltip("How close (metres) the controller tip must get to the button's collider.")]
        public float touchDistance = 0.012f;
        [Tooltip("Pokers under this transform are ignored (e.g. the hand holding the tablet this button is on).")]
        public Transform ignorePokersUnder;
        public UnityEvent onPressed = new UnityEvent();

        Vector3 m_CapRest;
        float m_PressedAt = -10f;
        AudioSource m_Audio;
        Collider m_TouchCollider;
        readonly List<XRPokeInteractor> m_Pokers = new List<XRPokeInteractor>();
        readonly HashSet<XRPokeInteractor> m_Touching = new HashSet<XRPokeInteractor>();

        protected override void Awake()
        {
            base.Awake();
            if (cap != null)
                m_CapRest = cap.localPosition;
            m_Audio = gameObject.AddComponent<AudioSource>();
            m_Audio.playOnAwake = false;
            m_Audio.spatialBlend = 1f;
            m_Audio.minDistance = 0.3f;
            m_TouchCollider = colliders.Count > 0 ? colliders[0] : GetComponent<Collider>();
        }

        void Start()
        {
            foreach (var poker in FindObjectsByType<XRPokeInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (ignorePokersUnder == null || !poker.transform.IsChildOf(ignorePokersUnder))
                    m_Pokers.Add(poker);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            m_Touching.Clear();
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            Press(args.interactorObject as XRBaseInputInteractor);
        }

        void Press(XRBaseInputInteractor hapticTarget)
        {
            if (Time.time - m_PressedAt < 0.4f)
                return;

            m_PressedAt = Time.time;
            m_Audio.PlayOneShot(ToneLibrary.Click());
            if (hapticTarget != null)
                hapticTarget.SendHapticImpulse(0.4f, 0.05f);
            onPressed.Invoke();
        }

        void Update()
        {
            if (pressOnTouch && m_TouchCollider != null)
            {
                foreach (var poker in m_Pokers)
                {
                    if (poker == null || !poker.isActiveAndEnabled)
                        continue;

                    var point = poker.attachTransform != null ? poker.attachTransform.position : poker.transform.position;
                    float distance = Vector3.Distance(m_TouchCollider.ClosestPoint(point), point);
                    if (distance <= touchDistance)
                    {
                        // Press once per touch; the tip has to leave before it can press again.
                        if (m_Touching.Add(poker))
                            Press(null);
                    }
                    else if (distance > touchDistance * 2.5f)
                    {
                        m_Touching.Remove(poker);
                    }
                }
            }

            if (cap == null)
                return;
            float t = Mathf.Clamp01((Time.time - m_PressedAt) / 0.25f);
            cap.localPosition = m_CapRest + Vector3.down * (pressDepth * (1f - t));
        }
    }
}
