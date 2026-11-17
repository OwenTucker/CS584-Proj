using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace T1DCGM
{
    /// <summary>
    /// Puts a grabbable back where it started when it is dropped on the floor, or on request. With <see cref="restock"/>,
    /// taking it from its spot leaves a fresh copy behind (an endless supply), and the taken one is removed instead of
    /// going home.
    /// </summary>
    public class ReturnHome : MonoBehaviour
    {
        public float floorHeight = 0.15f;
        [Tooltip("Leave a fresh copy in this spot when the item is picked up.")]
        public bool restock;

        Vector3 m_Position;
        Quaternion m_Rotation;
        XRGrabInteractable m_Grab;
        Rigidbody m_Body;
        bool m_Taken, m_SpawnPending;

        /// <summary>True for an item taken from the supply (a copy now stands in its place).</summary>
        public bool taken => m_Taken;

        void Awake()
        {
            m_Position = transform.position;
            m_Rotation = transform.rotation;
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Body = GetComponent<Rigidbody>();
            if (m_Grab != null) m_Grab.selectEntered.AddListener(OnGrabbed);
        }

        void OnDestroy()
        {
            if (m_Grab != null) m_Grab.selectEntered.RemoveListener(OnGrabbed);
        }

        void OnGrabbed(SelectEnterEventArgs _)
        {
            // Only the first pick-up from its own spot restocks; spawn next frame, outside the interaction event.
            if (restock && !m_Taken && Vector3.Distance(transform.position, m_Position) < 0.03f)
            {
                m_Taken = true;
                m_SpawnPending = true;
            }
        }

        void Update()
        {
            if (m_SpawnPending)
            {
                m_SpawnPending = false;
                Restock();
            }
            if (transform.position.y < floorHeight && (m_Grab == null || !m_Grab.isSelected))
            {
                if (m_Taken) Destroy(gameObject);
                else GoHome();
            }
        }

        void Restock()
        {
            var copy = Instantiate(gameObject, m_Position, m_Rotation, transform.parent);
            copy.name = gameObject.name;
            // The copy is made mid-grab: give it the resting physics of an item on the board, and drop any result label.
            if (copy.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            foreach (var label in copy.GetComponentsInChildren<BillboardLabel>(true))
                Destroy(label.gameObject);
        }

        /// <summary>Back to its spot; a taken copy is removed instead (the supply already has a fresh one).</summary>
        public void GoHome()
        {
            if (m_Grab != null && m_Grab.isSelected)
                return;
            if (m_Taken)
            {
                Destroy(gameObject);
                return;
            }
            if (m_Body != null)
            {
                m_Body.linearVelocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
            }
            transform.SetPositionAndRotation(m_Position, m_Rotation);
        }
    }
}
