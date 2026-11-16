using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace T1DCGM
{
    /// <summary>Dinner plate. Holds a heap of the scooped food and counts bread items resting on it.</summary>
    public class Plate : MonoBehaviour
    {
        public float radius = 0.13f;
        public FoodMound mound;

        public bool Accepts(Vector3 point)
        {
            var d = point - transform.position;
            return new Vector2(d.x, d.z).magnitude < radius * 1.1f && d.y > -0.02f && d.y < 0.35f;
        }

        public void AddFood(FoodKind kind, float ml)
        {
            if (mound == null) return;
            if (mound.volumeMl < 0.5f) mound.kind = kind;
            mound.Add(ml);
        }

        public float ScoopedCarbs => mound != null ? mound.Carbs : 0f;

        public List<BreadItem> BreadOnPlate()
        {
            var list = new List<BreadItem>();
            foreach (var b in FindObjectsByType<BreadItem>(FindObjectsSortMode.None))
            {
                var d = b.transform.position - transform.position;
                if (new Vector2(d.x, d.z).magnitude < radius && d.y > -0.03f && d.y < 0.2f)
                    list.Add(b);
            }
            return list;
        }

        public void Clear()
        {
            if (mound != null) mound.SetVolume(0f);
            foreach (var b in BreadOnPlate())
                if (b.TryGetComponent<ReturnHome>(out var home)) home.GoHome();
        }
    }
}
