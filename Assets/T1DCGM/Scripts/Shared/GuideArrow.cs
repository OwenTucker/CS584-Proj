using UnityEngine;

namespace T1DCGM
{
    /// <summary>Bobbing arrow that hovers over whatever the learner should interact with next.</summary>
    public class GuideArrow : MonoBehaviour
    {
        public GameObject visual;
        public float height = 0.1f;
        public float bobAmount = 0.015f;
        public float bobSpeed = 3f;
        public float spinSpeed = 90f;

        Transform m_Target;

        public void SetTarget(Transform target) => m_Target = target;

        void LateUpdate()
        {
            bool show = m_Target != null && m_Target.gameObject.activeInHierarchy;
            if (visual != null && visual.activeSelf != show)
                visual.SetActive(show);
            if (!show)
                return;

            float bob = Mathf.Sin(Time.time * bobSpeed) * bobAmount;
            transform.position = m_Target.position + Vector3.up * (height + bob);
            transform.rotation = Quaternion.Euler(0f, Time.time * spinSpeed, 0f);
        }
    }
}
