using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// A tool picked up with GRIP that snaps to the controller tip, so the part of the real controller that touches the
    /// skin is where the virtual tool touches it. The tool's <see cref="face"/> sits on the tip, and the face's -Y
    /// (pointing out of the face) lines up with the controller's forward direction.
    /// Release GRIP to put it back where it started.
    /// </summary>
    public class HeldTool : MonoBehaviour
    {
        [Tooltip("Contact face of the tool. Its local -Y points out of the face, toward the skin.")]
        public Transform face;
        public float pickupRadius = 0.12f;
        public string displayName = "tool";

        Vector3 m_HomePosition;
        Quaternion m_HomeRotation;
        Transform m_HomeParent;
        Transform m_Controller;
        Vector3 m_TipOffset;

        public bool held => m_Controller != null;

        void Awake()
        {
            m_HomeParent = transform.parent;
            m_HomePosition = transform.localPosition;
            m_HomeRotation = transform.localRotation;
        }

        public bool InReach(Vector3 tip) => Vector3.Distance(tip, ClosestPoint()) <= pickupRadius;

        Vector3 ClosestPoint()
        {
            var col = GetComponentInChildren<Collider>();
            return col != null ? col.bounds.center : transform.position;
        }

        public void Grab(Transform controller, Vector3 tipOffset)
        {
            m_Controller = controller;
            m_TipOffset = tipOffset;
        }

        public void Release()
        {
            m_Controller = null;
            transform.SetParent(m_HomeParent, false);
            transform.localPosition = m_HomePosition;
            transform.localRotation = m_HomeRotation;
        }

        void OnEnable() => Application.onBeforeRender += Follow;
        void OnDisable() => Application.onBeforeRender -= Follow;

        // Again just before rendering, after the controller pose gets its late update, so the tool does not lag behind.
        void LateUpdate() => Follow();

        void Follow()
        {
            if (m_Controller == null || face == null)
                return;

            // Rotation that sends the face's out-direction (-Y) to the controller's forward.
            var faceRotLocal = Quaternion.Inverse(transform.rotation) * face.rotation;
            var targetFaceRot = m_Controller.rotation * Quaternion.FromToRotation(Vector3.down, Vector3.forward);
            transform.rotation = targetFaceRot * Quaternion.Inverse(faceRotLocal);
            var tip = m_Controller.TransformPoint(m_TipOffset);
            transform.position += tip - face.position;
        }

        /// <summary>World direction pointing out of the face (into the skin when applied).</summary>
        public Vector3 FaceDirection => face != null ? -face.up : -transform.up;
        public Vector3 FacePosition => face != null ? face.position : transform.position;
    }
}
