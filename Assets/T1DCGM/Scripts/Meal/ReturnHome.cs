using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace T1DCGM
{
    /// <summary>Puts a grabbable back where it started when it is dropped on the floor, or on request.</summary>
    public class ReturnHome : MonoBehaviour
    {
        public float floorHeight = 0.15f;

        Vector3 m_Position;
        Quaternion m_Rotation;
        XRGrabInteractable m_Grab;
        Rigidbody m_Body;

        void Awake()
        {
            m_Position = transform.position;
            m_Rotation = transform.rotation;
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Body = GetComponent<Rigidbody>();
        }

        void Update()
        {
            if (transform.position.y < floorHeight && (m_Grab == null || !m_Grab.isSelected))
                GoHome();
        }

        public void GoHome()
        {
            if (m_Grab != null && m_Grab.isSelected)
                return;
            if (m_Body != null)
            {
                m_Body.linearVelocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
            }
            transform.SetPositionAndRotation(m_Position, m_Rotation);
        }
    }
}
