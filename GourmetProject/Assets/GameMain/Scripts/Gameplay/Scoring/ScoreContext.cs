using System;
using System.Collections.Generic;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GpBoard = GourmetProject.Gameplay.Board.Board;

namespace GourmetProject.Gameplay.Scoring
{
    /// <summary>结算过程中的可变上下文与累加器。</summary>
    public class ScoreContext
    {
        private const int MaxCommandsPerCalculation = 2048;

        private readonly List<DishScore> _dishScores = new List<DishScore>();
        private readonly List<ScoreLine> _lines = new List<ScoreLine>();
        private readonly List<ScoreEvent> _events = new List<ScoreEvent>();
        private readonly Queue<PendingScoreCommand> _commands = new Queue<PendingScoreCommand>();
        private bool _initialFinalModifiersRecorded;
        private bool _isResolvingCommands;
        private int _commandsExecuted;

        public ScoreContext(ScoreSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Board = snapshot.Board;
            Db = snapshot.Db;
            FinalFlat = snapshot.InitialFinalFlat;
            FinalMultiplier = snapshot.InitialFinalMultiplier;
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

        public float FlatBonus { get; private set; }

        public float Multiplier { get; private set; } = 1f;

        public float RawSum { get; private set; }

        public float FinalFlat { get; private set; }

        public float FinalMultiplier { get; private set; } = 1f;

        public IReadOnlyList<DishScore> DishScores => _dishScores;

        public IReadOnlyList<ScoreLine> Lines => _lines;

        public IReadOnlyList<ScoreEvent> Events => _events;

        public void EmitEvent(ScoreEventType type, string message)
        {
            int dishInstanceId = Dish != null ? Dish.Id : 0;
            string dishId = Dish != null ? Dish.Def.Id : string.Empty;
            _events.Add(new ScoreEvent(type, Phase, Source, dishInstanceId, dishId, CurrentCell, message));
        }

        public void BeginDish(DishInstance dish)
        {
            Dish = dish ?? throw new ArgumentNullException(nameof(dish));
            CurrentCell = null;
            Source = ScoreSource.Dish(dish);
            Phase = ScorePhase.BeforeDish;
            Tag = null;
            FlatBonus = 0f;
            Multiplier = 1f;
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

        public void AddFlat(float value)
        {
            SubmitCommand(new AddDishFlatCommand(value));
        }

        public void MultiplyBy(float value)
        {
            SubmitCommand(new MultiplyDishCommand(value));
        }

        public void AddFinalFlat(float value)
        {
            SubmitCommand(new AddFinalFlatCommand(value));
        }

        public void MultiplyFinalBy(float value)
        {
            SubmitCommand(new MultiplyFinalCommand(value));
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

        public DishScore CompleteDish()
        {
            if (Dish == null)
            {
                throw new InvalidOperationException("Cannot complete dish scoring before BeginDish.");
            }

            var score = new DishScore(Dish.Id, Dish.Def.Id, Dish.Def.Deliciousness, FlatBonus, Multiplier);
            _dishScores.Add(score);
            RawSum += score.Contribution;
            EmitEvent(ScoreEventType.DishCompleted, $"{Dish.Def.Name} 贡献 {score.Contribution}");
            return score;
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
            return new ScoreResult(_dishScores, RawSum, FinalFlat, FinalMultiplier, _lines, _events);
        }

        internal void ApplyDishFlatCommand(float value)
        {
            float before = FlatBonus;
            FlatBonus += value;
            AddLine(ScoreLineKind.DishFlat, value, before, FlatBonus, $"美味度 +{value}");
        }

        internal void ApplyDishMultiplierCommand(float value)
        {
            float before = Multiplier;
            Multiplier *= value;
            AddLine(ScoreLineKind.DishMultiplier, value, before, Multiplier, $"乘区 x{value}");
        }

        internal void ApplyFinalFlatCommand(float value)
        {
            float before = FinalFlat;
            FinalFlat += value;
            AddLine(ScoreLineKind.FinalFlat, value, before, FinalFlat, $"总分 +{value}");
        }

        internal void ApplyFinalMultiplierCommand(float value)
        {
            float before = FinalMultiplier;
            FinalMultiplier *= value;
            AddLine(ScoreLineKind.FinalMultiplier, value, before, FinalMultiplier, $"总分乘区 x{value}");
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

        private void AddLine(ScoreLineKind kind, float value, float before, float after, string fallbackMessage)
        {
            int dishInstanceId = Dish != null ? Dish.Id : 0;
            string dishId = Dish != null ? Dish.Def.Id : string.Empty;
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

    /// <summary>
    /// 分数结算命令。效果通过提交命令改变结算状态，命令执行时也可以继续提交派生命令。
    /// </summary>
    public interface IScoreCommand
    {
        string Name { get; }

        void Execute(ScoreContext context);
    }

    /// <summary>当前菜品加法区增加固定值。</summary>
    public sealed class AddDishFlatCommand : IScoreCommand
    {
        private readonly float _value;

        public AddDishFlatCommand(float value)
        {
            _value = value;
        }

        public string Name => "AddDishFlat";

        public void Execute(ScoreContext context) => context.ApplyDishFlatCommand(_value);
    }

    /// <summary>当前菜品乘区乘以固定值。</summary>
    public sealed class MultiplyDishCommand : IScoreCommand
    {
        private readonly float _value;

        public MultiplyDishCommand(float value)
        {
            _value = value;
        }

        public string Name => "MultiplyDish";

        public void Execute(ScoreContext context) => context.ApplyDishMultiplierCommand(_value);
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
