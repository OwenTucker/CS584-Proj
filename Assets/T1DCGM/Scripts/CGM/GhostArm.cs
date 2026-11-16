using System.Collections.Generic;
using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// Virtual stand-in for the learner's calibrated upper arm: a see-through sleeve, the allowed zone in green, old sites
    /// in red, and a ring showing where the applicator would land. The real arm is visible through passthrough; this is
    /// what the mirror can show (mirrors cannot reflect passthrough).
    /// </summary>
    public class GhostArm : MonoBehaviour
    {
        public Material sleeveMaterial;
        public Material zoneMaterial;
        public Material oldSiteMaterial;
        public Material outlineMaterial;
        public Material sensorMaterial;

        [Tooltip("Radius of the CGM sensor (a typical on-arm sensor is about 35 mm across).")]
        public float sensorRadius = 0.0175f;

        GameObject m_Sleeve, m_Zone;
        readonly List<GameObject> m_OldSites = new List<GameObject>();
        LineRenderer m_Outline;
        ArmModel m_Arm;

        static readonly Color k_Good = new Color(0.3f, 1f, 0.45f);
        static readonly Color k_Bad = new Color(1f, 0.35f, 0.3f);
        static readonly Color k_Idle = new Color(1f, 1f, 1f, 0.8f);

        public void Build(ArmModel arm, IList<Vector2> oldSites)
        {
            Clear();
            m_Arm = arm;
            m_Sleeve = Part("Sleeve", TubePatch(arm, -0.05f, 1.05f, -180f, 180f, arm.radius - 0.004f, 24, 6), sleeveMaterial);
            m_Zone = Part("Allowed Zone", TubePatch(arm, ArmModel.ZoneTMin, ArmModel.ZoneTMax, -ArmModel.ZoneHalfAngle,
                ArmModel.ZoneHalfAngle, arm.radius + 0.002f, 12, 8), zoneMaterial);

            foreach (var site in oldSites)
            {
                var dot = Part("Old Site", Disc(sensorRadius * 0.8f), oldSiteMaterial);
                PlaceOnSkin(dot.transform, site, 0.003f);
                m_OldSites.Add(dot);
            }

            if (m_Outline == null)
            {
                var go = new GameObject("Applicator Outline");
                go.transform.SetParent(transform, false);
                m_Outline = go.AddComponent<LineRenderer>();
                m_Outline.useWorldSpace = true;
                m_Outline.loop = true;
                m_Outline.positionCount = 32;
                m_Outline.widthMultiplier = 0.003f;
                m_Outline.sharedMaterial = outlineMaterial;
                m_Outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            m_Outline.enabled = false;
        }

        public void SetVisible(bool visible)
        {
            if (m_Sleeve != null) m_Sleeve.SetActive(visible);
            if (m_Zone != null) m_Zone.SetActive(visible);
            foreach (var s in m_OldSites) if (s != null) s.SetActive(visible);
            if (!visible && m_Outline != null) m_Outline.enabled = false;
        }

        /// <summary>Draws the sensor footprint where the applicator face would meet the skin.</summary>
        public void ShowOutline(Placement p, bool good, bool show)
        {
            if (m_Outline == null || m_Arm == null)
                return;
            m_Outline.enabled = show;
            if (!show)
                return;

            var centre = m_Arm.CentreAt(p.t) + p.normal * (m_Arm.radius + 0.004f);
            var right = Vector3.Cross(p.normal, m_Arm.axis).normalized;
            for (int i = 0; i < m_Outline.positionCount; i++)
            {
                float a = i * Mathf.PI * 2f / m_Outline.positionCount;
                m_Outline.SetPosition(i, centre + (right * Mathf.Cos(a) + m_Arm.axis * Mathf.Sin(a)) * sensorRadius);
            }
            var c = p.touching ? (good ? k_Good : k_Bad) : k_Idle;
            m_Outline.startColor = m_Outline.endColor = c;
        }

        /// <summary>Leaves a sensor on the skin (stays visible in every guidance tier: it is on the real arm).</summary>
        public GameObject PlaceSensor(Mesh sensorMesh, Vector2 site)
        {
            var go = new GameObject("Applied CGM Sensor", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = sensorMesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = sensorMaterial;
            PlaceOnSkin(go.transform, site, 0f);
            return go;
        }

        void PlaceOnSkin(Transform t, Vector2 site, float lift)
        {
            var normal = m_Arm.DirectionAt(site.y);
            t.position = m_Arm.CentreAt(site.x) + normal * (m_Arm.radius + lift);
            t.rotation = Quaternion.LookRotation(m_Arm.axis, normal); // local up = skin normal
        }

        public void Clear()
        {
            foreach (Transform child in transform)
                if (m_Outline == null || child != m_Outline.transform)
                    Destroy(child.gameObject);
            m_OldSites.Clear();
            m_Sleeve = m_Zone = null;
        }

        GameObject Part(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Curved patch of the arm cylinder, in world space (this object stays at the origin).</summary>
        static Mesh TubePatch(ArmModel arm, float t0, float t1, float a0, float a1, float r, int around, int along)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int j = 0; j <= along; j++)
            {
                float t = Mathf.Lerp(t0, t1, (float)j / along);
                for (int i = 0; i <= around; i++)
                {
                    float a = Mathf.Lerp(a0, a1, (float)i / around);
                    var n = arm.DirectionAt(a);
                    verts.Add(arm.CentreAt(t) + n * r);
                    normals.Add(n);
                    uvs.Add(new Vector2((float)i / around, (float)j / along));
                }
            }
            int row = around + 1;
            for (int j = 0; j < along; j++)
                for (int i = 0; i < around; i++)
                {
                    int v = j * row + i;
                    // Both windings so the patch is visible from inside and outside.
                    tris.AddRange(new[] { v, v + row, v + 1, v + 1, v + row, v + row + 1 });
                    tris.AddRange(new[] { v, v + 1, v + row, v + 1, v + row + 1, v + row });
                }
            var mesh = new Mesh { name = "Arm Patch" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Flat disc in the local XZ plane (normal +Y).</summary>
        static Mesh Disc(float radius)
        {
            const int n = 20;
            var verts = new List<Vector3> { Vector3.zero };
            var tris = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                verts.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
                if (i > 0) tris.AddRange(new[] { 0, i + 1, i });
            }
            var mesh = new Mesh { name = "Disc" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
