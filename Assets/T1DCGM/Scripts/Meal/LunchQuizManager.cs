using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace T1DCGM
{
    /// <summary>A dish already served at real size; the learner dials in a carb estimate on the tablet.</summary>
    [System.Serializable]
    public class GuessStation
    {
        public string foodName = "rice";
        public float actualCarbs;
        [Tooltip("Carbs in one cup of this food.")]
        public float carbsPerCup;
        [Tooltip("The serving in everyday units, e.g. \"1 1/2 cups\".")]
        public string amountText;
        [Tooltip("Station root: grabbable pieces under it go back to their spots on RESTART.")]
        public Transform root;
        public Transform focus;
        [Tooltip("Where the tablet parks during this round (above the dish).")]
        public Transform dock;
        [Tooltip("Shown with the answer: measuring cups holding the same serving.")]
        public GameObject reveal;
        public PressButton3D restartButton;
    }

    /// <summary>The learner puts pieces on a plate until they think it holds a random carb target.</summary>
    [System.Serializable]
    public class BuildStation
    {
        public string foodName = "bread";
        [Tooltip("Station root: the food pieces (CarbItem) under it are reset by RESTART and used to explain the target.")]
        public Transform root;
        public Plate plate;
        [Tooltip("Where the tablet parks during this round (above the plate).")]
        public Transform dock;
        public PressButton3D restartButton;
    }

    /// <summary>
    /// Lunch challenge. Part 1: estimate the carbs in dishes already served (rice, pasta), the way most meals are
    /// counted. Part 2: build a plate to a random carb target from pieces (bread, fruit). Each answer shows bars for what
    /// was counted and eaten, which way glucose goes, and the CGM trace. No doses are shown: the point is the direction.
    /// </summary>
    public class LunchQuizManager : MonoBehaviour
    {
        public InfoTablet tablet;
        public GuideArrow arrow;
        public List<GuessStation> guessStations = new List<GuessStation>();
        public List<BuildStation> buildStations = new List<BuildStation>();
        public AudioSource audioSource;
        public TMP_FontAsset labelFont;
        public float guessStart = 0f;
        [Tooltip("Build targets are drawn at random from this range, in steps of 5 g.")]
        public int minTarget = 25, maxTarget = 100;

        enum Phase { Intro = 0, Guess = 1, GuessResult = 2, Build = 3, BuildResult = 4, Summary = 5 }

        struct Result { public bool done; public string food; public float counted, ate; }

        Phase m_Phase;
        int m_Page, m_Round;
        float m_Guess;
        float[] m_Targets;
        Result[] m_Results;
        readonly List<GameObject> m_Labels = new List<GameObject>();
        InputAction m_Next, m_Back;

        static readonly Color k_Yellow = new Color(1f, 0.84f, 0.25f);

        static readonly (string title, string body, string visual)[] k_Intro =
        {
            ("Lunch with type 1 diabetes",
             "Before eating, you count the carbs and take insulin to match.\n\nNo labels here. Judge by eye.",
             "lunch"),
            ("Why the count matters",
             "<b>Too low:</b> glucose goes high.\n<b>Too high:</b> it can drop low.",
             "count"),
            ("Part 1: Guess",
             "Look at each dish, then dial in your carb guess.",
             "guess-intro"),
            ("Part 2: Build",
             "Fill a plate until it holds the carb target.\n\nTake a piece and another appears.",
             "build-intro"),
            ("Controls", "", "controls"),
        };

        int Rounds => guessStations.Count + buildStations.Count;

        void Awake()
        {
            m_Next = XRInput.Button("Next", "<XRController>{RightHand}/primaryButton", "<Keyboard>/space");
            m_Back = XRInput.Button("Back", "<XRController>{RightHand}/secondaryButton", "<Keyboard>/backspace");
            for (int i = 0; i < guessStations.Count; i++)
            {
                int round = i;
                var g = guessStations[i];
                if (g.restartButton != null) g.restartButton.onPressed.AddListener(() => Restart(round));
            }
            for (int i = 0; i < buildStations.Count; i++)
            {
                int round = guessStations.Count + i;
                var b = buildStations[i];
                if (b.restartButton != null) b.restartButton.onPressed.AddListener(() => Restart(round));
            }
            if (tablet != null)
            {
                tablet.onNext = Next;
                tablet.onBack = Back;
                tablet.onAdjust = Adjust;
            }
        }

        void OnEnable()
        {
            m_Next.Enable(); m_Next.performed += OnNextInput;
            m_Back.Enable(); m_Back.performed += OnBackInput;
        }

        void OnDisable()
        {
            m_Next.performed -= OnNextInput; m_Next.Disable();
            m_Back.performed -= OnBackInput; m_Back.Disable();
        }

        void OnDestroy()
        {
            m_Next?.Dispose();
            m_Back?.Dispose();
        }

        void Start()
        {
            foreach (var g in guessStations)
                if (g.reveal != null) g.reveal.SetActive(false);
            m_Results = new Result[Rounds];
            m_Targets = new float[buildStations.Count];
            for (int i = 0; i < buildStations.Count; i++)
                m_Targets[i] = 5f * Random.Range(minTarget / 5, maxTarget / 5 + 1);
            ShowPage(0);
        }

        void OnNextInput(InputAction.CallbackContext _) => Next();
        void OnBackInput(InputAction.CallbackContext _) => Back();

        // ------------------------------------------------------------------ flow

        /// <summary>A button, or the tablet's right-hand button: whatever moves the lesson forward on this page.</summary>
        void Next()
        {
            switch (m_Phase)
            {
                case Phase.Intro:
                    if (m_Page + 1 < k_Intro.Length) ShowPage(m_Page + 1);
                    else StartRound(0);
                    break;
                case Phase.Guess:
                    LockGuess();
                    break;
                case Phase.Build:
                    Done();
                    break;
                case Phase.GuessResult:
                case Phase.BuildResult:
                    // Next round not played yet (after a RESTART, rounds already done are skipped).
                    int next = m_Round + 1;
                    while (next < Rounds && m_Results[next].done) next++;
                    if (next < Rounds) StartRound(next);
                    else ShowSummary();
                    break;
                case Phase.Summary:
                    PlayAgain();
                    break;
            }
        }

        /// <summary>B button, or the tablet's left-hand button: previous page, or replay the CGM trace on a result.</summary>
        void Back()
        {
            if (m_Phase == Phase.Intro && m_Page > 0)
                ShowPage(m_Page - 1);
            else if (m_Phase == Phase.GuessResult || m_Phase == Phase.BuildResult)
                tablet.Replay();
        }

        /// <summary>Starts the whole lunch again, with new build targets.</summary>
        public void PlayAgain()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene.buildIndex >= 0 ? scene.buildIndex : 0);
        }

        void ShowPage(int page)
        {
            m_Phase = Phase.Intro;
            m_Page = page;
            var (title, body, visual) = k_Intro[page];
            if (page == 0 && CgmSession.applied)
                body = "Your CGM is on.\n\n" + body;
            bool last = page == k_Intro.Length - 1;
            tablet.Show(title, body, Dots(page, k_Intro.Length), last ? "START" : "NEXT", page > 0 ? "BACK" : null, visual);
            tablet.Dock(null);
            arrow.SetTarget(null);
        }

        void StartRound(int round)
        {
            ClearLabels();
            m_Round = round;
            if (round < guessStations.Count)
            {
                m_Phase = Phase.Guess;
                var g = guessStations[round];
                if (g.reveal != null) g.reveal.SetActive(false);
                m_Guess = guessStart;
                tablet.Show($"Guess {round + 1} of {guessStations.Count}: {Capital(g.foodName)}",
                    "How many grams of carbs?",
                    RoundCounter(round), "LOCK IN", null, "guess");
                tablet.SetValue(GuessText());
                tablet.Dock(g.dock);
                arrow.SetTarget(g.focus);
            }
            else
            {
                m_Phase = Phase.Build;
                int i = round - guessStations.Count;
                var b = buildStations[i];
                tablet.Show($"Build a plate: {Capital(b.foodName)}",
                    $"Fill the plate to <b>{m_Targets[i]:0} g</b> of carbs, then poke <b>DONE</b>.",
                    RoundCounter(round), "DONE", null, "target");
                tablet.SetValue($"{m_Targets[i]:0} g");
                tablet.Dock(b.dock);
                arrow.SetTarget(b.plate.transform);
            }
            Play(ToneLibrary.Click());
        }

        void Adjust(float delta)
        {
            if (m_Phase != Phase.Guess) return;
            m_Guess = Mathf.Clamp(m_Guess + delta, 0f, 300f);
            tablet.SetValue(GuessText());
        }

        string GuessText() => $"<b>{m_Guess:0}</b><size=55%> g</size>";

        void LockGuess()
        {
            if (m_Phase != Phase.Guess) return;
            var g = guessStations[m_Round];
            m_Results[m_Round] = new Result { done = true, food = g.foodName, counted = m_Guess, ate = g.actualCarbs };
            if (g.reveal != null) g.reveal.SetActive(true);
            m_Phase = Phase.GuessResult;

            Label(g.focus, $"{g.actualCarbs:0} g", 0.16f);

            // The per-cup rule first, so it can be reused next time: "33 g per cup x 2 1/2 cups = 83 g".
            float miss = m_Guess - g.actualCarbs;
            string cups = g.amountText.Replace(" cups", "").Replace(" cup", "");
            string body = $"<b>{g.carbsPerCup:0} g per cup</b> × {cups} = <b>{g.actualCarbs:0} g</b>\n{Effect(m_Guess, g.actualCarbs)}";
            ShowResult(Header(miss), body, CgmGraph.Simulate(g.actualCarbs, m_Guess), miss,
                "Your guess", m_Guess, "Actual", g.actualCarbs);
            arrow.SetTarget(null);
        }

        void Done()
        {
            if (m_Phase != Phase.Build) return;
            int index = m_Round - guessStations.Count;
            var b = buildStations[index];
            float target = m_Targets[index];
            float carbs = 0f;
            foreach (var item in b.plate.ItemsOnPlate())
            {
                carbs += item.carbs;
                Label(item.transform, $"{item.carbs:0} g");
            }
            m_Results[m_Round] = new Result { done = true, food = b.foodName, counted = target, ate = carbs };
            m_Phase = Phase.BuildResult;

            float miss = target - carbs; // counted the target, ate what is on the plate
            string body = (carbs <= 0f ? "Your plate was empty. " : "") + Lesson(b, target) + "\n" + Effect(target, carbs);
            ShowResult($"Your plate: {carbs:0} g", body, CgmGraph.Simulate(carbs, target), miss,
                "Target", target, "Your plate", carbs);
        }

        /// <summary>
        /// RESTART at a station: its food goes back (extra pieces taken from the supply are cleared) and that activity
        /// starts again; a build keeps the same target.
        /// </summary>
        void Restart(int round)
        {
            if (m_Phase == Phase.Intro || round > m_Round) return; // not reached yet
            var root = round < guessStations.Count ? guessStations[round].root : buildStations[round - guessStations.Count].root;
            if (root != null)
                foreach (var home in root.GetComponentsInChildren<ReturnHome>())
                    home.GoHome();
            m_Results[round] = default;
            StartRound(round);
        }

        void ShowResult(string title, string body, float[] readings, float miss, string countedName, float counted, string ateName, float ate)
        {
            bool more = false;
            for (int r = m_Round + 1; r < Rounds; r++) more |= !m_Results[r].done;
            tablet.Show(title, body, RoundCounter(m_Round) + "    CGM: 4 hours after eating",
                more ? "NEXT" : "RESULTS", "REPLAY", "result", readings);
            var chart = tablet.resultChart;
            if (chart != null)
            {
                float scale = Mathf.Max(counted, ate, 1f);
                chart.ShowRows(2);
                chart.SetLabel(0, countedName);
                chart.SetLabel(1, ateName);
                chart.SetBar(0, counted, scale, TabletChart.CountColor(counted, ate));
                chart.SetBar(1, ate, scale, TabletChart.Ate);
                chart.SetChip(0, "glucose\n" + TabletChart.Chip(counted, ate));
            }
            Play(Mathf.Abs(miss) <= TabletChart.CloseGrams ? ToneLibrary.Success() : ToneLibrary.Error());
        }

        void ShowSummary()
        {
            m_Phase = Phase.Summary;
            ClearLabels();
            float totalMiss = 0f, scale = 1f;
            int done = 0, low = 0, high = 0;
            foreach (var r in m_Results)
            {
                if (!r.done) continue;
                done++;
                float miss = r.counted - r.ate;
                totalMiss += Mathf.Abs(miss);
                if (miss < -TabletChart.CloseGrams) low++;
                else if (miss > TabletChart.CloseGrams) high++;
                scale = Mathf.Max(scale, r.counted, r.ate);
            }
            float avg = done > 0 ? totalMiss / done : 0f;

            var sb = new StringBuilder($"Average miss: <b>{avg:0} g</b>");
            if (low > 0) sb.Append($"   Low {low}x (↑)");
            if (high > 0) sb.Append($"   High {high}x (↓)");
            tablet.Show("Lunch results", sb.ToString(), "", "PLAY AGAIN", null, "summary");

            var chart = tablet.summaryChart;
            if (chart != null)
            {
                chart.ShowRows(m_Results.Length);
                for (int i = 0; i < m_Results.Length; i++)
                {
                    var r = m_Results[i];
                    chart.SetLabel(i, Capital(r.done ? r.food : i < guessStations.Count ? guessStations[i].foodName : buildStations[i - guessStations.Count].foodName));
                    chart.SetBar(2 * i, r.counted, scale, r.done ? TabletChart.CountColor(r.counted, r.ate) : TabletChart.Ate);
                    chart.SetBar(2 * i + 1, r.ate, scale, TabletChart.Ate);
                    chart.SetChip(i, r.done ? TabletChart.Chip(r.counted, r.ate) : "");
                }
                chart.SetStars(avg <= TabletChart.CloseGrams ? 3 : avg <= 2f * TabletChart.CloseGrams ? 2 : 1, 3);
            }
            tablet.Dock(null);
            arrow.SetTarget(null);
            Play(ToneLibrary.Finish());
        }

        // ------------------------------------------------------------------ text

        static string Header(float miss) =>
            Mathf.Abs(miss) <= TabletChart.CloseGrams ? "Close estimate" : miss < 0f ? "Counted too low" : "Counted too high";

        /// <summary>Which way the count was off and what that does to glucose (no doses).</summary>
        static string Effect(float counted, float ate)
        {
            float miss = counted - ate;
            if (Mathf.Abs(miss) <= TabletChart.CloseGrams) return "Close: glucose stays near range.";
            return miss < 0f
                ? "Too low: glucose runs high."
                : "Too high: glucose can drop low.";
        }

        /// <summary>"40 g is about 3 sandwich slices, 2 thick slices, or 1 bagel." from the pieces at the station.</summary>
        static string Lesson(BuildStation b, float target)
        {
            if (b.root == null) return "";
            var kinds = new Dictionary<string, float>();
            foreach (var item in b.root.GetComponentsInChildren<CarbItem>(true))
                if (item.carbs > 0f) kinds[item.displayName] = item.carbs;
            var list = new List<KeyValuePair<string, float>>(kinds);
            list.Sort((a, c) => a.Value.CompareTo(c.Value));
            var parts = new List<string>();
            foreach (var kv in list)
            {
                float halves = Mathf.Round(target / kv.Value * 2f);
                if (halves < 1f) continue;
                int whole = (int)(halves / 2f);
                bool half = halves % 2f > 0.5f;
                string amount = whole == 0 ? "1/2" : half ? $"{whole} 1/2" : $"{whole}";
                parts.Add($"{amount} {Plural(kv.Key.ToLowerInvariant(), halves > 2f ? 2 : 1)}");
            }
            if (parts.Count == 0) return "";
            if (parts.Count > 1) parts[parts.Count - 1] = "or " + parts[parts.Count - 1];
            return $"{target:0} g is about {string.Join(", ", parts)}.";
        }

        static string Plural(string name, int n) => n > 1 && !name.EndsWith("s") ? name + "s" : name;

        string RoundCounter(int round) => $"{round + 1} / {Rounds}";

        static string Dots(int page, int count)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < count; i++) sb.Append(i == page ? "<color=#fff>●</color> " : "<color=#678>●</color> ");
            return sb.ToString();
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        void Label(Transform target, string text, float height = 0.08f)
        {
            var go = new GameObject("Carb Label", typeof(RectTransform));
            go.transform.SetParent(target, false);
            var tmp = go.AddComponent<TextMeshPro>();
            if (labelFont != null) tmp.font = labelFont;
            tmp.text = $"<b>{text}</b>";
            tmp.fontSize = 0.28f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = k_Yellow;
            tmp.rectTransform.sizeDelta = new Vector2(0.2f, 0.05f);
            go.AddComponent<BillboardLabel>().height = height;
            m_Labels.Add(go);
        }

        void ClearLabels()
        {
            foreach (var l in m_Labels) if (l != null) Destroy(l);
            m_Labels.Clear();
        }

        void Play(AudioClip clip)
        {
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        }
    }
}
