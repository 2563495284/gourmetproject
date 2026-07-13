using System.Collections.Generic;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 风味效果注册表：按 <see cref="FlavorEffectType"/> 派发到具体 <see cref="IScoreEffect"/>。
    /// <see cref="CreateDefault"/> 注册全部内置效果；扩展时调用 <see cref="Register"/> 覆盖或新增。
    /// </summary>
    public sealed class FlavorEffectRegistry
    {
        private readonly Dictionary<FlavorEffectType, IScoreEffect> _effects = new Dictionary<FlavorEffectType, IScoreEffect>();

        public void Register(FlavorEffectType type, IScoreEffect effect) => _effects[type] = effect;

        public IScoreEffect Get(FlavorEffectType type) => _effects.TryGetValue(type, out IScoreEffect e) ? e : null;

        public static FlavorEffectRegistry CreateDefault()
        {
            var registry = new FlavorEffectRegistry();
            registry.Register(FlavorEffectType.AddFlat, new AddFlatEffect());
            registry.Register(FlavorEffectType.AddMult, new AddMultEffect());
            registry.Register(FlavorEffectType.PerAdjacentDish, new PerAdjacentDishEffect());
            registry.Register(FlavorEffectType.PerEmptyCell, new PerEmptyCellEffect());
            registry.Register(FlavorEffectType.PerOccupiedCell, new PerOccupiedCellEffect());
            registry.Register(FlavorEffectType.PerDishOnBoard, new PerDishOnBoardEffect());
            registry.Register(FlavorEffectType.GrantGold, new GrantGoldEffect());
            return registry;
        }
    }
}
