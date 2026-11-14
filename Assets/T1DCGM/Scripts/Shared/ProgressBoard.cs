using TMPro;
using UnityEngine;

namespace T1DCGM
{
    public enum FeedbackKind { Info, Success, Error }

    /// <summary>
    /// World-space board: part number and progress bar, a header (intro title or step title), the current
    /// instruction and feedback, and the controls at the bottom. Any field may be left empty.
    /// </summary>
    public class ProgressBoard : MonoBehaviour
    {
        public TMP_Text stepCounter;
        public RectTransform progressFill;
        [Tooltip("Large line at the top: yellow part title on intro screens, white step title during steps.")]
        public TMP_Text headerText;
        public TMP_Text instructionText;
        public TMP_Text feedbackText;
        public TMP_Text controlsText;

        [Header("Feedback")]
        public float feedbackDuration = 6f;

        static readonly Color k_Info = new Color(0.75f, 0.85f, 1f);
        static readonly Color k_Success = new Color(0.45f, 0.95f, 0.55f);
        static readonly Color k_Error = new Color(1f, 0.45f, 0.4f);

        float m_FeedbackUntil;

        /// <summary>Shows arbitrary content .</summary>
        public void Show(string counter, string header, Color headerColor, string instruction, string controls, float progress)
        {
            Set(stepCounter, counter);
            SetHeader(header, headerColor);
            Set(instructionText, instruction);
            Set(controlsText, controls);
            SetProgress(progress);
        }

        void SetHeader(string text, Color color)
        {
            if (headerText == null)
                return;
            headerText.text = text;
            headerText.color = color;
        }

        public void ShowFeedback(string message, FeedbackKind kind)
        {
            if (feedbackText == null)
                return;
            feedbackText.text = message;
            feedbackText.color = kind switch
            {
                FeedbackKind.Success => k_Success,
                FeedbackKind.Error => k_Error,
                _ => k_Info,
            };
            m_FeedbackUntil = Time.time + feedbackDuration;
        }

        /// <summary>Called every frame; clears feedback once it has been shown long enough.</summary>
        public void RefreshLive()
        {
            if (feedbackText != null && m_FeedbackUntil > 0f && Time.time > m_FeedbackUntil)
            {
                feedbackText.text = "";
                m_FeedbackUntil = 0f;
            }
        }

        void SetProgress(float t)
        {
            if (progressFill != null)
                progressFill.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
        }

        static void Set(TMP_Text text, string value)
        {
            if (text != null)
                text.text = value;
        }
    }
}
