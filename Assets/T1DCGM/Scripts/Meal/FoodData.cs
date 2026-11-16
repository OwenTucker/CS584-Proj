namespace T1DCGM
{
    /// <summary>Foods in the lunch challenge. Explicit values: serialized in scenes.</summary>
    public enum FoodKind { Rice = 0, Pasta = 1, Bread = 2 }

    /// <summary>
    /// Approximate carbohydrate values (USDA FoodData Central, rounded). Scoopable foods are measured by volume because
    /// that is what the learner sees; 1 US cup = 237 mL.
    /// </summary>
    public static class FoodData
    {
        public const float CupMl = 237f;
        public const float TargetCarbs = 50f;

        /// <summary>Cooked white rice: about 45 g carbs per cup.</summary>
        public const float RiceCarbsPerCup = 45f;
        /// <summary>Cooked pasta (penne): about 43 g carbs per cup.</summary>
        public const float PastaCarbsPerCup = 43f;

        public const float SandwichSliceCarbs = 14f;
        public const float ThickSliceCarbs = 22f;
        public const float BagelCarbs = 50f;

        public static float CarbsPerMl(FoodKind kind) => kind switch
        {
            FoodKind.Rice => RiceCarbsPerCup / CupMl,
            FoodKind.Pasta => PastaCarbsPerCup / CupMl,
            _ => 0f,
        };

        public static string Name(FoodKind kind) => kind switch
        {
            FoodKind.Rice => "rice",
            FoodKind.Pasta => "pasta",
            _ => "bread",
        };

        /// <summary>Mound height as a fraction of its base radius: rice slumps flatter than pasta.</summary>
        public static float MoundShape(FoodKind kind) => kind == FoodKind.Pasta ? 0.6f : 0.5f;
    }
}
