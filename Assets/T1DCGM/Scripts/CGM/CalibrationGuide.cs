using TMPro;
using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// Small figure of the learner seen from behind, shown during arm calibration. A pulsing dot marks where to touch
    /// next (top of shoulder, point of elbow, back of upper arm); points already recorded turn green.
    /// </summary>
    public class CalibrationGuide : MonoBehaviour
    {
        [Tooltip("Touch points in calibration order: shoulder, elbow, back of upper arm.")]
        public Transform[] targets;
        [Tooltip("Shown on each target once it has been recorded.")]
        public GameObject[] doneDots;
        public Transform marker;
        public TMP_Text caption;
        public string[] captions = { "TOP OF SHOULDER", "POINT OF ELBOW", "BACK OF UPPER ARM" };
        public float pulseSpeed = 5f;

        int m_Current = -1;
        Vector3 m_MarkerScale = Vector3.one;

        void Awake()
        {
            if (marker != null) m_MarkerScale = marker.localScale;
        }

        /// <summary>Shows the figure with target <paramref name="index"/> highlighted; -1 hides it.</summary>
        public void Show(int index)
        {
            m_Current = index;
            bool visible = targets != null && index >= 0 && index < targets.Length;
            gameObject.SetActive(visible);
            if (!visible)
                return;
            if (marker != null)
                marker.position = targets[index].position;
            if (doneDots != null)
                for (int i = 0; i < doneDots.Length; i++)
                    if (doneDots[i] != null) doneDots[i].SetActive(i < index);
            if (caption != null && captions != null && index < captions.Length)
                caption.text = $"<b>{index + 1}.</b> {captions[index]}";
        }

        void Update()
        {
            if (marker == null || m_Current < 0)
                return;
            float pulse = 1f + 0.35f * Mathf.Sin(Time.time * pulseSpeed);
            marker.localScale = m_MarkerScale * pulse;
        }
    }
}
