using TMPro;
using UnityEngine;

namespace T1DCGM
{
    /// <summary>One horizontal bar: a pivot at its left end (scaled in X), the coloured fill, and a value at its tip.</summary>
    [System.Serializable]
    public class TabletBar
    {
        public Transform pivot;
        public Renderer fill;
        public TMP_Text value;
    }

    /// <summary>
    /// Simple bar chart drawn on the tablet: rows of grams (what you counted vs what you ate), a glucose chip per row
    /// (up arrow: runs high, down arrow: can go low, tick: in range) and an optional star score. Bars grow in when shown.
    /// </summary>
    public class TabletChart : MonoBehaviour
    {
        public TabletBar[] bars;
        public TMP_Text[] labels;
        public TMP_Text[] chips;
        public GameObject[] rows;
        public TMP_Text stars;
        [Tooltip("Width (m) of a bar at the chart's largest value.")]
        public float maxWidth = 0.13f;
        public float growSeconds = 0.6f;

        public static readonly Color InRange = new Color(0.35f, 0.9f, 0.45f);
        public static readonly Color High = new Color(1f, 0.65f, 0.15f);
        public static readonly Color Low = new Color(1f, 0.3f, 0.3f);
        public static readonly Color Ate = new Color(0.78f, 0.84f, 0.92f);
        public const float CloseGrams = 7.5f;

        float[] m_Target;
        float m_ShownAt;

        /// <summary>Colour for a count: green if close, orange if counted low (glucose runs high), red if counted high.</summary>
        public static Color CountColor(float counted, float ate)
        {
            float miss = counted - ate;
            return Mathf.Abs(miss) <= CloseGrams ? InRange : miss < 0f ? High : Low;
        }

        /// <summary>Glucose chip text for a count.</summary>
        public static string Chip(float counted, float ate)
        {
            float miss = counted - ate;
            if (Mathf.Abs(miss) <= CloseGrams) return $"<color=#{Hex(InRange)}>✓ IN RANGE</color>";
            return miss < 0f ? $"<color=#{Hex(High)}>↑ HIGH</color>" : $"<color=#{Hex(Low)}>↓ LOW</color>";
        }

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        public void ShowRows(int count)
        {
            if (rows == null) return;
            for (int i = 0; i < rows.Length; i++)
                if (rows[i] != null) rows[i].SetActive(i < count);
        }

        public void SetLabel(int i, string text)
        {
            if (labels != null && i < labels.Length && labels[i] != null) labels[i].text = text;
        }

        public void SetChip(int i, string text)
        {
            if (chips != null && i < chips.Length && chips[i] != null) chips[i].text = text;
        }

        public void SetStars(int earned, int of)
        {
            if (stars == null) return;
            var sb = new System.Text.StringBuilder("<color=#ffd84a>");
            for (int i = 0; i < of; i++)
            {
                if (i == earned) sb.Append("</color><color=#4a5566>");
                sb.Append('★');
            }
            stars.text = sb.Append("</color>").ToString();
        }

        /// <summary>Sets bar <paramref name="i"/> to <paramref name="grams"/> on a scale where <paramref name="scale"/> fills the bar.</summary>
        public void SetBar(int i, float grams, float scale, Color color)
        {
            if (bars == null || i >= bars.Length || bars[i] == null) return;
            if (m_Target == null || m_Target.Length != bars.Length) m_Target = new float[bars.Length];
            m_Target[i] = scale > 0f ? maxWidth * Mathf.Clamp01(grams / scale) : 0f;
            var b = bars[i];
            if (b.fill != null) b.fill.material.color = color;
            if (b.value != null) b.value.text = $"<b>{grams:0} g</b>";
            m_ShownAt = Time.time;
            Apply(i, 0f);
        }

        void Apply(int i, float f)
        {
            var b = bars[i];
            float w = Mathf.Max(0.0005f, m_Target[i] * f);
            if (b.pivot != null)
            {
                var s = b.pivot.localScale;
                b.pivot.localScale = new Vector3(w, s.y, s.z);
                if (b.value != null)
                {
                    var p = b.value.transform.localPosition;
                    b.value.transform.localPosition = new Vector3(b.pivot.localPosition.x + w + 0.004f, p.y, p.z);
                }
            }
        }

        void Update()
        {
            if (m_Target == null) return;
            float t = growSeconds > 0f ? Mathf.Clamp01((Time.time - m_ShownAt) / growSeconds) : 1f;
            float f = 1f - (1f - t) * (1f - t); // ease out
            for (int i = 0; i < bars.Length && i < m_Target.Length; i++)
                if (bars[i] != null) Apply(i, f);
        }

        void OnEnable() => m_ShownAt = Time.time;
    }
}
