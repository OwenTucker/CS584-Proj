using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace T1DCGM
{
    /// <summary>A dish already served at real size; the learner dials in a carb estimate.</summary>
    [System.Serializable]
    public class GuessStation
    {
        public string foodName = "rice";
        public float actualCarbs;
        [Tooltip("The serving in everyday units, e.g. \"1 1/2 cups\".")]
        public string amountText;
        public Transform focus;
        public PressButton3D minusButton, plusButton, plusTenButton, lockButton;
        public TMP_Text readout;
        [Tooltip("Shown with the answer: measuring cups holding the same serving.")]
        public GameObject reveal;
    }

    /// <summary>The learner puts pieces on a plate until they think it holds a random carb target.</summary>
    [System.Serializable]
    public class BuildStation
    {
        public string foodName = "bread";
        [Tooltip("Station root: the food pieces (CarbItem) under it are used to explain what the target looks like.")]
        public Transform root;
        public Plate plate;
        public PressButton3D doneButton, resetButton;
        public TMP_Text doneLabel;
    }

    /// <summary>
    /// Lunch challenge. Part 1: estimate the carbs in dishes already served (rice, pasta), the way most meals are
    /// counted. Part 2: build a plate to a random carb target from pieces (bread, fruit). Each answer says which way the
    /// estimate was off and plays the CGM trace that follows. No doses are shown: the point is the direction of the error.
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

        struct Result { public string food; public float estimate, actual; }

        Phase m_Phase;
        int m_Page, m_Round;
        float m_Guess;
        float[] m_Targets;
        readonly List<Result> m_Results = new List<Result>();
        readonly List<GameObject> m_Labels = new List<GameObject>();
        InputAction m_Next, m_Back;

        static readonly Color k_Yellow = new Color(1f, 0.84f, 0.25f);

        static readonly (string title, string body)[] k_Intro =
        {
            ("Lunch with type 1 diabetes",
             "Before you eat, you count the carbs and take insulin to match.\n\nThere is rarely a label or a scale. You judge by eye."),
            ("Why the count matters",
             "Your insulin is based on your carb count.\n\n<b>Count too low:</b> blood glucose runs high.\n<b>Count too high:</b> it can drop too low."),
            ("Part 1: Guess",
             "Two dishes are already served.\n\nLook closely at real size, then dial in your carb estimate."),
            ("Part 2: Build",
             "Fill a plate with bread, then with fruit, until you think it holds the carb target on this tablet."),
            ("Controls",
             "Thumbstick forward: teleport\nGrip: pick up food\nFingertip: poke buttons\nA: next    B: back"),
        };

        int Rounds => guessStations.Count + buildStations.Count;

        void Awake()
        {
            m_Next = XRInput.Button("Next", "<XRController>{RightHand}/primaryButton", "<Keyboard>/space");
            m_Back = XRInput.Button("Back", "<XRController>{RightHand}/secondaryButton", "<Keyboard>/backspace");
            for (int i = 0; i < guessStations.Count; i++)
            {
                int index = i;
                var g = guessStations[i];
                if (g.minusButton != null) g.minusButton.onPressed.AddListener(() => Adjust(index, -5f));
                if (g.plusButton != null) g.plusButton.onPressed.AddListener(() => Adjust(index, 5f));
                if (g.plusTenButton != null) g.plusTenButton.onPressed.AddListener(() => Adjust(index, 10f));
                if (g.lockButton != null) g.lockButton.onPressed.AddListener(() => LockGuess(index));
            }
            for (int i = 0; i < buildStations.Count; i++)
            {
                int index = i;
                var b = buildStations[i];
                if (b.doneButton != null) b.doneButton.onPressed.AddListener(() => OnDone(index));
                if (b.resetButton != null) b.resetButton.onPressed.AddListener(() => OnReset(index));
            }
            if (tablet != null)
            {
                tablet.onNext = Next;
                tablet.onBack = Back;
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
            {
                if (g.reveal != null) g.reveal.SetActive(false);
                if (g.readout != null) g.readout.text = "";
            }
            m_Targets = new float[buildStations.Count];
            for (int i = 0; i < buildStations.Count; i++)
            {
                m_Targets[i] = 5f * Random.Range(minTarget / 5, maxTarget / 5 + 1);
                var b = buildStations[i];
                if (b.doneLabel != null) b.doneLabel.text = "<b>DONE</b>";
            }
            ShowPage(0);
        }

        void OnNextInput(InputAction.CallbackContext _) => Next();
        void OnBackInput(InputAction.CallbackContext _) => Back();

        // ------------------------------------------------------------------ flow

        void Next()
        {
            switch (m_Phase)
            {
                case Phase.Intro:
                    if (m_Page + 1 < k_Intro.Length) ShowPage(m_Page + 1);
                    else StartRound(0);
                    break;
                case Phase.GuessResult:
                case Phase.BuildResult:
                    ClearLabels();
                    if (m_Round + 1 < Rounds) StartRound(m_Round + 1);
                    else ShowSummary();
                    break;
                case Phase.Summary:
                    PlayAgain();
                    break;
            }
        }

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
            var (title, body) = k_Intro[page];
            if (page == 0 && CgmSession.applied)
                body = "Your new CGM is reading every 5 minutes.\n\n" + body;
            bool last = page == k_Intro.Length - 1;
            tablet.Show(title, body, Dots(page, k_Intro.Length), last ? "START" : "NEXT", page > 0 ? "BACK" : null);
            arrow.SetTarget(null);
        }

        void StartRound(int round)
        {
            m_Round = round;
            if (round < guessStations.Count)
            {
                m_Phase = Phase.Guess;
                var g = guessStations[round];
                m_Guess = guessStart;
                UpdateReadout(g);
                tablet.Show($"Guess {round + 1} of {guessStations.Count}: {Capital(g.foodName)}",
                    $"How many grams of carbs are in this {g.foodName}?\n\nSet your estimate with the buttons, then poke <b>LOCK IN</b>.",
                    RoundCounter(round), "", null);
                arrow.SetTarget(g.focus);
            }
            else
            {
                m_Phase = Phase.Build;
                int i = round - guessStations.Count;
                var b = buildStations[i];
                if (b.doneLabel != null) b.doneLabel.text = "<b>DONE</b>";
                tablet.Show($"Build a {m_Targets[i]:0} g plate: {Capital(b.foodName)}",
                    $"Put {b.foodName} on the plate until you think it holds <b>{m_Targets[i]:0} g</b> of carbs.\n\nPoke <b>DONE</b> when it looks right.",
                    RoundCounter(round), "", null);
                arrow.SetTarget(b.plate.transform);
            }
            Play(ToneLibrary.Click());
        }

        void Adjust(int index, float delta)
        {
            if (m_Phase != Phase.Guess || index != m_Round) return;
            m_Guess = Mathf.Clamp(m_Guess + delta, 0f, 300f);
            UpdateReadout(guessStations[index]);
        }

        void UpdateReadout(GuessStation g)
        {
            if (g.readout != null) g.readout.text = $"<b>{m_Guess:0} g</b>";
        }

        void LockGuess(int index)
        {
            if (m_Phase != Phase.Guess || index != m_Round) return;
            var g = guessStations[index];
            m_Results.Add(new Result { food = g.foodName, estimate = m_Guess, actual = g.actualCarbs });
            if (g.reveal != null) g.reveal.SetActive(true);
            m_Phase = Phase.GuessResult;

            float miss = m_Guess - g.actualCarbs;
            var sb = new StringBuilder();
            sb.Append($"This is <b>{g.amountText}</b> of {g.foodName}: <b>{g.actualCarbs:0} g</b>. You guessed {m_Guess:0} g");
            sb.Append(Mathf.Abs(miss) < 2.5f ? ", spot on." : miss < 0f ? $", <b>{-miss:0} g low</b>." : $", <b>{miss:0} g high</b>.");
            sb.Append("\n").Append(Effect(m_Guess, g.actualCarbs));
            ShowResult(Header(miss), sb.ToString(), CgmGraph.Simulate(g.actualCarbs, m_Guess), miss);
            arrow.SetTarget(null);
        }

        void OnDone(int index)
        {
            int round = guessStations.Count + index;
            if (round != m_Round) return;
            if (m_Phase == Phase.BuildResult) { Next(); return; }
            if (m_Phase != Phase.Build) return;

            var b = buildStations[index];
            float target = m_Targets[index];
            var items = b.plate.ItemsOnPlate();
            float carbs = 0f;
            var counts = new SortedDictionary<string, (int n, float each)>();
            foreach (var item in items)
            {
                carbs += item.carbs;
                counts[item.displayName] = counts.TryGetValue(item.displayName, out var c) ? (c.n + 1, item.carbs) : (1, item.carbs);
                Label(item.transform, $"{item.carbs:0} g");
            }
            m_Results.Add(new Result { food = b.foodName, estimate = target, actual = carbs });
            if (b.doneLabel != null) b.doneLabel.text = "<b>NEXT</b>";
            m_Phase = Phase.BuildResult;

            float miss = target - carbs; // counted the target, ate what is on the plate
            var sb = new StringBuilder();
            if (items.Count == 0) sb.Append("Your plate was empty. ");
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add($"{kv.Value.n} {Plural(kv.Key.ToLowerInvariant(), kv.Value.n)} ({kv.Value.each:0} g)");
            if (parts.Count > 0) sb.Append(string.Join(", ", parts)).Append(". ");
            sb.Append(Lesson(b, target));
            sb.Append("\n").Append(Effect(target, carbs));
            ShowResult($"Your plate: {carbs:0} g (target {target:0} g)", sb.ToString(), CgmGraph.Simulate(carbs, target), miss);
        }

        void OnReset(int index)
        {
            if (m_Phase != Phase.Build || guessStations.Count + index != m_Round) return;
            buildStations[index].plate.Clear();
        }

        void ShowResult(string title, string body, float[] readings, float miss)
        {
            bool close = Mathf.Abs(miss) <= 7.5f;
            tablet.Show(title, body, RoundCounter(m_Round) + "    CGM, 4 hours after eating",
                m_Round + 1 < Rounds ? "NEXT" : "RESULTS", "REPLAY", readings);
            Play(close ? ToneLibrary.Success() : ToneLibrary.Error());
        }

        void ShowSummary()
        {
            m_Phase = Phase.Summary;
            var sb = new StringBuilder();
            float totalMiss = 0f;
            int low = 0, high = 0;
            foreach (var r in m_Results)
            {
                float miss = r.estimate - r.actual;
                totalMiss += Mathf.Abs(miss);
                if (miss < -7.5f) low++;
                else if (miss > 7.5f) high++;
                sb.AppendLine($"{Capital(r.food)}: counted {r.estimate:0} g, ate {r.actual:0} g");
            }
            float avg = m_Results.Count > 0 ? totalMiss / m_Results.Count : 0f;
            sb.Append($"\nAverage miss: <b>{avg:0} g</b> per meal.");
            if (low > 0) sb.Append($" {low} counted low (glucose high).");
            if (high > 0) sb.Append($" {high} counted high (glucose low).");
            tablet.Show("Lunch results", sb.ToString(), "", "PLAY AGAIN", null);
            arrow.SetTarget(null);
            Play(ToneLibrary.Finish());
        }

        // ------------------------------------------------------------------ text

        static string Header(float miss) =>
            Mathf.Abs(miss) <= 7.5f ? "Close estimate" : miss < 0f ? "Counted too low" : "Counted too high";

        /// <summary>Which way the count was off and what that does to glucose (no doses).</summary>
        static string Effect(float counted, float ate)
        {
            float miss = counted - ate;
            if (Mathf.Abs(miss) <= 7.5f) return "Close count: glucose should stay near range.";
            return miss < 0f
                ? "Counted low: the insulin covers less than you ate, so glucose runs high."
                : "Counted high: the insulin covers more than you ate, so glucose can drop low.";
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
            for (int i = 0; i < count; i++) sb.Append(i == page ? "<color=#fff>o</color> " : "<color=#678>o</color> ");
            return sb.ToString();
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        void Label(Transform target, string text)
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
            go.AddComponent<BillboardLabel>().height = 0.08f;
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
