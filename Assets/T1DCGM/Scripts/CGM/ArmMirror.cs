using UnityEngine;

namespace T1DCGM
{
    /// <summary>
    /// "Mirror" for the back of the arm. A real mirror cannot reflect passthrough, so a camera behind the calibrated arm
    /// films the virtual sleeve, zone, controllers and applicator, and the image is shown flipped (like a mirror) on a
    /// panel in front of the learner.
    /// </summary>
    public class ArmMirror : MonoBehaviour
    {
        public Camera mirrorCamera;
        public Renderer display;
        public float distance = 0.5f;

        RenderTexture m_Texture;

        void Awake()
        {
            if (mirrorCamera == null)
                return;
            m_Texture = new RenderTexture(768, 768, 24) { name = "Arm Mirror" };
            mirrorCamera.targetTexture = m_Texture;
            // URP renders a camera with a target texture as a normal (non-XR) camera; stereoTargetEye is built-in only.
            mirrorCamera.clearFlags = CameraClearFlags.SolidColor;
            mirrorCamera.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 1f);
            if (display != null)
            {
                var mat = display.material; // instance
                mat.mainTexture = m_Texture;
                if (mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", m_Texture);
                    mat.SetTextureScale("_BaseMap", new Vector2(-1f, 1f)); // mirror image
                    mat.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));
                }
            }
            SetActive(false);
        }

        void OnDestroy()
        {
            if (m_Texture != null)
                m_Texture.Release();
        }

        public void Aim(ArmModel arm)
        {
            if (mirrorCamera == null || !arm.valid)
                return;
            var target = arm.CentreAt(0.5f);
            var from = target + (arm.backDir * 0.85f + arm.outDir * 0.35f + Vector3.up * 0.2f).normalized * distance;
            mirrorCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from, Vector3.up));
        }

        public void SetActive(bool active)
        {
            if (mirrorCamera != null) mirrorCamera.enabled = active;
            if (display != null) display.transform.parent.gameObject.SetActive(active);
        }
    }
}
