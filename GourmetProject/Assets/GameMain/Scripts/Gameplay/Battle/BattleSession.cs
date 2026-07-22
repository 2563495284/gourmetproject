using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Gameplay.Tags;
using GpTable = GourmetProject.Gameplay.Board.DiningTable;

namespace GourmetProject.Gameplay.Battle
{
    /// <summary>
    /// 一局局内战斗的完整逻辑（纯 C#，可单测）：持有餐桌与菜谱槽，处理「上菜」随机摆放与「吃」结算。
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
        private readonly List<RecipeScoreFlatDelta> _lastRecipeScoreFlatDeltas = new List<RecipeScoreFlatDelta>();
        private readonly List<RecipeScoreMultiplierDelta> _lastRecipeScoreMultiplierDeltas = new List<RecipeScoreMultiplierDelta>();
        private int _nextInstanceId = 1;
        private int _appetizerRemoved;
        private float _settlementDishMultiplierFlat;
        private string _settlementDishMultiplierItemId = string.Empty;
        private string _settlementDishMultiplierItemName = string.Empty;
        private float _randomServeMultiplierMin;
        private float _randomServeMultiplierMax;
        private float _randomServeMultiplierStep;

        public BattleSession(
            GpTable board,
            GameplayDatabase db,
            IRandomStream rng,
            IEnumerable<RecipeSlot> slots,
            int requiredScore,
            ScoreCalculator calculator = null,
            IReadOnlyDictionary<string, int> runSettledCounts = null)
        {
            DiningTable = board ?? throw new ArgumentNullException(nameof(board));
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

        public GpTable DiningTable { get; }

        public GameplayDatabase Database => _db;

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
            SetHappyCakeLayers(layers);
        }

        /// <summary>本次美食品鉴结束后清空蛋糕层数。</summary>
        public void ClearHappyCakeLayers()
        {
            SetHappyCakeLayers(0);
        }

        /// <summary>本局允许的最大上菜次数（-1 表示不限；Boss 机制「限量供应」会设上限）。</summary>
        public int MaxServes { get; set; } = -1;

        public int GoldCostPerServe { get; set; }

        public bool AutoServeSecondDish { get; set; }

        public bool RemoveFirstServedDishes { get; set; }

        public int FirstServedDishesToRemove { get; set; }

        public bool AlternateServeMultiplier { get; set; }

        public bool RandomServeMultiplier { get; set; }

        public void ConfigureRandomServeMultiplier(float min, float max, float step)
        {
            if (max < min)
            {
                (min, max) = (max, min);
            }

            _randomServeMultiplierMin = min;
            _randomServeMultiplierMax = max;
            _randomServeMultiplierStep = step > 0f ? step : 0f;
        }

        public bool ReverseSettlementOrder { get; set; }

        public int MinimumServesForScore { get; set; }

        public bool HalveBaseScore { get; set; }

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

        /// <summary>本局待发放的主动道具数量（银材质结算掷骰命中累积；由 Game 层在结算后发放）。</summary>
        public int PendingActiveItemGrants { get; private set; }

        /// <summary>本次结算各 BaseId 的结算增量（供 Game 层累加进 GameRun 大局历史）。</summary>
        public IReadOnlyDictionary<string, int> LastSettledIncrements { get; private set; } = new Dictionary<string, int>();

        public IReadOnlyList<RecipeScoreFlatDelta> LastRecipeScoreFlatDeltas => _lastRecipeScoreFlatDeltas;

        public IReadOnlyList<RecipeScoreMultiplierDelta> LastRecipeScoreMultiplierDeltas => _lastRecipeScoreMultiplierDeltas;

        /// <summary>本次品鉴菜谱内容（BaseId 列表，供菜谱检测）。</summary>
        public IReadOnlyList<string> RecipeBaseIds => _recipeBaseIds;

        /// <summary>一次甜蜜传递请求成功落到至少一个目标后触发。</summary>
        public event Action<SkillTransferRequest> SweetTransferTriggered;

        public event Action<DishInstance, int> Served;

        /// <summary>欢乐蛋糕层数变化（旧值, 新值），供表现层驱动 HUD 与场景蛋糕演出。</summary>
        public event Action<int, int> HappyCakeLayersChanged;

        public event Action<DishInstance, float> ServeMultiplierFlatApplied;

        public float SweetTransferTargetMultiplier { get; set; } = 1f;

