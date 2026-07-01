using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Board
{
    /// <summary>
    /// 棋盘上的一个菜品实例：引用菜品定义，记录其朝向、占格、技能集合与风味。
    /// 技能与风味在创建时由「初始技能列表 + 单槽风味」确定，运行时可被道具追加/替换。
    /// </summary>
    public sealed class DishInstance
    {
        private readonly List<GridPos> _occupiedCells;

        public DishInstance(int id, DishDef def, Placement placement, IReadOnlyList<string> skillIds, string flavorId)
        {
            Id = id;
            Def = def ?? throw new ArgumentNullException(nameof(def));
            Placement = placement;
            SkillIds = skillIds ?? Array.Empty<string>();
            FlavorId = flavorId ?? string.Empty;

            _occupiedCells = placement.Orientation.Cells
                .Select(c => c.Offset(placement.Origin.X, placement.Origin.Y))
                .ToList();
        }

        /// <summary>棋盘内唯一序号，用于稳定排序与表现层映射。</summary>
        public int Id { get; }

        public DishDef Def { get; }

        public Placement Placement { get; }

        /// <summary>该实例的最终技能 id 列表（数量无上限）。</summary>
        public IReadOnlyList<string> SkillIds { get; }

        /// <summary>该实例的最终风味 id（单槽，可空）。</summary>
        public string FlavorId { get; }

        /// <summary>是否带有风味。</summary>
        public bool HasFlavor => !string.IsNullOrEmpty(FlavorId);

        /// <summary>绝对占格列表。</summary>
        public IReadOnlyList<GridPos> OccupiedCells => _occupiedCells;

        public bool Occupies(GridPos cell) => _occupiedCells.Contains(cell);
    }
}
