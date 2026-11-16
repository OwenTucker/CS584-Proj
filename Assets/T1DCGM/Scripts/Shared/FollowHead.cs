using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// Keeps a panel at a comfortable spot below and to the side of the view. It stays still while the user glances
    /// around (so it can be poked) and glides back when they turn or walk away.
    /// </summary>
    public class FollowHead : MonoBehaviour
    {
        public Transform head;
        [Tooltip("Offset in the head's yaw frame: x right, y up, z forward.")]
        public Vector3 offset = new Vector3(-0.16f, -0.24f, 0.45f);
        public float angleThreshold = 30f;
        public float distanceThreshold = 0.25f;
        public float speed = 5f;

        bool m_Moving = true;

        void Start()
        {
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
        }

        void LateUpdate()
        {
            if (head == null || head.position.y < 0.3f)
                return; // headset pose not valid yet

            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            var yaw = Quaternion.LookRotation(forward.normalized, Vector3.up);
            var target = head.position + yaw * offset;

            var toPanel = Vector3.ProjectOnPlane(transform.position - head.position, Vector3.up);
            float angle = Vector3.Angle(toPanel, yaw * new Vector3(offset.x, 0f, offset.z));
            if (!m_Moving && (angle > angleThreshold || Vector3.Distance(transform.position, target) > distanceThreshold))
                m_Moving = true;
            if (m_Moving)
            {
                transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
                if (Vector3.Distance(transform.position, target) < 0.01f)
                    m_Moving = false;
            }
            // Face the eyes (text reads from the -Z side).
            transform.rotation = Quaternion.LookRotation(transform.position - head.position, Vector3.up);
        }

        /// <summary>Jump straight to the resting spot (e.g. after a teleport).</summary>
        public void Snap()
        {
            m_Moving = true;
            if (head == null) return;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) return;
            transform.position = head.position + Quaternion.LookRotation(forward.normalized, Vector3.up) * offset;
        }
    }
}
