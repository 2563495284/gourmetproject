using System;
using System.Collections.Generic;
using System.Reflection;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using UnityEngine.Scripting;

namespace GourmetProject.Game.Meta.BossDebuffs
{
    /// <summary>声明一个 Boss Debuff 模型对应的 TbBossDebuff.id。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class BossDebuffModelAttribute : Attribute
    {
        public BossDebuffModelAttribute(string debuffId)
        {
            DebuffId = debuffId;
        }

        public string DebuffId { get; }
    }

    /// <summary>
    /// 单场 Boss Debuff 行为模型。每个配置 id 对应一个子类，只覆写需要参与的经营挑战装配阶段。
    /// 机制数值由子类拥有；配置只保留展示、抽取、解锁和隐藏分元数据。
    /// </summary>
    public abstract class BossDebuffModel
    {
        protected GameRun Run { get; private set; }

        protected cfg.BossDebuff Definition { get; private set; }

        public string DebuffId => Definition?.Id ?? string.Empty;

        internal void Bind(GameRun run, cfg.BossDebuff definition)
        {
            Run = run;
            Definition = definition;
        }

        /// <summary>
        /// 在构建餐桌画布前调整最大宽高。
        /// 加行/加列/删行/删列类 Debuff 不要改这里：改包围盒会重居中初始碎片，玩家拼贴会对不上被丢掉。
        /// </summary>
        public virtual void ModifyTableBounds(ref int maxWidth, ref int maxHeight)
        {
        }

        /// <summary>在创建餐桌前修改本场食谱条目。</summary>
        public virtual void ModifyRecipeSlots(List<RecipeSlot> slots, IRandomStream rng)
        {
        }

        /// <summary>餐桌形状构建完成、永久格子材质叠加前修改格子结构。</summary>
        public virtual void ModifyBuiltTable(DiningTable table, int recipeEntryCount, IRandomStream rng)
        {
        }

        /// <summary>永久格子材质叠加后修改本场临时格子状态。</summary>
        public virtual void ModifyPreparedTable(DiningTable table, int recipeEntryCount, IRandomStream rng)
        {
        }

        /// <summary>基础会话配置完成后注入本场机制。</summary>
        public virtual void ApplyToBattle(BattleSession session)
        {
        }
    }

    /// <summary>debuffId → BossDebuffModel 子类的严格注册表。</summary>
    public static class BossDebuffModelRegistry
    {
        private static Dictionary<string, Type> _map;

        public static IReadOnlyCollection<string> RegisteredIds
        {
            get
            {
                EnsureBuilt();
                return _map.Keys;
            }
        }

        public static BossDebuffModel Create(GameRun run, cfg.BossDebuff definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            EnsureBuilt();
            if (!_map.TryGetValue(definition.Id, out Type type))
            {
                throw new InvalidOperationException($"星级评鉴 Debuff '{definition.Id}' 没有对应的模型类。");
            }

            var model = (BossDebuffModel)Activator.CreateInstance(type);
            model.Bind(run, definition);
            return model;
        }

        public static void ValidateDefinitions(cfg.Tables tables)
        {
            if (tables?.TbBossDebuff == null)
            {
                throw new InvalidOperationException("TbBossDebuff 未加载。");
            }

            EnsureBuilt();
            var configured = new HashSet<string>(StringComparer.Ordinal);
            foreach (cfg.BossDebuff definition in tables.TbBossDebuff.DataList)
            {
                if (definition == null || string.IsNullOrEmpty(definition.Id))
                {
                    throw new InvalidOperationException("TbBossDebuff 包含空 id。");
                }

                configured.Add(definition.Id);
                if (!_map.ContainsKey(definition.Id))
                {
                    throw new InvalidOperationException($"星级评鉴 Debuff '{definition.Id}' 没有对应的模型类。");
                }
            }

            foreach (string registeredId in _map.Keys)
            {
                if (!configured.Contains(registeredId))
                {
                    throw new InvalidOperationException($"星级评鉴 Debuff 模型 '{registeredId}' 没有对应的配置行。");
                }
            }
        }

