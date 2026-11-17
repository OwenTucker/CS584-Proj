using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace T1DCGM.EditorTools
{
    /// <summary>The lunch tablet: page text, a picture per page, estimate buttons, result bars and a CGM graph.</summary>
    public static partial class DemoSceneBuilder
    {
        // Tablet local space: X right, Y up, the screen faces -Z (toward the learner). Origin at the screen centre.
        const float k_TabW = 0.34f, k_TabH = 0.22f, k_TabPad = 0.014f, k_TabZ = -0.002f;
        static readonly Color k_Muted = new Color(0.6f, 0.68f, 0.78f);
        static readonly Color k_Gold = new Color(1f, 0.84f, 0.25f);
        // Centre of the picture area when the text is on the left (a 0.15 m square on the right).
        static readonly Vector2 k_Pic = new Vector2(k_TabW / 2f - k_TabPad - 0.075f, -0.011f);

        static InfoTablet BuildTablet(Transform head, List<GuessStation> guess, List<BuildStation> build)
        {
            const float W = k_TabW, H = k_TabH;
            // Arrows, tick, star and dot used on the pages; add them to the dynamic atlas now so they ship in the build.
            if (s_Font != null && s_Font.atlasPopulationMode == AtlasPopulationMode.Dynamic)
            {
                s_Font.TryAddCharacters("↑↓→✓★●", out _);
                EditorUtility.SetDirty(s_Font);
            }

            var root = new GameObject("Tablet (follows your view)");
            var follow = root.AddComponent<FollowHead>();
            follow.head = head;
            follow.offset = new Vector3(0f, -0.17f, 0.42f); // straight ahead, just below eye level
            follow.minHeight = 1.3f;                       // stays clear of the food and the counter
            root.transform.position = new Vector3(head.position.x, 1.45f, head.position.z + 0.42f);
            var tablet = root.AddComponent<InfoTablet>();
            tablet.mover = follow;
            var t = root.transform;

            var frame = Part("Frame", t, SaveMesh("Tablet_Frame", MeshFactory.RoundedBox("Tablet_Frame", new Vector3(W + 0.02f, 0.01f, H + 0.02f), 0.014f)), s_Mats["Dark"]);
            frame.transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, 0.005f), Quaternion.Euler(90f, 0f, 0f));
            var screen = Part("Screen", t, SaveMesh("Tablet_Screen", MeshFactory.RoundedBox("Tablet_Screen", new Vector3(W, 0.001f, H), 0.008f)), s_Mats["TabletScreen"]);
            screen.transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, -0.0005f), Quaternion.Euler(90f, 0f, 0f));
            NoShadow(root);

            tablet.titleText = TabText(t, "Title", "", 0.13f, k_Gold, new Vector2(W - 2f * k_TabPad, 0.026f), new Vector2(0f, H / 2f - k_TabPad - 0.012f), TextAlignmentOptions.Left);
            tablet.titleText.enableAutoSizing = true;
            tablet.titleText.fontSizeMin = 0.07f;
            tablet.titleText.fontSizeMax = 0.13f;

            tablet.bodyTop = H / 2f - k_TabPad - 0.03f;
            tablet.bodyLeft = -W / 2f + k_TabPad;
            tablet.fullWidth = W - 2f * k_TabPad;
            tablet.splitWidth = 0.15f;
            tablet.fullHeight = 0.15f;
            tablet.bandHeight = 0.042f;
            var body = TabText(t, "Body", "", 0.09f, Color.white, new Vector2(tablet.fullWidth, tablet.fullHeight), new Vector2(0f, tablet.bodyTop), TextAlignmentOptions.TopLeft);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.enableAutoSizing = true;
            body.fontSizeMin = 0.05f;
            body.fontSizeMax = 0.09f;
            body.lineSpacing = 4f;
            body.rectTransform.pivot = new Vector2(0.5f, 1f);
            tablet.bodyText = body;

            tablet.footerText = TabText(t, "Footer", "", 0.055f, k_Muted, new Vector2(W - 2f * k_TabPad, 0.014f), new Vector2(0f, -H / 2f + 0.009f), TextAlignmentOptions.Left);

            BuildTabletGraph(tablet, t);

            // One picture per kind of page.
            var plate = build.Count > 0 ? build[0].plate.transform : null;
            Transform Food(int station, string name) => station < build.Count ? FindChild(build[station].root.gameObject, name) : null;
            tablet.visuals.Add(LunchPicture(t, plate, Food(0, "Thick slice"), Food(1, "Apple"), Food(1, "Orange")));
            tablet.visuals.Add(CountPicture(t));
            tablet.visuals.Add(GuessIntroPicture(t, guess));
            tablet.visuals.Add(BuildIntroPicture(t, plate, Food(0, "Sandwich slice"), Food(1, "Banana")));
            tablet.visuals.Add(ControlsPicture(t));
            tablet.visuals.Add(GuessPanel(t, tablet));
            tablet.visuals.Add(TargetPicture(t));
            var result = ResultChart(t);
            tablet.resultChart = result.root.GetComponent<TabletChart>();
            tablet.visuals.Add(result);
            var summary = SummaryChart(t, guess.Count + build.Count);
            tablet.summaryChart = summary.root.GetComponent<TabletChart>();
            tablet.visuals.Add(summary);
            foreach (var v in tablet.visuals)
            {
                NoShadow(v.root);
                v.root.SetActive(false);
            }

            // NEXT (A) on the right, BACK (B) on the left, each with the controller button beside it.
            tablet.nextButton = TabletButton(t, "Next Button", new Vector3(W / 2f - 0.05f, -H / 2f - 0.045f, 0f), "NEXT", s_Mats["Green"], out var nextLabel);
            tablet.nextLabel = nextLabel;
            KeyHint(tablet.nextButton.transform, "A", 0.064f);
            tablet.backButton = TabletButton(t, "Back Button", new Vector3(-W / 2f + 0.05f, -H / 2f - 0.045f, 0f), "BACK", s_Mats["Grey"], out var backLabel);
            tablet.backLabel = backLabel;
            KeyHint(tablet.backButton.transform, "B", -0.064f);
            return tablet;
        }

        static void BuildTabletGraph(InfoTablet tablet, Transform t)
        {
            const float W = k_TabW, H = k_TabH;
            var graphRoot = new GameObject("CGM Graph").transform;
            graphRoot.SetParent(t, false);
            var origin = new Vector3(-W / 2f + 0.03f, -H / 2f + 0.024f, k_TabZ);
            var graphGo = new GameObject("Graph");
            graphGo.transform.SetParent(graphRoot, false);
            graphGo.transform.localPosition = origin;
            var graph = graphGo.AddComponent<CgmGraph>();
            graph.width = W - 0.05f;
            graph.height = 0.042f;
            graph.lineMaterial = s_Mats["Line"];
            foreach (var mg in new[] { 70f, 180f })
            {
                var label = Text3D($"{mg}", graphRoot, $"{mg:0}", 0.042f, k_Muted, new Vector2(0.02f, 0.01f));
                label.alignment = TextAlignmentOptions.Right;
                label.transform.localPosition = origin + new Vector3(-0.012f, graph.height * Mathf.InverseLerp(graph.minMg, graph.maxMg, mg), 0f);
            }
            var endLabel = Text3D("4 h", graphRoot, "4 h", 0.042f, k_Muted, new Vector2(0.02f, 0.01f));
            endLabel.alignment = TextAlignmentOptions.Right;
            endLabel.transform.localPosition = origin + new Vector3(graph.width - 0.01f, -0.006f, 0f);
            tablet.graphRoot = graphRoot.gameObject;
            tablet.graph = graph;
            graphRoot.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ pictures

        static TabletVisual Picture(Transform tablet, string key, bool split, out Transform root)
        {
            root = new GameObject($"Picture: {key}").transform;
            root.SetParent(tablet, false);
            return new TabletVisual { key = key, root = root.gameObject, split = split };
        }

        /// <summary>Text on the tablet face at tablet coordinates <paramref name="pos"/>.</summary>
        static TextMeshPro TabText(Transform parent, string name, string text, float size, Color color, Vector2 box, Vector2 pos,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var tmp = Text3D(name, parent, text, size, color, box);
            tmp.alignment = align;
            tmp.transform.localPosition = new Vector3(pos.x, pos.y, k_TabZ);
            return tmp;
        }

        /// <summary>Little 3D scene standing out of the screen, tipped toward the learner as if seen on a table.</summary>
        static Transform Diorama(Transform parent, Vector2 pos, float scale, float depth)
        {
            var d = new GameObject("Diorama").transform;
            d.SetParent(parent, false);
            d.localPosition = new Vector3(pos.x, pos.y, -depth);
            d.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            d.localScale = Vector3.one * scale;
            return d;
        }

        /// <summary>Copies only the meshes of <paramref name="source"/> (no physics or grab), at real size, into a diorama.</summary>
        static void MiniCopy(Transform source, Transform parent, Vector3 localPos, float yaw)
        {
            if (source == null) return;
            var root = new GameObject($"Mini {source.name}").transform;
            root.SetParent(parent, false);
            root.SetLocalPositionAndRotation(localPos, Quaternion.Euler(0f, yaw, 0f));
            var toSource = Matrix4x4.TRS(source.position, source.rotation, Vector3.one).inverse;
            foreach (var mf in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<TMP_Text>() != null || !mf.TryGetComponent<MeshRenderer>(out var mr))
                    continue;
                var m = toSource * mf.transform.localToWorldMatrix;
                var part = new GameObject(mf.name, typeof(MeshFilter), typeof(MeshRenderer));
                part.transform.SetParent(root, false);
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.GetComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = part.GetComponent<MeshRenderer>();
                r.sharedMaterials = mr.sharedMaterials;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        // Page 1: a plate of food and a question mark.
        static TabletVisual LunchPicture(Transform tablet, Transform plate, Transform slice, Transform apple, Transform orange)
        {
            var v = Picture(tablet, "lunch", true, out var root);
            var d = Diorama(root, k_Pic + new Vector2(0f, -0.03f), 0.5f, 0.06f);
            MiniCopy(plate, d, Vector3.zero, 0f);
            MiniCopy(slice, d, new Vector3(-0.045f, 0.019f, 0f), 20f);
            MiniCopy(apple, d, new Vector3(0.055f, 0.007f, 0.035f), 0f);
            MiniCopy(orange, d, new Vector3(0.05f, 0.007f, -0.05f), 0f);
            TabText(root, "Question", "<b>? g</b> <size=55%>of carbs</size>", 0.2f, k_Gold, new Vector2(0.15f, 0.03f), k_Pic + new Vector2(0f, 0.058f));
            return v;
        }

        // Page 2: what a count that is too low or too high looks like on a CGM.
        static TabletVisual CountPicture(Transform tablet)
        {
            var v = Picture(tablet, "count", true, out var root);
            float x0 = k_Pic.x - 0.068f;
            string high = ColorUtility.ToHtmlStringRGB(TabletChart.High), low = ColorUtility.ToHtmlStringRGB(TabletChart.Low);
            TabText(root, "Too Low", $"Count too low → <color=#{high}><b>↑ HIGH</b></color>", 0.06f, Color.white, new Vector2(0.15f, 0.012f), new Vector2(k_Pic.x, 0.056f));
            PreviewGraph(root, new Vector2(x0, 0.006f), 0.136f, 0.038f, 60f, 25f);
            TabText(root, "Too High", $"Count too high → <color=#{low}><b>↓ LOW</b></color>", 0.06f, Color.white, new Vector2(0.15f, 0.012f), new Vector2(k_Pic.x, -0.017f));
            PreviewGraph(root, new Vector2(x0, -0.075f), 0.136f, 0.038f, 25f, 60f);
            return v;
        }

        static CgmGraph PreviewGraph(Transform parent, Vector2 origin, float w, float h, float eaten, float counted)
        {
            var go = new GameObject("Example CGM");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(origin.x, origin.y, k_TabZ);
            var g = go.AddComponent<CgmGraph>();
            g.width = w;
            g.height = h;
            g.maxMg = 300f;
            g.lineMaterial = s_Mats["Line"];
            g.preview = true;
            g.previewEaten = eaten;
            g.previewCounted = counted;
            return g;
        }

        // Page 3: the two served dishes.
        static TabletVisual GuessIntroPicture(Transform tablet, List<GuessStation> guess)
        {
            var v = Picture(tablet, "guess-intro", true, out var root);
            if (guess.Count > 0)
            {
                MiniCopy(guess[0].focus, Diorama(root, k_Pic + new Vector2(-0.04f, -0.035f), 0.42f, 0.035f), Vector3.zero, 0f);
                TabText(root, "Rice ?", $"<b>{guess[0].foodName}  ? g</b>", 0.07f, k_Gold, new Vector2(0.07f, 0.014f), k_Pic + new Vector2(-0.04f, 0.04f));
            }
            if (guess.Count > 1)
            {
                MiniCopy(guess[1].focus, Diorama(root, k_Pic + new Vector2(0.038f, -0.035f), 0.36f, 0.04f), Vector3.zero, 0f);
                TabText(root, "Pasta ?", $"<b>{guess[1].foodName}  ? g</b>", 0.07f, k_Gold, new Vector2(0.07f, 0.014f), k_Pic + new Vector2(0.038f, 0.04f));
            }
            return v;
        }

        // Page 4: a plate being built toward a target.
        static TabletVisual BuildIntroPicture(Transform tablet, Transform plate, Transform slice, Transform banana)
        {
            var v = Picture(tablet, "build-intro", true, out var root);
            var d = Diorama(root, k_Pic + new Vector2(0f, -0.03f), 0.5f, 0.06f);
            MiniCopy(plate, d, Vector3.zero, 0f);
            MiniCopy(slice, d, new Vector3(-0.045f, 0.013f, 0.01f), -12f);
            MiniCopy(slice, d, new Vector3(-0.04f, 0.026f, 0.005f), 8f);
            MiniCopy(banana, d, new Vector3(0.05f, 0.024f, 0f), 80f);
            TabText(root, "Target", "<b>target: ? g</b>", 0.09f, k_Gold, new Vector2(0.15f, 0.02f), k_Pic + new Vector2(0f, 0.058f));
            return v;
        }

        // Page 5: the controls, each with the button that does it.
        static TabletVisual ControlsPicture(Transform tablet)
        {
            var v = Picture(tablet, "controls", false, out var root);
            var rows = new (string key, float width, Material mat, string text)[]
            {
                ("A", 0f, s_Mats["Dark"], "Next, lock in, done  <color=#9aa6b8>(right controller)</color>"),
                ("B", 0f, s_Mats["Dark"], "Back, or replay the CGM graph"),
                ("STICK", 0.05f, s_Mats["Dark"], "Push forward, aim, let go: teleport"),
                ("GRIP", 0.05f, s_Mats["Dark"], "Hold to pick up food"),
                ("TOUCH", 0.05f, s_Mats["Dark"], "Fingertip: poke tablet and counter buttons"),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                float y = 0.05f - 0.03f * i;
                Badge(root, rows[i].key, new Vector2(-0.12f, y), rows[i].width, rows[i].mat);
                var text = TabText(root, $"Row {i}", rows[i].text, 0.075f, Color.white, new Vector2(0.24f, 0.02f), new Vector2(-0.085f, y), TextAlignmentOptions.Left);
                text.rectTransform.pivot = new Vector2(0f, 0.5f);
            }
            return v;
        }

        /// <summary>Round controller-button badge (A, B) or a pill with a word (GRIP).</summary>
        static void Badge(Transform parent, string text, Vector2 pos, float width, Material mat)
        {
            GameObject disc;
            if (width <= 0f)
            {
                disc = Part("Badge", parent, SaveMesh("Badge_Circle", MeshFactory.Cylinder("Badge_Circle", 0.011f, 0f, 0.002f, 32, MeshFactory.Axis.Z)), mat);
                disc.transform.localPosition = new Vector3(pos.x, pos.y, -0.0035f);
                var ring = Part("Badge Ring", parent, SaveMesh("Badge_Ring", FoodModels.Lathe(new[]
                {
                    V(0.0108f, 0f), V(0.0125f, 0f), V(0.0125f, 0.0022f), V(0.0108f, 0.0022f),
                }, 40).ToMesh("Badge_Ring")), s_Mats["White"]);
                ring.transform.SetLocalPositionAndRotation(new Vector3(pos.x, pos.y, -0.0014f), Quaternion.Euler(-90f, 0f, 0f));
                NoShadow(ring);
            }
            else
            {
                string key = $"Badge_Pill_{width * 1000f:0}";
                disc = Part("Badge", parent, SaveMesh(key, MeshFactory.RoundedBox(key, new Vector3(width, 0.002f, 0.02f), 0.0099f)), mat);
                disc.transform.SetLocalPositionAndRotation(new Vector3(pos.x, pos.y, -0.0025f), Quaternion.Euler(90f, 0f, 0f));
            }
            NoShadow(disc);
            var label = Text3D("Badge Text", parent, $"<b>{text}</b>", width <= 0f ? 0.1f : 0.055f, Color.white, new Vector2(Mathf.Max(width, 0.022f), 0.02f));
            label.transform.localPosition = new Vector3(pos.x, pos.y, -0.0045f);
        }

        /// <summary>A/B badge floating beside a tablet button (the button is rotated so its cap faces the learner).</summary>
        static void KeyHint(Transform button, string key, float dx)
        {
            var anchor = new GameObject($"Key Hint ({key})").transform;
            anchor.SetParent(button, false);
            anchor.SetLocalPositionAndRotation(new Vector3(dx, 0.012f, 0f), Quaternion.Euler(90f, 0f, 0f)); // back to tablet axes
            Badge(anchor, key, Vector2.zero, 0f, s_Mats["Dark"]);
        }

        // Guess round: the estimate in big numbers, and -10 / -5 / +5 / +10 buttons under it.
        static TabletVisual GuessPanel(Transform tablet, InfoTablet info)
        {
            var v = Picture(tablet, "guess", false, out var root);
            TabText(root, "Caption", "your estimate", 0.06f, k_Muted, new Vector2(0.2f, 0.012f), new Vector2(0f, 0.024f));
            v.value = TabText(root, "Value", "", 0.38f, Color.white, new Vector2(0.24f, 0.045f), new Vector2(0f, -0.006f));
            var amounts = new[] { -10f, -5f, 5f, 10f };
            info.adjustAmounts = amounts;
            info.adjustButtons = new PressButton3D[amounts.Length];
            for (int i = 0; i < amounts.Length; i++)
            {
                string label = amounts[i] > 0f ? $"+{amounts[i]:0}" : $"{amounts[i]:0}";
                info.adjustButtons[i] = SmallTabletButton(root, $"Adjust {label}", new Vector3(-0.105f + 0.07f * i, -0.062f, 0f), label,
                    amounts[i] > 0f ? s_Mats["Blue"] : s_Mats["Grey"]);
            }
            return v;
        }

        // Build round: the carb target in a gold ring.
        static TabletVisual TargetPicture(Transform tablet)
        {
            var v = Picture(tablet, "target", true, out var root);
            var ring = Part("Ring", root, SaveMesh("Tablet_Target_Ring", FoodModels.Lathe(new[]
            {
                V(0.05f, 0f), V(0.056f, 0f), V(0.056f, 0.002f), V(0.05f, 0.002f),
            }, 72).ToMesh("Tablet_Target_Ring")), s_Mats["Arrow"]);
            ring.transform.SetLocalPositionAndRotation(new Vector3(k_Pic.x, k_Pic.y + 0.004f, -0.001f), Quaternion.Euler(-90f, 0f, 0f));
            TabText(root, "Caption", "carb target", 0.06f, k_Muted, new Vector2(0.09f, 0.012f), k_Pic + new Vector2(0f, 0.028f));
            v.value = TabText(root, "Value", "", 0.32f, k_Gold, new Vector2(0.095f, 0.04f), k_Pic + new Vector2(0f, 0.002f));
            TabText(root, "Under", "on your plate", 0.05f, k_Muted, new Vector2(0.09f, 0.012f), k_Pic + new Vector2(0f, -0.026f));
            return v;
        }

        // Result: "what you counted" vs "what you ate" bars, and where glucose goes. The CGM graph plays below.
        static TabletVisual ResultChart(Transform tablet)
        {
            var v = Picture(tablet, "result", false, out var root);
            var chart = root.gameObject.AddComponent<TabletChart>();
            chart.maxWidth = 0.13f;
            const float pivotX = -0.086f, barH = 0.016f;
            var rows = new GameObject[2];
            var bars = new TabletBar[2];
            var labels = new TMP_Text[2];
            for (int i = 0; i < 2; i++)
            {
                float y = 0.014f - 0.028f * i;
                rows[i] = new GameObject($"Row {i}");
                rows[i].transform.SetParent(root, false);
                labels[i] = TabText(rows[i].transform, "Label", "", 0.065f, Color.white, new Vector2(0.066f, barH), new Vector2(pivotX - 0.004f - 0.033f, y), TextAlignmentOptions.Right);
                bars[i] = ChartBar(rows[i].transform, pivotX, y, barH, 0.06f);
            }
            chart.rows = rows;
            chart.bars = bars;
            chart.labels = labels;
            var chip = TabText(root, "Glucose", "", 0.08f, Color.white, new Vector2(0.075f, 0.04f), new Vector2(0.12f, 0f));
            chip.enableAutoSizing = true;
            chip.fontSizeMin = 0.05f;
            chip.fontSizeMax = 0.085f;
            chart.chips = new TMP_Text[] { chip };
            return v;
        }

        // Summary: one row per meal (counted bar over ate bar), a glucose chip each, and a star score.
        static TabletVisual SummaryChart(Transform tablet, int rounds)
        {
            var v = Picture(tablet, "summary", false, out var root);
            var chart = root.gameObject.AddComponent<TabletChart>();
            chart.maxWidth = 0.13f;
            const float pivotX = -0.086f, barH = 0.008f;
            var rows = new GameObject[rounds];
            var bars = new TabletBar[rounds * 2];
            var labels = new TMP_Text[rounds];
            var chips = new TMP_Text[rounds];
            for (int i = 0; i < rounds; i++)
            {
                float y = 0.014f - 0.026f * i;
                rows[i] = new GameObject($"Row {i}");
                rows[i].transform.SetParent(root, false);
                labels[i] = TabText(rows[i].transform, "Label", "", 0.065f, Color.white, new Vector2(0.066f, 0.016f), new Vector2(pivotX - 0.004f - 0.033f, y), TextAlignmentOptions.Right);
                bars[2 * i] = ChartBar(rows[i].transform, pivotX, y + 0.005f, barH, 0.042f);
                bars[2 * i + 1] = ChartBar(rows[i].transform, pivotX, y - 0.005f, barH, 0.042f);
                chips[i] = TabText(rows[i].transform, "Glucose", "", 0.065f, Color.white, new Vector2(0.075f, 0.016f), new Vector2(0.125f, y));
            }
            chart.rows = rows;
            chart.bars = bars;
            chart.labels = labels;
            chart.chips = chips;
            chart.stars = TabText(root, "Stars", "", 0.16f, k_Gold, new Vector2(0.09f, 0.026f), new Vector2(0.11f, k_TabH / 2f - k_TabPad - 0.012f), TextAlignmentOptions.Right);
            TabText(root, "Legend", "Top bar: what you counted (colour shows where glucose goes).   Bottom bar: what you ate.",
                0.045f, k_Muted, new Vector2(k_TabW - 2f * k_TabPad, 0.012f), new Vector2(0f, -k_TabH / 2f + 0.012f));
            return v;
        }

        /// <summary>Bar growing to the right from <paramref name="x"/>, with its value at the tip.</summary>
        static TabletBar ChartBar(Transform parent, float x, float y, float height, float valueSize)
        {
            var pivot = new GameObject("Bar").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = new Vector3(x, y, k_TabZ + 0.0004f);
            pivot.localScale = new Vector3(0.001f, 1f, 1f);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Fill";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(pivot, false);
            quad.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            quad.transform.localScale = new Vector3(1f, height, 1f);
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = s_Mats["Line"];
            r.shadowCastingMode = ShadowCastingMode.Off;
            var value = TabText(parent, "Value", "", valueSize, Color.white, new Vector2(0.05f, height + 0.006f), new Vector2(x + 0.004f, y), TextAlignmentOptions.Left);
            value.rectTransform.pivot = new Vector2(0f, 0.5f);
            return new TabletBar { pivot = pivot, fill = r, value = value };
        }

        // ------------------------------------------------------------------ tablet buttons

        /// <summary>Poke button standing on the tablet's lower edge, cap toward the learner, label on the cap.</summary>
        static PressButton3D TabletButton(Transform parent, string name, Vector3 pos, string label, Material cap, out TextMeshPro labelText)
        {
            var button = PokeButton(name, parent, pos, label, cap, out labelText);
            button.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // +Y (the cap) toward the viewer
            var bb = labelText.GetComponent<BillboardLabel>();
            if (bb != null) Object.DestroyImmediate(bb);
            labelText.transform.SetLocalPositionAndRotation(new Vector3(0f, 0.035f, 0f), Quaternion.Euler(90f, 0f, 0f));
            labelText.fontSize = 0.1f;
            labelText.color = Color.white;
            NoShadow(button.gameObject);
            return button;
        }

        /// <summary>Smaller poke button on the tablet face (for the estimate), cap toward the learner.</summary>
        static PressButton3D SmallTabletButton(Transform parent, string name, Vector3 pos, string label, Material capMaterial)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetLocalPositionAndRotation(pos, Quaternion.Euler(-90f, 0f, 0f));
            Box("Base", root.transform, s_Mats["Dark"], new Vector3(0f, 0.004f, 0f), new Vector3(0.058f, 0.008f, 0.042f), false);
            var cap = Part("Cap", root.transform, SaveMesh("Small_Button_Cap", MeshFactory.Cylinder("Small_Button_Cap", 0.019f, 0.008f, 0.018f, 32, MeshFactory.Axis.Y)), capMaterial);
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.012f, 0f);
            col.size = new Vector3(0.058f, 0.024f, 0.042f);
            var button = root.AddComponent<PressButton3D>();
            button.cap = cap.transform;
            button.pressDepth = 0.004f;
            button.colliders.Add(col);
            var text = Text3D("Label", cap.transform, $"<b>{label}</b>", 0.11f, Color.white, new Vector2(0.04f, 0.02f));
            text.transform.SetLocalPositionAndRotation(new Vector3(0f, 0.0185f, 0f), Quaternion.Euler(90f, 0f, 0f));
            NoShadow(root);
            return button;
        }
    }
}
