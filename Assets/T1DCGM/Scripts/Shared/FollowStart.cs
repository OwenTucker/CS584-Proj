using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// Moves this transform (a station root on the floor) under the user's head and turns it to face where they look,
    /// once the headset pose is valid. Used in mixed reality, where the content has to fit around the user's real room
    /// instead of a fixed virtual layout. Call <see cref="Recenter"/> to do it again.
    /// </summary>
    public class FollowStart : MonoBehaviour
    {
        public Transform head;
        [Tooltip("Seconds to wait after the scene loads before trusting the headset pose.")]
        public float delay = 0.6f;

        bool m_Placed;

        public bool placed => m_Placed;

        void Start()
        {
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
        }

        void Update()
        {
            if (!m_Placed && head != null && Time.timeSinceLevelLoad > delay && head.position.y > 0.3f)
                Recenter();
        }

        public void Recenter()
        {
            if (head == null)
                return;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f)
                forward = Vector3.forward;
            transform.SetPositionAndRotation(new Vector3(head.position.x, transform.position.y, head.position.z),
                Quaternion.LookRotation(forward.normalized, Vector3.up));
            m_Placed = true;
        }
    }
}
