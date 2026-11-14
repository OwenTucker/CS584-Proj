using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace T1DCGM
{
    /// <summary>Decides whether the learner may leave the current scene through a <see cref="SceneTeleportPad"/>.</summary>
    public interface ISceneGate
    {
        /// <summary>Return true to load the next scene. When false, the gate tells the learner why.</summary>
        bool TryLeave();
    }

    /// <summary>
    /// Permanent Teleportation Anchor between the supply room and the pump station. Teleporting onto it loads the other scene
    /// when its gate allows (it lights up then); otherwise the learner just lands on it and the board explains why not.
    /// </summary>
    [RequireComponent(typeof(TeleportationAnchor))]
    public class SceneTeleportPad : MonoBehaviour
    {
        public string sceneName;
        [Tooltip("Component implementing ISceneGate (the scene's manager).")]
        public MonoBehaviour gate;
        public Renderer padRenderer;
        public Color availableColor = new Color(0.2f, 0.85f, 0.4f);
        public Color unavailableColor = new Color(0.45f, 0.47f, 0.5f);
        public float delay = 0.3f;

        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");
        bool m_Loading;
        bool? m_Available;

        void Awake()
        {
            GetComponent<TeleportationAnchor>().teleporting.AddListener(_ => Go());
            SetAvailable(false);
        }

        /// <summary>Lights the pad up when the trip to the other scene is needed.</summary>
        public void SetAvailable(bool available)
        {
            if (m_Available == available || padRenderer == null)
                return;
            m_Available = available;
            var block = new MaterialPropertyBlock();
            block.SetColor(k_BaseColor, available ? availableColor : unavailableColor);
            padRenderer.SetPropertyBlock(block);
        }

        public void Go()
        {
            if (m_Loading || string.IsNullOrEmpty(sceneName))
                return;
            if (gate is ISceneGate g && !g.TryLeave())
                return;
            m_Loading = true;
            StartCoroutine(LoadAfterDelay());
        }

        IEnumerator LoadAfterDelay()
        {
            yield return new WaitForSeconds(delay);
            SceneManager.LoadScene(sceneName);
        }
    }
}
