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
        private readonly List<string> _recipeBaseIds = new List<string>();
        private readonly Dictionary<string, int> _mealSettled = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _runSettled = new Dictionary<string, int>();
        private int _nextInstanceId = 1;

        public BattleSession(
            GpBoard board,
            GameplayDatabase db,
            IRandomStream rng,
            IEnumerable<RecipeSlot> slots,
            int requiredScore,
            ScoreCalculator calculator = null,
            IReadOnlyDictionary<string, int> runSettledCounts = null)
        {
            Board = board ?? throw new ArgumentNullException(nameof(board));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _slots = new List<RecipeSlot>(slots ?? Array.Empty<RecipeSlot>());
            RequiredScore = requiredScore;
            _calculator = calculator ?? new ScoreCalculator();

            if (runSettledCounts != null)
            {
                foreach (KeyValuePair<string, int> kv in runSettledCounts)
                {
                    _runSettled[kv.Key] = kv.Value;
                }
            }

            CaptureRecipeSnapshot();
        }

        public GpBoard Board { get; }

        public IReadOnlyList<RecipeSlot> Slots => _slots;

        public int RequiredScore { get; }

        /// <summary>局级加法修正（由道具/Buff 注入，影响最终结算）。</summary>
        public float FinalFlat { get; set; }

        /// <summary>局级乘区修正（由道具/Buff 注入，影响最终结算）。</summary>
        public float FinalMultiplier { get; set; } = 1f;

        /// <summary>每道菜额外「视为食物数」（由被动道具注入，影响计数类前提）。</summary>
        public int ExtraCountAsPerDish { get; set; }

        /// <summary>蛋糕层数 buff 阈值下调（由被动道具「蛋糕捷径」注入）。</summary>
        public int CakeLayerThresholdReduction { get; set; }

        /// <summary>蛋糕层数每次净增时的额外加成（由被动道具「蛋糕膨胀」注入）。</summary>
        public int CakeLayerAccelBonus { get; set; }

        /// <summary>设置本次品鉴的初始蛋糕层数（道具「蛋糕打底」/跨局保留）。下限 0。</summary>
        public void SeedHappyCakeLayers(int layers)
        {
            HappyCakeLayers = layers < 0 ? 0 : layers;
        }

        /// <summary>本局允许的最大上菜次数（-1 表示不限；Boss 周「限量供应」会设上限）。</summary>
        public int MaxServes { get; set; } = -1;

        /// <summary>本局已上菜次数。</summary>
        public int ServesUsed { get; private set; }

        /// <summary>本次品鉴共享的全局「欢乐蛋糕层数」，随上菜/结算累加，跨品鉴重置。</summary>
        public int HappyCakeLayers { get; private set; }

        /// <summary>是否已结算（吃过）。</summary>
        public bool IsSettled { get; private set; }

        /// <summary>结算结果（未结算时为 null）。</summary>
        public ScoreResult LastResult { get; private set; }

        /// <summary>本局待入账的金币增量（上菜 OnServe + 结算经济运营累积；由 Game 层写回 GameRun.Gold）。</summary>
        public float PendingGold { get; private set; }

        /// <summary>本次结算各 BaseId 的结算增量（供 Game 层累加进 GameRun 大局历史）。</summary>
        public IReadOnlyDictionary<string, int> LastSettledIncrements { get; private set; } = new Dictionary<string, int>();

        /// <summary>本次品鉴菜谱内容（BaseId 列表，供菜谱检测）。</summary>
        public IReadOnlyList<string> RecipeBaseIds => _recipeBaseIds;

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

            var candidates = new List<ServeCandidate>();
            var weights = new List<float>();
            for (int i = 0; i < slot.Remaining.Count; i++)
            {
                DishDef dish = _db.GetDish(slot.Remaining[i]);
                if (dish == null)
                {
                    continue;
                }

                List<Placement> placements = Board.FindValidPlacements(dish);
                foreach (Placement placement in placements)
                {
                    candidates.Add(new ServeCandidate(i, dish, placement));
                    weights.Add(Math.Max(1, dish.Shape.CellCount));
                }
            }

            if (candidates.Count == 0)
            {
                return ServeResult.Fail(ServeOutcome.NoFittingDish);
            }

            ServeCandidate chosen = candidates[_rng.WeightedPickIndex(weights)];
            List<string> skills = TagComposer.ComposeSkills(chosen.Dish.SkillIds);
            string flavor = TagComposer.ComposeFlavor(new[] { chosen.Dish.FlavorId });
            var instance = new DishInstance(_nextInstanceId++, chosen.Dish, chosen.Placement, skills, flavor);
            Board.Place(instance);
            slot.RemoveAt(chosen.SlotEntryIndex);
            ServesUsed++;

            // 上菜时（OnServe）规则：直接改运行时状态（技能）并积累金币/全局层数。
            ServeRuleResolver.ServeResolveResult serveResult =
                ServeRuleResolver.ResolveOnServe(Board, _db, BuildHistory(), instance, HappyCakeLayers);
            PendingGold += serveResult.Gold;
            HappyCakeLayers = Math.Max(0, HappyCakeLayers + serveResult.HappyCakeLayerDelta + AccelFor(serveResult.HappyCakeLayerDelta));
            ApplyTransferRequests(serveResult.TransferRequests);
            ApplyCopySkillRequests(serveResult.CopySkillRequests);
            ApplyTempCopyRequests(serveResult.TempCopySourceIds);

            return new ServeResult(ServeOutcome.Placed, instance);
        }

        /// <summary>计算当前棋盘的预览分数（不标记结算，不产生副作用），供 UI 实时展示。</summary>
        public ScoreResult PreviewScore()
        {
            return _calculator.Calculate(Board, _db, FinalFlat, FinalMultiplier, history: BuildHistory(), initialHappyCakeLayers: HappyCakeLayers, extraCountAsPerDish: ExtraCountAsPerDish, cakeLayerThresholdReduction: CakeLayerThresholdReduction);
        }

        /// <summary>「吃」：结算、应用副作用（金币/层数/技能传递/历史）并记录结果。</summary>
        public ScoreResult Settle()
        {
            ScoreResult result = _calculator.Calculate(Board, _db, FinalFlat, FinalMultiplier, history: BuildHistory(), initialHappyCakeLayers: HappyCakeLayers, extraCountAsPerDish: ExtraCountAsPerDish, cakeLayerThresholdReduction: CakeLayerThresholdReduction);
            ApplySideEffects(result);
            LastResult = result;
            IsSettled = true;
            return LastResult;
        }

        /// <summary>把历史/菜谱打包为只读快照注入结算（读取本次结算之前的状态）。</summary>
        private IScoreHistory BuildHistory()
        {
            return new ScoreHistory(
                new Dictionary<string, int>(_runSettled),
                new Dictionary<string, int>(_mealSettled),
                _recipeBaseIds);
        }

        private void CaptureRecipeSnapshot()
        {
            foreach (RecipeSlot slot in _slots)
            {
                foreach (string dishId in slot.Remaining)
                {
                    DishDef def = _db.GetDish(dishId);
                    if (def != null)
                    {
                        _recipeBaseIds.Add(def.BaseId);
                    }
                }
            }
        }

        private void ApplySideEffects(ScoreResult result)
        {
            // 金币入账（结算侧效果）。
            PendingGold += result.GoldDelta;

            // 全局欢乐蛋糕层数：写回品鉴级计数器（层数净增时叠加道具加速）。
            HappyCakeLayers = Math.Max(0, HappyCakeLayers + result.HappyCakeLayerDelta + AccelFor(result.HappyCakeLayerDelta));

            // 技能传递。
            foreach (SkillTransferSideEffect transfer in result.SkillTransfers)
            {
                DishInstance inst = FindInstance(transfer.TargetInstanceId);
                if (inst == null)
                {
                    continue;
                }

                string label = string.IsNullOrEmpty(transfer.SourceName) ? null : $"{transfer.SourceName}<甜蜜传递>";
                foreach (SkillEffect effect in transfer.Effects)
                {
                    inst.AddTransferredSkill(effect, label);
                }
            }

            // 永久分 / 永久乘区 / 视为食物数：写回实例（品鉴内跨结算持久）。
            foreach (KeyValuePair<int, float> kv in result.PermanentFlatDeltas)
            {
                FindInstance(kv.Key)?.AddPermanentFlat(kv.Value);
            }

            foreach (KeyValuePair<int, float> kv in result.PermanentMultDeltas)
            {
                FindInstance(kv.Key)?.MultiplyPermanentMult(kv.Value);
            }

            // 历史累计：本次结算把盘面每道菜的 BaseId 计入大局/小局。
            var increments = new Dictionary<string, int>();
            foreach (DishInstance dish in Board.Dishes)
            {
                string baseId = dish.Def.BaseId;
                increments.TryGetValue(baseId, out int inc);
                increments[baseId] = inc + 1;
            }

            foreach (KeyValuePair<string, int> kv in increments)
            {
                _mealSettled.TryGetValue(kv.Key, out int meal);
                _mealSettled[kv.Key] = meal + kv.Value;
                _runSettled.TryGetValue(kv.Key, out int run);
                _runSettled[kv.Key] = run + kv.Value;
            }

            LastSettledIncrements = increments;
        }

        /// <summary>甜蜜传递落地：对每个请求，用随机流在候选目标中均权取 Count 个（0=全部），把技能追加给它们并标注来源。</summary>
        private void ApplyTransferRequests(IReadOnlyList<SkillTransferRequest> requests)
        {
            if (requests == null || requests.Count == 0)
            {
                return;
            }

            foreach (SkillTransferRequest request in requests)
            {
                if (request.CandidateTargetIds.Count == 0 || request.Effects.Count == 0)
                {
                    continue;
                }

                var targets = new List<int>(request.CandidateTargetIds);
                if (request.Count > 0 && targets.Count > request.Count)
                {
                    _rng.Shuffle(targets);
                    targets = targets.GetRange(0, request.Count);
                }

                string sourceLabel = $"{request.SourceName}<甜蜜传递>";
                foreach (int targetId in targets)
                {
                    DishInstance target = FindInstance(targetId);
                    if (target == null || target.Id == request.SourceInstanceId)
                    {
                        continue;
                    }

                    foreach (SkillEffect effect in request.Effects)
                    {
                        target.AddTransferredSkill(effect, sourceLabel);
                    }
                }
            }
        }

        /// <summary>技能复制落地：对每个请求，用随机流从候选池挑选 Count 个不同技能加到目标实例。</summary>
        private void ApplyCopySkillRequests(IReadOnlyList<CopySkillRequest> requests)
        {
            if (requests == null || requests.Count == 0)
            {
                return;
            }

            foreach (CopySkillRequest request in requests)
            {
                DishInstance target = FindInstance(request.TargetInstanceId);
                if (target == null || request.Candidates.Count == 0)
                {
                    continue;
                }

                var pool = new List<string>(request.Candidates);
                _rng.Shuffle(pool);
                int take = Math.Min(request.Count, pool.Count);
                for (int i = 0; i < take; i++)
                {
                    target.AddSkill(pool[i]);
                }
            }
        }

        /// <summary>临时复制落地：对每个源实例，在空格中克隆一份带同样技能/风味的临时实例并摆放。</summary>
        private void ApplyTempCopyRequests(IReadOnlyList<int> sourceIds)
        {
            if (sourceIds == null || sourceIds.Count == 0)
            {
                return;
            }

            foreach (int sourceId in sourceIds)
            {
                DishInstance source = FindInstance(sourceId);
                if (source == null)
                {
                    continue;
                }

                List<Placement> placements = Board.FindValidPlacements(source.Def);
                if (placements.Count == 0)
                {
                    continue;
                }

                Placement placement = placements[_rng.Range(0, placements.Count)];
                var clone = new DishInstance(_nextInstanceId++, source.Def, placement, source.SkillIds, source.FlavorId);
                clone.CopySkillSourcesFrom(source);
                clone.CopyTransferredSkillsFrom(source);
                clone.MarkTemporary();
                Board.Place(clone);
            }
        }

        /// <summary>清理本次品鉴产生的临时克隆实例（品鉴结束时调用）。</summary>
        public void ClearTemporaryDishes()
        {
            var temporaries = new List<DishInstance>();
            foreach (DishInstance dish in Board.Dishes)
            {
                if (dish.IsTemporary)
                {
                    temporaries.Add(dish);
                }
            }

            foreach (DishInstance dish in temporaries)
            {
                Board.RemoveDish(dish);
            }
        }

        /// <summary>层数净增（delta&gt;0）时返回额外加速层数，否则 0（道具「蛋糕膨胀」）。</summary>
        private int AccelFor(int delta)
        {
            return delta > 0 ? CakeLayerAccelBonus : 0;
        }

        private DishInstance FindInstance(int id)
        {
            foreach (DishInstance dish in Board.Dishes)
            {
                if (dish.Id == id)
                {
                    return dish;
                }
            }

            return null;
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

        private readonly struct ServeCandidate
        {
            public ServeCandidate(int slotEntryIndex, DishDef dish, Placement placement)
            {
                SlotEntryIndex = slotEntryIndex;
                Dish = dish;
                Placement = placement;
            }

            public int SlotEntryIndex { get; }

            public DishDef Dish { get; }

            public Placement Placement { get; }
        }
    }
}
