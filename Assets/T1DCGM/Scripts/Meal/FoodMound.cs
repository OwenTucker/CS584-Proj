using System.Collections.Generic;
using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// Procedural heap of a scoopable food whose size follows its volume, at true scale. The heap is a lumpy paraboloid
    /// (volume = pi r^2 h / 2, with h = shape * r); pasta adds loose penne pieces on the surface.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class FoodMound : MonoBehaviour
    {
        public FoodKind kind = FoodKind.Rice;
        [Tooltip("Millilitres of food in the heap.")]
        public float volumeMl;
        public float maxVolumeMl = 1500f;
        [Tooltip("Height / base radius. 0 uses the default for the food.")]
        public float shape;
        public float lumpiness = 0.004f;

        [Header("Pieces (pasta)")]
        public Mesh pieceMesh;
        public Material pieceMaterial;
        [Tooltip("Visible pieces per millilitre.")]
        public float piecesPerMl = 0.14f;
        public int maxPieces = 160;

        MeshFilter m_Filter;
        float m_BuiltVolume = -1f;
        readonly List<Transform> m_Pieces = new List<Transform>();
        readonly List<Vector4> m_PieceSeeds = new List<Vector4>();
        int m_Seed;

        public float radius { get; private set; }
        public float height { get; private set; }
        public float Carbs => volumeMl * FoodData.CarbsPerMl(kind);

        void Awake()
        {
            m_Filter = GetComponent<MeshFilter>();
            m_Seed = Mathf.Abs(GetInstanceID()) % 1000;
            Rebuild();
        }

        public void SetVolume(float ml)
        {
            volumeMl = Mathf.Clamp(ml, 0f, maxVolumeMl);
            if (Mathf.Abs(volumeMl - m_BuiltVolume) > 0.5f || (volumeMl == 0f && m_BuiltVolume != 0f))
                Rebuild();
        }

        public void Add(float ml) => SetVolume(volumeMl + ml);

        public void Rebuild()
        {
            if (m_Filter == null) m_Filter = GetComponent<MeshFilter>();
            m_BuiltVolume = volumeMl;
            float k = shape > 0f ? shape : FoodData.MoundShape(kind);
            float v = volumeMl * 1e-6f; // m^3
            radius = v > 0f ? Mathf.Pow(2f * v / (Mathf.PI * k), 1f / 3f) : 0f;
            height = k * radius;

            var old = m_Filter.sharedMesh;
            if (radius < 0.002f)
            {
                m_Filter.sharedMesh = null;
                GetComponent<MeshRenderer>().enabled = false;
            }
            else
            {
                GetComponent<MeshRenderer>().enabled = true;
                m_Filter.sharedMesh = BuildMesh(radius, height);
            }
            if (old != null && old != m_Filter.sharedMesh) Destroy(old);
            UpdatePieces();
        }

        Mesh BuildMesh(float r, float h)
        {
            const int rings = 9;
            var profile = new List<Vector2> { new Vector2(0f, 0f), new Vector2(r, 0f) };
            for (int i = 0; i <= rings; i++)
            {
                float rho = r * (1f - (float)i / rings);
                profile.Add(new Vector2(rho, h * (1f - (rho / r) * (rho / r))));
            }
            var mesh = MeshFactory.Lathe($"{kind} Mound", profile, 28, MeshFactory.Axis.Y);

            // Lumps: displace by noise of the position, so duplicated seam vertices move together (no cracks).
            var verts = mesh.vertices;
            float amp = Mathf.Min(lumpiness, h * 0.25f);
            for (int i = 0; i < verts.Length; i++)
            {
                var p = verts[i];
                if (p.y < 1e-4f) continue; // keep the base flat on the plate
                float n = Mathf.PerlinNoise(p.x * 60f + m_Seed, p.z * 60f + p.y * 30f) - 0.5f;
                var dir = new Vector3(p.x, p.y * 1.5f + 0.001f, p.z).normalized;
                verts[i] = p + dir * (n * 2f * amp);
            }
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void UpdatePieces()
        {
            if (pieceMesh == null || pieceMaterial == null)
                return;
            int count = Mathf.Min(maxPieces, Mathf.RoundToInt(volumeMl * piecesPerMl));
            var rng = new System.Random(m_Seed + m_PieceSeeds.Count * 7919);
            while (m_PieceSeeds.Count < count)
                m_PieceSeeds.Add(new Vector4((float)rng.NextDouble(), (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble()));
            while (m_Pieces.Count < count)
            {
                var go = new GameObject("Piece", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = pieceMesh;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = pieceMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                m_Pieces.Add(go.transform);
            }
            for (int i = 0; i < m_Pieces.Count; i++)
            {
                bool on = i < count;
                m_Pieces[i].gameObject.SetActive(on);
                if (!on) continue;
                var s = m_PieceSeeds[i];
                float rho = Mathf.Sqrt(s.x) * radius * 0.92f;
                float a = s.y * Mathf.Deg2Rad;
                float y = height * (1f - (rho / Mathf.Max(radius, 1e-4f)) * (rho / Mathf.Max(radius, 1e-4f)));
                m_Pieces[i].localPosition = new Vector3(Mathf.Cos(a) * rho, y + 0.003f, Mathf.Sin(a) * rho);
                // Piece meshes run along local Z: lie them down at a random heading with a little tilt.
                m_Pieces[i].localRotation = Quaternion.Euler(0f, s.z, 0f) * Quaternion.Euler((s.w - 0.5f) * 40f, 0f, 0f);
            }
        }
    }
}
