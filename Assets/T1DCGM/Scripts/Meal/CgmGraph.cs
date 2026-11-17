using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// CGM app screen: one dot per reading (every 5 minutes), the 70 to 180 mg/dL target band, and the latest value.
    /// Drawn in this transform's local XY plane, origin at the bottom-left corner, facing -Z (readable from the front).
    /// </summary>
    public class CgmGraph : MonoBehaviour
    {
        public float width = 0.6f;
        public float height = 0.3f;
        public float minMg = 40f, maxMg = 350f;
        public float hours = 4f;
        public Material lineMaterial;
        public TMP_Text valueText;
        public TMP_Text captionText;

        public const float MinutesPerReading = 5f;
        public const float Low = 70f, High = 180f;

        LineRenderer m_Line;
        readonly List<Transform> m_Dots = new List<Transform>();
        Mesh m_DotMesh;

        static readonly Color k_InRange = new Color(0.35f, 0.9f, 0.45f);
        static readonly Color k_HighColor = new Color(1f, 0.65f, 0.15f);
        static readonly Color k_LowColor = new Color(1f, 0.25f, 0.25f);

        void Awake()
        {
            m_Line = NewLine("Readings Line", Mathf.Max(0.0008f, width * 0.005f), new Color(1f, 1f, 1f, 0.6f));
            Band(Low, k_LowColor);
            Band(High, k_HighColor);
        }

        LineRenderer NewLine(string name, float w, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.widthMultiplier = w;
            lr.sharedMaterial = lineMaterial;
            lr.startColor = lr.endColor = c;
            lr.positionCount = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.alignment = LineAlignment.TransformZ;
            return lr;
        }

        void Band(float mg, Color c)
        {
            var lr = NewLine($"{mg} mg/dL", Mathf.Max(0.0005f, width * 0.0025f), new Color(c.r, c.g, c.b, 0.7f));
            lr.positionCount = 2;
            lr.SetPosition(0, Point(0f, mg, -0.001f));
            lr.SetPosition(1, Point(hours * 60f, mg, -0.001f));
        }

        Vector3 Point(float minutes, float mg, float z = -0.002f) =>
            new Vector3(width * Mathf.Clamp01(minutes / (hours * 60f)), height * Mathf.InverseLerp(minMg, maxMg, Mathf.Clamp(mg, minMg, maxMg)), z);

        /// <summary>Shows the first <paramref name="count"/> readings.</summary>
        public void Plot(IList<float> readings, int count)
        {
            count = Mathf.Clamp(count, 0, readings.Count);
            m_Line.positionCount = count;
            if (m_DotMesh == null) m_DotMesh = Quad();
            while (m_Dots.Count < count)
            {
                var go = new GameObject("Reading", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = m_DotMesh;
                go.GetComponent<MeshRenderer>().material = lineMaterial;
                go.transform.localScale = Vector3.one * Mathf.Max(0.0015f, width * 0.012f);
                m_Dots.Add(go.transform);
            }
            for (int i = 0; i < m_Dots.Count; i++)
            {
                bool on = i < count;
                m_Dots[i].gameObject.SetActive(on);
                if (!on) continue;
                var p = Point(i * MinutesPerReading, readings[i], -0.003f);
                m_Dots[i].localPosition = p;
                m_Dots[i].GetComponent<MeshRenderer>().material.color = ColorFor(readings[i]);
                m_Line.SetPosition(i, Point(i * MinutesPerReading, readings[i]));
            }

            if (valueText != null)
            {
                if (count == 0) { valueText.text = "--"; return; }
                float v = readings[count - 1];
                string trend = "";
                if (count >= 4)
                {
                    float slope = (v - readings[count - 4]) / (3f * MinutesPerReading); // mg/dL per minute
                    trend = slope > 2f ? "  ^^" : slope > 1f ? "  ^" : slope < -2f ? "  vv" : slope < -1f ? "  v" : "  ->";
                }
                string mg = v <= 40f ? "LOW" : Mathf.RoundToInt(v).ToString();
                valueText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(ColorFor(v))}>{mg}</color><size=45%> mg/dL{trend}</size>";
            }
            if (captionText != null)
                captionText.text = $"{(count - 1) * MinutesPerReading / 60f:0.0} h after lunch";
        }

        public static Color ColorFor(float mg) => mg < Low ? k_LowColor : mg > High ? k_HighColor : k_InRange;

        static Mesh Quad()
        {
            var m = new Mesh { name = "Dot" };
            m.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) });
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            m.RecalculateNormals();
            return m;
        }

        /// <summary>
        /// Very simple teaching model (not for dosing). The dose covers <paramref name="dosedCarbs"/>; the meal had
        /// <paramref name="eatenCarbs"/>. Carbs act faster than rapid insulin, so even a perfect dose gives a moderate
        /// post-meal rise; any mismatch shows up hours later as a high or a low.
        /// </summary>
        public static float[] Simulate(float eatenCarbs, float dosedCarbs, float hours = 4f, float start = 110f)
        {
            const float mgPerGram = 3f;          // teaching model: glucose change per gram of carbs not matched by insulin
            const float carbTau = 40f, insulinTau = 55f;
            int n = Mathf.RoundToInt(hours * 60f / MinutesPerReading) + 1;
            var values = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i * MinutesPerReading;
                float g = start + mgPerGram * (eatenCarbs * Absorbed(t, carbTau) - dosedCarbs * Absorbed(t, insulinTau));
                // Small sensor noise so it looks like real readings.
                g += (Mathf.PerlinNoise(i * 0.37f, eatenCarbs * 0.01f) - 0.5f) * 6f;
                values[i] = Mathf.Clamp(g, 40f, 400f);
            }
            return values;
        }

        // Fraction absorbed by time t for a gamma(2) action curve.
        static float Absorbed(float t, float tau) => 1f - (1f + t / tau) * Mathf.Exp(-t / tau);
    }
}