        public float SweetTransferSourceMultiplier { get; set; } = 1f;

        public void AddPendingGold(float amount)
        {
            PendingGold += amount;
        }

        public void AddServeMultiplierFlat(DishInstance dish, float value)
        {
            if (dish == null || Math.Abs(value) < 0.0001f)
            {
                return;
            }

            dish.AddServeMultiplierFlat(value);
            ServeMultiplierFlatApplied?.Invoke(dish, value);
        }

        public void ApplySettlementDishMultiplierFlat(float value, string itemId, string itemName)
        {
            if (value <= 0f)
            {
                return;
            }

            _settlementDishMultiplierFlat += value;
            _settlementDishMultiplierItemId = string.IsNullOrEmpty(_settlementDishMultiplierItemId)
                ? itemId ?? string.Empty
                : _settlementDishMultiplierItemId;
            _settlementDishMultiplierItemName = string.IsNullOrEmpty(_settlementDishMultiplierItemName)
                ? itemName ?? itemId ?? string.Empty
                : _settlementDishMultiplierItemName;
        }

        /// <summary>从指定菜谱槽随机上一道能放下的菜，并随机朝向/位置摆上餐桌。</summary>
        public ServeResult Serve(int slotIndex)
        {
            return ServeInternal(slotIndex, allowAutoServe: true);
        }

