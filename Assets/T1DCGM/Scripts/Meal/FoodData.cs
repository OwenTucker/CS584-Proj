namespace T1DCGM
{
    /// <summary>
    /// Carbohydrate values used in the lunch challenge (USDA FoodData Central, rounded). Volumes are US cups (237 mL),
    /// the unit people with diabetes are taught to picture.
    /// </summary>
    public static class FoodData
    {
        public const float CupMl = 237f;
        public const float TargetCarbs = 50f;

        /// <summary>Cooked long-grain white rice: 1 cup (158 g) has about 45 g carbs.</summary>
        public const float RiceCarbsPerCup = 45f;
        /// <summary>Cooked penne: 1 cup (107 g, short tubes pack loosely) has about 33 g carbs (31 g per 100 g).</summary>
        public const float PastaCarbsPerCup = 33f;

        public const float SandwichSliceCarbs = 14f;
        public const float ThickSliceCarbs = 22f;
        public const float BagelCarbs = 50f;

        /// <summary>Medium banana, 18 to 20 cm (118 g).</summary>
        public const float BananaCarbs = 27f;
        /// <summary>Medium apple, 7.5 cm across (182 g).</summary>
        public const float AppleCarbs = 25f;
        /// <summary>Medium orange, 6.5 cm across (131 g).</summary>
        public const float OrangeCarbs = 15f;
        /// <summary>Watermelon flesh: 7.6 g carbs per 100 g, about 0.96 g per mL.</summary>
        public const float WatermelonCarbsPerMl = 0.073f;

        /// <summary>Insulin-to-carb ratio used to turn a carb count into a dose (1 unit per 10 g, a common starting ratio).</summary>
        public const float GramsPerUnit = 10f;

        /// <summary>"1 1/2 cups" style text for a number of cups, to the nearest quarter.</summary>
        public static string Cups(float cups)
        {
            int quarters = UnityEngine.Mathf.RoundToInt(cups * 4f);
            int whole = quarters / 4, rest = quarters % 4;
            string frac = rest switch { 1 => "1/4", 2 => "1/2", 3 => "3/4", _ => "" };
            string amount = whole == 0 ? (frac == "" ? "0" : frac) : frac == "" ? $"{whole}" : $"{whole} {frac}";
            return amount + (quarters > 4 ? " cups" : " cup");
        }
    }
}
