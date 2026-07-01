using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>
    /// 结算过程中的可变上下文与累加器。
    /// 每道菜有独立累加器（加法区/乘区/额外结算次数），支持跨菜改分；
    /// 所有菜在全部阶段跑完后统一定稿（deferred finalization），因此技能可以改到别的菜。
    /// 层数/金币/技能传递等对外副作用只累积，不在结算中直接改实例（保证预览安全、纯计算）。
    /// </summary>
    public class ScoreContext
    {
        private const int MaxCommandsPerCalculation = 2048;

        private sealed class DishAccumulator
        {
            public DishInstance Dish;
            public float Base;
            public float Flat;
            public float Mult = 1f;
            public int ExtraTimes;
        }

        private readonly Dictionary<int, DishAccumulator> _accums = new Dictionary<int, DishAccumulator>();
        private readonly List<int> _order = new List<int>();
        private readonly List<DishScore> _dishScores = new List<DishScore>();
        private readonly List<ScoreLine> _lines = new List<ScoreLine>();
        private readonly List<ScoreEvent> _events = new List<ScoreEvent>();
        private readonly Queue<PendingScoreCommand> _commands = new Queue<PendingScoreCommand>();
        private readonly Dictionary<int, int> _layerDeltas = new Dictionary<int, int>();
        private readonly List<SkillTransferSideEffect> _skillTransfers = new List<SkillTransferSideEffect>();
        private DishAccumulator _current;
        private bool _initialFinalModifiersRecorded;
        private bool _finalized;
        private bool _isResolvingCommands;
        private int _commandsExecuted;

        public ScoreContext(ScoreSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Board = snapshot.Board;
            Db = snapshot.Db;
            FinalFlat = snapshot.InitialFinalFlat;
            FinalMultiplier = snapshot.InitialFinalMultiplier;

            // 预建全部菜的累加器，保证「A 改 B 的分」无论 B 是否已开始都有效。
            foreach (DishInstance dish in snapshot.DishesInDefaultOrder)
            {
                EnsureAccumulator(dish);
            }
        }

        public ScoreSnapshot Snapshot { get; }

        public GpBoard Board { get; }

        public GameplayDatabase Db { get; }

        public DishInstance Dish { get; private set; }

        public GridPos? CurrentCell { get; private set; }

        public ScoreSource Source { get; private set; }

        public ScorePhase Phase { get; private set; }

        /// <summary>当前正在结算的技能/风味/格子标签效果；非效果来源时为空。</summary>
        public IEffectDef Tag { get; set; }

        public float FlatBonus => _current?.Flat ?? 0f;

        public float Multiplier => _current?.Mult ?? 1f;

        public float RawSum { get; private set; }

        public float FinalFlat { get; private set; }

        public float FinalMultiplier { get; private set; } = 1f;

        /// <summary>本次结算产生的金币增量（副作用，由 Game 层在正式结算后入账）。</summary>
        public float GoldDelta { get; private set; }

        public IReadOnlyList<DishScore> DishScores => _dishScores;

        public IReadOnlyList<ScoreLine> Lines => _lines;

        public IReadOnlyList<ScoreEvent> Events => _events;

        public IReadOnlyDictionary<int, int> LayerDeltas => _layerDeltas;

        public IReadOnlyList<SkillTransferSideEffect> SkillTransfers => _skillTransfers;

        public void EmitEvent(ScoreEventType type, string message)
        {
            int dishInstanceId = Dish != null ? Dish.Id : 0;
            string dishId = Dish != null ? Dish.Def.Id : string.Empty;
            _events.Add(new ScoreEvent(type, Phase, Source, dishInstanceId, dishId, CurrentCell, message));
        }

        public void BeginDish(DishInstance dish)
        {
            Dish = dish ?? throw new ArgumentNullException(nameof(dish));
            _current = EnsureAccumulator(dish);
            CurrentCell = null;
            Source = ScoreSource.Dish(dish);
            Phase = ScorePhase.BeforeDish;
            Tag = null;
            EmitEvent(ScoreEventType.DishStarted, $"开始结算 {dish.Def.Name}");
        }

        public void RecordDishBase()
        {
            if (Dish == null)
            {
                return;
            }

            Phase = ScorePhase.DishBase;
            Source = ScoreSource.Dish(Dish);
            _lines.Add(new ScoreLine(
                Phase,
                ScoreLineKind.DishBase,
                Source,
                Dish.Id,
                Dish.Def.Id,
                null,
                Dish.Def.Deliciousness,
                0f,
                Dish.Def.Deliciousness,
                $"{Dish.Def.Name} 基础美味度 {Dish.Def.Deliciousness}"));
        }

        public void Apply(ScoreEffectEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            ScorePhase previousPhase = Phase;
            ScoreSource previousSource = Source;
            GridPos? previousCell = CurrentCell;
            IEffectDef previousTag = Tag;
            Phase = entry.Phase;
            Source = entry.Source;
            CurrentCell = entry.Cell;
            Tag = entry.Tag;
            string effectName = Source.Name;
            EmitEvent(ScoreEventType.EffectStarted, $"开始效果 {effectName}");
            try
            {
                entry.Effect.Apply(this);
                ResolveCommandQueue();
                EmitEvent(ScoreEventType.EffectFinished, $"结束效果 {effectName}");
            }
            finally
            {
                Phase = previousPhase;
                Source = previousSource;
                CurrentCell = previousCell;
                Tag = previousTag;
            }
        }

        // ------- 兼容旧 API：作用于「当前菜」 -------

        public void AddFlat(float value)
        {
            int id = Dish != null ? Dish.Id : 0;
            SubmitCommand(new AddDishFlatCommand(id, value));
        }

        public void MultiplyBy(float value)
        {
            int id = Dish != null ? Dish.Id : 0;
            SubmitCommand(new MultiplyDishCommand(id, value));
        }

        // ------- 跨菜 / 副作用 API -------

        public void AddFlatTo(DishInstance target, float value)
        {
            if (target == null)
            {
                return;
            }

            SubmitCommand(new AddDishFlatCommand(target.Id, value));
        }

        public void MultiplyTo(DishInstance target, float value)
        {
            if (target == null)
            {
                return;
            }

            SubmitCommand(new MultiplyDishCommand(target.Id, value));
        }

        /// <summary>把来源菜「加法区分数（含基础）」的 fraction 比例转移给目标菜（作用于加法层）。</summary>
        public void TransferScore(DishInstance from, DishInstance to, float fraction)
        {
            if (from == null || to == null || from.Id == to.Id)
            {
                return;
            }

            SubmitCommand(new TransferScoreCommand(from.Id, to.Id, fraction));
        }

        /// <summary>目标菜额外结算 times 次（贡献额外计入总分）。</summary>
        public void AddExtraSettlement(DishInstance target, int times)
        {
            if (target == null || times <= 0)
            {
                return;
            }

            SubmitCommand(new ExtraSettlementCommand(target.Id, times));
        }

        public void GrantGold(float value)
        {
            SubmitCommand(new GrantGoldCommand(value));
        }

        public void AddFinalFlat(float value)
        {
            SubmitCommand(new AddFinalFlatCommand(value));
        }

        public void MultiplyFinalBy(float value)
        {
            SubmitCommand(new MultiplyFinalCommand(value));
        }

        /// <summary>层数改动（副作用，正式结算后应用到实例）。mult=true 时按乘法。</summary>
        public void ChangeLayers(DishInstance target, float value, bool mult)
        {
            if (target == null)
            {
                return;
            }

            SubmitCommand(new ChangeLayerCommand(target.Id, value, mult));
        }

        /// <summary>登记技能传递（副作用，正式结算后应用到实例的运行时技能集）。</summary>
        public void RecordSkillTransfer(DishInstance target, IReadOnlyList<string> skillIds)
        {
            if (target == null || skillIds == null || skillIds.Count == 0)
            {
                return;
            }

            _skillTransfers.Add(new SkillTransferSideEffect(target.Id, skillIds));
            EmitEvent(ScoreEventType.CommandExecuted, $"技能传递给 {target.Def.Name}（{skillIds.Count} 个）");
        }

        public void SubmitCommand(IScoreCommand command)
        {
            if (command == null)
            {
                return;
            }

            _commands.Enqueue(new PendingScoreCommand(command, Phase, Source, CurrentCell, Tag));
            ResolveCommandQueue();
        }

        /// <summary>逐菜阶段结束（仅发事件；分数定稿延迟到 FinalizeDishes）。</summary>
        public void CompleteDish()
        {
            if (Dish == null)
            {
                return;
            }

            EmitEvent(ScoreEventType.DishCompleted, $"{Dish.Def.Name} 阶段结束");
        }

        /// <summary>所有逐菜阶段跑完后，统一把每道菜的累加器定稿为贡献并求和。</summary>
        public void FinalizeDishes()
        {
            if (_finalized)
            {
                return;
            }

            _finalized = true;
            RawSum = 0f;
            foreach (int id in _order)
            {
                DishAccumulator a = _accums[id];
                var score = new DishScore(a.Dish.Id, a.Dish.Def.Id, a.Base, a.Flat, a.Mult);
                _dishScores.Add(score);
                RawSum += score.Contribution * (1 + a.ExtraTimes);
            }
        }

        public void RecordInitialFinalModifiers()
        {
            if (_initialFinalModifiersRecorded)
            {
                return;
            }

            _initialFinalModifiersRecorded = true;
            var source = ScoreSource.FinalModifier("initial_final_modifier", "局级修正");
            if (Math.Abs(Snapshot.InitialFinalFlat) > 0.0001f)
            {
                _lines.Add(new ScoreLine(
                    ScorePhase.Final,
                    ScoreLineKind.FinalFlat,
                    source,
                    0,
                    string.Empty,
                    null,
                    Snapshot.InitialFinalFlat,
                    0f,
                    FinalFlat,
                    $"局级加法 +{Snapshot.InitialFinalFlat}"));
            }

            if (Math.Abs(Snapshot.InitialFinalMultiplier - 1f) > 0.0001f)
            {
                _lines.Add(new ScoreLine(
                    ScorePhase.Final,
                    ScoreLineKind.FinalMultiplier,
                    source,
                    0,
                    string.Empty,
                    null,
                    Snapshot.InitialFinalMultiplier,
                    1f,
                    FinalMultiplier,
                    $"局级乘区 x{Snapshot.InitialFinalMultiplier}"));
            }
        }

        public ScoreResult ToResult()
        {
            return new ScoreResult(_dishScores, RawSum, FinalFlat, FinalMultiplier, _lines, _events, GoldDelta, _layerDeltas, _skillTransfers);
        }

        // ------- 命令实际改分（internal，供命令调用） -------

        internal void ApplyDishFlatCommand(int dishId, float value)
        {
            if (!_accums.TryGetValue(dishId, out DishAccumulator a))
            {
                return;
            }

            float before = a.Flat;
            a.Flat += value;
            AddLine(a, ScoreLineKind.DishFlat, value, before, a.Flat, $"美味度 +{value}");
        }

        internal void ApplyDishMultiplierCommand(int dishId, float value)
        {
            if (!_accums.TryGetValue(dishId, out DishAccumulator a))
            {
                return;
            }

            float before = a.Mult;
            a.Mult *= value;
            AddLine(a, ScoreLineKind.DishMultiplier, value, before, a.Mult, $"乘区 x{value}");
        }

        internal void ApplyTransferScoreCommand(int fromId, int toId, float fraction)
        {
            if (!_accums.TryGetValue(fromId, out DishAccumulator from) || !_accums.TryGetValue(toId, out DishAccumulator to))
            {
                return;
            }

            float amount = (from.Base + from.Flat) * fraction;
            float beforeFrom = from.Flat;
            from.Flat -= amount;
            AddLine(from, ScoreLineKind.DishFlat, -amount, beforeFrom, from.Flat, $"分数传出 -{amount}");
            float beforeTo = to.Flat;
            to.Flat += amount;
            AddLine(to, ScoreLineKind.DishFlat, amount, beforeTo, to.Flat, $"分数传入 +{amount}");
        }

        internal void ApplyExtraSettlementCommand(int dishId, int times)
        {
            if (!_accums.TryGetValue(dishId, out DishAccumulator a))
            {
                return;
            }

            int before = a.ExtraTimes;
            a.ExtraTimes += times;
            AddLine(a, ScoreLineKind.ExtraSettlement, times, before, a.ExtraTimes, $"额外结算 +{times} 次");
        }

        internal void ApplyGrantGoldCommand(float value)
        {
            float before = GoldDelta;
            GoldDelta += value;
            AddLine(_current, ScoreLineKind.Gold, value, before, GoldDelta, $"获得金币 +{value}");
        }

        internal void ApplyChangeLayerCommand(int dishId, float value, bool mult)
        {
            if (!_accums.TryGetValue(dishId, out DishAccumulator a))
            {
                return;
            }

            _layerDeltas.TryGetValue(dishId, out int current);
            int baseLayers = a.Dish.Layers + current;
            int after = mult ? (int)Math.Round(baseLayers * value, MidpointRounding.AwayFromZero) : baseLayers + (int)value;
            if (after < 0)
            {
                after = 0;
            }

            _layerDeltas[dishId] = after - a.Dish.Layers;
            AddLine(a, ScoreLineKind.Layer, after - baseLayers, baseLayers, after, mult ? $"层数 x{value}" : $"层数 {(value >= 0 ? "+" : string.Empty)}{value}");
        }

        internal void ApplyFinalFlatCommand(float value)
        {
            float before = FinalFlat;
            FinalFlat += value;
            AddLine(null, ScoreLineKind.FinalFlat, value, before, FinalFlat, $"总分 +{value}");
        }

        internal void ApplyFinalMultiplierCommand(float value)
        {
            float before = FinalMultiplier;
            FinalMultiplier *= value;
            AddLine(null, ScoreLineKind.FinalMultiplier, value, before, FinalMultiplier, $"总分乘区 x{value}");
        }

        private DishAccumulator EnsureAccumulator(DishInstance dish)
        {
            if (!_accums.TryGetValue(dish.Id, out DishAccumulator a))
            {
                a = new DishAccumulator { Dish = dish, Base = dish.Def.Deliciousness };
                _accums[dish.Id] = a;
                _order.Add(dish.Id);
            }

            return a;
        }

        private void ResolveCommandQueue()
        {
            if (_isResolvingCommands)
            {
                return;
            }

            _isResolvingCommands = true;
            try
            {
                while (_commands.Count > 0)
                {
                    if (_commandsExecuted++ >= MaxCommandsPerCalculation)
                    {
                        throw new InvalidOperationException("Score command limit exceeded. Check for recursive scoring effects.");
                    }

                    PendingScoreCommand pending = _commands.Dequeue();
                    ScorePhase previousPhase = Phase;
                    ScoreSource previousSource = Source;
                    GridPos? previousCell = CurrentCell;
                    IEffectDef previousTag = Tag;
                    Phase = pending.Phase;
                    Source = pending.Source;
                    CurrentCell = pending.Cell;
                    Tag = pending.Tag;
                    try
                    {
                        EmitEvent(ScoreEventType.CommandExecuted, $"执行命令 {pending.Command.Name}");
                        pending.Command.Execute(this);
                    }
                    finally
                    {
                        Phase = previousPhase;
                        Source = previousSource;
                        CurrentCell = previousCell;
                        Tag = previousTag;
                    }
                }
            }
            finally
            {
                _isResolvingCommands = false;
            }
        }

        private void AddLine(DishAccumulator accum, ScoreLineKind kind, float value, float before, float after, string fallbackMessage)
        {
            int dishInstanceId = accum != null ? accum.Dish.Id : (Dish != null ? Dish.Id : 0);
            string dishId = accum != null ? accum.Dish.Def.Id : (Dish != null ? Dish.Def.Id : string.Empty);
            string sourceName = Source != null ? Source.Name : string.Empty;
            string message = string.IsNullOrEmpty(sourceName) ? fallbackMessage : $"{sourceName}: {fallbackMessage}";
            _lines.Add(new ScoreLine(Phase, kind, Source, dishInstanceId, dishId, CurrentCell, value, before, after, message));
        }

        private sealed class PendingScoreCommand
        {
            public PendingScoreCommand(
                IScoreCommand command,
                ScorePhase phase,
                ScoreSource source,
                GridPos? cell,
                IEffectDef tag)
            {
                Command = command;
                Phase = phase;
                Source = source;
                Cell = cell;
                Tag = tag;
            }

            public IScoreCommand Command { get; }

            public ScorePhase Phase { get; }

            public ScoreSource Source { get; }

            public GridPos? Cell { get; }

            public IEffectDef Tag { get; }
        }
    }

    /// <summary>技能传递副作用：把 SkillIds 追加给某目标实例。</summary>
    public sealed class SkillTransferSideEffect
    {
        public SkillTransferSideEffect(int targetInstanceId, IReadOnlyList<string> skillIds)
        {
            TargetInstanceId = targetInstanceId;
            SkillIds = skillIds ?? Array.Empty<string>();
        }

        public int TargetInstanceId { get; }

        public IReadOnlyList<string> SkillIds { get; }
    }

    /// <summary>
    /// 分数结算命令。效果通过提交命令改变结算状态，命令执行时也可以继续提交派生命令。
    /// </summary>
    public interface IScoreCommand
    {
        string Name { get; }

        void Execute(ScoreContext context);
    }

    /// <summary>指定菜品加法区增加固定值。</summary>
    public sealed class AddDishFlatCommand : IScoreCommand
    {
        private readonly int _dishId;
        private readonly float _value;

        public AddDishFlatCommand(int dishId, float value)
        {
            _dishId = dishId;
            _value = value;
        }

        public string Name => "AddDishFlat";

        public void Execute(ScoreContext context) => context.ApplyDishFlatCommand(_dishId, _value);
    }

    /// <summary>指定菜品乘区乘以固定值。</summary>
    public sealed class MultiplyDishCommand : IScoreCommand
    {
        private readonly int _dishId;
        private readonly float _value;

        public MultiplyDishCommand(int dishId, float value)
        {
            _dishId = dishId;
            _value = value;
        }

        public string Name => "MultiplyDish";

        public void Execute(ScoreContext context) => context.ApplyDishMultiplierCommand(_dishId, _value);
    }

    /// <summary>分数按比例从来源菜转移到目标菜（加法层）。</summary>
    public sealed class TransferScoreCommand : IScoreCommand
    {
        private readonly int _fromId;
        private readonly int _toId;
        private readonly float _fraction;

        public TransferScoreCommand(int fromId, int toId, float fraction)
        {
            _fromId = fromId;
            _toId = toId;
            _fraction = fraction;
        }

        public string Name => "TransferScore";

        public void Execute(ScoreContext context) => context.ApplyTransferScoreCommand(_fromId, _toId, _fraction);
    }

    /// <summary>目标菜额外结算若干次。</summary>
    public sealed class ExtraSettlementCommand : IScoreCommand
    {
        private readonly int _dishId;
        private readonly int _times;

        public ExtraSettlementCommand(int dishId, int times)
        {
            _dishId = dishId;
            _times = times;
        }

        public string Name => "ExtraSettlement";

        public void Execute(ScoreContext context) => context.ApplyExtraSettlementCommand(_dishId, _times);
    }

    /// <summary>获得金币（副作用）。</summary>
    public sealed class GrantGoldCommand : IScoreCommand
    {
        private readonly float _value;

        public GrantGoldCommand(float value)
        {
            _value = value;
        }

        public string Name => "GrantGold";

        public void Execute(ScoreContext context) => context.ApplyGrantGoldCommand(_value);
    }

    /// <summary>层数改动（副作用）。</summary>
    public sealed class ChangeLayerCommand : IScoreCommand
    {
        private readonly int _dishId;
        private readonly float _value;
        private readonly bool _mult;

        public ChangeLayerCommand(int dishId, float value, bool mult)
        {
            _dishId = dishId;
            _value = value;
            _mult = mult;
        }

        public string Name => "ChangeLayer";

        public void Execute(ScoreContext context) => context.ApplyChangeLayerCommand(_dishId, _value, _mult);
    }

    /// <summary>最终总分加法区增加固定值。</summary>
    public sealed class AddFinalFlatCommand : IScoreCommand
    {
        private readonly float _value;

        public AddFinalFlatCommand(float value)
        {
            _value = value;
        }

        public string Name => "AddFinalFlat";

        public void Execute(ScoreContext context) => context.ApplyFinalFlatCommand(_value);
    }

    /// <summary>最终总分乘区乘以固定值。</summary>
    public sealed class MultiplyFinalCommand : IScoreCommand
    {
        private readonly float _value;

        public MultiplyFinalCommand(float value)
        {
            _value = value;
        }

        public string Name => "MultiplyFinal";

        public void Execute(ScoreContext context) => context.ApplyFinalMultiplierCommand(_value);
    }

    /// <summary>立即解析另一个效果，用于“触发时再触发一个效果”的连锁结算。</summary>
    public sealed class ResolveScoreEffectCommand : IScoreCommand
    {
        private readonly ScoreEffectEntry _entry;

        public ResolveScoreEffectCommand(ScoreEffectEntry entry)
        {
            _entry = entry;
        }

        public string Name => "ResolveScoreEffect";

        public void Execute(ScoreContext context) => context.Apply(_entry);
    }
}
