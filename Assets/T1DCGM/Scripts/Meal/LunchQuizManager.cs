using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace T1DCGM
{
    /// <summary>One serving station on the buffet: a food, a plate and its buttons.</summary>
    [System.Serializable]
    public class LunchStation
    {
        public FoodKind kind;
        public Plate plate;
        public ServingSpoon spoon;
        public PressButton3D doneButton;
        public TMP_Text doneLabel;
        public PressButton3D resetButton;
        [Tooltip("See-through heap showing what 50 g really looks like (scoopable foods).")]
        public FoodMound targetMound;
        [Tooltip("Shown with the answer: 1 cup ball (about a fist), labels.")]
        public GameObject reveal;
    }

    /// <summary>
    /// Lunch challenge: serve 50 g of carbs of rice, pasta and bread, judged only by eye at true scale. After each
    /// plate the real amount is revealed next to it at real size; at the end the CGM shows what the meal did.
    /// </summary>
    public class LunchQuizManager : MonoBehaviour
    {
        public ProgressBoard board;
        public GuideArrow arrow;
        public List<LunchStation> stations = new List<LunchStation>();
        public CgmGraph wallGraph;
        public CgmGraph phoneGraph;
        public GameObject cgmDisplay;
        public Transform cgmTarget;
        public AudioSource audioSource;
        public TMP_FontAsset labelFont;
        [Tooltip("Seconds to play back 4 hours of CGM readings.")]
        public float playbackSeconds = 14f;

        enum Phase { Intro = 0, Serving = 1, Reveal = 2, Final = 3 }

        Phase m_Phase;
        int m_Round;
        readonly float[] m_Carbs = new float[3];
        readonly List<GameObject> m_Labels = new List<GameObject>();
        InputAction m_Continue;
        float[] m_Readings;
        float m_PlaybackStart = -1f;

        static readonly Color k_Yellow = new Color(1f, 0.84f, 0.25f);

        void Awake()
        {
            m_Continue = XRInput.Button("Continue", "<XRController>{RightHand}/primaryButton", "<Keyboard>/space");
            for (int i = 0; i < stations.Count; i++)
            {
                int index = i;
                var s = stations[i];
                if (s.doneButton != null) s.doneButton.onPressed.AddListener(() => OnDone(index));
                if (s.resetButton != null) s.resetButton.onPressed.AddListener(() => OnReset(index));
            }
        }

        void OnEnable() { m_Continue.Enable(); m_Continue.performed += OnContinue; }
        void OnDisable() { m_Continue.performed -= OnContinue; m_Continue.Disable(); }
        void OnDestroy() => m_Continue?.Dispose();

        void Start()
        {
            foreach (var s in stations)
            {
                if (s.targetMound != null)
                {
                    s.targetMound.SetVolume(FoodData.TargetCarbs / FoodData.CarbsPerMl(s.kind));
                    s.targetMound.gameObject.SetActive(false);
                }
                if (s.reveal != null) s.reveal.SetActive(false);
                SetLabel(s, "DONE");
            }
            if (cgmDisplay != null) cgmDisplay.SetActive(false);
            ShowIntro();
        }

        void Update()
        {
            board.RefreshLive();
            if (m_PlaybackStart >= 0f && m_Readings != null)
            {
                float f = Mathf.Clamp01((Time.time - m_PlaybackStart) / playbackSeconds);
                int count = Mathf.Max(1, Mathf.CeilToInt(f * m_Readings.Length));
                if (wallGraph != null) wallGraph.Plot(m_Readings, count);
                if (phoneGraph != null) phoneGraph.Plot(m_Readings, count);
                if (f >= 1f) m_PlaybackStart = -1f;
            }
        }

        // ------------------------------------------------------------------ flow

        void ShowIntro()
        {
            m_Phase = Phase.Intro;
            string cgmLine = CgmSession.applied ? "Your CGM is on and reading every 5 minutes. Time for lunch.\n\n" : "Time for lunch.\n\n";
            board.Show("LUNCH", "Count your carbs by eye", k_Yellow,
                cgmLine +
                "At each station, serve yourself food worth <b>50 g of carbohydrate</b>: rice, then pasta, then bread. " +
                "No scale and no numbers: judge by what you see, at real size, next to your own hands.\n\n" +
                "<b>Rice and pasta:</b> grab the serving spoon with GRIP, dip it into the pan, then tip it over your plate.\n" +
                "<b>Bread:</b> put pieces on your plate.\n\nPoke <b>DONE</b> when you think your plate holds 50 g.",
                "A: start      Thumbstick forward: teleport", 0f);
            arrow.SetTarget(null);
        }

        void OnContinue(InputAction.CallbackContext _)
        {
            if (m_Phase == Phase.Intro)
                StartRound(0);
            else if (m_Phase == Phase.Final)
                PlayCgm();
        }

        void StartRound(int round)
        {
            m_Round = round;
            m_Phase = Phase.Serving;
            var s = stations[round];
            SetLabel(s, "DONE");
            arrow.SetTarget(s.plate.transform);
            string how = s.kind == FoodKind.Bread
                ? "Pick the bread up with GRIP and put it on the plate. Mix and match any pieces."
                : $"Grab the serving spoon with GRIP, dip it into the {FoodData.Name(s.kind)}, then tip it over the plate. Repeat until it looks right.";
            board.Show($"STATION {round + 1} OF {stations.Count}", $"Serve 50 g of carbs of {FoodData.Name(s.kind)}", Color.white,
                $"Go to the <b>{FoodData.Name(s.kind).ToUpperInvariant()}</b> station (yellow arrow).\n\n{how}\n\nPoke <b>DONE</b> when you think it is 50 g. Poke <b>RESET</b> to empty the plate.",
                "GRIP: grab      Thumbstick forward: teleport      Poke: buttons", (float)round / stations.Count);
        }

        void OnDone(int index)
        {
            if (index != m_Round) return;
            if (m_Phase == Phase.Serving) Reveal(index);
            else if (m_Phase == Phase.Reveal) Next();
        }

        void OnReset(int index)
        {
            if (index != m_Round || m_Phase != Phase.Serving) return;
            stations[index].plate.Clear();
            board.ShowFeedback("Plate emptied.", FeedbackKind.Info);
        }

        void Reveal(int index)
        {
            var s = stations[index];
            var sb = new StringBuilder();
            float carbs;
            if (s.kind == FoodKind.Bread)
            {
                var items = s.plate.BreadOnPlate();
                carbs = 0f;
                var counts = new SortedDictionary<string, (int n, float each)>();
                foreach (var b in items)
                {
                    carbs += b.carbs;
                    counts[b.displayName] = counts.TryGetValue(b.displayName, out var c) ? (c.n + 1, b.carbs) : (1, b.carbs);
                    Label(b.transform, $"{b.carbs:0} g", 0.07f);
                }
                if (items.Count == 0)
                    sb.AppendLine("Your plate was empty.");
                foreach (var kv in counts)
                    sb.AppendLine($"{kv.Value.n} x {kv.Key} ({kv.Value.each:0} g each)");
                sb.AppendLine($"Total: <b>{carbs:0} g</b> of carbs. {Verdict(carbs)}");
                sb.AppendLine();
                sb.Append($"50 g is about <b>3 1/2 sandwich slices</b>, or <b>2 thick slices</b>, or <b>one bagel</b>. Same carbs, very different sizes.");
            }
            else
            {
                float ml = s.plate.mound != null ? s.plate.mound.volumeMl : 0f;
                carbs = s.plate.ScoopedCarbs;
                float targetCups = FoodData.TargetCarbs / FoodData.CarbsPerMl(s.kind) / FoodData.CupMl;
                sb.AppendLine($"You served about <b>{ml / FoodData.CupMl:0.0} cups</b> of {FoodData.Name(s.kind)} = <b>{carbs:0} g</b> of carbs. {Verdict(carbs)}");
                sb.AppendLine();
                sb.Append($"50 g of {FoodData.Name(s.kind)} is about <b>{targetCups:0.0} cups</b>: the <color=#7f7>green heap</color> beside your plate. " +
                          "The ball is <b>1 cup</b>, about the size of your fist. Hold your fist next to it.");
                if (s.targetMound != null) s.targetMound.gameObject.SetActive(true);
                if (s.spoon != null && s.spoon.spilledMl > 5f)
                    sb.Append($"\n<size=80%>About {s.spoon.spilledMl / FoodData.CupMl:0.0} cups ended up on the table instead of the plate.</size>");
            }

            m_Carbs[index] = carbs;
            if (s.reveal != null) s.reveal.SetActive(true);
            m_Phase = Phase.Reveal;
            SetLabel(s, "NEXT");
            float diff = carbs - FoodData.TargetCarbs;
            bool close = Mathf.Abs(diff) <= 7.5f;
            board.Show($"STATION {index + 1} OF {stations.Count}", close ? "Great estimate!" : diff > 0 ? $"{diff:0} g too much" : $"{-diff:0} g too little",
                close ? new Color(0.45f, 0.95f, 0.55f) : k_Yellow, sb.ToString(), "Poke NEXT to continue", (index + 1f) / stations.Count);
            Play(close ? ToneLibrary.Success() : ToneLibrary.Error());
        }

        static string Verdict(float carbs)
        {
            float diff = carbs - FoodData.TargetCarbs;
            if (Mathf.Abs(diff) <= 7.5f) return "Within 7.5 g of the target.";
            return diff > 0f ? $"That is {diff:0} g over." : $"That is {-diff:0} g under.";
        }

        void Next()
        {
            ClearLabels();
            if (m_Round + 1 < stations.Count)
                StartRound(m_Round + 1);
            else
                ShowFinal();
        }

        void ShowFinal()
        {
            m_Phase = Phase.Final;
            float eaten = 0f;
            foreach (var c in m_Carbs) eaten += c;
            float dosed = FoodData.TargetCarbs * stations.Count;
            m_Readings = CgmGraph.Simulate(eaten, dosed);
            float peak = 0f, low = 999f;
            foreach (var r in m_Readings) { peak = Mathf.Max(peak, r); low = Mathf.Min(low, r); }

            string outcome =
                low < CgmGraph.Low ? "<color=#f66>Low.</color> You took insulin for more carbs than you ate. A low like this needs fast sugar (about 15 g) and a re-check in 15 minutes." :
                peak > 250f || m_Readings[m_Readings.Length - 1] > CgmGraph.High ? "<color=#fa4>High.</color> You ate more carbs than you took insulin for, so glucose kept climbing." :
                "<color=#7f7>Mostly in range.</color> A rise after eating is normal; your dose matched your plate.";

            var sb = new StringBuilder();
            for (int i = 0; i < stations.Count; i++)
                sb.AppendLine($"{Capital(FoodData.Name(stations[i].kind))}: {m_Carbs[i]:0} g");
            sb.AppendLine($"You dosed insulin for <b>{dosed:0} g</b> (3 x 50 g) and ate <b>{eaten:0} g</b>.");
            sb.AppendLine();
            sb.Append(outcome);
            board.Show("AFTER LUNCH", "Watch your CGM", k_Yellow, sb.ToString(), "A: replay the CGM      Grab the phone to read it up close", 1f);
            if (cgmDisplay != null) cgmDisplay.SetActive(true);
            arrow.SetTarget(cgmTarget);
            Play(ToneLibrary.Finish());
            PlayCgm();
        }

        void PlayCgm()
        {
            if (m_Readings == null) return;
            m_PlaybackStart = Time.time;
        }

        // ------------------------------------------------------------------ helpers

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static void SetLabel(LunchStation s, string text)
        {
            if (s.doneLabel != null) s.doneLabel.text = text;
        }

        void Label(Transform target, string text, float height)
        {
            var go = new GameObject("Carb Label", typeof(RectTransform));
            go.transform.SetParent(target, false);
            var tmp = go.AddComponent<TextMeshPro>();
            if (labelFont != null) tmp.font = labelFont;
            tmp.text = $"<b>{text}</b>";
            tmp.fontSize = 0.3f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = k_Yellow;
            tmp.rectTransform.sizeDelta = new Vector2(0.2f, 0.05f);
            var bb = go.AddComponent<BillboardLabel>();
            bb.height = height;
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
