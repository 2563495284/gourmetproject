using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Gameplay.Model;

namespace GourmetProject.Gameplay.Board
{
    /// <summary>
    /// 棋盘上的一个菜品实例：引用菜品定义，记录其朝向、占格与最终标签集合。
    /// 标签集合在创建时由「固有标签 + 唯一标签 A/B 替换规则」确定。
    /// </summary>
    public sealed class DishInstance
    {
        private readonly List<GridPos> _occupiedCells;

        public DishInstance(int id, DishDef def, Placement placement, IReadOnlyList<string> tagIds)
        {
            Id = id;
            Def = def ?? throw new ArgumentNullException(nameof(def));
            Placement = placement;
            TagIds = tagIds ?? Array.Empty<string>();

            _occupiedCells = placement.Orientation.Cells
                .Select(c => c.Offset(placement.Origin.X, placement.Origin.Y))
                .ToList();
        }

        /// <summary>棋盘内唯一序号，用于稳定排序与表现层映射。</summary>
        public int Id { get; }

        public DishDef Def { get; }

        public Placement Placement { get; }

        /// <summary>该实例的最终标签 id 列表（固有 + 唯一 A/B）。</summary>
        public IReadOnlyList<string> TagIds { get; }

        /// <summary>绝对占格列表。</summary>
        public IReadOnlyList<GridPos> OccupiedCells => _occupiedCells;

        public bool Occupies(GridPos cell) => _occupiedCells.Contains(cell);
    }
}
