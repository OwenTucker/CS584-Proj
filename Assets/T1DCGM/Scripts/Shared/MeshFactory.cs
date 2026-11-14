using System.Collections.Generic;
using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// Procedural modelling helpers used to build the custom medical equipment meshes (vial, syringe, cartridge, inserter...).
    /// </summary>
    public static class MeshFactory
    {
        public enum Axis { Y, Z }

        /// <summary>
        /// Revolves a profile of (radius, height) points around an axis. Order the profile from the bottom centre,
        /// outward, up the side, and back in to the top centre so the normals face outward.
        /// Each profile segment gets its own ring of vertices, so edges between segments stay crisp.
        /// </summary>
        public static Mesh Lathe(string name, IList<Vector2> profile, int sides = 32, Axis axis = Axis.Y)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            float totalLength = 0f;
            for (int i = 1; i < profile.Count; i++)
                totalLength += Vector2.Distance(profile[i - 1], profile[i]);
            float run = 0f;

            for (int s = 0; s < profile.Count - 1; s++)
            {
                Vector2 a = profile[s];
                Vector2 b = profile[s + 1];
                Vector2 d = b - a;
                if (d.sqrMagnitude < 1e-12f)
                    continue;

                // Outward normal in (radius, height) space.
                Vector2 n = new Vector2(d.y, -d.x).normalized;
                float v0 = run / totalLength;
                run += d.magnitude;
                float v1 = run / totalLength;

                int baseIndex = vertices.Count;
                for (int i = 0; i <= sides; i++)
                {
                    float u = (float)i / sides;
                    float angle = u * Mathf.PI * 2f;
                    float c = Mathf.Cos(angle), sn = Mathf.Sin(angle);

                    vertices.Add(Orient(new Vector3(a.x * c, a.x * sn, a.y), axis));
                    vertices.Add(Orient(new Vector3(b.x * c, b.x * sn, b.y), axis));
                    Vector3 normal = Orient(new Vector3(n.x * c, n.x * sn, n.y), axis);
                    normals.Add(normal);
                    normals.Add(normal);
                    uvs.Add(new Vector2(u, v0));
                    uvs.Add(new Vector2(u, v1));
                }

                for (int i = 0; i < sides; i++)
                {
                    int a0 = baseIndex + i * 2;      // start ring, angle i
                    int b0 = a0 + 1;                 // end ring, angle i
                    int a1 = a0 + 2;                 // start ring, angle i+1
                    int b1 = a0 + 3;                 // end ring, angle i+1
                    triangles.Add(a0); triangles.Add(a1); triangles.Add(b0);
                    triangles.Add(a1); triangles.Add(b1); triangles.Add(b0);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Simple closed cylinder along the chosen axis from h0 to h1.</summary>
        public static Mesh Cylinder(string name, float radius, float h0, float h1, int sides = 24, Axis axis = Axis.Z)
        {
            return Lathe(name, new[]
            {
                new Vector2(0f, h0), new Vector2(radius, h0),
                new Vector2(radius, h1), new Vector2(0f, h1),
            }, sides, axis);
        }

        /// <summary>Box with softly bevelled vertical edges, built as an extruded rounded rectangle (Y is up).</summary>
        public static Mesh RoundedBox(string name, Vector3 size, float cornerRadius, int cornerSegments = 4)
        {
            var outline = new List<Vector2>();
            float hx = size.x * 0.5f - cornerRadius, hz = size.z * 0.5f - cornerRadius;
            Vector2[] centres = { new(hx, hz), new(-hx, hz), new(-hx, -hz), new(hx, -hz) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= cornerSegments; i++)
                {
                    float angle = (c * 90f + 90f * i / cornerSegments) * Mathf.Deg2Rad;
                    outline.Add(centres[c] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * cornerRadius);
                }

            float y0 = -size.y * 0.5f, y1 = size.y * 0.5f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            int count = outline.Count;

            // Sides (outline runs counter-clockwise seen from above).
            for (int i = 0; i < count; i++)
            {
                Vector2 p = outline[i], q = outline[(i + 1) % count];
                Vector2 edge = q - p;
                Vector3 n = new Vector3(edge.y, 0f, -edge.x).normalized;
                int b = vertices.Count;
                vertices.Add(new Vector3(p.x, y0, p.y)); vertices.Add(new Vector3(q.x, y0, q.y));
                vertices.Add(new Vector3(p.x, y1, p.y)); vertices.Add(new Vector3(q.x, y1, q.y));
                for (int k = 0; k < 4; k++) normals.Add(n);
                triangles.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }

            // Caps as triangle fans around the centre.
            foreach (bool top in new[] { true, false })
            {
                float y = top ? y1 : y0;
                Vector3 n = top ? Vector3.up : Vector3.down;
                int centre = vertices.Count;
                vertices.Add(new Vector3(0f, y, 0f));
                normals.Add(n);
                for (int i = 0; i < count; i++)
                {
                    vertices.Add(new Vector3(outline[i].x, y, outline[i].y));
                    normals.Add(n);
                }
                for (int i = 0; i < count; i++)
                {
                    int a = centre + 1 + i, b = centre + 1 + (i + 1) % count;
                    if (top) triangles.AddRange(new[] { centre, b, a });
                    else triangles.AddRange(new[] { centre, a, b });
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Profiles are built around +Z; rotate -90 degrees about X to stand them on +Y (keeps winding intact).
        static Vector3 Orient(Vector3 p, Axis axis) => axis == Axis.Z ? p : new Vector3(p.x, p.z, -p.y);
    }
}
