using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// A tool picked up with GRIP that follows the controller tip.
    /// By default it snaps so the tool's <see cref="face"/> sits on the tip, with the face's -Y (pointing out of the face)
    /// along the controller's forward direction: the part of the real controller that touches the skin is where the
    /// virtual tool touches it.
    /// With <see cref="keepGrabAngle"/>, the tool instead keeps the angle it had relative to the hand when it was picked
    /// up (its middle sits on the tip), so it can be held however is comfortable, and a second hand can turn it
    /// (<see cref="BeginTwist"/>).
    /// Release GRIP to put it back where it started.
    /// </summary>
    public class HeldTool : MonoBehaviour
    {
        [Tooltip("Contact face of the tool. Its local -Y points out of the face, toward the skin.")]
        public Transform face;
        public float pickupRadius = 0.12f;
        public string displayName = "tool";
        [Tooltip("Keep the angle the tool had relative to the controller when grabbed, instead of snapping the face forward.")]
        public bool keepGrabAngle;

        Vector3 m_HomePosition;
        Quaternion m_HomeRotation;
        Transform m_HomeParent;
        Transform m_Controller;
        Vector3 m_TipOffset;
        Quaternion m_GrabRotation;
        Vector3 m_GrabCentre;
        Transform m_Twister;
        Quaternion m_TwistStart;

        public bool held => m_Controller != null;
        public bool twisting => m_Twister != null;
        /// <summary>World centre of the tool (its collider's middle).</summary>
        public Vector3 Centre => ClosestPoint();

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
            m_GrabRotation = Quaternion.Inverse(controller.rotation) * transform.rotation;
            m_GrabCentre = transform.InverseTransformPoint(ClosestPoint());
        }

        /// <summary>
        /// Second hand takes hold (only with <see cref="keepGrabAngle"/>): turning <paramref name="otherHand"/> turns the
        /// tool by the same amount around its middle, while the first hand keeps carrying it.
        /// </summary>
        public void BeginTwist(Transform otherHand)
        {
            if (!held || !keepGrabAngle || otherHand == null)
                return;
            m_Twister = otherHand;
            m_TwistStart = otherHand.rotation;
        }

        /// <summary>Second hand lets go: the new angle stays.</summary>
        public void EndTwist()
        {
            if (m_Twister == null)
                return;
            if (m_Controller != null)
                m_GrabRotation = Quaternion.Inverse(m_Controller.rotation) * transform.rotation;
            m_Twister = null;
        }

        public void Release()
        {
            m_Twister = null;
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

            var tip = m_Controller.TransformPoint(m_TipOffset);
            if (keepGrabAngle)
            {
                // Same angle in the hand as when it was picked up; its middle rides on the controller tip.
                var rotation = m_Controller.rotation * m_GrabRotation;
                if (m_Twister != null)
                    rotation = m_Twister.rotation * Quaternion.Inverse(m_TwistStart) * rotation; // add the second hand's turn
                transform.rotation = rotation;
                transform.position += tip - transform.TransformPoint(m_GrabCentre);
                return;
            }

            // Rotation that sends the face's out-direction (-Y) to the controller's forward.
            var faceRotLocal = Quaternion.Inverse(transform.rotation) * face.rotation;
            var targetFaceRot = m_Controller.rotation * Quaternion.FromToRotation(Vector3.down, Vector3.forward);
            transform.rotation = targetFaceRot * Quaternion.Inverse(faceRotLocal);
            transform.position += tip - face.position;
        }

        /// <summary>World direction pointing out of the face (into the skin when applied).</summary>
        public Vector3 FaceDirection => face != null ? -face.up : -transform.up;
        public Vector3 FacePosition => face != null ? face.position : transform.position;
    }
}
