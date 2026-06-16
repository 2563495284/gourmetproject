namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>分数结算阶段。数值顺序即默认执行顺序。</summary>
    public enum ScorePhase
    {
        BeforeAll = 0,
        BeforeDish = 100,
        DishBase = 200,
        DishTags = 300,
        CellTags = 400,
        AfterDish = 500,
        AfterAllDishes = 600,
        Final = 700,
    }
}
