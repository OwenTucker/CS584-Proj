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
        const float k_TableZ = 0.53f;      // counter centre; 0.62 deep, so the front edge is at 0.22
        const float k_TableDepth = 0.62f;
        const float k_BackWall = 1.45f;
        static readonly float[] k_StationX = { -1.35f, -0.45f, 0.45f, 1.35f };

        /// <summary>Scene 2: a cafeteria buffet. Guess the carbs in two served dishes, then build two plates to random targets.</summary>
        static void BuildLunchScene()
        {
            var scene = NewScene();
            SetupLunchLighting();
            var rig = AddXRRig(new Vector3(0f, 0f, -1.1f), 0f);
            var head = rig.GetComponentInChildren<Camera>(true).transform;
            var env = new GameObject("Environment").transform;
            BuildCafeteria(env);

            var guess = new List<GuessStation>
            {
                GuessStationRice(env, k_StationX[0]),
                GuessStationPasta(env, k_StationX[1]),
            };
            var build = new List<BuildStation>
            {
                BreadStation(env, k_StationX[2]),
                FruitStation(env, k_StationX[3]),
            };

            var pads = new GameObject("Teleport Anchors").transform;
            pads.SetParent(env);
            for (int i = 0; i < k_StationX.Length; i++)
                TeleportPad(pads, $"Station {i + 1} Pad", new Vector3(k_StationX[i], 0f, -0.05f), 0f, s_Mats["Pad"], "");

            BuildDiningArea(env);
            BuildDecorTable(env);
            var arrow = BuildGuideArrow();
            var tablet = BuildTablet(head, guess, build);

            var managerGo = new GameObject("Lunch Quiz Manager");
            var manager = managerGo.AddComponent<LunchQuizManager>();
            manager.tablet = tablet;
            manager.arrow = arrow;
            manager.guessStations = guess;
            manager.buildStations = build;
            manager.labelFont = s_Font;
            manager.audioSource = AddAudio(managerGo);

            EditorSceneManager.SaveScene(scene, k_LunchScenePath);
        }

        static void SetupLunchLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.78f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.57f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.31f, 0.26f);
            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(48f, 70f, 0f); // daylight through the left-hand windows
                sun.intensity = 0.9f;
                sun.color = new Color(1f, 0.95f, 0.86f);
            }
        }

        // ------------------------------------------------------------------ room

        static void BuildCafeteria(Transform env)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor (teleport)";
            floor.transform.SetParent(env);
            floor.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            floor.GetComponent<Renderer>().sharedMaterial = s_Mats["WoodFloor"];
            var area = floor.AddComponent<TeleportationArea>();
            area.interactionLayers = InteractionLayerMask.GetMask("Teleport");

            // Walls: wood wainscot below a chair rail, warm paint above. No shadows from the shell (the sun "comes in").
            const float h = 2.9f, t = 0.1f, front = -3.6f, side = 3.4f;
            float depth = k_BackWall - front, midZ = (k_BackWall + front) / 2f;
            var shell = new GameObject("Room Shell").transform;
            shell.SetParent(env);
            Wall(shell, "Back Wall", new Vector3(0f, h / 2f, k_BackWall + t / 2f), new Vector3(2f * side, h, t));
            Wall(shell, "Front Wall", new Vector3(0f, h / 2f, front - t / 2f), new Vector3(2f * side, h, t));
            Wall(shell, "Left Wall", new Vector3(-side - t / 2f, h / 2f, midZ), new Vector3(t, h, depth));
            Wall(shell, "Right Wall", new Vector3(side + t / 2f, h / 2f, midZ), new Vector3(t, h, depth));
            NoShadow(Box("Ceiling", shell, s_Mats["Ceiling"], new Vector3(0f, h + 0.02f, midZ), new Vector3(2f * side, 0.04f, depth), false));
            foreach (var (pos, size) in new[]
            {
                (new Vector3(-side + 0.01f, 0.5f, midZ), new Vector3(0.02f, 1f, depth)),
                (new Vector3(side - 0.01f, 0.5f, midZ), new Vector3(0.02f, 1f, depth)),
                (new Vector3(0f, 0.5f, front + 0.01f), new Vector3(2f * side, 1f, 0.02f)),
            })
            {
                NoShadow(Box("Wainscot", shell, s_Mats["Wainscot"], pos, size, false));
                NoShadow(Box("Chair Rail", shell, s_Mats["Trim"], pos + new Vector3(0f, 0.51f, 0f), new Vector3(Mathf.Max(size.x, 0.035f), 0.03f, Mathf.Max(size.z, 0.035f)), false));
            }
            // Tiled splashback behind the buffet.
            var tile = Box("Splashback", shell, s_Mats["Tile"], new Vector3(0f, 1.25f, k_BackWall - 0.005f), new Vector3(4.2f, 0.9f, 0.01f), false);
            NoShadow(tile);
            Window(shell, new Vector3(-side + 0.03f, 1.78f, -0.7f));
            Window(shell, new Vector3(-side + 0.03f, 1.78f, -2.4f));
            Logo(shell, new Vector3(0f, 2.2f, k_BackWall));

            BuildBuffetCounter(env);
            for (int i = 0; i < k_StationX.Length; i++)
                PendantLamp(env, new Vector3(k_StationX[i], 2.12f, k_TableZ + 0.05f), h);
            Plant(env, new Vector3(-2.95f, 0f, 1.05f), 1.1f);
            Plant(env, new Vector3(2.95f, 0f, -3.15f), 1.25f);
            Plant(env, new Vector3(-2.95f, 0f, -3.15f), 0.9f);
        }

        static void Wall(Transform parent, string name, Vector3 pos, Vector3 size) =>
            NoShadow(Box(name, parent, s_Mats["Wall"], pos, size, true));

        static GameObject NoShadow(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static void Window(Transform parent, Vector3 pos)
        {
            var root = new GameObject("Window").transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(0f, 90f, 0f); // faces +X, into the room
            const float w = 1.2f, hgt = 1.3f, f = 0.06f;
            var view = GameObject.CreatePrimitive(PrimitiveType.Quad);
            view.name = "View";
            Object.DestroyImmediate(view.GetComponent<Collider>());
            view.transform.SetParent(root, false);
            view.transform.localScale = new Vector3(w, hgt, 1f);
            view.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            view.GetComponent<Renderer>().sharedMaterial = s_Mats["WindowView"];
            NoShadow(view);
            // Frames sit in front of the view (local +Z is into the room) so no two surfaces share a depth.
            NoShadow(Box("Frame Top", root, s_Mats["Trim"], new Vector3(0f, hgt / 2f, 0.025f), new Vector3(w + f, f, 0.05f), false));
            NoShadow(Box("Frame Bottom", root, s_Mats["Trim"], new Vector3(0f, -hgt / 2f, 0.045f), new Vector3(w + 0.12f, f, 0.09f), false));
            NoShadow(Box("Frame L", root, s_Mats["Trim"], new Vector3(-w / 2f, 0f, 0.025f), new Vector3(f, hgt + 0.01f, 0.05f), false));
            NoShadow(Box("Frame R", root, s_Mats["Trim"], new Vector3(w / 2f, 0f, 0.025f), new Vector3(f, hgt + 0.01f, 0.05f), false));
            NoShadow(Box("Mullion V", root, s_Mats["Trim"], new Vector3(0f, 0f, 0.016f), new Vector3(0.03f, hgt - f, 0.03f), false));
            NoShadow(Box("Mullion H", root, s_Mats["Trim"], new Vector3(0f, 0.1f, 0.012f), new Vector3(w - f, 0.024f, 0.024f), false));
        }

        /// <summary>"VR-T1D" sign on the back wall: neon-style lettering and a blood-drop mark on a dark plate.</summary>
        static void Logo(Transform parent, Vector3 wallPoint)
        {
            var root = new GameObject("VR-T1D Logo").transform;
            root.SetParent(parent, false);
            root.localPosition = wallPoint;
            const float w = 1.7f, h = 0.5f, d = 0.04f;
            NoShadow(Part("Plate", root, SaveMesh("Logo_Plate", MeshFactory.RoundedBox("Logo_Plate", new Vector3(w, d, h), 0.08f)), s_Mats["LogoPlate"]))
                .transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, -d / 2f - 0.002f), Quaternion.Euler(90f, 0f, 0f));
            NoShadow(Part("Edge Light", root, SaveMesh("Logo_Edge", MeshFactory.RoundedBox("Logo_Edge", new Vector3(w - 0.05f, 0.004f, h - 0.05f), 0.06f)), s_Mats["LogoEdge"]))
                .transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, -d - 0.004f), Quaternion.Euler(90f, 0f, 0f));
            NoShadow(Part("Plate Face", root, SaveMesh("Logo_Face", MeshFactory.RoundedBox("Logo_Face", new Vector3(w - 0.07f, 0.004f, h - 0.07f), 0.05f)), s_Mats["LogoPlate"]))
                .transform.SetLocalPositionAndRotation(new Vector3(0f, 0f, -d - 0.009f), Quaternion.Euler(90f, 0f, 0f));

            // Blood drop: circle with a pointed top, extruded toward the room.
            const float r = 0.085f, tip = 1.95f * r;
            float tangent = Mathf.Acos(r / tip) * Mathf.Rad2Deg;
            var outline = new List<Vector2>();
            for (int i = 0; i <= 28; i++)
            {
                float a = Mathf.Lerp(90f + tangent, 450f - tangent, i / 28f) * Mathf.Deg2Rad;
                outline.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            outline.Add(new Vector2(0f, tip));
            NoShadow(Part("Drop", root, SaveMesh("Logo_Drop", FoodModels.Extrude(outline, 0.03f).ToMesh("Logo_Drop")), s_Mats["LogoDrop"]))
                .transform.SetLocalPositionAndRotation(new Vector3(-0.58f, -0.06f, -d - 0.02f), Quaternion.Euler(-90f, 0f, 0f));

            var text = Text3D("Wordmark", root, "<color=#62d6ff>VR</color><color=#ff5a6e>-</color>T1D", 3.4f, Color.white, new Vector2(1.15f, 0.4f));
            text.fontStyle = FontStyles.Bold | FontStyles.Italic;
            text.characterSpacing = 6f;
            text.alignment = TextAlignmentOptions.Center;
            var neon = NeonMaterial();
            if (neon != null) text.fontSharedMaterial = neon;
            text.enableAutoSizing = true;
            text.fontSizeMin = 1.5f;
            text.fontSizeMax = 3.4f;
            text.transform.localPosition = new Vector3(0.14f, 0f, -d - 0.016f);
        }

        /// <summary>Copy of the font material with an outline and glow, for the neon lettering.</summary>
        static Material NeonMaterial()
        {
            if (s_Font == null || s_Font.material == null)
                return null;
            var shader = Shader.Find("TextMeshPro/Distance Field");
            if (shader == null)
                return null;
            string path = $"{k_Root}/Materials/LogoNeon.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(s_Font.material) { name = "LogoNeon" };
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                m.CopyPropertiesFromMaterial(s_Font.material);
            }
            m.shader = shader;
            m.SetTexture("_MainTex", s_Font.material.GetTexture("_MainTex"));
            m.EnableKeyword("GLOW_ON");
            m.SetColor("_GlowColor", new Color(0.35f, 0.8f, 1f, 0.75f));
            m.SetFloat("_GlowOffset", 0f);
            m.SetFloat("_GlowInner", 0.05f);
            m.SetFloat("_GlowOuter", 0.45f);
            m.SetFloat("_GlowPower", 0.75f);
            m.SetFloat("_OutlineWidth", 0.06f);
            m.SetColor("_OutlineColor", new Color(0.2f, 0.65f, 1f, 1f));
            EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildBuffetCounter(Transform env)
        {
            var root = new GameObject("Buffet Counter").transform;
            root.SetParent(env);
            const float len = 3.8f;
            Box("Counter Body", root, s_Mats["Wainscot"], new Vector3(0f, (k_TableTop - 0.03f) / 2f, k_TableZ), new Vector3(len, k_TableTop - 0.03f, k_TableDepth), true);
            Box("Kick Plate", root, s_Mats["Dark"], new Vector3(0f, 0.05f, k_TableZ - k_TableDepth / 2f - 0.002f), new Vector3(len, 0.1f, 0.01f), false);
            Box("Counter Top", root, s_Mats["Steel"], new Vector3(0f, k_TableTop - 0.015f, k_TableZ), new Vector3(len + 0.04f, 0.03f, k_TableDepth + 0.04f), true);
            Box("Tray Rail", root, s_Mats["Steel"], new Vector3(0f, k_TableTop - 0.06f, k_TableZ - k_TableDepth / 2f - 0.1f), new Vector3(len, 0.02f, 0.2f), false);
            for (float x = -len / 2f + 0.1f; x <= len / 2f; x += 0.95f)
                Box("Rail Bracket", root, s_Mats["Steel"], new Vector3(x, k_TableTop - 0.08f, k_TableZ - k_TableDepth / 2f - 0.1f), new Vector3(0.02f, 0.03f, 0.2f), false);

            // Sneeze guard: tilted glass on steel posts, high enough to reach under.
            var glass = Box("Sneeze Guard Glass", root, s_Mats["Glass"], new Vector3(0f, 1.34f, k_TableZ + 0.15f), new Vector3(len, 0.008f, 0.36f), false);
            glass.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            NoShadow(glass);
            for (int i = 0; i <= 4; i++)
            {
                float x = -len / 2f + 0.05f + i * (len - 0.1f) / 4f;
                Part("Post", root, SaveMesh("Guard_Post", MeshFactory.Cylinder("Guard_Post", 0.009f, 0f, 0.5f, 12, MeshFactory.Axis.Y)), s_Mats["Steel"])
                    .transform.localPosition = new Vector3(x, k_TableTop, k_TableZ + 0.29f);
            }
        }

        static void PendantLamp(Transform parent, Vector3 pos, float ceiling)
        {
            var root = new GameObject("Pendant Lamp").transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            NoShadow(Part("Cord", root, SaveMesh("Lamp_Cord", MeshFactory.Cylinder("Lamp_Cord", 0.004f, 0f, 1f, 6, MeshFactory.Axis.Y)), s_Mats["Dark"]))
                .transform.localScale = new Vector3(1f, ceiling - pos.y, 1f);
            NoShadow(Part("Shade", root, SaveMesh("Lamp_Shade", FoodModels.Lathe(new[]
            {
                V(0.03f, 0.1f), V(0.13f, 0f), V(0.128f, -0.004f), V(0.028f, 0.096f), V(0.02f, 0.115f), V(0f, 0.118f),
            }, 40).ToMesh("Lamp_Shade")), s_Mats["LampShade"]));
            NoShadow(Part("Bulb", root, SaveMesh("Lamp_Bulb", FoodModels.Ellipsoid(new Vector3(0.03f, 0.03f, 0.035f), 16, 12).ToMesh("Lamp_Bulb")), s_Mats["Bulb"]))
                .transform.SetLocalPositionAndRotation(new Vector3(0f, 0.035f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 2.6f;
            light.intensity = 0.9f;
            light.color = new Color(1f, 0.86f, 0.68f);
            light.shadows = LightShadows.None;
        }

        static void Plant(Transform parent, Vector3 pos, float height)
        {
            var root = new GameObject("Plant").transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            Part("Pot", root, SaveMesh("Plant_Pot", FoodModels.Lathe(new[]
            {
                V(0f, 0f), V(0.13f, 0f), V(0.17f, 0.38f), V(0.18f, 0.4f), V(0.16f, 0.4f), V(0.15f, 0.36f), V(0f, 0.36f),
            }, 32).ToMesh("Plant_Pot")), s_Mats["Terracotta"]);
            var leaf = SaveMesh("Plant_Leaf", FoodModels.Ellipsoid(new Vector3(0.05f, 0.006f, 0.22f), 10, 8).ToMesh("Plant_Leaf"));
            var rng = new System.Random(Mathf.RoundToInt(pos.x * 100f + pos.z * 7f));
            for (int i = 0; i < 16; i++)
            {
                float yaw = i * 137.5f + (float)rng.NextDouble() * 20f;
                float tilt = 25f + (float)rng.NextDouble() * 45f;
                var l = Part("Leaf", root, leaf, s_Mats["Leaf"]);
                var dir = Quaternion.Euler(-90f + tilt, yaw, 0f);
                l.transform.localRotation = dir;
                l.transform.localPosition = new Vector3(0f, 0.36f + height * 0.25f * (float)rng.NextDouble(), 0f) + dir * new Vector3(0f, 0f, 0.2f);
                l.transform.localScale = Vector3.one * (0.7f + 0.6f * height * (float)rng.NextDouble());
            }
        }

        // ------------------------------------------------------------------ guess stations (served dishes)

        static Transform StationRoot(Transform env, string name, float x)
        {
            var root = new GameObject(name).transform;
            root.SetParent(env);
            root.localPosition = new Vector3(x, k_TableTop, 0f);
            return root;
        }

        /// <summary>
        /// Where the tablet parks for this station: over the front of the counter, in front of the sneeze guard, high enough
        /// that the food below stays in view (the height follows the learner's eyes at run time).
        /// </summary>
        static Transform TabletDock(Transform root)
        {
            var dock = new GameObject("Tablet Dock").transform;
            dock.SetParent(root, false);
            dock.localPosition = new Vector3(0f, 1.5f - k_TableTop, 0.31f);
            return dock;
        }

        static GuessStation GuessStationRice(Transform env, float x)
        {
            var root = StationRoot(env, "Rice Station (guess)", x);
            var dish = new GameObject("Bowl of rice").transform;
            dish.SetParent(root, false);
            dish.localPosition = new Vector3(0f, 0f, 0.42f);
            Part("Bowl", dish, SaveMesh("Rice_Bowl", FoodModels.RiceBowl().ToMesh("Rice_Bowl")), s_Mats["Ceramic"]);
            Part("Band", dish, SaveMesh("Rice_Bowl_Band", FoodModels.RiceBowlBand().ToMesh("Rice_Bowl_Band")), s_Mats["BowlBand"]);
            var pile = FoodModels.BuildPile("Rice_Bowl_Serving", new FoodModels.PileSpec
            {
                food = FoodModels.PileFood.Rice, halfSize = 0.07f, cell = 0.0015f, floor = FoodModels.RiceBowlFloor,
                targetMl = 1.5f * FoodData.CupMl, spread = 0.03f, containerRadius = FoodModels.RiceBowlInner, repose = 42f, seed = 17,
            });
            AddPile(dish, pile, s_Mats["RiceGrain"], s_Mats["RiceCore"]);
            return FinishGuessStation(root, dish, "rice", "Steamed rice", FoodModels.PileFood.Rice, pile.volumeMl, FoodData.RiceCarbsPerCup,
                new Vector3(0f, 0.04f, 0f), new Vector3(0.138f, 0.08f, 0.138f), 0.45f);
        }

        static GuessStation GuessStationPasta(Transform env, float x)
        {
            var root = StationRoot(env, "Pasta Station (guess)", x);
            var dish = new GameObject("Bowl of penne").transform;
            dish.SetParent(root, false);
            dish.localPosition = new Vector3(0f, 0f, 0.45f);
            Part("Bowl", dish, SaveMesh("Pasta_Bowl", FoodModels.PastaBowl().ToMesh("Pasta_Bowl")), s_Mats["Ceramic"]);
            var pile = FoodModels.BuildPile("Pasta_Bowl_Serving", new FoodModels.PileSpec
            {
                food = FoodModels.PileFood.Penne, halfSize = 0.11f, cell = 0.002f, floor = FoodModels.PastaBowlFloor,
                targetMl = 2.5f * FoodData.CupMl, spread = 0.05f, containerRadius = FoodModels.PastaBowlInner, repose = 28f, seed = 5,
            });
            AddPile(dish, pile, s_Mats["Penne"], null);
            return FinishGuessStation(root, dish, "penne", "Penne", FoodModels.PileFood.Penne, pile.volumeMl, FoodData.PastaCarbsPerCup,
                new Vector3(0f, 0.035f, 0f), new Vector3(0.262f, 0.07f, 0.262f), 0.7f);
        }

        /// <summary>
        /// Shared part of a guess station: the served amount to the nearest quarter cup (so "per cup x cups" adds up
        /// exactly), a grabbable bowl, the measuring-cup answer, a tablet dock and a RESTART button.
        /// </summary>
        static GuessStation FinishGuessStation(Transform root, Transform dish, string food, string cardName, FoodModels.PileFood pileFood,
            float volumeMl, float carbsPerCup, Vector3 colliderCentre, Vector3 colliderSize, float mass)
        {
            float cups = Mathf.Round(volumeMl / FoodData.CupMl * 4f) / 4f;
            var col = dish.gameObject.AddComponent<BoxCollider>();
            col.center = colliderCentre;
            col.size = colliderSize;
            Grab(dish.gameObject, col, mass).useDynamicAttach = true; // held where it is grabbed
            dish.gameObject.AddComponent<ReturnHome>();

            var g = new GuessStation
            {
                foodName = food,
                carbsPerCup = carbsPerCup,
                actualCarbs = Mathf.Floor(cups * carbsPerCup + 0.5f),
                amountText = FoodData.Cups(cups),
                root = root,
                focus = dish,
                dock = TabletDock(root),
            };
            ChafingDish(root, cardName);
            g.reveal = CupsReveal(root, pileFood, cups, carbsPerCup, g.amountText, g.actualCarbs);
            g.restartButton = PokeButton("Restart Button", root, new Vector3(0.24f, 0f, 0.27f), "RESTART", s_Mats["Blue"], out _);
            return g;
        }

        static void AddPile(Transform parent, FoodModels.PileResult pile, Material pieces, Material core)
        {
            var p = Part("Pieces", parent, SaveMesh(pile.pieces.name, pile.pieces), pieces);
            p.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (pile.core != null && core != null)
                Part("Core", parent, SaveMesh(pile.core.name, pile.core), core).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Closed roll-top chafing dish behind the glass, as on a real buffet line, with a name card.</summary>
        static void ChafingDish(Transform root, string name)
        {
            var dish = new GameObject("Chafing Dish").transform;
            dish.SetParent(root, false);
            dish.localPosition = new Vector3(0f, 0f, 0.71f);
            Box("Stand", dish, s_Mats["Steel"], new Vector3(0f, 0.05f, 0f), new Vector3(0.52f, 0.1f, 0.24f), false);
            var lid = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lid.name = "Roll-top Lid";
            Object.DestroyImmediate(lid.GetComponent<Collider>());
            lid.transform.SetParent(dish, false);
            lid.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            lid.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            lid.transform.localScale = new Vector3(0.23f, 0.25f, 0.23f);
            lid.GetComponent<Renderer>().sharedMaterial = s_Mats["Steel"];
            Part("Handle", dish, SaveMesh("Lid_Handle", MeshFactory.Cylinder("Lid_Handle", 0.008f, -0.05f, 0.05f, 10, MeshFactory.Axis.Z)), s_Mats["Dark"])
                .transform.SetLocalPositionAndRotation(new Vector3(0f, 0.225f, 0f), Quaternion.Euler(0f, 90f, 0f));
            NameCard(root, name, new Vector3(0.2f, 0f, 0.53f));
        }

        /// <summary>Small folded card standing at <paramref name="pos"/>, leaning back, with a name and an optional second line.</summary>
        static void NameCard(Transform root, string name, Vector3 pos, float width = 0.12f, float height = 0.06f, string detail = null)
        {
            var card = new GameObject($"Name Card ({name})").transform;
            card.SetParent(root, false);
            card.localPosition = pos;
            var tilt = Quaternion.Euler(-12f, 0f, 0f);
            var body = Box("Card", card, s_Mats["White"], tilt * new Vector3(0f, height / 2f, 0f), new Vector3(width, height, 0.003f), false);
            body.transform.localRotation = tilt;
            NoShadow(body);
            string text = detail == null ? $"<b>{name}</b>" : $"<b>{name}</b>\n<size=72%><color=#5a6270>{detail}</color></size>";
            var t = Text3D("Text", card, text, 0.14f, new Color(0.15f, 0.15f, 0.18f), new Vector2(width - 0.01f, height - 0.012f));
            t.transform.localPosition = tilt * new Vector3(0f, height / 2f, -0.0025f);
            t.transform.localRotation = tilt;
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.04f;
            t.fontSizeMax = 0.12f;
            t.lineSpacing = -10f;
        }

        /// <summary>Answer for a served dish: the same amount in measuring cups (full cups plus a part cup).</summary>
        static GameObject CupsReveal(Transform root, FoodModels.PileFood food, float cups, float carbsPerCup, string amountText, float totalCarbs)
        {
            var reveal = new GameObject("Answer: measuring cups").transform;
            reveal.SetParent(root, false);
            reveal.localPosition = new Vector3(-0.31f, 0f, 0.42f);
            var sizes = new List<float>();
            float left = cups;
            while (left >= 0.875f) { sizes.Add(1f); left -= 1f; }
            if (left >= 0.375f) sizes.Add(0.5f);
            else if (left >= 0.125f) sizes.Add(0.25f);

            float z = (sizes.Count - 1) * 0.06f;
            int index = 0;
            foreach (float size in sizes)
            {
                float ml = size * FoodData.CupMl;
                string key = $"{(food == FoodModels.PileFood.Rice ? "Rice" : "Penne")}_Cup_{size * 100f:0}";
                var cup = new GameObject($"{size} cup").transform;
                cup.SetParent(reveal, false);
                cup.localPosition = new Vector3(0f, 0f, z);
                cup.localRotation = Quaternion.Euler(0f, 180f, 0f); // handle points away from the dish
                Part("Cup", cup, SaveMesh($"Measuring_Cup_{size * 100f:0}", FoodModels.MeasuringCup(ml, out float r, out float depth).ToMesh($"Measuring_Cup_{size * 100f:0}")), s_Mats["Steel"]);
                var pile = FoodModels.BuildPile(key, new FoodModels.PileSpec
                {
                    food = food, halfSize = r + 0.004f, cell = food == FoodModels.PileFood.Rice ? 0.0015f : 0.002f,
                    floor = (px, pz) => px * px + pz * pz > r * r ? float.PositiveInfinity : 0.0015f,
                    targetMl = ml, spread = 0f, containerRadius = r, repose = 25f, seed = 3 + (int)(size * 8f),
                    maxTop = 0.0015f + depth + (food == FoodModels.PileFood.Rice ? 0.003f : 0.014f),
                });
                AddPile(cup, pile, food == FoodModels.PileFood.Rice ? s_Mats["RiceGrain"] : s_Mats["Penne"], s_Mats["RiceCore"]);

                // Each cup can be picked up, and says what it holds; heights are staggered so the labels don't overlap.
                var col = cup.gameObject.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, (depth + 0.003f) / 2f, 0f);
                col.size = new Vector3(2f * r + 0.006f, depth + 0.003f, 2f * r + 0.006f);
                Grab(cup.gameObject, col, 0.15f).useDynamicAttach = true;
                cup.gameObject.AddComponent<ReturnHome>();
                string name = size >= 1f ? "1 cup" : size >= 0.5f ? "1/2 cup" : "1/4 cup";
                Label(cup, $"{name}: {Mathf.Floor(size * carbsPerCup + 0.5f):0} g", new Vector3(0f, 0.07f + 0.03f * index, 0f), 0.13f, Color.white, true);
                index++;
                z -= 0.12f;
            }
            Label(reveal, $"{amountText} = {totalCarbs:0} g", new Vector3(0f, 0.08f + 0.03f * sizes.Count, 0f), 0.2f, new Color(1f, 0.84f, 0.25f), true);
            return reveal.gameObject;
        }

        // ------------------------------------------------------------------ build stations (pieces)

        /// <summary>Plate, tablet dock and a blue RESTART button (DONE is on the tablet, or the A button).</summary>
        static BuildStation BuildStationBase(Transform root, string food)
        {
            var plate = BuildPlate(root, new Vector3(0f, 0f, 0.4f));
            var b = new BuildStation { foodName = food, root = root, plate = plate, dock = TabletDock(root) };
            b.restartButton = PokeButton("Restart Button", root, new Vector3(-0.24f, 0f, 0.27f), "RESTART", s_Mats["Blue"], out _);
            return b;
        }

        static BuildStation BreadStation(Transform env, float x)
        {
            var root = StationRoot(env, "Bread Station (build)", x);
            Box("Bread Board", root, s_Mats["Wood"], new Vector3(0f, 0.01f, 0.68f), new Vector3(0.56f, 0.02f, 0.3f), true);
            const float cardZ = 0.565f, cardY = 0.02f;
            NameCard(root, "Sandwich bread", new Vector3(-0.17f, cardY, cardZ), 0.12f, 0.05f, "thin slice");
            NameCard(root, "Thick-cut bread", new Vector3(-0.02f, cardY, cardZ), 0.12f, 0.05f, "thick slice");
            NameCard(root, "Bagel", new Vector3(0.16f, cardY, cardZ), 0.1f, 0.05f, "whole");

            // Same food, different sizes. Stacked so the learner sees thickness.
            for (int i = 0; i < 4; i++)
                Slice(root, "Sandwich slice", new Vector3(-0.17f, 0.02f + 0.0125f * i + 0.006f, 0.68f), 0.11f, 0.115f, 0.012f, FoodData.SandwichSliceCarbs);
            for (int i = 0; i < 3; i++)
                Slice(root, "Thick slice", new Vector3(-0.02f, 0.02f + 0.025f * i + 0.012f, 0.68f), 0.12f, 0.125f, 0.024f, FoodData.ThickSliceCarbs);
            for (int i = 0; i < 2; i++)
                Bagel(root, new Vector3(0.16f, 0.02f + 0.033f * i, 0.68f));

            return BuildStationBase(root, "bread");
        }

        static BuildStation FruitStation(Transform env, float x)
        {
            var root = StationRoot(env, "Fruit Station (build)", x);
            Box("Fruit Board", root, s_Mats["Wood"], new Vector3(0f, 0.01f, 0.68f), new Vector3(0.62f, 0.02f, 0.3f), true);
            const float top = 0.02f, cardZ = 0.55f;
            NameCard(root, "Banana", new Vector3(-0.2f, top, cardZ), 0.1f, 0.045f, "medium");
            NameCard(root, "Apple", new Vector3(-0.03f, top, cardZ), 0.09f, 0.045f, "medium");
            NameCard(root, "Orange", new Vector3(0.08f, top, cardZ), 0.09f, 0.045f, "medium");
            NameCard(root, "Watermelon", new Vector3(0.23f, top, cardZ), 0.11f, 0.045f, "1 wedge");
            Banana(root, new Vector3(-0.2f, top, 0.61f), 10f);
            Banana(root, new Vector3(-0.2f, top, 0.72f), -6f);
            Apple(root, new Vector3(-0.03f, top, 0.62f), 0f);
            Apple(root, new Vector3(-0.03f, top, 0.75f), 140f);
            Orange(root, new Vector3(0.08f, top, 0.62f));
            Orange(root, new Vector3(0.08f, top, 0.75f));
            for (int i = 0; i < 2; i++)
            {
                Watermelon(root, new Vector3(0.23f, top + 0.0125f + 0.0255f * i, 0.635f), 180f);
                Watermelon(root, new Vector3(0.23f, top + 0.0125f + 0.0255f * i, 0.77f), 0f);
            }
            return BuildStationBase(root, "fruit");
        }

        static Plate BuildPlate(Transform parent, Vector3 localPosition)
        {
            var root = new GameObject("Plate");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            // Standard 10.5 inch (26.7 cm) dinner plate.
            Part("Plate Mesh", root.transform, SaveMesh("Dinner_Plate", FoodModels.Lathe(new[]
            {
                V(0f, 0.001f), V(0.09f, 0.001f), V(0.095f, 0f), V(0.1335f, 0.017f), V(0.1335f, 0.02f), V(0.126f, 0.02f), V(0.095f, 0.007f), V(0f, 0.007f),
            }, 56).ToMesh("Dinner_Plate")), s_Mats["Ceramic"]);
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.0035f, 0f);
            col.size = new Vector3(0.2f, 0.007f, 0.2f);
            var plate = root.AddComponent<Plate>();
            plate.radius = 0.125f;
            return plate;
        }

        static GameObject FoodItem(Transform parent, string displayName, Vector3 localPosition, float yaw, float carbs, Vector3 colliderCentre, Vector3 colliderSize, float mass)
        {
            var root = new GameObject(displayName);
            root.transform.SetParent(parent, false);
            root.transform.SetLocalPositionAndRotation(localPosition, Quaternion.Euler(0f, yaw, 0f));
            var col = root.AddComponent<BoxCollider>();
            col.center = colliderCentre;
            col.size = colliderSize;
            Grab(root, col, mass);
            var item = root.AddComponent<CarbItem>();
            item.displayName = displayName;
            item.carbs = carbs;
            root.AddComponent<ReturnHome>().restock = true; // an endless supply: taking one leaves another
            return root;
        }

        static void Slice(Transform parent, string displayName, Vector3 localPosition, float width, float height, float thickness, float carbs)
        {
            var root = FoodItem(parent, displayName, localPosition, 0f, carbs, Vector3.zero, new Vector3(width, thickness, height), 0.03f);
            string key = $"Bread_{width * 1000f:0}x{thickness * 1000f:0}";
            Part("Crust", root.transform, SaveMesh(key + "_Crust", FoodModels.Extrude(FoodModels.SliceOutline(width, height, 0f), thickness).ToMesh(key + "_Crust")), s_Mats["Crust"]);
            Part("Crumb", root.transform, SaveMesh(key + "_Crumb", FoodModels.Extrude(FoodModels.SliceOutline(width, height, 0.0045f), thickness + 0.0008f, false, true, 6f).ToMesh(key + "_Crumb")), s_Mats["Crumb"]);
        }

        static void Bagel(Transform parent, Vector3 localPosition)
        {
            var root = FoodItem(parent, "Bagel", localPosition, 0f, FoodData.BagelCarbs, new Vector3(0f, 0.016f, 0f), new Vector3(0.1f, 0.032f, 0.1f), 0.1f);
            var profile = new List<Vector2>();
            for (int i = 0; i <= 32; i++)
            {
                float a = (-90f + 360f * i / 32f) * Mathf.Deg2Rad;
                float squash = Mathf.Sin(a) > 0f ? 1f : 0.8f; // flatter underneath
                profile.Add(V(0.032f + 0.019f * Mathf.Cos(a), 0.0145f + 0.0165f * Mathf.Sin(a) * squash + 0.0013f));
            }
            Part("Bagel Mesh", root.transform, SaveMesh("Bagel", FoodModels.Lathe(profile, 40).ToMesh("Bagel")), s_Mats["Bagel"]);
        }

        static void Banana(Transform parent, Vector3 localPosition, float yaw)
        {
            var mesh = FoodModels.Banana(out var stemPoint, out var stemDir, out var tip);
            var root = FoodItem(parent, "Banana", localPosition + new Vector3(0f, 0.017f, 0f), yaw, FoodData.BananaCarbs,
                new Vector3(0f, 0f, 0.015f), new Vector3(0.18f, 0.034f, 0.05f), 0.12f);
            Part("Banana Mesh", root.transform, SaveMesh("Banana", mesh.ToMesh("Banana")), s_Mats["Banana"]);
            var stem = Part("Stem", root.transform, SaveMesh("Banana_Stem", FoodModels.Stem(0.022f, 0.0045f).ToMesh("Banana_Stem")), s_Mats["Stem"]);
            stem.transform.SetLocalPositionAndRotation(stemPoint, Quaternion.FromToRotation(Vector3.up, stemDir));
            var nub = Part("Tip", root.transform, SaveMesh("Banana_Tip", FoodModels.Ellipsoid(new Vector3(0.003f, 0.003f, 0.003f), 8, 6).ToMesh("Banana_Tip")), s_Mats["Stem"]);
            nub.transform.localPosition = tip;
        }

        static void Apple(Transform parent, Vector3 localPosition, float yaw)
        {
            var root = FoodItem(parent, "Apple", localPosition, yaw, FoodData.AppleCarbs, new Vector3(0f, 0.033f, 0f), new Vector3(0.075f, 0.066f, 0.075f), 0.18f);
            Part("Apple Mesh", root.transform, SaveMesh("Apple", FoodModels.Apple().ToMesh("Apple")), s_Mats["Apple"]);
            var stem = Part("Stem", root.transform, SaveMesh("Apple_Stem", FoodModels.Stem(0.016f, 0.0018f).ToMesh("Apple_Stem")), s_Mats["Stem"]);
            stem.transform.SetLocalPositionAndRotation(new Vector3(0f, 0.055f, 0f), Quaternion.Euler(0f, 0f, 10f));
        }

        static void Orange(Transform parent, Vector3 localPosition)
        {
            var root = FoodItem(parent, "Orange", localPosition, 0f, FoodData.OrangeCarbs, new Vector3(0f, 0.033f, 0f), new Vector3(0.068f, 0.066f, 0.068f), 0.13f);
            Part("Orange Mesh", root.transform, SaveMesh("Orange", FoodModels.Orange().ToMesh("Orange")), s_Mats["Orange"]);
            var navel = Part("Navel", root.transform, SaveMesh("Orange_Navel", FoodModels.Ellipsoid(new Vector3(0.003f, 0.0012f, 0.003f), 8, 6).ToMesh("Orange_Navel")), s_Mats["Stem"]);
            navel.transform.localPosition = new Vector3(0f, 0.0655f, 0f);
        }

        /// <summary>Watermelon wedge lying flat (2.5 cm thick); returns its carbs, from the flesh volume.</summary>
        static float Watermelon(Transform parent, Vector3 localPosition, float yaw)
        {
            const float rFlesh = 0.12f, rWhite = 0.127f, rRind = 0.134f, angle = 55f, t = 0.025f;
            float fleshMl = angle * Mathf.Deg2Rad / 2f * rFlesh * rFlesh * t * 1e6f;
            float carbs = Mathf.Round(fleshMl * FoodData.WatermelonCarbsPerMl);
            // Pivot near the middle of the wedge.
            var centre = new Vector3(0f, 0f, 0.075f);
            var root = FoodItem(parent, "Watermelon wedge", localPosition, yaw, carbs, Vector3.zero, new Vector3(0.12f, t, 0.13f), 0.18f);
            var mesh = new GameObject("Wedge").transform;
            mesh.SetParent(root.transform, false);
            mesh.localPosition = -centre;
            Part("Flesh", mesh, SaveMesh("Watermelon_Flesh", FoodModels.Sector(0f, rFlesh, angle, t, 16).ToMesh("Watermelon_Flesh")), s_Mats["Watermelon"]);
            Part("White Rind", mesh, SaveMesh("Watermelon_White", FoodModels.Sector(rFlesh, rWhite, angle, t, 16).ToMesh("Watermelon_White")), s_Mats["RindWhite"]);
            Part("Green Rind", mesh, SaveMesh("Watermelon_Rind", FoodModels.Sector(rWhite, rRind, angle, t * 0.96f, 16).ToMesh("Watermelon_Rind")), s_Mats["RindGreen"]);
            var seed = SaveMesh("Watermelon_Seed", FoodModels.Ellipsoid(new Vector3(0.0025f, 0.0009f, 0.0042f), 8, 6).ToMesh("Watermelon_Seed"));
            var rng = new System.Random(8);
            foreach (float y in new[] { t / 2f, -t / 2f })
                for (int i = 0; i < 9; i++)
                {
                    float a = (90f + ((float)rng.NextDouble() - 0.5f) * angle * 0.75f) * Mathf.Deg2Rad;
                    float r = 0.055f + (float)rng.NextDouble() * 0.04f;
                    var s = Part("Seed", mesh, seed, s_Mats["Seed"]);
                    s.transform.SetLocalPositionAndRotation(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r),
                        Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), Vector3.up));
                    s.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            return carbs;
        }

        // ------------------------------------------------------------------ dining area and decor

        static void BuildDiningArea(Transform env)
        {
            var root = new GameObject("Dining Area").transform;
            root.SetParent(env);
            foreach (var pos in new[] { new Vector3(-1.7f, 0f, -2.5f), new Vector3(1.5f, 0f, -2.6f) })
            {
                var table = new GameObject("Table").transform;
                table.SetParent(root, false);
                table.localPosition = pos;
                Part("Top", table, SaveMesh("Table_Top", FoodModels.Lathe(new[]
                {
                    V(0f, 0.72f), V(0.45f, 0.72f), V(0.46f, 0.735f), V(0.45f, 0.75f), V(0f, 0.75f),
                }, 48).ToMesh("Table_Top")), s_Mats["Wood"]);
                Part("Pedestal", table, SaveMesh("Table_Pedestal", FoodModels.Lathe(new[]
                {
                    V(0f, 0f), V(0.24f, 0f), V(0.24f, 0.025f), V(0.05f, 0.05f), V(0.04f, 0.72f), V(0f, 0.72f),
                }, 32).ToMesh("Table_Pedestal")), s_Mats["Dark"]);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 90f + 45f;
                    var dir = Quaternion.Euler(0f, a, 0f);
                    Chair(table, dir * new Vector3(0f, 0f, 0.62f), a + 180f);
                    var plate = Part("Place Plate", table, SaveMesh("Dinner_Plate", FoodModels.Lathe(new[]
                    {
                        V(0f, 0.001f), V(0.09f, 0.001f), V(0.095f, 0f), V(0.1335f, 0.017f), V(0.1335f, 0.02f), V(0.126f, 0.02f), V(0.095f, 0.007f), V(0f, 0.007f),
                    }, 56).ToMesh("Dinner_Plate")), s_Mats["Ceramic"]);
                    plate.transform.localPosition = dir * new Vector3(0f, 0f, 0.3f) + new Vector3(0f, 0.75f, 0f);
                    var glass = Part("Glass", table, SaveMesh("Water_Glass", FoodModels.Lathe(new[]
                    {
                        V(0f, 0f), V(0.032f, 0f), V(0.037f, 0.12f), V(0.034f, 0.12f), V(0.03f, 0.006f), V(0f, 0.006f),
                    }, 24).ToMesh("Water_Glass")), s_Mats["Glass"]);
                    glass.transform.localPosition = dir * new Vector3(0.17f, 0f, 0.25f) + new Vector3(0f, 0.75f, 0f);
                    NoShadow(glass);
                }
            }
        }

        static void Chair(Transform parent, Vector3 pos, float yaw)
        {
            var root = new GameObject("Chair").transform;
            root.SetParent(parent, false);
            root.SetLocalPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            Box("Seat", root, s_Mats["Wood"], new Vector3(0f, 0.45f, 0f), new Vector3(0.42f, 0.03f, 0.42f), false);
            Box("Back", root, s_Mats["Wood"], new Vector3(0f, 0.72f, -0.2f), new Vector3(0.42f, 0.32f, 0.025f), false);
            foreach (var (x, z) in new[] { (-0.19f, -0.19f), (0.19f, -0.19f), (-0.19f, 0.19f), (0.19f, 0.19f) })
                Box("Leg", root, s_Mats["Dark"], new Vector3(x, 0.22f, z), new Vector3(0.025f, 0.44f, 0.025f), false);
        }

        /// <summary>Drinks and desserts along the right wall, from the low-poly food pack, scaled to real-world sizes.</summary>
        static void BuildDecorTable(Transform env)
        {
            var root = new GameObject("Drinks and Desserts").transform;
            root.SetParent(env);
            const float x = 3.0f, top = 0.9f;
            Box("Side Counter", root, s_Mats["Wainscot"], new Vector3(x, (top - 0.03f) / 2f, -0.9f), new Vector3(0.7f, top - 0.03f, 2.0f), true);
            Box("Side Counter Top", root, s_Mats["Steel"], new Vector3(x, top - 0.015f, -0.9f), new Vector3(0.74f, 0.03f, 2.04f), true);

            // name, largest real-world dimension (m)
            var items = new (string prefab, float size)[]
            {
                ("Cookies", 0.15f), ("Doughnut D", 0.09f), ("Doughnut H", 0.09f), ("Cheesecake B", 0.12f),
                ("Soda can C", 0.122f), ("Soda bottle Glass A", 0.25f), ("Coffee Cup C", 0.1f), ("Pretzel", 0.12f),
            };
            float z = -1.75f;
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
                go.transform.position += new Vector3(x, top, z) - new Vector3(b.center.x, b.min.y, b.center.z);
                z += Mathf.Max(0.18f, size + 0.06f);
            }
        }

        static void Label(Transform parent, string text, Vector3 localPosition, float fontSize, Color? color = null, bool billboard = false)
        {
            var anchor = new GameObject("Label").transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = localPosition;
            Text3D("Text", anchor, $"<b>{text}</b>", fontSize, color ?? Color.white, new Vector2(0.5f, 0.06f));
            if (billboard)
                anchor.gameObject.AddComponent<BillboardLabel>().height = localPosition.y;
            else
                anchor.rotation = Quaternion.identity; // faces -Z, toward the learner at the counter
        }
    }
}
