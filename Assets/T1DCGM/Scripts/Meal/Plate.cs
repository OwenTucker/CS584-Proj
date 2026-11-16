using System.Collections.Generic;
using UnityEngine;

namespace T1DCGM
{
    /// <summary>Dinner plate: counts the food items resting on it.</summary>
    public class Plate : MonoBehaviour
    {
        public float radius = 0.13f;

        public List<CarbItem> ItemsOnPlate()
        {
            var list = new List<CarbItem>();
            foreach (var item in FindObjectsByType<CarbItem>(FindObjectsSortMode.None))
            {
                var d = item.transform.position - transform.position;
                if (new Vector2(d.x, d.z).magnitude < radius && d.y > -0.03f && d.y < 0.25f)
                    list.Add(item);
            }
            return list;
        }

        public float Carbs()
        {
            float total = 0f;
            foreach (var item in ItemsOnPlate())
                total += item.carbs;
            return total;
        }

        public void Clear()
        {
            foreach (var item in ItemsOnPlate())
                if (item.TryGetComponent<ReturnHome>(out var home))
                    home.GoHome();
        }
    }
}
