using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Gameplay.Tags;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Gameplay.Battle
{
    /// <summary>
    /// 一局局内战斗的完整逻辑（纯 C#，可单测）：持有棋盘与菜谱槽，处理「上菜」随机摆放与「吃」结算。
    /// 所有随机经由注入的确定性流，保证同种子可复现。表现层（BattleForm）只读取状态并转发操作。
    /// </summary>
    public sealed class BattleSession
    {
        private readonly GameplayDatabase _db;
        private readonly IRandomStream _rng;
        private readonly ScoreCalculator _calculator;
        private readonly List<RecipeSlot> _slots;
        private int _nextInstanceId = 1;

        public BattleSession(
            GpBoard board,
            GameplayDatabase db,
            IRandomStream rng,
            IEnumerable<RecipeSlot> slots,
            int requiredScore,
            ScoreCalculator calculator = null)
        {
            Board = board ?? throw new ArgumentNullException(nameof(board));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _slots = new List<RecipeSlot>(slots ?? Array.Empty<RecipeSlot>());
            RequiredScore = requiredScore;
            _calculator = calculator ?? new ScoreCalculator();
        }

        public GpBoard Board { get; }

        public IReadOnlyList<RecipeSlot> Slots => _slots;

        public int RequiredScore { get; }

        /// <summary>局级加法修正（由道具/Buff 注入，影响最终结算）。</summary>
        public float FinalFlat { get; set; }

        /// <summary>局级乘区修正（由道具/Buff 注入，影响最终结算）。</summary>
        public float FinalMultiplier { get; set; } = 1f;

        /// <summary>本局允许的最大上菜次数（-1 表示不限；Boss 周「限量供应」会设上限）。</summary>
        public int MaxServes { get; set; } = -1;

        /// <summary>本局已上菜次数。</summary>
        public int ServesUsed { get; private set; }

        /// <summary>是否已结算（吃过）。</summary>
        public bool IsSettled { get; private set; }

        /// <summary>结算结果（未结算时为 null）。</summary>
        public ScoreResult LastResult { get; private set; }

        /// <summary>从指定菜谱槽随机上一道能放下的菜，并随机朝向/位置摆上棋盘。</summary>
        public ServeResult Serve(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }

            if (MaxServes >= 0 && ServesUsed >= MaxServes)
            {
                return ServeResult.Fail(ServeOutcome.LimitReached);
            }

            RecipeSlot slot = _slots[slotIndex];
            if (slot.IsEmpty)
            {
                return ServeResult.Fail(ServeOutcome.SlotEmpty);
            }

            // 收集该槽内「能放下」的菜品下标（保证能放得下，符合策划要求）。
            var fittingIndices = new List<int>();
            for (int i = 0; i < slot.Remaining.Count; i++)
            {
                DishDef def = _db.GetDish(slot.Remaining[i]);
                if (def != null && Board.CanFit(def))
                {
                    fittingIndices.Add(i);
                }
            }

            if (fittingIndices.Count == 0)
            {
                return ServeResult.Fail(ServeOutcome.NoFittingDish);
            }

            int chosen = fittingIndices[_rng.Range(0, fittingIndices.Count)];
            DishDef dish = _db.GetDish(slot.Remaining[chosen]);

            List<Placement> placements = Board.FindValidPlacements(dish);
            Placement placement = placements[_rng.Range(0, placements.Count)];

            List<string> tags = TagComposer.Compose(dish.InherentTags, _db);
            var instance = new DishInstance(_nextInstanceId++, dish, placement, tags);

            Board.Place(instance);
            slot.RemoveAt(chosen);
            ServesUsed++;

            return new ServeResult(ServeOutcome.Placed, instance);
        }

        /// <summary>计算当前棋盘的预览分数（不标记结算），供 UI 实时展示。</summary>
        public ScoreResult PreviewScore()
        {
            return _calculator.Calculate(Board, _db, FinalFlat, FinalMultiplier);
        }

        /// <summary>「吃」：结算并记录结果。返回是否达标过关。</summary>
        public ScoreResult Settle()
        {
            LastResult = _calculator.Calculate(Board, _db, FinalFlat, FinalMultiplier);
            IsSettled = true;
            return LastResult;
        }

        public bool IsWin => IsSettled && LastResult != null && LastResult.Total >= RequiredScore;

        /// <summary>清空棋盘（主动道具「重摆铃」）。已结算后不允许。</summary>
        public void ClearBoard()
        {
            if (IsSettled)
            {
                return;
            }

            Board.Clear();
        }

        /// <summary>当前所有菜谱槽是否都无法再上菜（用于提示玩家结算）。</summary>
        public bool CanServeAny()
        {
            if (MaxServes >= 0 && ServesUsed >= MaxServes)
            {
                return false;
            }

            foreach (RecipeSlot slot in _slots)
            {
                if (slot.IsEmpty)
                {
                    continue;
                }

                foreach (string dishId in slot.Remaining)
                {
                    DishDef def = _db.GetDish(dishId);
                    if (def != null && Board.CanFit(def))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
