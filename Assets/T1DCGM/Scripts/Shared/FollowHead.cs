using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// Keeps a panel in front of the view, a little below eye level and above tables. It stays still while the user glances
    /// around (so it can be poked) and glides back when they turn or walk away. Give it a <see cref="dock"/> and it glides
    /// there instead (e.g. above the next food station) and waits, still facing the user.
    /// </summary>
    public class FollowHead : MonoBehaviour
    {
        public Transform head;
        [Tooltip("Offset in the head's yaw frame: x right, y up, z forward.")]
        public Vector3 offset = new Vector3(0f, -0.17f, 0.4f);
        [Tooltip("Never lower than this (m above the floor), so it stays clear of tables and counters.")]
        public float minHeight = 1.3f;
        public float angleThreshold = 30f;
        public float distanceThreshold = 0.25f;
        public float speed = 5f;

        [Header("Docked")]
        [Tooltip("When set, the panel parks here instead of following the view.")]
        public Transform dock;
        [Tooltip("Docked height: this far below the eyes, kept between the two limits below.")]
        public float dockBelowEyes = 0.1f;
        public float dockMinHeight = 1.35f, dockMaxHeight = 1.7f;
        [Tooltip("Slower glide when docking, so the learner can follow it to the station.")]
        public float dockSpeed = 2.2f;

        bool m_Moving = true;

        void Start()
        {
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
        }

        /// <summary>Parks the panel at <paramref name="target"/>, or follows the view again when null.</summary>
        public void SetDock(Transform target)
        {
            dock = target;
            m_Moving = true;
        }

        void LateUpdate()
        {
            if (head == null || head.position.y < 0.3f)
                return; // headset pose not valid yet

            Vector3 target;
            bool far;
            if (dock != null)
            {
                target = dock.position;
                target.y = Mathf.Clamp(head.position.y - dockBelowEyes, dockMinHeight, dockMaxHeight);
                far = Vector3.Distance(transform.position, target) > 0.12f; // ignore small head bobs
            }
            else
            {
                var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
                var yaw = Quaternion.LookRotation(forward.normalized, Vector3.up);
                target = head.position + yaw * offset;
                target.y = Mathf.Max(target.y, minHeight);
                var toPanel = Vector3.ProjectOnPlane(transform.position - head.position, Vector3.up);
                float angle = Vector3.Angle(toPanel, yaw * new Vector3(offset.x, 0f, offset.z));
                far = angle > angleThreshold || Vector3.Distance(transform.position, target) > distanceThreshold;
            }

            if (!m_Moving && far)
                m_Moving = true;
            if (m_Moving)
            {
                float s = dock != null ? dockSpeed : speed;
                transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-s * Time.deltaTime));
                if (Vector3.Distance(transform.position, target) < 0.01f)
                    m_Moving = false;
            }
            // Face the eyes (text reads from the -Z side), staying upright.
            var look = transform.position - head.position;
            if (look.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(look, Vector3.up);
        }

        /// <summary>Jump straight to the resting spot (e.g. after a teleport).</summary>
        public void Snap()
        {
            m_Moving = true;
            if (head == null) return;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) return;
            var p = head.position + Quaternion.LookRotation(forward.normalized, Vector3.up) * offset;
            transform.position = new Vector3(p.x, Mathf.Max(p.y, minHeight), p.z);
        }
    }
}
