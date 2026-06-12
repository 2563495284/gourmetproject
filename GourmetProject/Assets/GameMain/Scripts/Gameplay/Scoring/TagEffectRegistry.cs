using System.Collections.Generic;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 标签效果注册表：按 <see cref="TagEffectType"/> 派发到具体 <see cref="ITagEffect"/>。
    /// <see cref="CreateDefault"/> 注册全部内置效果；扩展时调用 <see cref="Register"/> 覆盖或新增。
    /// </summary>
    public sealed class TagEffectRegistry
    {
        private readonly Dictionary<TagEffectType, ITagEffect> _effects = new Dictionary<TagEffectType, ITagEffect>();

        public void Register(TagEffectType type, ITagEffect effect) => _effects[type] = effect;

        public ITagEffect Get(TagEffectType type) => _effects.TryGetValue(type, out ITagEffect e) ? e : null;

        public static TagEffectRegistry CreateDefault()
        {
            var registry = new TagEffectRegistry();
            registry.Register(TagEffectType.AddFlat, new AddFlatEffect());
            registry.Register(TagEffectType.AddMult, new AddMultEffect());
            registry.Register(TagEffectType.PerAdjacentDish, new PerAdjacentDishEffect());
            registry.Register(TagEffectType.PerEmptyCell, new PerEmptyCellEffect());
            registry.Register(TagEffectType.PerOccupiedCell, new PerOccupiedCellEffect());
            registry.Register(TagEffectType.PerDishOnBoard, new PerDishOnBoardEffect());
            return registry;
        }
    }
}
