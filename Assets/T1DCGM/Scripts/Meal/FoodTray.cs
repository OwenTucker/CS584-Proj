using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace T1DCGM
{
    /// <summary>Hotel pan of a scoopable food. Dipping a <see cref="ServingSpoon"/> inside <see cref="area"/> fills it.</summary>
    public class FoodTray : MonoBehaviour
    {
        public FoodKind kind;
        public BoxCollider area;

        public bool Contains(Vector3 point) => area != null && area.bounds.Contains(point);
    }
}
