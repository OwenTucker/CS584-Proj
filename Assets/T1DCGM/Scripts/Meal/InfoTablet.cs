using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace T1DCGM
{
    /// <summary>A picture (or control panel) shown on part of the tablet screen for one kind of page.</summary>
    [System.Serializable]
    public class TabletVisual
    {
        public string key;
        public GameObject root;
        [Tooltip("Text left, picture right. Otherwise the text is a short band at the top and the picture fills the rest.")]
        public bool split = true;
        [Tooltip("Optional number on the picture (a guess, a target), set by the page.")]
        public TMP_Text value;
    }

    /// <summary>
    /// Floating tablet: one short page at a time with a picture, NEXT and BACK buttons (also the controller's A and B),
    /// +/- buttons for estimates, bar charts and a CGM graph for results.
    /// </summary>
    public class InfoTablet : MonoBehaviour
    {
        public TMP_Text titleText;
        public TMP_Text bodyText;
        public TMP_Text footerText;
        public PressButton3D nextButton;
        public TMP_Text nextLabel;
        public PressButton3D backButton;
        public TMP_Text backLabel;
        public GameObject graphRoot;
        public CgmGraph graph;
        public FollowHead mover;

        [Header("Pictures")]
        public List<TabletVisual> visuals = new List<TabletVisual>();
        public TabletChart resultChart;
        public TabletChart summaryChart;

        [Header("Estimate buttons")]
        public PressButton3D[] adjustButtons;
        public float[] adjustAmounts;

        [Header("Body text layout (tablet local, metres)")]
        public float bodyTop = 0.066f;
        public float bodyLeft = -0.156f;
        public float fullWidth = 0.312f, splitWidth = 0.15f;
        public float fullHeight = 0.15f, bandHeight = 0.034f;

        public System.Action onNext, onBack;
        public System.Action<float> onAdjust;

        float[] m_Readings;
        float m_PlayStart = -1f;
        TabletVisual m_Visual;
        const float k_PlaySeconds = 6f;

        void Awake()
        {
            if (nextButton != null) nextButton.onPressed.AddListener(() => onNext?.Invoke());
            if (backButton != null) backButton.onPressed.AddListener(() => onBack?.Invoke());
            if (adjustButtons != null)
                for (int i = 0; i < adjustButtons.Length; i++)
                {
                    float amount = adjustAmounts != null && i < adjustAmounts.Length ? adjustAmounts[i] : 0f;
                    if (adjustButtons[i] != null) adjustButtons[i].onPressed.AddListener(() => onAdjust?.Invoke(amount));
                }
        }

        /// <summary>
        /// Shows a page. <paramref name="next"/> / <paramref name="back"/> label the two buttons (null or empty hides one).
        /// <paramref name="visual"/> picks a picture by key; pass readings to play a CGM graph along the bottom.
        /// </summary>
        public void Show(string title, string body, string footer, string next, string back, string visual = null, float[] readings = null)
        {
            if (titleText != null) titleText.text = title;
            if (footerText != null) footerText.text = footer;
            if (nextLabel != null) nextLabel.text = $"<b>{next}</b>";
            if (nextButton != null) nextButton.gameObject.SetActive(!string.IsNullOrEmpty(next));
            if (backLabel != null) backLabel.text = $"<b>{back}</b>";
            if (backButton != null) backButton.gameObject.SetActive(!string.IsNullOrEmpty(back));

            m_Visual = null;
            foreach (var v in visuals)
            {
                bool on = v.key == visual;
                if (v.root != null) v.root.SetActive(on);
                if (on) m_Visual = v;
            }

            if (bodyText != null)
            {
                bodyText.text = body;
                bool split = m_Visual != null && m_Visual.split;
                bool band = !split && (m_Visual != null || readings != null);
                float w = split ? splitWidth : fullWidth;
                var rt = bodyText.rectTransform;
                rt.sizeDelta = new Vector2(w, band ? bandHeight : fullHeight);
                var p = rt.localPosition;
                rt.localPosition = new Vector3(bodyLeft + w / 2f, bodyTop, p.z);
            }

            m_Readings = readings;
            if (graphRoot != null) graphRoot.SetActive(readings != null);
            m_PlayStart = readings != null ? Time.time : -1f;
            if (readings != null && graph != null) graph.Plot(readings, 1);
        }

        /// <summary>Sets the number shown on the current picture (e.g. the estimate as it changes).</summary>
        public void SetValue(string text)
        {
            if (m_Visual != null && m_Visual.value != null) m_Visual.value.text = text;
        }

        /// <summary>Glide to a spot (above a station), or back in front of the user when null.</summary>
        public void Dock(Transform target)
        {
            if (mover != null) mover.SetDock(target);
        }

        /// <summary>Plays the current CGM graph again from the start.</summary>
        public void Replay()
        {
            if (m_Readings != null) m_PlayStart = Time.time;
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
