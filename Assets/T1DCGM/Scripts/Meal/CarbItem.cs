using UnityEngine;

namespace T1DCGM
{
    /// <summary>A piece of food the learner can put on a plate (a bread slice, a banana...), with its carbohydrate content.</summary>
    public class CarbItem : MonoBehaviour
    {
        public string displayName = "Sandwich bread slice";
        public float carbs = FoodData.SandwichSliceCarbs;
    }
}
