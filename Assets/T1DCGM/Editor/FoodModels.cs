using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace T1DCGM.EditorTools
{
    /// <summary>Mesh under construction: positions, normals, UVs, colours and triangles (winding fixed from the normals).</summary>
    sealed class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<int> T = new List<int>();

        public int Add(Vector3 p, Vector3 n, Vector2 uv)
        {
            V.Add(p); N.Add(n); UV.Add(uv); C.Add(Color.white);
            return V.Count - 1;
        }

        /// <summary>Adds a triangle facing the way its vertex normals point.</summary>
        public void Tri(int a, int b, int c)
        {
            var cross = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            if (Vector3.Dot(cross, N[a] + N[b] + N[c]) >= 0f) { T.Add(a); T.Add(b); T.Add(c); }
            else { T.Add(a); T.Add(c); T.Add(b); }
        }

        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        /// <summary>Grid of (rows+1) x (cols+1) vertices from a function, closed or open in the column direction.</summary>
        public void Grid(int rows, int cols, Func<int, int, (Vector3 p, Vector3 n, Vector2 uv)> at)
        {
            int start = V.Count;
            for (int r = 0; r <= rows; r++)
                for (int c = 0; c <= cols; c++)
                {
                    var (p, n, uv) = at(r, c);
                    Add(p, n, uv);
                }
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int a = start + r * (cols + 1) + c;
                    int b = a + 1, d = a + cols + 1, e = d + 1;
                    Quad(a, b, e, d);
                }
        }

        public void Append(MeshBuilder m, Matrix4x4 xf, Color tint)
        {
            int o = V.Count;
            for (int i = 0; i < m.V.Count; i++)
            {
                V.Add(xf.MultiplyPoint3x4(m.V[i]));
                N.Add(xf.MultiplyVector(m.N[i]).normalized);
                UV.Add(m.UV[i]);
                C.Add(m.C[i] * tint);
            }
            foreach (int t in m.T) T.Add(t + o);
        }

        public Mesh ToMesh(string name, bool colors = false)
        {
            var mesh = new Mesh { name = name };
            if (V.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(V);
            mesh.SetNormals(N);
            mesh.SetUVs(0, UV);
            if (colors) mesh.SetColors(C);
            mesh.SetTriangles(T, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }

    /// <summary>
    /// Procedural food and tableware for the lunch scene: grain-by-grain rice and piece-by-piece pasta settled into real
    /// bowls (so the visible amount is the real amount), fruit, bread, and their textures. All sizes in metres.
    /// </summary>
    static class FoodModels
    {
        // ------------------------------------------------------------------ primitives

        /// <summary>Smooth surface of revolution around Y. Profile runs bottom centre, out, up, back to top centre.</summary>
        public static MeshBuilder Lathe(IList<Vector2> profile, int sides)
        {
            var mb = new MeshBuilder();
            int rows = profile.Count;
            var normals = new Vector2[rows];
            for (int j = 0; j < rows; j++)
            {
                Vector2 n = Vector2.zero;
                if (j > 0) { var d = profile[j] - profile[j - 1]; n += new Vector2(d.y, -d.x).normalized; }
                if (j < rows - 1) { var d = profile[j + 1] - profile[j]; n += new Vector2(d.y, -d.x).normalized; }
                normals[j] = n.sqrMagnitude > 1e-8f ? n.normalized : Vector2.up;
            }
            float total = 0f;
            for (int j = 1; j < rows; j++) total += Vector2.Distance(profile[j], profile[j - 1]);
            var vCoord = new float[rows];
            for (int j = 1; j < rows; j++) vCoord[j] = vCoord[j - 1] + Vector2.Distance(profile[j], profile[j - 1]) / total;

            mb.Grid(rows - 1, sides, (r, c) =>
            {
                float a = c * Mathf.PI * 2f / sides, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                var p = profile[r];
                var n = normals[r];
                return (new Vector3(p.x * cs, p.y, p.x * sn), new Vector3(n.x * cs, n.y, n.x * sn).normalized, new Vector2((float)c / sides, vCoord[r]));
            });
            return mb;
        }

        /// <summary>Ellipsoid with semi-axes (x, y, z), long axis along Z.</summary>
        public static MeshBuilder Ellipsoid(Vector3 radii, int around, int along)
        {
            var mb = new MeshBuilder();
            mb.Grid(along, around, (r, c) =>
            {
                float th = Mathf.PI * r / along, ph = Mathf.PI * 2f * c / around;
                var unit = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Sin(th) * Mathf.Sin(ph), -Mathf.Cos(th));
                var p = Vector3.Scale(unit, radii);
                var n = new Vector3(p.x / (radii.x * radii.x), p.y / (radii.y * radii.y), p.z / (radii.z * radii.z)).normalized;
                return (p, n, new Vector2((float)c / around, (float)r / along));
            });
            return mb;
        }

        /// <summary>Flat prism from a star-shaped outline in XZ (counter-clockwise from above), thickness along Y.</summary>
        public static MeshBuilder Extrude(IList<Vector2> outline, float thickness, bool sides = true, bool caps = true, float uvScale = 8f)
        {
            var mb = new MeshBuilder();
            float y0 = -thickness / 2f, y1 = thickness / 2f;
            int n = outline.Count;
            if (sides)
            {
                float run = 0f;
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = outline[i], q = outline[(i + 1) % n];
                    Vector2 prev = outline[(i - 1 + n) % n], next = outline[(i + 2) % n];
                    Vector3 np = SideNormal(prev, p, q), nq = SideNormal(p, q, next);
                    float len = Vector2.Distance(p, q);
                    int a = mb.Add(new Vector3(p.x, y0, p.y), np, new Vector2(run * uvScale, 0f));
                    int b = mb.Add(new Vector3(q.x, y0, q.y), nq, new Vector2((run + len) * uvScale, 0f));
                    int c = mb.Add(new Vector3(q.x, y1, q.y), nq, new Vector2((run + len) * uvScale, thickness * uvScale));
                    int d = mb.Add(new Vector3(p.x, y1, p.y), np, new Vector2(run * uvScale, thickness * uvScale));
                    mb.Quad(a, b, c, d);
                    run += len;
                }
            }
            if (caps)
            {
                Vector2 centre = Vector2.zero;
                foreach (var p in outline) centre += p;
                centre /= n;
                foreach (bool top in new[] { true, false })
                {
                    float y = top ? y1 : y0;
                    var nrm = top ? Vector3.up : Vector3.down;
                    int ci = mb.Add(new Vector3(centre.x, y, centre.y), nrm, centre * uvScale);
                    int first = mb.V.Count;
                    foreach (var p in outline) mb.Add(new Vector3(p.x, y, p.y), nrm, p * uvScale);
                    for (int i = 0; i < n; i++) mb.Tri(ci, first + i, first + (i + 1) % n);
                }
            }
            return mb;
        }

        static Vector3 SideNormal(Vector2 a, Vector2 b, Vector2 c)
        {
            // Smooth unless the corner is sharp.
            Vector2 e1 = (b - a).normalized, e2 = (c - b).normalized;
            Vector2 n1 = new Vector2(e1.y, -e1.x), n2 = new Vector2(e2.y, -e2.x);
            Vector2 n = Vector2.Dot(n1, n2) > 0.5f ? (n1 + n2).normalized : n2;
            return new Vector3(n.x, 0f, n.y);
        }

        /// <summary>Annular sector prism (r0 may be 0), centred on +Z, lying flat; thickness along Y.</summary>
        public static MeshBuilder Sector(float r0, float r1, float angleDeg, float thickness, int segments)
        {
            var outline = new List<Vector2>();
            float a0 = (90f - angleDeg / 2f) * Mathf.Deg2Rad, a1 = (90f + angleDeg / 2f) * Mathf.Deg2Rad;
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Lerp(a0, a1, (float)i / segments);
                outline.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r1);
            }
            if (r0 <= 0f)
                outline.Add(Vector2.zero);
            else
                for (int i = segments; i >= 0; i--)
                {
                    float a = Mathf.Lerp(a0, a1, (float)i / segments);
                    outline.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r0);
                }
            if (r0 <= 0f)
                return Extrude(outline, thickness);

            // Annulus: not star-shaped, so build caps as strips.
            var mb = Extrude(outline, thickness, true, false);
            foreach (bool top in new[] { true, false })
            {
                float y = top ? thickness / 2f : -thickness / 2f;
                var nrm = top ? Vector3.up : Vector3.down;
                mb.Grid(1, segments, (r, c) =>
                {
                    float a = Mathf.Lerp(a0, a1, (float)c / segments);
                    float rad = r == 0 ? r0 : r1;
                    var p = new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad);
                    return (p, nrm, new Vector2(p.x, p.z) * 8f);
                });
            }
            return mb;
        }

        // ------------------------------------------------------------------ piles (rice grains, penne)

        /// <summary>
        /// Height field over a container. Pieces are dropped one by one: each slides downhill while the slope under it is
        /// steeper than the food's angle of repose, then rests on whatever is below and raises the surface. The result is a
        /// natural heap, and its volume is the bulk volume a measuring cup would read.
        /// </summary>
        sealed class Pile
        {
            readonly float m_Cell, m_Half;
            readonly int m_N;
            readonly float[] m_H, m_Floor;

            public Pile(float halfSize, float cell, Func<float, float, float> floorAt)
            {
                m_Cell = cell;
                m_Half = halfSize;
                m_N = Mathf.CeilToInt(2f * halfSize / cell);
                m_H = new float[m_N * m_N];
                m_Floor = new float[m_N * m_N];
                for (int z = 0; z < m_N; z++)
                    for (int x = 0; x < m_N; x++)
                    {
                        float f = floorAt(-halfSize + (x + 0.5f) * cell, -halfSize + (z + 0.5f) * cell);
                        m_H[z * m_N + x] = f;
                        m_Floor[z * m_N + x] = f;
                    }
            }

            int Index(float x, float z)
            {
                int ix = Mathf.FloorToInt((x + m_Half) / m_Cell), iz = Mathf.FloorToInt((z + m_Half) / m_Cell);
                if (ix < 0 || iz < 0 || ix >= m_N || iz >= m_N) return -1;
                return iz * m_N + ix;
            }

            public float H(float x, float z)
            {
                int i = Index(x, z);
                return i < 0 ? float.PositiveInfinity : m_H[i];
            }

            public float Volume()
            {
                double v = 0;
                for (int i = 0; i < m_H.Length; i++)
                    if (!float.IsInfinity(m_Floor[i])) v += m_H[i] - m_Floor[i];
                return (float)(v * m_Cell * m_Cell);
            }

            /// <summary>Drops a piece (half length along its axis, half width, half thickness). Returns false if it does not fit.</summary>
            public bool Drop(Random rng, Vector2 xz, float halfLen, float halfWid, float halfThick, float repose, bool tube, float nest, float maxTop,
                out Vector3 position, out Quaternion rotation, out float roll)
            {
                position = default; rotation = Quaternion.identity; roll = 0f;
                // Slide downhill while too steep.
                float step = Mathf.Max(halfWid * 2f, m_Cell * 2f), maxRise = step * Mathf.Tan(repose * Mathf.Deg2Rad);
                for (int it = 0; it < 80; it++)
                {
                    float here = H(xz.x, xz.y);
                    if (float.IsInfinity(here)) return false;
                    Vector2 best = xz; float bestH = here;
                    int start = rng.Next(8);
                    for (int k = 0; k < 8; k++)
                    {
                        float a = (start + k) * Mathf.PI / 4f;
                        var p = xz + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * step;
                        float h = H(p.x, p.y);
                        if (h < bestH) { bestH = h; best = p; }
                    }
                    if (here - bestH <= maxRise) break;
                    xz = best;
                }

                float yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
                var axis = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
                var side = new Vector2(-axis.y, axis.x);
                float EndHeight(float s0, float s1)
                {
                    float m = float.NegativeInfinity;
                    for (float s = s0; s <= s1 + 1e-4f; s += 0.25f)
                        for (int w = -1; w <= 1; w++)
                        {
                            var p = xz + axis * (s * halfLen) + side * (w * halfWid * 0.7f);
                            m = Mathf.Max(m, H(p.x, p.y));
                        }
                    return m;
                }
                float hA = EndHeight(-1f, -0.5f), hB = EndHeight(0.5f, 1f), hM = EndHeight(-0.25f, 0.25f);
                if (float.IsInfinity(hA) || float.IsInfinity(hB) || float.IsInfinity(hM)) return false;

                float maxSlope = Mathf.Tan(35f * Mathf.Deg2Rad);
                float slope = Mathf.Clamp((hB - hA) / (2f * halfLen), -maxSlope, maxSlope);
                // nest < 1 lets pieces settle partly between the ones below (tubes interleave in a real heap).
                float yc = Mathf.Max((hA + hB) / 2f, hM, hA + slope * halfLen, hB - slope * halfLen) + halfThick * nest;
                if (yc + halfThick > maxTop) return false; // levelled off, like scraping a measuring cup

                // Raise the surface under the piece.
                float reach = halfLen + halfWid;
                int x0 = Mathf.FloorToInt((xz.x - reach + m_Half) / m_Cell), x1 = Mathf.CeilToInt((xz.x + reach + m_Half) / m_Cell);
                int z0 = Mathf.FloorToInt((xz.y - reach + m_Half) / m_Cell), z1 = Mathf.CeilToInt((xz.y + reach + m_Half) / m_Cell);
                for (int iz = Mathf.Max(0, z0); iz <= Mathf.Min(m_N - 1, z1); iz++)
                    for (int ix = Mathf.Max(0, x0); ix <= Mathf.Min(m_N - 1, x1); ix++)
                    {
                        var p = new Vector2(-m_Half + (ix + 0.5f) * m_Cell, -m_Half + (iz + 0.5f) * m_Cell) - xz;
                        float s = Vector2.Dot(p, axis), w = Vector2.Dot(p, side);
                        float top;
                        if (tube)
                        {
                            if (Mathf.Abs(s) > halfLen || Mathf.Abs(w) > halfWid) continue;
                            top = yc + slope * s + halfThick * Mathf.Sqrt(Mathf.Max(0f, 1f - (w / halfWid) * (w / halfWid)));
                        }
                        else
                        {
                            float q = (s / halfLen) * (s / halfLen) + (w / halfWid) * (w / halfWid);
                            if (q > 1f) continue;
                            top = yc + slope * s + halfThick * Mathf.Sqrt(1f - q);
                        }
                        int i = iz * m_N + ix;
                        if (top > m_H[i]) m_H[i] = top;
                    }

                position = new Vector3(xz.x, yc, xz.y);
                var dir = new Vector3(axis.x, slope, axis.y).normalized;
                roll = (float)rng.NextDouble();
                rotation = Quaternion.LookRotation(dir, Vector3.up);
                return true;
            }

            /// <summary>Surface just under the pieces, so gaps between the visible top layer show more food, not the bowl.</summary>
            public MeshBuilder CoreSurface(float drop, float minFill)
            {
                var mb = new MeshBuilder();
                var idx = new int[m_N * m_N];
                for (int z = 0; z < m_N; z++)
                    for (int x = 0; x < m_N; x++)
                    {
                        int i = z * m_N + x;
                        idx[i] = -1;
                        if (float.IsInfinity(m_Floor[i]) || m_H[i] - m_Floor[i] < minFill) continue;
                        // 3x3 average for a soft surface.
                        float sum = 0f; int cnt = 0;
                        for (int dz = -1; dz <= 1; dz++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, zz = z + dz;
                                if (xx < 0 || zz < 0 || xx >= m_N || zz >= m_N) continue;
                                int j = zz * m_N + xx;
                                if (float.IsInfinity(m_Floor[j])) continue;
                                sum += m_H[j]; cnt++;
                            }
                        float y = Mathf.Max(m_Floor[i] + 0.0005f, sum / cnt - drop);
                        var p = new Vector3(-m_Half + (x + 0.5f) * m_Cell, y, -m_Half + (z + 0.5f) * m_Cell);
                        idx[i] = mb.Add(p, Vector3.up, new Vector2(p.x, p.z) * 12f);
                    }
                // Normals from neighbours.
                for (int z = 0; z < m_N; z++)
                    for (int x = 0; x < m_N; x++)
                    {
                        int i = z * m_N + x;
                        if (idx[i] < 0) continue;
                        float hl = Get(x - 1, z, i), hr = Get(x + 1, z, i), hd = Get(x, z - 1, i), hu = Get(x, z + 1, i);
                        mb.N[idx[i]] = new Vector3(hl - hr, 2f * m_Cell, hd - hu).normalized;
                    }
                for (int z = 0; z < m_N - 1; z++)
                    for (int x = 0; x < m_N - 1; x++)
                    {
                        int a = idx[z * m_N + x], b = idx[z * m_N + x + 1], c = idx[(z + 1) * m_N + x + 1], d = idx[(z + 1) * m_N + x];
                        if (a >= 0 && b >= 0 && c >= 0 && d >= 0) mb.Quad(a, b, c, d);
                        else if (a >= 0 && b >= 0 && c >= 0) mb.Tri(a, b, c);
                        else if (a >= 0 && c >= 0 && d >= 0) mb.Tri(a, c, d);
                    }
                return mb;

                float Get(int x, int z, int fallback)
                {
                    if (x < 0 || z < 0 || x >= m_N || z >= m_N) return mb.V[idx[fallback]].y;
                    int j = idx[z * m_N + x];
                    return j < 0 ? mb.V[idx[fallback]].y : mb.V[j].y;
                }
            }
        }

        public enum PileFood { Rice, Penne }

        public struct PileSpec
        {
            public PileFood food;
            public float halfSize, cell;
            public Func<float, float, float> floor;
            public float targetMl;
            [Tooltip("Spread of drop points (m); 0 = uniform over the container.")]
            public float spread;
            public float containerRadius;
            public float repose;
            public int seed;
            [Tooltip("Highest a piece may rest (a measuring cup's brim); 0 = no limit.")]
            public float maxTop;
        }

        public struct PileResult
        {
            public Mesh pieces, core;
            public int count;
            public float volumeMl;
        }

        // Cooked long-grain rice grain: about 7 x 2.4 x 2.1 mm.
        const float k_RiceHalfLen = 0.0036f, k_RiceHalfWid = 0.0012f, k_RiceHalfThick = 0.00105f;
        // Cooked penne rigate: 5 cm tip to tip, 1.1 cm across.
        const float k_PenneLen = 0.044f, k_PenneR = 0.0055f, k_PenneWall = 0.0011f;

        public static PileResult BuildPile(string name, PileSpec spec)
        {
            var rng = new Random(spec.seed);
            var pile = new Pile(spec.halfSize, spec.cell, spec.floor);
            bool rice = spec.food == PileFood.Rice;
            float hl = rice ? k_RiceHalfLen : k_PenneLen / 2f, hw = rice ? k_RiceHalfWid : k_PenneR, ht = rice ? k_RiceHalfThick : k_PenneR;

            var placed = new List<(Vector3 p, Quaternion r, float roll)>();
            int failures = 0, attempts = 0; // consecutive misses (drop point outside the dish, or no room near the wall)
            float volume = 0f;
            while (failures < 3000 && attempts++ < 400000)
            {
                Vector2 xz;
                if (spec.spread > 0f)
                {
                    // Gaussian around the centre (Box-Muller).
                    double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                    double m = Math.Sqrt(-2.0 * Math.Log(u1));
                    xz = new Vector2((float)(m * Math.Cos(2 * Math.PI * u2)), (float)(m * Math.Sin(2 * Math.PI * u2))) * spec.spread;
                }
                else
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt((float)rng.NextDouble()) * spec.containerRadius;
                    xz = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }
                if (xz.magnitude > spec.containerRadius) { failures++; continue; }
                if (!pile.Drop(rng, xz, hl, hw, ht, spec.repose, !rice, rice ? 1f : 0.55f, spec.maxTop > 0f ? spec.maxTop : float.PositiveInfinity, out var pos, out var rot, out var roll)) { failures++; continue; }
                placed.Add((pos, rot, roll));
                failures = 0;
                if (placed.Count % (rice ? 50 : 2) == 0)
                {
                    volume = pile.Volume() * 1e6f;
                    if (volume >= spec.targetMl) break;
                }
            }
            volume = pile.Volume() * 1e6f;

            var piece = rice ? RiceGrain() : PenneMesh();
            var all = new MeshBuilder();
            int shown = 0;
            foreach (var (p, r, roll01) in placed)
            {
                float surface = pile.H(p.x, p.z);
                float depth = surface - (p.y + ht);
                if (rice && depth > 4f * ht) continue; // hidden under the top layers: the core surface stands in
                float ao = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(1f - depth / (rice ? 4f * ht : 3f * ht)));
                float shade = 0.94f + (float)rng.NextDouble() * 0.08f;
                var tint = new Color(ao * shade, ao * shade, ao * shade * (rice ? 0.985f : 1f), 1f);
                // Rice lies mostly flat (small roll); penne can turn any way round its axis.
                float rollDeg = rice ? (roll01 - 0.5f) * 50f : roll01 * 360f;
                all.Append(piece, Matrix4x4.TRS(p, r * Quaternion.Euler(0f, 0f, rollDeg), Vector3.one), tint);
                shown++;
            }
            var result = new PileResult
            {
                pieces = all.ToMesh(name + "_Pieces", true),
                count = placed.Count,
                volumeMl = volume,
            };
            if (rice)
                result.core = pile.CoreSurface(ht * 2.2f, ht).ToMesh(name + "_Core");
            Debug.Log($"[T1D] {name}: {placed.Count} {(rice ? "grains" : "pieces")} ({shown} drawn), {volume:0} mL = {volume / FoodData.CupMl:0.00} cups.");
            return result;
        }

        static MeshBuilder s_Grain, s_Penne;

        static MeshBuilder RiceGrain()
        {
            if (s_Grain != null) return s_Grain;
            // Slightly tapered ends and a gentle bend, like a cooked grain.
            var mb = Ellipsoid(new Vector3(k_RiceHalfWid, k_RiceHalfThick, k_RiceHalfLen), 6, 5);
            for (int i = 0; i < mb.V.Count; i++)
            {
                var v = mb.V[i];
                float t = v.z / k_RiceHalfLen;
                v.y += 0.00035f * (1f - t * t);
                mb.V[i] = v;
            }
            return s_Grain = mb;
        }

        /// <summary>Penne rigate along Z: ridged tube with both ends cut on the same diagonal.</summary>
        public static MeshBuilder PenneMesh()
        {
            if (s_Penne != null) return s_Penne;
            var mb = new MeshBuilder();
            const int around = 24, along = 4;
            const float cut = 0.85f; // end offset per metre of height across the tube (about 40 degrees)
            float rOut(float ph) => k_PenneR + 0.00022f * Mathf.Cos(ph * 14f);
            float rIn = k_PenneR - k_PenneWall;
            Vector3 P(float r, float ph, float t)
            {
                float y = r * Mathf.Sin(ph);
                float z = Mathf.Lerp(-k_PenneLen / 2f, k_PenneLen / 2f, t) + cut * y;
                return new Vector3(r * Mathf.Cos(ph), y, z);
            }
            // Outside and inside walls.
            mb.Grid(along, around, (r, c) =>
            {
                float ph = Mathf.PI * 2f * c / around;
                return (P(rOut(ph), ph, (float)r / along), new Vector3(Mathf.Cos(ph), Mathf.Sin(ph), 0f), new Vector2((float)c / around, (float)r / along));
            });
            mb.Grid(along, around, (r, c) =>
            {
                float ph = Mathf.PI * 2f * c / around;
                return (P(rIn, ph, (float)r / along), -new Vector3(Mathf.Cos(ph), Mathf.Sin(ph), 0f), new Vector2((float)c / around, (float)r / along));
            });
            // Cut ends.
            var endNormal = new Vector3(0f, -cut, 1f).normalized;
            foreach (int end in new[] { 0, 1 })
            {
                var n = end == 1 ? endNormal : -endNormal;
                mb.Grid(1, around, (r, c) =>
                {
                    float ph = Mathf.PI * 2f * c / around;
                    return (P(r == 0 ? rIn : rOut(ph), ph, end), n, new Vector2((float)c / around, r));
                });
            }
            return s_Penne = mb;
        }

        // ------------------------------------------------------------------ tableware

        /// <summary>Rice bowl, 13 cm across and 6 cm tall. Inner surface: y = 0.012 + 0.048 (r / 0.065)^2.</summary>
        public const float RiceBowlInner = 0.065f;
        public static float RiceBowlFloor(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            if (r > RiceBowlInner) return float.PositiveInfinity;
            return 0.012f + 0.048f * (r / RiceBowlInner) * (r / RiceBowlInner);
        }

        static float RiceBowlOuterR(float y) => 0.03f + 0.039f * Mathf.Pow(Mathf.Clamp01((y - 0.008f) / 0.052f), 1f / 1.6f);

        public static MeshBuilder RiceBowl()
        {
            var prof = new List<Vector2> { new Vector2(0f, 0.001f), new Vector2(0.026f, 0.001f), new Vector2(0.03f, 0f), new Vector2(0.032f, 0.004f), new Vector2(0.03f, 0.008f) };
            for (int i = 1; i <= 12; i++)
            {
                float y = 0.008f + 0.052f * i / 12f;
                prof.Add(new Vector2(RiceBowlOuterR(y), y));
            }
            prof.Add(new Vector2(0.0692f, 0.0612f));
            prof.Add(new Vector2(0.0665f, 0.0612f));
            for (int i = 12; i >= 0; i--)
            {
                float r = RiceBowlInner * i / 12f;
                prof.Add(new Vector2(r, RiceBowlFloor(r, 0f)));
            }
            return Lathe(prof, 48);
        }

        /// <summary>Thin painted band just under the rice bowl rim.</summary>
        public static MeshBuilder RiceBowlBand() => Lathe(new[]
        {
            new Vector2(RiceBowlOuterR(0.0525f) + 0.0004f, 0.0525f), new Vector2(RiceBowlOuterR(0.0565f) + 0.0004f, 0.0565f),
        }, 48);

        /// <summary>Wide pasta bowl, 26 cm across: a 21 cm well 4 cm deep and a flat rim.</summary>
        public const float PastaBowlInner = 0.105f;
        public static float PastaBowlFloor(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            if (r > PastaBowlInner) return float.PositiveInfinity;
            return 0.008f + 0.04f * Mathf.Pow(Mathf.Clamp01((r - 0.05f) / (PastaBowlInner - 0.05f)), 1.6f);
        }

        public static MeshBuilder PastaBowl()
        {
            var prof = new List<Vector2> { new Vector2(0f, 0.001f), new Vector2(0.05f, 0.001f), new Vector2(0.055f, 0f) };
            for (int i = 1; i <= 10; i++)
            {
                float r = 0.055f + 0.075f * i / 10f;
                prof.Add(new Vector2(r, 0.044f * Mathf.Pow((r - 0.055f) / 0.075f, 1.6f)));
            }
            prof.Add(new Vector2(0.131f, 0.049f));
            prof.Add(new Vector2(0.128f, 0.051f));
            prof.Add(new Vector2(PastaBowlInner + 0.002f, 0.049f));
            for (int i = 12; i >= 0; i--)
            {
                float r = PastaBowlInner * i / 12f;
                prof.Add(new Vector2(r, PastaBowlFloor(r, 0f)));
            }
            return Lathe(prof, 56);
        }

        /// <summary>Steel measuring cup holding <paramref name="ml"/> to the brim; returns its inner radius and depth.</summary>
        public static MeshBuilder MeasuringCup(float ml, out float innerR, out float depth)
        {
            innerR = Mathf.Lerp(0.028f, 0.0425f, Mathf.InverseLerp(59f, 237f, ml));
            depth = ml * 1e-6f / (Mathf.PI * innerR * innerR);
            const float wall = 0.0012f, floorY = 0.0015f;
            float d = depth + floorY;
            var mb = Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(innerR + wall, 0f), new Vector2(innerR + wall, d), new Vector2(innerR + wall + 0.0015f, d + 0.0012f),
                new Vector2(innerR, d + 0.0012f), new Vector2(innerR, floorY), new Vector2(0f, floorY),
            }, 40);
            // Flat handle.
            var handle = Extrude(new[]
            {
                new Vector2(innerR, -0.009f), new Vector2(innerR + 0.085f, -0.007f), new Vector2(innerR + 0.09f, 0f),
                new Vector2(innerR + 0.085f, 0.007f), new Vector2(innerR, 0.009f),
            }, 0.0025f);
            mb.Append(handle, Matrix4x4.Translate(new Vector3(0f, d, 0f)), Color.white);
            return mb;
        }

        // ------------------------------------------------------------------ fruit and bread

        /// <summary>Medium apple, about 7.8 cm across, sitting on the table at y = 0.</summary>
        public static MeshBuilder Apple()
        {
            const float R = 0.037f;
            var prof = new List<Vector2>();
            for (int j = 0; j <= 28; j++)
            {
                float ph = Mathf.PI * j / 28f;
                float r = R * Mathf.Sin(ph) * (1f + 0.1f * -Mathf.Cos(ph));
                float y = R * (1f - Mathf.Cos(ph)) * 0.92f;
                if (ph > Mathf.PI / 2f) y -= 0.0095f * Mathf.Exp(-(r / 0.012f) * (r / 0.012f));
                else y += 0.004f * Mathf.Exp(-(r / 0.009f) * (r / 0.009f));
                prof.Add(new Vector2(r, y));
            }
            return Lathe(prof, 40);
        }

        public static MeshBuilder Stem(float length, float radius)
        {
            var mb = Lathe(new[] { new Vector2(0f, 0f), new Vector2(radius, 0f), new Vector2(radius * 0.8f, length), new Vector2(radius * 1.2f, length + 0.001f), new Vector2(0f, length + 0.0012f) }, 8);
            for (int i = 0; i < mb.V.Count; i++)
            {
                var v = mb.V[i];
                v.x += 0.15f * v.y * v.y / length; // slight curve
                mb.V[i] = v;
            }
            return mb;
        }

        /// <summary>Medium orange, 7 cm across, slightly flattened.</summary>
        public static MeshBuilder Orange()
        {
            const float R = 0.035f;
            var prof = new List<Vector2>();
            for (int j = 0; j <= 24; j++)
            {
                float ph = Mathf.PI * j / 24f;
                prof.Add(new Vector2(R * Mathf.Sin(ph), R * 0.94f * (1f - Mathf.Cos(ph))));
            }
            return Lathe(prof, 40);
        }

        /// <summary>
        /// Medium banana (about 19 cm along the outside curve) lying on its side: a tapered, slightly five-sided body swept
        /// along an arc in XZ. UV v runs from the stem (0) to the tip (1).
        /// </summary>
        public static MeshBuilder Banana(out Vector3 stemPoint, out Vector3 stemDir, out Vector3 tipPoint)
        {
            const float Rc = 0.14f, arc = 70f * Mathf.Deg2Rad, Rmax = 0.0175f;
            const int rows = 40, around = 30;
            Vector3 Centre(float s) { float a = (s - 0.5f) * arc; return new Vector3(Rc * Mathf.Sin(a), 0f, Rc * (1f - Mathf.Cos(a))); }
            Vector3 Inward(float s) { float a = (s - 0.5f) * arc; return new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)); }
            float Radius(float s) => Rmax * Mathf.Pow(Mathf.Sin(Mathf.PI * s), 0.5f) * (1f - 0.35f * Mathf.Exp(-((s - 0.05f) / 0.06f) * ((s - 0.05f) / 0.06f)));
            var mb = new MeshBuilder();
            mb.Grid(rows, around, (r, c) =>
            {
                float s = (float)r / rows, th = Mathf.PI * 2f * c / around;
                var radial = Mathf.Cos(th) * Inward(s) + Mathf.Sin(th) * Vector3.up;
                float rho = Radius(s) * (1f + 0.045f * Mathf.Cos(5f * th));
                var p = Centre(s) + radial * rho;
                // Ends close to a point: point the normals along the axis there.
                var tangent = (Centre(Mathf.Min(1f, s + 0.01f)) - Centre(Mathf.Max(0f, s - 0.01f))).normalized;
                var n = s < 0.01f ? -tangent : s > 0.99f ? tangent : radial;
                return (p, n, new Vector2((float)c / around, s));
            });
            stemPoint = Centre(0.01f);
            stemDir = -(Centre(0.03f) - Centre(0f)).normalized;
            tipPoint = Centre(1f);
            return mb;
        }

        /// <summary>Loaf-shaped slice outline (square base, domed crown) in XZ, counter-clockwise, centred.</summary>
        public static List<Vector2> SliceOutline(float width, float height, float inset)
        {
            var pts = new List<Vector2>();
            float hw = width / 2f - inset, hh = height / 2f - inset, rc = 0.01f;
            float shoulder = -hh + 0.62f * (2f * hh);
            // Bottom corners.
            for (int i = 0; i <= 4; i++) { float a = Mathf.Lerp(180f, 270f, i / 4f) * Mathf.Deg2Rad; pts.Add(new Vector2(-hw + rc + rc * Mathf.Cos(a), -hh + rc + rc * Mathf.Sin(a))); }
            for (int i = 0; i <= 4; i++) { float a = Mathf.Lerp(270f, 360f, i / 4f) * Mathf.Deg2Rad; pts.Add(new Vector2(hw - rc + rc * Mathf.Cos(a), -hh + rc + rc * Mathf.Sin(a))); }
            // Crown: a little wider than the sides, like a sandwich loaf.
            float crownW = hw + 0.004f, crownH = hh - shoulder;
            for (int i = 0; i <= 20; i++)
            {
                float a = Mathf.Lerp(-12f, 192f, i / 20f) * Mathf.Deg2Rad;
                pts.Add(new Vector2(crownW * Mathf.Cos(a), shoulder + crownH * Mathf.Sin(a)));
            }
            return pts;
        }

        // ------------------------------------------------------------------ textures

        static Texture2D NewTex(string name, int size, bool repeat = true) =>
            new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp };

        static float Periodic(float u, float v, int[] freqs, float[] phases, float vScale)
        {
            float s = 0f;
            for (int i = 0; i < freqs.Length; i++) s += Mathf.Sin(2f * Mathf.PI * freqs[i] * u + phases[i] + v * vScale * (i + 1));
            return s / freqs.Length;
        }

        /// <summary>Red apple with yellow streaks, paler at the bottom, and small light dots.</summary>
        public static Texture2D AppleTexture()
        {
            const int S = 256;
            var tex = NewTex("Apple Skin", S);
            var rng = new Random(11);
            int[] f = { 3, 7, 13, 23, 37 };
            var ph = new float[f.Length];
            for (int i = 0; i < ph.Length; i++) ph[i] = (float)rng.NextDouble() * 6.28f;
            var red = new Color(0.66f, 0.06f, 0.06f); var deep = new Color(0.45f, 0.03f, 0.05f); var yellow = new Color(0.86f, 0.68f, 0.22f);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (float)x / S, v = (float)y / S;
                    float streak = Periodic(u, v, f, ph, 3f) * 0.5f + 0.5f;
                    float pale = Mathf.Clamp01(1f - v * 2.2f) * 0.8f + Mathf.Clamp01((v - 0.9f) * 6f) * 0.5f;
                    var c = Color.Lerp(deep, red, streak);
                    c = Color.Lerp(c, yellow, Mathf.Clamp01(pale + (streak - 0.75f) * 1.2f));
                    px[y * S + x] = c;
                }
            for (int i = 0; i < 380; i++)
            {
                int x = rng.Next(S), y = rng.Next(S);
                px[y * S + x] = Color.Lerp(px[y * S + x], new Color(0.95f, 0.85f, 0.6f), 0.55f);
            }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        /// <summary>Yellow banana: green near the stem, brown ends, faint darker ridges and a few speckles.</summary>
        public static Texture2D BananaTexture()
        {
            const int S = 256;
            var tex = NewTex("Banana Skin", S);
            var rng = new Random(5);
            var yellow = new Color(0.97f, 0.84f, 0.27f); var green = new Color(0.62f, 0.7f, 0.2f); var brown = new Color(0.25f, 0.17f, 0.08f);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (float)x / S, v = (float)y / S;
                    var c = Color.Lerp(yellow, green, Mathf.Clamp01(1f - v / 0.16f) * 0.85f);
                    float ridge = Mathf.Pow(Mathf.Abs(Mathf.Cos(Mathf.PI * 5f * u)), 40f);
                    c = Color.Lerp(c, c * 0.8f, ridge * 0.6f);
                    c = Color.Lerp(c, brown, Mathf.Clamp01((v - 0.965f) / 0.02f));
                    c = Color.Lerp(c, brown * 1.4f, Mathf.Clamp01((0.025f - v) / 0.02f));
                    px[y * S + x] = c;
                }
            for (int i = 0; i < 70; i++)
            {
                int cx = rng.Next(S), cy = rng.Next(20, S - 20), r = 1 + rng.Next(2);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = (cx + dx + S) % S, y = Mathf.Clamp(cy + dy, 0, S - 1);
                        px[y * S + x] = Color.Lerp(px[y * S + x], brown, 0.5f);
                    }
            }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        /// <summary>Orange peel: warm orange with fine darker pores.</summary>
        public static Texture2D OrangeTexture()
        {
            const int S = 256;
            var tex = NewTex("Orange Peel", S);
            var rng = new Random(9);
            var baseC = new Color(1f, 0.52f, 0.05f);
            var px = new Color[S * S];
            for (int i = 0; i < px.Length; i++) px[i] = baseC * (0.95f + (float)rng.NextDouble() * 0.06f);
            for (int i = 0; i < 2600; i++)
            {
                int x = rng.Next(S), y = rng.Next(S);
                px[y * S + x] = baseC * 0.78f;
            }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        /// <summary>Bread crumb: cream with irregular small holes.</summary>
        public static Texture2D CrumbTexture()
        {
            const int S = 256;
            var tex = NewTex("Bread Crumb", S);
            var rng = new Random(21);
            var baseC = new Color(0.95f, 0.88f, 0.72f);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    px[y * S + x] = baseC * (0.94f + 0.08f * Mathf.PerlinNoise(x * 0.08f, y * 0.08f));
            for (int i = 0; i < 700; i++)
            {
                int cx = rng.Next(S), cy = rng.Next(S);
                float rx = 0.8f + (float)rng.NextDouble() * 2.5f, ry = rx * (0.5f + (float)rng.NextDouble() * 0.7f);
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        float d = dx * dx / (rx * rx) + dy * dy / (ry * ry);
                        if (d > 1f) continue;
                        int x = (cx + dx + S) % S, y = (cy + dy + S) % S;
                        px[y * S + x] = Color.Lerp(baseC * 0.72f, px[y * S + x], d);
                    }
            }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        /// <summary>Oak floor planks, 4 across the texture, with grain and joints.</summary>
        public static Texture2D PlankTexture()
        {
            const int S = 512;
            var tex = NewTex("Wood Planks", S);
            var rng = new Random(4);
            var px = new Color[S * S];
            const int planks = 4;
            var tints = new float[planks * 3];
            for (int i = 0; i < tints.Length; i++) tints[i] = 0.85f + (float)rng.NextDouble() * 0.25f;
            var offsets = new int[planks];
            for (int i = 0; i < planks; i++) offsets[i] = rng.Next(S);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int plank = x * planks / S;
                    int along = (y + offsets[plank]) % S;
                    int board = along * 3 / S;
                    float tint = tints[plank * 3 + board];
                    float grain = Mathf.PerlinNoise(x * 0.9f / planks, along * 0.02f) * 0.6f + Mathf.PerlinNoise(x * 0.25f, along * 0.004f) * 0.4f;
                    var c = new Color(0.62f, 0.44f, 0.27f) * tint * (0.82f + 0.3f * grain);
                    bool seam = (x % (S / planks)) < 2 || (along % (S / 3)) < 2;
                    px[y * S + x] = seam ? c * 0.55f : c;
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        /// <summary>White subway tiles (3:1) with grey grout.</summary>
        public static Texture2D TileTexture()
        {
            const int S = 256;
            var tex = NewTex("Subway Tile", S);
            var px = new Color[S * S];
            const int rows = 8, tileW = 128;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int row = y * rows / S;
                    int xx = (x + (row % 2) * tileW / 2) % tileW;
                    int yy = y % (S / rows);
                    bool grout = xx < 3 || yy < 3;
                    float edge = Mathf.Min(Mathf.Min(xx, tileW - xx), Mathf.Min(yy, S / rows - yy));
                    float shade = 0.93f + 0.05f * Mathf.Clamp01(edge / 8f);
                    px[y * S + x] = grout ? new Color(0.62f, 0.62f, 0.6f) : new Color(shade, shade, shade * 0.98f);
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        /// <summary>Daylight view for the windows: sky, distant trees, lawn.</summary>
        public static Texture2D WindowViewTexture()
        {
            const int W = 256, H = 256;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "Window View", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = (float)y / H;
                    var sky = Color.Lerp(new Color(0.85f, 0.92f, 1f), new Color(0.45f, 0.68f, 0.95f), v);
                    float treeLine = 0.32f + 0.08f * Mathf.PerlinNoise(x * 0.05f, 3f) + 0.03f * Mathf.PerlinNoise(x * 0.3f, 7f);
                    Color c = sky;
                    if (v < treeLine) c = Color.Lerp(new Color(0.18f, 0.36f, 0.16f), new Color(0.3f, 0.5f, 0.22f), Mathf.PerlinNoise(x * 0.2f, y * 0.2f));
                    if (v < 0.18f) c = Color.Lerp(new Color(0.42f, 0.62f, 0.3f), new Color(0.5f, 0.7f, 0.35f), Mathf.PerlinNoise(x * 0.1f, y * 0.4f));
                    px[y * W + x] = c;
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }
    }
}
