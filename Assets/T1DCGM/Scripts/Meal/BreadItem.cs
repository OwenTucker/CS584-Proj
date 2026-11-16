using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace T1DCGM
{
    /// <summary>A bread item with its carbohydrate content.</summary>
    public class BreadItem : MonoBehaviour
    {
        public string displayName = "Sandwich bread slice";
        public float carbs = FoodData.SandwichSliceCarbs;
    }
}
