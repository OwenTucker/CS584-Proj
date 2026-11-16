using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace T1DCGM.EditorTools
{
    public static partial class DemoSceneBuilder
    {
        const string k_FoodPack = "Assets/Food Pack-Demo/Prefabs";
        const float k_TableTop = 0.9f;
        const float k_TableZ = 0.55f;      // counter centre; 0.62 deep, so the front edge is at 0.24 and pans are in reach
        const float k_TableDepth = 0.62f;
        const float k_PlateZ = 0.39f;
        const float k_PanZ = 0.69f;
        static readonly float[] k_StationX = { -0.95f, 0f, 0.95f };

        /// <summary>Scene 2: virtual cafeteria with a buffet line of rice, pasta and bread, and the CGM display.</summary>
        static void BuildLunchScene()
        {
            var scene = NewScene();
            AddXRRig(new Vector3(0f, 0f, -1.1f), 0f);
            var env = new GameObject("Environment").transform;
            BuildCafeteria(env);

            var board = BuildBoard("Board", env, new Vector3(0f, 1.78f, 1.55f), Quaternion.identity, new Vector2(1.7f, 0.95f));
            Box("Board Backing", env, s_Mats["Dark"], new Vector3(0f, 1.78f, 1.565f), new Vector3(1.76f, 1.01f, 0.01f), false);

            var stations = new List<LunchStation>
            {
                ScoopStation(env, FoodKind.Rice, k_StationX[0]),
                ScoopStation(env, FoodKind.Pasta, k_StationX[1]),
                BreadStation(env, k_StationX[2]),
            };

            var pads = new GameObject("Teleport Anchors").transform;
            pads.SetParent(env);
            TeleportPad(pads, "Rice Pad", new Vector3(k_StationX[0], 0f, 0f), 0f, s_Mats["Blue"], "RICE");
            TeleportPad(pads, "Pasta Pad", new Vector3(k_StationX[1], 0f, 0f), 0f, s_Mats["Blue"], "PASTA");
            TeleportPad(pads, "Bread Pad", new Vector3(k_StationX[2], 0f, 0f), 0f, s_Mats["Blue"], "BREAD");
            TeleportPad(pads, "CGM Pad", new Vector3(2.05f, 0f, 0.4f), 0f, s_Mats["Teal"], "CGM");

            var (wallGraph, phoneGraph, display, phone) = BuildCgmCorner(env);
            BuildDecorTable(env);
            var arrow = BuildGuideArrow();

            var managerGo = new GameObject("Lunch Quiz Manager");
            var manager = managerGo.AddComponent<LunchQuizManager>();
            manager.board = board;
            manager.arrow = arrow;
            manager.stations = stations;
            manager.wallGraph = wallGraph;
            manager.phoneGraph = phoneGraph;
            manager.cgmDisplay = display;
            manager.cgmTarget = phone;
            manager.labelFont = s_Font;
            manager.audioSource = AddAudio(managerGo);

            EditorSceneManager.SaveScene(scene, k_LunchScenePath);
        }

        static void BuildCafeteria(Transform env)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor (teleport)";
            floor.transform.SetParent(env);
            floor.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            floor.GetComponent<Renderer>().sharedMaterial = s_Mats["Floor"];
            var area = floor.AddComponent<TeleportationArea>();
            area.interactionLayers = InteractionLayerMask.GetMask("Teleport");

            var wall = s_Mats["Wall"];
            const float h = 2.8f, t = 0.1f;
            Box("Back Wall", env, wall, new Vector3(0f, h / 2f, 1.65f), new Vector3(8f, h, t), true);
            Box("Front Wall", env, wall, new Vector3(0f, h / 2f, -3.5f), new Vector3(8f, h, t), true);
            Box("Left Wall", env, wall, new Vector3(-3.4f, h / 2f, -0.9f), new Vector3(t, h, 5.2f), true);
            Box("Right Wall", env, wall, new Vector3(3.4f, h / 2f, -0.9f), new Vector3(t, h, 5.2f), true);

            // Buffet counter with a steel top and a sneeze-guard rail.
            Box("Buffet Counter", env, s_Mats["Counter"], new Vector3(0f, (k_TableTop - 0.02f) / 2f, k_TableZ), new Vector3(2.9f, k_TableTop - 0.02f, k_TableDepth), true);
            Box("Counter Top", env, s_Mats["Steel"], new Vector3(0f, k_TableTop - 0.01f, k_TableZ), new Vector3(2.94f, 0.02f, k_TableDepth + 0.04f), true);

            var sign = Text3D("Sign", env, "<b>LUNCH: COUNT YOUR CARBS</b>", 1.2f, new Color(0.15f, 0.3f, 0.5f), new Vector2(4f, 0.2f));
            sign.transform.localPosition = new Vector3(0f, 2.45f, 1.59f);
        }

        /// <summary>Rice or pasta: hotel pan, serving spoon, plate, buttons and the answer.</summary>
        static LunchStation ScoopStation(Transform env, FoodKind kind, float x)
        {
            string name = FoodData.Name(kind);
            var root = new GameObject($"{char.ToUpperInvariant(name[0]) + name.Substring(1)} Station").transform;
            root.SetParent(env);
            root.localPosition = new Vector3(x, k_TableTop, 0f);
            var foodMat = kind == FoodKind.Rice ? s_Mats["Rice"] : s_Mats["PastaBase"];

            // Hotel pan (half size, 32.5 x 26.5 cm, 6.5 cm deep) full of food.
            var pan = new GameObject($"{name} pan").transform;
            pan.SetParent(root, false);
            pan.localPosition = new Vector3(0f, 0f, k_PanZ);
            const float pw = 0.325f, pd = 0.265f, ph = 0.065f, wt = 0.004f;
            Box("Bottom", pan, s_Mats["Steel"], new Vector3(0f, wt / 2f, 0f), new Vector3(pw, wt, pd), false);
            Box("Wall L", pan, s_Mats["Steel"], new Vector3(-pw / 2f, ph / 2f, 0f), new Vector3(wt, ph, pd), false);
            Box("Wall R", pan, s_Mats["Steel"], new Vector3(pw / 2f, ph / 2f, 0f), new Vector3(wt, ph, pd), false);
            Box("Wall B", pan, s_Mats["Steel"], new Vector3(0f, ph / 2f, pd / 2f), new Vector3(pw, ph, wt), false);
            Box("Wall F", pan, s_Mats["Steel"], new Vector3(0f, ph / 2f, -pd / 2f), new Vector3(pw, ph, wt), false);
            var slab = Part("Food", pan, SaveMesh($"Pan_{name}", MeshFactory.RoundedBox($"Pan_{name}", new Vector3(pw - 0.01f, 0.05f, pd - 0.01f), 0.02f)), foodMat);
            slab.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            if (kind == FoodKind.Pasta)
                ScatterPenne(pan, new Vector3(pw - 0.03f, pd - 0.03f), 0.056f, 90);

            var areaGo = new GameObject("Scoop Area");
            areaGo.layer = 2; // Ignore Raycast: keep the teleport ray from hitting it
            areaGo.transform.SetParent(pan, false);
            var area = areaGo.AddComponent<BoxCollider>();
            area.isTrigger = true;
            area.center = new Vector3(0f, 0.07f, 0f);
            area.size = new Vector3(pw, 0.11f, pd);
            var tray = pan.gameObject.AddComponent<FoodTray>();
            tray.kind = kind;
            tray.area = area;
            Label(pan, name.ToUpperInvariant(), new Vector3(0f, 0.2f, 0f), 0.35f);

            var plate = BuildPlate(root, new Vector3(0f, 0f, k_PlateZ), kind);
            var spoon = BuildSpoon(root, new Vector3(0.25f, 0f, 0.36f), kind);
            var station = new LunchStation { kind = kind, plate = plate, spoon = spoon };
            Buttons(root, station);

            // Answer: what 50 g looks like, and a 1 cup ball (about a fist) for comparison.
            var reveal = new GameObject("Answer (hidden until DONE)").transform;
            reveal.SetParent(root, false);
            var target = NewMound(reveal, $"50 g of {name}", new Vector3(-0.33f, 0f, k_PlateZ), kind, s_Mats["TargetFood"], false);
            station.targetMound = target;
            Label(target.transform, "<b>50 g</b>", new Vector3(0f, 0.12f, 0f), 0.3f, new Color(0.45f, 1f, 0.55f));
            float cupRadius = Mathf.Pow(3f * FoodData.CupMl * 1e-6f / (4f * Mathf.PI), 1f / 3f);
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "1 Cup (about a fist)";
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.transform.SetParent(reveal, false);
            ball.transform.localPosition = new Vector3(-0.33f, cupRadius, 0.6f);
            ball.transform.localScale = Vector3.one * cupRadius * 2f;
            ball.GetComponent<Renderer>().sharedMaterial = s_Mats["CupBall"];
            Label(ball.transform, "1 cup = a fist", new Vector3(0f, 0.11f, 0f), 0.22f, new Color(1f, 0.9f, 0.4f), true);
            station.reveal = reveal.gameObject;
            return station;
        }

        static LunchStation BreadStation(Transform env, float x)
        {
            var root = new GameObject("Bread Station").transform;
            root.SetParent(env);
            root.localPosition = new Vector3(x, k_TableTop, 0f);

            var boardGo = Box("Bread Board", root, s_Mats["Wood"], new Vector3(0f, 0.01f, k_PanZ), new Vector3(0.42f, 0.02f, 0.3f), true);
            Label(boardGo.transform.parent, "BREAD", new Vector3(0f, 0.2f, k_PanZ), 0.35f);

            // Three kinds: same food, different sizes. Stacked so the learner sees thickness.
            for (int i = 0; i < 4; i++)
                Slice(root, "Sandwich slice", new Vector3(-0.13f, 0.02f + 0.0125f * i + 0.006f, k_PanZ), new Vector3(0.11f, 0.012f, 0.115f), FoodData.SandwichSliceCarbs);
            for (int i = 0; i < 3; i++)
                Slice(root, "Thick slice", new Vector3(0f, 0.02f + 0.025f * i + 0.012f, k_PanZ), new Vector3(0.12f, 0.024f, 0.125f), FoodData.ThickSliceCarbs);
            for (int i = 0; i < 2; i++)
                Bagel(root, new Vector3(0.14f, 0.02f + 0.033f * i, k_PanZ));

            var plate = BuildPlate(root, new Vector3(0f, 0f, k_PlateZ), FoodKind.Bread);
            var station = new LunchStation { kind = FoodKind.Bread, plate = plate };
            Buttons(root, station);

            var reveal = new GameObject("Answer (hidden until DONE)").transform;
            reveal.SetParent(root, false);
            var t = Text3D("50 g of bread", reveal,
                "<b>50 g of carbs =</b>\n3 1/2 sandwich slices\n2 thick slices\n1 bagel", 0.22f, new Color(0.45f, 1f, 0.55f), new Vector2(0.4f, 0.16f));
                        t.gameObject.AddComponent<BillboardLabel>().height = 0.12f;
            // BillboardLabel follows its parent, so give it its own anchor.
            var anchor = new GameObject("Label Anchor").transform;
            anchor.SetParent(reveal, false);
            anchor.localPosition = new Vector3(-0.34f, 0f, k_PlateZ);
            t.transform.SetParent(anchor, false);
            station.reveal = reveal.gameObject;
            return station;
        }

        static void Buttons(Transform root, LunchStation station)
        {
            station.doneButton = PokeButton("Done Button", root, new Vector3(0.2f, 0f, 0.29f), "DONE", s_Mats["Green"], out var doneLabel);
            station.doneLabel = doneLabel;
            station.resetButton = PokeButton("Reset Button", root, new Vector3(-0.2f, 0f, 0.29f), "RESET", s_Mats["Red"], out _);
        }

        static Plate BuildPlate(Transform parent, Vector3 localPosition, FoodKind kind)
        {
            var root = new GameObject("Plate");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            // Standard 10.5 inch (26.7 cm) dinner plate.
            Part("Plate Mesh", root.transform, SaveMesh("Dinner_Plate", MeshFactory.Lathe("Dinner_Plate", new[]
            {
                V(0f, 0f), V(0.095f, 0f), V(0.1335f, 0.017f), V(0.1335f, 0.02f), V(0.126f, 0.02f), V(0.095f, 0.007f), V(0f, 0.007f),
            }, 48, MeshFactory.Axis.Y)), s_Mats["Plate"]);
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.0035f, 0f);
            col.size = new Vector3(0.2f, 0.007f, 0.2f);
            var plate = root.AddComponent<Plate>();
            plate.radius = 0.125f;
            if (kind != FoodKind.Bread)
                plate.mound = NewMound(root.transform, "Served food", new Vector3(0f, 0.007f, 0f), kind,
                    kind == FoodKind.Rice ? s_Mats["Rice"] : s_Mats["PastaBase"], true);
            return plate;
        }

        static FoodMound NewMound(Transform parent, string name, Vector3 localPosition, FoodKind kind, Material material, bool pieces)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            var mound = go.AddComponent<FoodMound>();
            mound.kind = kind;
            if (pieces && kind == FoodKind.Pasta)
            {
                mound.pieceMesh = PenneMesh();
                mound.pieceMaterial = s_Mats["Penne"];
            }
            return mound;
        }

        static Mesh PenneMesh() => SaveMesh("Penne", MeshFactory.Lathe("Penne", new[]
        {
            // Open tube: outside, rim, inside (both ends open), 3.6 cm long.
            V(0.0045f, -0.018f), V(0.0048f, 0f), V(0.0045f, 0.018f), V(0.0032f, 0.018f), V(0.0035f, 0f), V(0.0032f, -0.018f), V(0.0045f, -0.018f),
        }, 10, MeshFactory.Axis.Z));

        static void ScatterPenne(Transform pan, Vector2 size, float y, int count)
        {
            var mesh = PenneMesh();
            var rng = new System.Random(3);
            var group = new GameObject("Penne").transform;
            group.SetParent(pan, false);
            for (int i = 0; i < count; i++)
            {
                var p = Part("Piece", group, mesh, s_Mats["Penne"]);
                p.transform.localPosition = new Vector3(((float)rng.NextDouble() - 0.5f) * size.x, y + (float)rng.NextDouble() * 0.006f, ((float)rng.NextDouble() - 0.5f) * size.y);
                p.transform.localRotation = Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 30f, (float)rng.NextDouble() * 360f, 0f);
                p.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>Serving spoon lying on the counter; hold it by the handle.</summary>
        static ServingSpoon BuildSpoon(Transform parent, Vector3 localPosition, FoodKind kind)
        {
            var root = new GameObject("Serving Spoon");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition + new Vector3(0f, 0.002f, 0f);
            // Handle toward the learner, bowl beside the pan (local +Z runs from handle to bowl).

            var handle = Part("Handle", root.transform, SaveMesh("Spoon_Handle", MeshFactory.Cylinder("Spoon_Handle", 0.0065f, 0f, 0.25f, 16, MeshFactory.Axis.Z)), s_Mats["Steel"]);
            handle.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            var bowl = new GameObject("Bowl").transform;
            bowl.SetParent(root.transform, false);
            bowl.localPosition = new Vector3(0f, 0f, 0.29f);
            Part("Bowl Mesh", bowl, SaveMesh("Spoon_Bowl", MeshFactory.Lathe("Spoon_Bowl", new[]
            {
                V(0f, 0f), V(0.03f, 0.002f), V(0.043f, 0.016f), V(0.044f, 0.02f), V(0.041f, 0.02f), V(0.03f, 0.006f), V(0f, 0.004f),
            }, 32, MeshFactory.Axis.Y)), s_Mats["Steel"]);
            var contents = NewMound(bowl, "Spoon Contents", new Vector3(0f, 0.004f, 0f), kind, kind == FoodKind.Rice ? s_Mats["Rice"] : s_Mats["PastaBase"], true);
            contents.maxVolumeMl = 200f;
            contents.piecesPerMl = 0.2f;

            var grip = new GameObject("Grip").transform; // hand holds the handle end, spoon pointing forward
            grip.SetParent(root.transform, false);
            grip.localPosition = new Vector3(0f, 0.012f, 0.05f);

            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.012f, 0.165f);
            col.size = new Vector3(0.088f, 0.026f, 0.34f);
            var grab = Grab(root, col, 0.15f);
            grab.attachTransform = grip;
            var spoon = root.AddComponent<ServingSpoon>();
            spoon.bowl = bowl;
            spoon.contents = contents;
            root.AddComponent<ReturnHome>();
            return spoon;
        }

        static void Slice(Transform parent, string displayName, Vector3 localPosition, Vector3 size, float carbs)
        {
            var root = new GameObject(displayName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            string key = $"Bread_{size.x * 1000f:0}x{size.y * 1000f:0}";
            Part("Crust", root.transform, SaveMesh(key + "_Crust", MeshFactory.RoundedBox(key + "_Crust", size, 0.025f)), s_Mats["Crust"]);
            Part("Crumb", root.transform, SaveMesh(key + "_Crumb", MeshFactory.RoundedBox(key + "_Crumb",
                new Vector3(size.x - 0.008f, size.y + 0.0006f, size.z - 0.008f), 0.021f)), s_Mats["Crumb"]);
            var col = root.AddComponent<BoxCollider>();
            col.size = size;
            Grab(root, col, 0.03f);
            var item = root.AddComponent<BreadItem>();
            item.displayName = displayName;
            item.carbs = carbs;
            root.AddComponent<ReturnHome>();
        }

        static void Bagel(Transform parent, Vector3 localPosition)
        {
            var root = new GameObject("Bagel");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            var profile = new List<Vector2>();
            for (int i = 0; i <= 24; i++)
            {
                float a = (-90f + 360f * i / 24f) * Mathf.Deg2Rad;
                profile.Add(V(0.032f + 0.018f * Mathf.Cos(a), 0.016f + 0.016f * Mathf.Sin(a)));
            }
            Part("Bagel Mesh", root.transform, SaveMesh("Bagel", MeshFactory.Lathe("Bagel", profile, 32, MeshFactory.Axis.Y)), s_Mats["Bagel"]);
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.016f, 0f);
            col.size = new Vector3(0.1f, 0.032f, 0.1f);
            Grab(root, col, 0.1f);
            var item = root.AddComponent<BreadItem>();
            item.displayName = "Bagel";
            item.carbs = FoodData.BagelCarbs;
            root.AddComponent<ReturnHome>();
        }

        /// <summary>Wall display and a phone, both showing the CGM after lunch.</summary>
        static (CgmGraph wall, CgmGraph phone, GameObject display, Transform phoneTransform) BuildCgmCorner(Transform env)
        {
            var display = new GameObject("CGM Display (after lunch)").transform;
            display.SetParent(env);
            display.localPosition = new Vector3(2.05f, 1.62f, 1.58f);
            Box("Panel", display, s_Mats["Dark"], new Vector3(0f, 0f, 0.01f), new Vector3(1.05f, 0.72f, 0.02f), false);
            var title = Text3D("Title", display, "<b>YOUR CGM</b>  readings every 5 minutes", 0.38f, Color.white, new Vector2(1f, 0.06f));
            title.transform.localPosition = new Vector3(0f, 0.3f, -0.002f);
            var value = Text3D("Value", display, "--", 0.9f, Color.white, new Vector2(1f, 0.12f));
            value.transform.localPosition = new Vector3(0f, 0.19f, -0.002f);
            var caption = Text3D("Caption", display, "", 0.28f, new Color(0.7f, 0.75f, 0.8f), new Vector2(1f, 0.05f));
            caption.transform.localPosition = new Vector3(0f, -0.32f, -0.002f);
            var graphGo = new GameObject("Graph");
            graphGo.transform.SetParent(display, false);
            graphGo.transform.localPosition = new Vector3(-0.45f, -0.27f, -0.004f);
            var wall = graphGo.AddComponent<CgmGraph>();
            wall.width = 0.9f;
            wall.height = 0.38f;
            wall.lineMaterial = s_Mats["Line"];
            wall.valueText = value;
            wall.captionText = caption;
            AxisLabels(display, new Vector3(-0.45f, -0.27f, -0.004f), wall);

            // Side table with a phone running the CGM app.
            Box("Side Table", env, s_Mats["Wood"], new Vector3(2.05f, 0.375f, 1.0f), new Vector3(0.6f, 0.75f, 0.4f), true);
            var phone = new GameObject("Phone");
            phone.transform.SetParent(env, false);
            phone.transform.localPosition = new Vector3(2.05f, 0.755f, 0.95f);
            // Screen text runs away from the learner, so it reads upright when looking down at the table.
            Part("Body", phone.transform, SaveMesh("Phone_Body", MeshFactory.RoundedBox("Phone_Body", new Vector3(0.075f, 0.008f, 0.155f), 0.01f)), s_Mats["Dark"]);
            var screen = Part("Screen", phone.transform, SaveMesh("Phone_Screen", MeshFactory.RoundedBox("Phone_Screen", new Vector3(0.069f, 0.001f, 0.148f), 0.008f)), s_Mats["PhoneScreen"]);
            screen.transform.localPosition = new Vector3(0f, 0.0045f, 0f);
            var face = new GameObject("Screen Content").transform; // local -Z faces up out of the screen
            face.SetParent(phone.transform, false);
            face.localPosition = new Vector3(0f, 0.0052f, 0f);
            face.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var pValue = Text3D("Value", face, "--", 0.16f, Color.white, new Vector2(0.065f, 0.025f));
            pValue.transform.localPosition = new Vector3(0f, 0.045f, 0f);
            var pTitle = Text3D("App", face, "<b>CGM</b>", 0.07f, new Color(0.6f, 0.8f, 1f), new Vector2(0.065f, 0.012f));
            pTitle.transform.localPosition = new Vector3(0f, 0.065f, 0f);
            var pGraphGo = new GameObject("Graph");
            pGraphGo.transform.SetParent(face, false);
            pGraphGo.transform.localPosition = new Vector3(-0.031f, -0.06f, 0f);
            var phoneGraph = pGraphGo.AddComponent<CgmGraph>();
            phoneGraph.width = 0.062f;
            phoneGraph.height = 0.07f;
            phoneGraph.lineMaterial = s_Mats["Line"];
            phoneGraph.valueText = pValue;
            var col = phone.AddComponent<BoxCollider>();
            col.size = new Vector3(0.075f, 0.01f, 0.155f);
            Grab(phone, col, 0.2f);
            phone.AddComponent<ReturnHome>();
            Label(phone.transform, "phone", new Vector3(0f, 0.08f, 0f), 0.18f, Color.white, true);

            display.gameObject.SetActive(true);
            return (wall, phoneGraph, display.gameObject, phone.transform);
        }

        static void AxisLabels(Transform parent, Vector3 origin, CgmGraph g)
        {
            foreach (var mg in new[] { 70f, 180f, 300f })
            {
                var t = Text3D($"{mg}", parent, $"{mg:0}", 0.2f, new Color(0.7f, 0.75f, 0.8f), new Vector2(0.08f, 0.03f));
                t.alignment = TextAlignmentOptions.Right;
                t.transform.localPosition = origin + new Vector3(-0.05f, g.height * Mathf.InverseLerp(g.minMg, g.maxMg, mg), 0f);
            }
            for (int h = 0; h <= 4; h++)
            {
                var t = Text3D($"{h} h", parent, $"{h} h", 0.2f, new Color(0.7f, 0.75f, 0.8f), new Vector2(0.08f, 0.03f));
                t.transform.localPosition = origin + new Vector3(g.width * h / 4f, -0.025f, 0f);
            }
        }

        /// <summary>Other dishes from the low-poly food pack, scaled to real-world sizes, for a fuller buffet.</summary>
        static void BuildDecorTable(Transform env)
        {
            var root = new GameObject("Other Dishes").transform;
            root.SetParent(env);
            Box("Side Counter", root, s_Mats["Counter"], new Vector3(-2.75f, 0.44f, 0.2f), new Vector3(0.75f, 0.88f, 2.2f), true);
            var sign = Text3D("Sign", root, "<b>Also on the menu</b>\n<size=70%>not part of today's challenge</size>", 0.6f, new Color(0.15f, 0.3f, 0.5f), new Vector2(1.6f, 0.3f));
            sign.transform.SetPositionAndRotation(new Vector3(-3.33f, 1.6f, 0.2f), Quaternion.Euler(0f, -90f, 0f));

            // name, largest real-world dimension (m)
            var items = new (string prefab, float size)[]
            {
                ("Pizza Margarita", 0.30f), ("Curry plate", 0.27f), ("Udon", 0.2f), ("Hotdog A", 0.18f),
                ("Cookies", 0.15f), ("Doughnut D", 0.09f), ("Soda can C", 0.122f),
            };
            float z = -0.7f;
            foreach (var (prefabName, size) in items)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{k_FoodPack}/{prefabName}.prefab");
                if (prefab == null)
                {
                    Debug.LogWarning($"[T1D] Food pack prefab not found: {prefabName}");
                    continue;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                go.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
                var b = RendererBounds(go.transform);
                float max = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (max > 1e-4f) go.transform.localScale *= size / max;
                b = RendererBounds(go.transform);
                go.transform.position += new Vector3(-2.75f, 0.88f, z) - new Vector3(b.center.x, b.min.y, b.center.z);
                z += Mathf.Max(0.2f, size + 0.06f);
            }
        }

        static void Label(Transform parent, string text, Vector3 localPosition, float fontSize, Color? color = null, bool billboard = false)
        {
            var anchor = new GameObject("Label").transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = localPosition;
            var t = Text3D("Text", anchor, $"<b>{text}</b>", fontSize, color ?? Color.white, new Vector2(0.5f, 0.06f));
            if (billboard)
                anchor.gameObject.AddComponent<BillboardLabel>().height = localPosition.y;
            else
                anchor.rotation = Quaternion.identity; // faces -Z, toward the learner at the counter
        }
    }
}
