using UnityEngine;

namespace T1DCGM
{
    /// <summary>Keeps a label floating above its parent, upright and facing the viewer.</summary>
    public class BillboardLabel : MonoBehaviour
    {
        public float height = 0.035f;

        Camera m_Camera;

        void LateUpdate()
        {
            var parent = transform.parent;
            if (parent == null)
                return;
            if (m_Camera == null)
                m_Camera = Camera.main;

            transform.position = parent.position + Vector3.up * height;
            if (m_Camera != null)
                transform.rotation = Quaternion.LookRotation(transform.position - m_Camera.transform.position, Vector3.up);
        }
    }
}
