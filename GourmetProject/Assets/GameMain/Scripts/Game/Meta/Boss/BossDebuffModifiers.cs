namespace GourmetProject.Game.Meta
{
    public static class BossDebuffModifiers
    {
        public const string Indulgent = "feast_indulgent";
        public const string Binge = "feast_binge";
        public const string KidsMeal = "feast_kids_meal";
        public const string WeightLoss = "feast_weight_loss";
        public const string Gluttony = "feast_gluttony";
        public const string Omakase = "feast_omakase";
        public const string LightMeal = "feast_light_meal";
        public const string VeganMeal = "feast_vegan_meal";
        public const string DineAndDash = "feast_dine_and_dash";
        public const string FineDining = "feast_fine_dining";
        public const string Vegetarian = "feast_vegetarian";
        public const string CarbMeal = "feast_carb_meal";
        public const string DarkCuisine = "feast_dark_cuisine";
        public const string ComboMeal = "feast_combo_meal";
        public const string LateNight = "feast_late_night";
        public const string Appetizer = "feast_appetizer";
        public const string Tasting = "feast_tasting";
        public const string Buffet = "feast_buffet";

        public const string LegacyLimitServe = "limit_serve";
        public const string LegacySmallBoard = "small_board";

        public static bool IsLimitServe(string modifier) => modifier == LegacyLimitServe;

        public static bool IsSmallBoard(string modifier) => modifier == LegacySmallBoard;
    }
}