        /// <summary>
        /// 当前餐桌状态下，指定菜谱条目是否至少存在一个合法上菜位置。
        /// 与真正上菜共用同一套风味旋转/回退规则，供 HUD 实时展示可放置状态。
        /// </summary>
        public bool CanFitRecipeEntry(int slotIndex, int entryIndex)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count)
            {
                return false;
            }

            RecipeSlot slot = _slots[slotIndex];
            if (entryIndex < 0 || entryIndex >= slot.Entries.Count)
            {
                return false;
            }

            RecipeSlotEntry entry = slot.Entries[entryIndex];
            DishDef dish = _db.GetDish(entry.DishId);
            return dish != null && FindServePlacements(dish, entry).Count > 0;
        }

        private ServeResult ServeInternal(int slotIndex, bool allowAutoServe)
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
            for (int i = 0; i < slot.Entries.Count; i++)
            {
                DishDef dish = _db.GetDish(slot.Entries[i].DishId);
                if (dish == null)
                {
                    continue;
                }

                List<Placement> placements = FindServePlacements(dish, slot.Entries[i]);

                if (placements.Count > 0)
                {
                    candidates.Add(new ServeCandidate(i, dish, placements));
                }
            }

            if (candidates.Count == 0)
            {
                return ServeResult.Fail(ServeOutcome.NoFittingDish);
            }

            ServeCandidate chosen = candidates[_rng.Range(0, candidates.Count)];
            RecipeSlotEntry entry = slot.RemoveEntryAt(chosen.SlotEntryIndex);
            Placement placement = chosen.Placements[_rng.Range(0, chosen.Placements.Count)];
            List<string> skills = ComposeServeSkills(chosen.Dish, entry);
            List<string> flavors = ComposeServeFlavors(chosen.Dish, entry);
            var instance = new DishInstance(_nextInstanceId++, chosen.Dish, placement, skills, flavors);
            instance.SetSourceRecipeIndex(slotIndex, entry.SourceDishIndex);
            ApplyEntryFlags(instance, entry);
            ApplyServeModifiers(instance);
            DiningTable.Place(instance);
            ServesUsed++;
            Served?.Invoke(instance, ServesUsed);

            // 上菜时（OnServe）规则：直接改运行时状态（技能）并积累金币/全局层数。
            if (!instance.SkillsDisabled)
            {
                ServeRuleResolver.ServeResolveResult serveResult =
                    ServeRuleResolver.ResolveOnServe(DiningTable, _db, BuildHistory(), instance, HappyCakeLayers);
                PendingGold += serveResult.Gold;
                SetHappyCakeLayers(HappyCakeLayers + serveResult.HappyCakeLayerDelta + AccelFor(serveResult.HappyCakeLayerDelta));
                ApplyTransferRequests(serveResult.TransferRequests);
                ApplyCopySkillRequests(serveResult.CopySkillRequests);
            }

            if (GoldCostPerServe > 0)
            {
                PendingGold -= GoldCostPerServe;
            }

            bool removedAfterServe = false;
            if (RemoveFirstServedDishes && _appetizerRemoved < FirstServedDishesToRemove)
            {
                _appetizerRemoved++;
                DiningTable.RemoveDish(instance);
                removedAfterServe = true;
            }

            if (allowAutoServe && AutoServeSecondDish)
            {
                ServeInternal(slotIndex, allowAutoServe: false);
            }

            return new ServeResult(ServeOutcome.Placed, instance, removedAfterServe);
        }

        private List<Placement> FindServePlacements(DishDef dish, RecipeSlotEntry entry)
        {
            // 麻：菜谱里带「麻」风味的菜在上菜前即按逆时针 n×90° 旋转，用旋转后的形状随机放置；放不下则回退不旋转。
            int numbSteps = NumbStepsFor(ComposeServeFlavors(dish, entry));
            List<Placement> placements = numbSteps > 0
                ? DiningTable.FindValidPlacementsRotatedCcw(dish, numbSteps)
                : DiningTable.FindValidPlacements(dish);
            if (placements.Count == 0 && numbSteps > 0)
            {
                placements = DiningTable.FindValidPlacements(dish);
            }

            return placements;
        }

        /// <summary>计算当前餐桌的预览分数（不标记结算，不产生副作用），供 UI 实时展示。</summary>
        public ScoreResult PreviewScore()
        {
            if (MinimumServesForScore > 0 && ServesUsed < MinimumServesForScore)
            {
                return ZeroScoreResult();
            }

            return _calculator.Calculate(DiningTable, _db, FinalFlat, FinalMultiplier, extraSources: BuildSettlementExtraSources(), history: BuildHistory(), initialHappyCakeLayers: HappyCakeLayers, extraCountAsPerDish: ExtraCountAsPerDish, cakeLayerThresholdReduction: CakeLayerThresholdReduction, reverseDishOrder: ReverseSettlementOrder, unservedRecipeDishes: BuildUnservedRecipeDishes());
        }

        /// <summary>「吃」：结算、应用副作用（金币/层数/技能传递/历史）并记录结果。</summary>
        public ScoreResult Settle()
        {
            ScoreResult result = MinimumServesForScore > 0 && ServesUsed < MinimumServesForScore
                ? ZeroScoreResult()
                : _calculator.Calculate(DiningTable, _db, FinalFlat, FinalMultiplier, extraSources: BuildSettlementExtraSources(), history: BuildHistory(), initialHappyCakeLayers: HappyCakeLayers, extraCountAsPerDish: ExtraCountAsPerDish, cakeLayerThresholdReduction: CakeLayerThresholdReduction, reverseDishOrder: ReverseSettlementOrder, unservedRecipeDishes: BuildUnservedRecipeDishes(), copySkillSelector: SelectCopySkills, transferTargetSelector: SelectTransferTargets);
            ApplySideEffects(result);
            LastResult = result;
            IsSettled = true;
            return LastResult;
        }

        private static ScoreResult ZeroScoreResult()
        {
            return new ScoreResult(Array.Empty<DishScore>(), 0f, 0f, 1f);
        }

        private IReadOnlyList<int> SelectTransferTargets(IReadOnlyList<int> candidates, int count)
        {
            if (candidates == null || candidates.Count == 0 || count <= 0)
            {
                return Array.Empty<int>();
            }

            var targets = new List<int>(candidates);
            if (targets.Count > count)
            {
                _rng.Shuffle(targets);
                targets = targets.GetRange(0, count);
            }

            return targets;
        }

        private List<string> ComposeServeSkills(DishDef dish, RecipeSlotEntry entry)
        {
            if (entry == null || entry.ExtraSkillIds.Count == 0)
            {
                return TagComposer.ComposeSkills(dish.SkillIds);
            }

            var ids = new List<string>(dish.SkillIds.Count + entry.ExtraSkillIds.Count);
            ids.AddRange(dish.SkillIds);
            ids.AddRange(entry.ExtraSkillIds);
            return TagComposer.ComposeSkills(ids);
        }

        /// <summary>统计一组风味里「麻」(Rotate) 的逆时针旋转步数（各麻风味 effectValue 之和）。</summary>
        /// <summary>
        /// 合成上菜风味：菜谱变体自带风味 + 玩家用「调味小票」永久附加的额外风味。
        /// 有额外风味时解除单槽上限以支持叠加（如甜×n）；无额外风味时沿用单槽语义（后者覆盖）。
        /// </summary>
        private List<string> ComposeServeFlavors(DishDef dish, RecipeSlotEntry entry)
        {
            if (entry == null || entry.ExtraFlavorIds.Count == 0)
            {
                return TagComposer.ComposeFlavors(new[] { dish.FlavorId });
            }

            var ids = new List<string>(1 + entry.ExtraFlavorIds.Count) { dish.FlavorId };
            ids.AddRange(entry.ExtraFlavorIds);
            return TagComposer.ComposeFlavors(ids, removeFlavorCap: true);
        }

        private int NumbStepsFor(IReadOnlyList<string> flavorIds)
        {
            if (flavorIds == null)
            {
                return 0;
            }

            int steps = 0;
            foreach (string flavorId in flavorIds)
            {
                Gameplay.Model.FlavorDef flavor = _db.GetFlavor(flavorId);
                if (flavor != null && flavor.EffectType == Gameplay.Model.FlavorEffectType.Rotate)
                {
                    steps += (int)flavor.EffectValue;
                }
            }

            return steps;
        }

        private void ApplyEntryFlags(DishInstance instance, RecipeSlotEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (entry.DisableSkills)
            {
                instance.DisableSkills();
            }

            if (entry.ExcludeFromScore)
            {
                instance.ExcludeFromScore();
            }

            if (entry.ScoreMultiplier > 0f && Math.Abs(entry.ScoreMultiplier - 1f) > 0.0001f)
            {
                instance.MultiplyPermanentMult(entry.ScoreMultiplier);
            }

            if (Math.Abs(entry.ScoreFlatBonus) > 0.0001f)
            {
                instance.AddPermanentFlat(entry.ScoreFlatBonus);
            }
        }

        private void ApplyServeModifiers(DishInstance instance)
        {
            if (instance == null)
            {
                return;
            }

            if (HalveBaseScore)
            {
                instance.MultiplyTemporaryBase(0.5f);
            }

            if (AlternateServeMultiplier)
            {
                instance.MultiplyServeMultiplier(ServesUsed % 2 == 0 ? 0.5f : 1.5f);
            }
            else if (RandomServeMultiplier)
            {
                if (_randomServeMultiplierStep <= 0f)
                {
                    return;
                }

                float span = Math.Max(0f, _randomServeMultiplierMax - _randomServeMultiplierMin);
                int stepCount = Math.Max(0, (int)Math.Round(span / _randomServeMultiplierStep, MidpointRounding.AwayFromZero));
                int stepIndex = stepCount > 0 ? _rng.Range(0, stepCount + 1) : 0;
                float multiplier = Math.Min(_randomServeMultiplierMax, _randomServeMultiplierMin + stepIndex * _randomServeMultiplierStep);
                instance.MultiplyServeMultiplier(multiplier);
            }
        }

        /// <summary>把历史/菜谱打包为只读快照注入结算（读取本次结算之前的状态）。</summary>
        private IScoreHistory BuildHistory()
        {
            return new ScoreHistory(
                new Dictionary<string, int>(_runSettled),
                new Dictionary<string, int>(_mealSettled),
                _recipeBaseIds);
        }

        /// <summary>收集当前仍未上菜的菜谱条目（槽索引 + dishId），供酸/咸在整体结算末尾遍历。</summary>
        private List<UnservedRecipeDish> BuildUnservedRecipeDishes()
        {
            var result = new List<UnservedRecipeDish>();
            for (int slotIndex = 0; slotIndex < _slots.Count; slotIndex++)
            {
                foreach (RecipeSlotEntry entry in _slots[slotIndex].Entries)
                {
                    result.Add(new UnservedRecipeDish(slotIndex, entry.DishId));
                }
            }

            return result;
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
            _lastRecipeScoreFlatDeltas.Clear();
            _lastRecipeScoreMultiplierDeltas.Clear();

            // 金币入账（结算侧效果）。
            PendingGold += result.GoldDelta;

            // 银材质：对每个「1/3 获得道具」请求掷骰（仅正式结算掷，预览不掷，保证可复现纯净）。
            for (int i = 0; i < result.SilverItemRollRequests; i++)
            {
                if (_rng.NextBool(1.0 / 3.0))
                {
                    PendingActiveItemGrants++;
                }
            }

            // 全局欢乐蛋糕层数：写回品鉴级计数器（层数净增时叠加道具加速）。
            SetHappyCakeLayers(HappyCakeLayers + result.HappyCakeLayerDelta + AccelFor(result.HappyCakeLayerDelta));

            // 技能传递。
            var transferSourcesMultiplied = new HashSet<int>();
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
                    inst.AddTransferredSkill(effect, label, transfer.SourceInstanceId);
                }

                ApplySweetTransferTargetMultiplier(inst);
                if (transfer.SourceInstanceId > 0 && transferSourcesMultiplied.Add(transfer.SourceInstanceId))
                {
                    ApplySweetTransferSourceMultiplier(FindInstance(transfer.SourceInstanceId));
                }
            }

            // 技能复制：结算阶段只登记候选池，正式结算后由会话随机流落地，避免预览消耗 RNG。
            ApplyCopySkillRequests(result.CopySkillRequests);

            // 永久分 / 永久乘区 / 视为食物数：写回实例（品鉴内跨结算持久）。
            foreach (KeyValuePair<int, float> kv in result.PermanentFlatDeltas)
            {
                DishInstance inst = FindInstance(kv.Key);
                if (inst == null)
                {
                    continue;
                }

                inst.AddPermanentFlat(kv.Value);
                if (inst.SourceSlotIndex >= 0 && inst.SourceDishIndex >= 0)
                {
                    _lastRecipeScoreFlatDeltas.Add(new RecipeScoreFlatDelta(
                        inst.SourceSlotIndex,
                        inst.SourceDishIndex,
                        kv.Value));
                }
            }

            foreach (KeyValuePair<int, float> kv in result.PermanentMultDeltas)
            {
                DishInstance inst = FindInstance(kv.Key);
                if (inst == null)
                {
                    continue;
                }

                inst.MultiplyPermanentMult(kv.Value);
                if (inst.SourceSlotIndex >= 0 && inst.SourceDishIndex >= 0)
                {
                    _lastRecipeScoreMultiplierDeltas.Add(new RecipeScoreMultiplierDelta(
                        inst.SourceSlotIndex,
                        inst.SourceDishIndex,
                        kv.Value));
                }
            }

            // 历史累计：本次结算把盘面每道菜的 BaseId 计入大局/小局。
            var increments = new Dictionary<string, int>();
            foreach (DishInstance dish in DiningTable.Dishes)
            {
                if (dish.ExcludedFromScore)
                {
                    continue;
                }

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
                bool transferred = false;
                bool sourceMultiplied = false;
                foreach (int targetId in targets)
                {
                    DishInstance target = FindInstance(targetId);
                    if (target == null || target.Id == request.SourceInstanceId)
                    {
                        continue;
                    }

                    foreach (SkillEffect effect in request.Effects)
                    {
                        target.AddTransferredSkill(effect, sourceLabel, request.SourceInstanceId);
                    }

                    ApplySweetTransferTargetMultiplier(target);
                    if (!sourceMultiplied)
                    {
                        ApplySweetTransferSourceMultiplier(FindInstance(request.SourceInstanceId));
                        sourceMultiplied = true;
                    }
                    transferred = true;
                }

                if (transferred)
                {
                    SweetTransferTriggered?.Invoke(request);
                }
            }
        }

        private IReadOnlyList<IScoreEffectSource> BuildSettlementExtraSources()
        {
            if (_settlementDishMultiplierFlat <= 0f)
            {
                return Array.Empty<IScoreEffectSource>();
            }

            return new IScoreEffectSource[]
            {
                new AllDishMultiplierFlatSource(
                    _settlementDishMultiplierFlat,
                    _settlementDishMultiplierItemId,
                    _settlementDishMultiplierItemName),
            };
        }

        private void ApplySweetTransferTargetMultiplier(DishInstance target)
        {
            if (target != null && SweetTransferTargetMultiplier > 0f && Math.Abs(SweetTransferTargetMultiplier - 1f) > 0.0001f)
            {
                target.MultiplyPermanentMult(SweetTransferTargetMultiplier);
            }
        }

        private void ApplySweetTransferSourceMultiplier(DishInstance source)
        {
            if (source != null && SweetTransferSourceMultiplier > 0f && Math.Abs(SweetTransferSourceMultiplier - 1f) > 0.0001f)
            {
                source.MultiplyPermanentMult(SweetTransferSourceMultiplier);
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

                IReadOnlyList<string> selected = request.SelectedSkillIds.Count > 0
                    ? request.SelectedSkillIds
                    : SelectCopySkills(request.Candidates, request.Count);
                string label = string.IsNullOrEmpty(request.SourceName) ? null : $"{request.SourceName}<技能复制>";
                for (int i = 0; i < selected.Count; i++)
                {
                    target.AddSkill(selected[i], label);
                }
            }
        }

        private IReadOnlyList<string> SelectCopySkills(IReadOnlyList<string> candidates, int count)
        {
            if (candidates == null || candidates.Count == 0 || count <= 0)
            {
                return Array.Empty<string>();
            }

            var pool = new List<string>(candidates);
            _rng.Shuffle(pool);
            int take = Math.Min(count, pool.Count);
            var selected = new List<string>(take);
            for (int i = 0; i < take; i++)
            {
                selected.Add(pool[i]);
            }

            return selected;
        }

        /// <summary>清理本次品鉴产生的临时克隆实例（品鉴结束时调用）。</summary>
        public void ClearTemporaryDishes()
        {
            var temporaries = new List<DishInstance>();
            foreach (DishInstance dish in DiningTable.Dishes)
            {
                if (dish.IsTemporary)
                {
                    temporaries.Add(dish);
                }
            }

            foreach (DishInstance dish in temporaries)
            {
                DiningTable.RemoveDish(dish);
            }
        }

        /// <summary>层数净增（delta&gt;0）时返回额外加速层数，否则 0（道具「蛋糕膨胀」）。</summary>
        private int AccelFor(int delta)
        {
            return delta > 0 ? CakeLayerAccelBonus : 0;
        }

        private void SetHappyCakeLayers(int layers)
        {
            int before = HappyCakeLayers;
            HappyCakeLayers = Math.Max(0, layers);
            if (before != HappyCakeLayers)
            {
                HappyCakeLayersChanged?.Invoke(before, HappyCakeLayers);
            }
        }

        private DishInstance FindInstance(int id)
        {
            foreach (DishInstance dish in DiningTable.Dishes)
            {
                if (dish.Id == id)
                {
                    return dish;
                }
            }

            return null;
        }

        /// <summary>读档恢复待领奖界面时，把会话标记为已结算的只读 UI 状态；不触发任何结算副作用。</summary>
        public void RestoreSettledForRewardView(int total)
        {
            LastResult = new ScoreResult(Array.Empty<DishScore>(), Math.Max(0, total), 0f, 1f);
            IsSettled = true;
        }

        public bool IsWin => IsSettled && LastResult != null && LastResult.Total >= RequiredScore;

        /// <summary>清空餐桌（主动道具「重摆铃」）。已结算后不允许。</summary>
        public void ClearBoard()
        {
            if (IsSettled)
            {
                return;
            }

            DiningTable.Clear();
        }

        /// <summary>按 Id 查找餐桌上的菜；不存在返回 null。</summary>
        public DishInstance FindDishById(int dishId)
        {
            foreach (DishInstance dish in DiningTable.Dishes)
            {
                if (dish.Id == dishId)
                {
                    return dish;
                }
            }

            return null;
        }

        /// <summary>主动道具：给指定餐桌菜永久加分（对标杀戮尖塔2 火焰药水打目标）。成功返回 true。</summary>
        public bool AddPermanentScoreToDish(int dishId, float amount)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            if (dish == null)
            {
                return false;
            }

            dish.AddPermanentFlat(amount);
            return true;
        }

        /// <summary>主动道具：给指定餐桌菜永久乘区加成。成功返回 true。</summary>
        public bool MultiplyScoreOnDish(int dishId, float multiplier)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            if (dish == null)
            {
                return false;
            }

            dish.MultiplyPermanentMult(multiplier);
            return true;
        }

        /// <summary>主动道具：给指定餐桌菜加「视为食物数」。成功返回 true。</summary>
        public bool AddCountAsToDish(int dishId, int amount)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            if (dish == null)
            {
                return false;
            }

            dish.AddCountAsBonus(amount);
            return true;
        }

        public bool AddFlavorToDishById(int dishId, string flavorId)
        {
            if (IsSettled || string.IsNullOrEmpty(flavorId))
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            if (dish == null)
            {
                return false;
            }

            dish.AddFlavor(flavorId);
            return true;
        }

        public bool RemoveFlavorFromDishById(int dishId, string flavorId)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            return dish != null && dish.RemoveFlavor(flavorId);
        }

        public bool ReplaceFlavorOnDishById(int dishId, string toFlavorId)
        {
            if (IsSettled || string.IsNullOrEmpty(toFlavorId))
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            return dish != null && dish.ReplaceFlavor(toFlavorId);
        }

        /// <summary>主动道具：移除指定餐桌菜（对标破坏族）。成功返回 true。</summary>
        public bool DestroyDishById(int dishId)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance dish = FindDishById(dishId);
            if (dish == null)
            {
                return false;
            }

            DiningTable.RemoveDish(dish);
            return true;
        }

        /// <summary>
        /// 主动道具：复制指定餐桌菜到空位（对标增殖族）。摆放位置由战斗随机流选取，
        /// 复制产物为常驻实例。空位不足返回 false。
        /// </summary>
        public bool DuplicateDishById(int dishId)
        {
            if (IsSettled)
            {
                return false;
            }

            DishInstance source = FindDishById(dishId);
            if (source == null)
            {
                return false;
            }

            List<Placement> placements = DiningTable.FindValidPlacements(source.Def);
            if (placements.Count == 0)
            {
                return false;
            }

            Placement placement = placements[_rng.Range(0, placements.Count)];
            var clone = new DishInstance(_nextInstanceId++, source.Def, placement, source.SkillIds, source.FlavorIds);
            clone.SetSourceRecipeIndex(source.SourceSlotIndex, source.SourceDishIndex);
            clone.CopySkillSourcesFrom(source);
            clone.CopyTransferredSkillsFrom(source);
            DiningTable.Place(clone);
            return true;
        }

        public bool GenerateDishAt(string dishId, GridPos origin)
        {
            if (IsSettled || string.IsNullOrEmpty(dishId))
            {
                return false;
            }

            DishDef def = _db.GetDish(dishId);
            if (def == null)
            {
                return false;
            }

            IReadOnlyList<DishShape> orientations = def.Shape.GetOrientations(def.AllowRotate);
            for (int i = 0; i < orientations.Count; i++)
            {
                DishShape shape = orientations[i];
                var placement = new Placement(shape, i, origin);
                if (!DiningTable.CanPlace(shape, origin))
                {
                    continue;
                }

                IReadOnlyList<string> flavors = string.IsNullOrEmpty(def.FlavorId)
                    ? Array.Empty<string>()
                    : new[] { def.FlavorId };
                var instance = new DishInstance(_nextInstanceId++, def, placement, def.SkillIds, flavors);
                DiningTable.Place(instance);
                return true;
            }

            return false;
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
                    if (def != null && DiningTable.CanFit(def))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private sealed class AllDishMultiplierFlatSource : IScoreEffectSource
        {
            private readonly float _value;
            private readonly string _itemId;
            private readonly string _itemName;

            public AllDishMultiplierFlatSource(float value, string itemId, string itemName)
            {
                _value = value;
                _itemId = itemId ?? string.Empty;
                _itemName = itemName ?? _itemId;
            }

            public void CollectEffects(ScoreSnapshot snapshot, ScoreEffectCollector collector)
            {
                if (_value <= 0f)
                {
                    return;
                }

                collector.Add(new ScoreEffectEntry(
                    ScorePhase.AfterAllDishes,
                    ScoreSource.Relic(_itemId, _itemName),
                    new AllDishMultiplierFlatEffect(_value)));
            }
        }

        private sealed class AllDishMultiplierFlatEffect : IScoreEffect
        {
            private readonly float _value;

            public AllDishMultiplierFlatEffect(float value)
            {
                _value = value;
            }

            public void Apply(ScoreContext context)
            {
                foreach (DishInstance dish in context.DiningTable.Dishes)
                {
                    context.AddMultFlatTo(dish, _value);
                }
            }
        }

        private readonly struct ServeCandidate
        {
            public ServeCandidate(int slotEntryIndex, DishDef dish, IReadOnlyList<Placement> placements)
            {
                SlotEntryIndex = slotEntryIndex;
                Dish = dish;
                Placements = placements;
            }

            public int SlotEntryIndex { get; }

            public DishDef Dish { get; }

            public IReadOnlyList<Placement> Placements { get; }
        }
    }
}
