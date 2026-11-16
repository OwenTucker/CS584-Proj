using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace T1DCGM
{
    /// <summary>
    /// Serving spoon: dip the bowl into a tray to fill it (the longer it stays in, the fuller it gets), tip it over a
    /// plate to serve. Food tipped out anywhere else is lost, food tipped over the tray goes back.
    /// </summary>
    public class ServingSpoon : MonoBehaviour
    {
        [Tooltip("Centre of the spoon bowl; its local up points out of the bowl.")]
        public Transform bowl;
        public FoodMound contents;
        [Tooltip("Heaped capacity in millilitres (a cafeteria serving spoon holds about a third of a cup).")]
        public float capacityMl = 85f;
        public float fillRateMl = 90f;
        public float pourRateMl = 350f;

        FoodTray[] m_Trays;
        Plate[] m_Plates;

        public float spilledMl { get; private set; }

        void Start()
        {
            m_Trays = FindObjectsByType<FoodTray>(FindObjectsSortMode.None);
            m_Plates = FindObjectsByType<Plate>(FindObjectsSortMode.None);
        }

        void Update()
        {
            if (bowl == null || contents == null)
                return;
            float upright = Vector3.Dot(bowl.up, Vector3.up);
            var pos = bowl.position;

            foreach (var tray in m_Trays)
            {
                if (!tray.Contains(pos) || upright < 0.4f)
                    continue;
                if (contents.volumeMl < 0.5f)
                    contents.kind = tray.kind;
                if (contents.kind == tray.kind)
                    contents.SetVolume(Mathf.Min(capacityMl, contents.volumeMl + fillRateMl * Time.deltaTime));
                return;
            }

            if (upright < 0.25f && contents.volumeMl > 0f)
            {
                float amount = Mathf.Min(contents.volumeMl, pourRateMl * Time.deltaTime);
                contents.SetVolume(contents.volumeMl - amount);
                var plate = PlateUnder(pos);
                if (plate != null)
                    plate.AddFood(contents.kind, amount);
                else if (!TrayUnder(pos))
                    spilledMl += amount;
            }
        }

        Plate PlateUnder(Vector3 pos)
        {
            foreach (var p in m_Plates)
                if (p.Accepts(pos)) return p;
            return null;
        }

        bool TrayUnder(Vector3 pos)
        {
            foreach (var t in m_Trays)
            {
                var b = t.area.bounds;
                if (pos.x > b.min.x && pos.x < b.max.x && pos.z > b.min.z && pos.z < b.max.z && pos.y > b.min.y) return true;
            }
            return false;
        }

        public void Empty() => contents?.SetVolume(0f);
    }
}
