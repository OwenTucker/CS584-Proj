using TMPro;
using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// Small tablet held in the left hand: one short page at a time, with NEXT and BACK buttons the right hand pokes,
    /// and an optional CGM graph for results.
    /// </summary>
    public class InfoTablet : MonoBehaviour
    {
        public TMP_Text titleText;
        public TMP_Text bodyText;
        public TMP_Text footerText;
        public PressButton3D nextButton;
        public TMP_Text nextLabel;
        public PressButton3D backButton;
        public GameObject graphRoot;
        public CgmGraph graph;
        [Tooltip("Body text area when the graph is hidden / shown (top-left anchored size).")]
        public float bodyHeightFull = 0.085f, bodyHeightWithGraph = 0.04f;

        public System.Action onNext, onBack;

        float[] m_Readings;
        float m_PlayStart = -1f;
        const float k_PlaySeconds = 6f;

        void Awake()
        {
            if (nextButton != null) nextButton.onPressed.AddListener(() => onNext?.Invoke());
            if (backButton != null) backButton.onPressed.AddListener(() => onBack?.Invoke());
        }

        /// <summary>Shows a page. Pass readings to show a CGM graph under the text (played back over a few seconds).</summary>
        public void Show(string title, string body, string footer, string next, bool back, float[] readings = null)
        {
            if (titleText != null) titleText.text = title;
            if (bodyText != null)
            {
                bodyText.text = body;
                var size = bodyText.rectTransform.sizeDelta;
                bodyText.rectTransform.sizeDelta = new Vector2(size.x, readings != null ? bodyHeightWithGraph : bodyHeightFull);
            }
            if (footerText != null) footerText.text = footer;
            if (nextLabel != null) nextLabel.text = $"<b>{next}</b>";
            if (nextButton != null) nextButton.gameObject.SetActive(!string.IsNullOrEmpty(next));
            if (backButton != null) backButton.gameObject.SetActive(back);

            m_Readings = readings;
            if (graphRoot != null) graphRoot.SetActive(readings != null);
            m_PlayStart = readings != null ? Time.time : -1f;
            if (readings != null && graph != null) graph.Plot(readings, 1);
        }

        void Update()
        {
            if (m_PlayStart < 0f || m_Readings == null || graph == null)
                return;
            float f = Mathf.Clamp01((Time.time - m_PlayStart) / k_PlaySeconds);
            graph.Plot(m_Readings, Mathf.Max(1, Mathf.CeilToInt(f * m_Readings.Length)));
            if (f >= 1f) m_PlayStart = -1f;
        }
    }
}