        private static void EnsureBuilt()
        {
            if (_map != null)
            {
                return;
            }

            var map = new Dictionary<string, Type>(StringComparer.Ordinal);
            Type baseType = typeof(BossDebuffModel);
            foreach (Type type in baseType.Assembly.GetTypes())
            {
                if (type.IsAbstract || !baseType.IsAssignableFrom(type))
                {
                    continue;
                }

                BossDebuffModelAttribute attr = type.GetCustomAttribute<BossDebuffModelAttribute>(false);
                if (attr == null || string.IsNullOrEmpty(attr.DebuffId))
                {
                    continue;
                }

                if (map.TryGetValue(attr.DebuffId, out Type existing))
                {
                    throw new InvalidOperationException(
                        $"星级评鉴 Debuff '{attr.DebuffId}' 同时绑定了 {existing.FullName} 与 {type.FullName}。");
                }

                map.Add(attr.DebuffId, type);
            }

            _map = map;
        }
    }

    internal static class BossDebuffOperations
    {
        public static void CopyRecipeEntries(List<RecipeSlot> slots, int copyCount)
        {
            if (slots == null || copyCount <= 0)
            {
                return;
            }

            foreach (RecipeSlot slot in slots)
            {
                var originals = new List<RecipeSlotEntry>();
                foreach (RecipeSlotEntry entry in slot.Entries)
                {
                    originals.Add(entry.Clone());
                }

                for (int copyIndex = 0; copyIndex < copyCount; copyIndex++)
                {
                    foreach (RecipeSlotEntry original in originals)
                    {
                        RecipeSlotEntry copy = original.Clone();
                        copy.MarkTemporaryCopy();
                        slot.AddEntry(copy);
                    }
                }
            }
        }

        public static void MarkRandomRecipeEntries(
            List<RecipeSlot> slots,
            int count,
            IRandomStream rng,
            bool disableSkills = false,
            bool excludeFromScore = false)
        {
            if (slots == null || rng == null || count <= 0)
            {
                return;
            }

            var entries = new List<RecipeSlotEntry>();
            foreach (RecipeSlot slot in slots)
            {
                entries.AddRange(slot.Entries);
            }

            rng.Shuffle(entries);
            int take = Math.Min(count, entries.Count);
            for (int i = 0; i < take; i++)
            {
                if (disableSkills)
                {
                    entries[i].MarkSkillsDisabled();
                }

                if (excludeFromScore)
                {
                    entries[i].MarkExcludedFromScore();
                }
            }
        }

        public static void AddBottomCells(DiningTable table)
        {
            List<GridPos> cells = table.ExistingCells();
            var toAdd = new List<GridPos>();
            for (int x = 0; x < table.Width; x++)
            {
                int maxY = -1;
                foreach (GridPos cell in cells)
                {
                    if (cell.X == x && cell.Y > maxY)
                    {
                        maxY = cell.Y;
                    }
                }

                if (maxY >= 0)
                {
                    toAdd.Add(new GridPos(x, maxY + 1));
                }
            }

            foreach (GridPos cell in toAdd)
            {
                table.SetExists(cell, true);
            }
        }

        public static void AddRightCells(DiningTable table)
        {
            List<GridPos> cells = table.ExistingCells();
            var toAdd = new List<GridPos>();
            for (int y = 0; y < table.Height; y++)
            {
                int maxX = -1;
                foreach (GridPos cell in cells)
                {
                    if (cell.Y == y && cell.X > maxX)
                    {
                        maxX = cell.X;
                    }
                }

                if (maxX >= 0)
                {
                    toAdd.Add(new GridPos(maxX + 1, y));
                }
            }

            foreach (GridPos cell in toAdd)
            {
                table.SetExists(cell, true);
            }
        }

        public static void RemoveBottomCells(DiningTable table)
        {
            List<GridPos> cells = table.ExistingCells();
            var toRemove = new HashSet<GridPos>();
            for (int x = 0; x < table.Width; x++)
            {
                int maxY = -1;
                foreach (GridPos cell in cells)
                {
                    if (cell.X == x && cell.Y > maxY)
                    {
                        maxY = cell.Y;
                    }
                }

                if (maxY >= 0)
                {
                    toRemove.Add(new GridPos(x, maxY));
                }
            }

            foreach (GridPos cell in toRemove)
            {
                table.SetExists(cell, false);
            }
        }

        public static void RemoveRightCells(DiningTable table)
        {
            List<GridPos> cells = table.ExistingCells();
            var toRemove = new HashSet<GridPos>();
            for (int y = 0; y < table.Height; y++)
            {
                int maxX = -1;
                foreach (GridPos cell in cells)
                {
                    if (cell.Y == y && cell.X > maxX)
                    {
                        maxX = cell.X;
                    }
                }

                if (maxX >= 0)
                {
                    toRemove.Add(new GridPos(maxX, y));
                }
            }

            foreach (GridPos cell in toRemove)
            {
                table.SetExists(cell, false);
            }
        }
    }
}
