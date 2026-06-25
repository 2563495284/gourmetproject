using GourmetProject.Game.Run;
namespace GourmetProject.Game.Meta
{
    /// <summary>行动执行后，编排层需要进行的后续动作类型。</summary>
    public enum ActionOutcomeKind
    {
        /// <summary>已即时结算，只需展示反馈文案。</summary>
        Immediate,

        /// <summary>需要打开一场美食挑战战斗。</summary>
        Battle,

        /// <summary>需要弹出事件（可能带选项）。</summary>
        Event,

        /// <summary>需要打开商店。</summary>
        Shop,
    }

    /// <summary>行动执行结果：告诉编排层（BattleForm）下一步该做什么。</summary>
    public sealed class ActionOutcome
    {
        private ActionOutcome(ActionOutcomeKind kind)
        {
            Kind = kind;
        }

        public ActionOutcomeKind Kind { get; private set; }

        /// <summary>反馈文案（Immediate）。</summary>
        public string Feedback { get; private set; } = string.Empty;

        /// <summary>战斗目标分（Battle）。</summary>
        public int RequiredScore { get; private set; }

        /// <summary>战斗特殊机制（Battle）。</summary>
        public string Modifier { get; private set; } = string.Empty;

        /// <summary>战斗随机流 key（Battle）。</summary>
        public string BattleKey { get; private set; } = string.Empty;

        /// <summary>事件 id（Event）。</summary>
        public string EventId { get; private set; } = string.Empty;

        public static ActionOutcome Immediate(string feedback) =>
            new ActionOutcome(ActionOutcomeKind.Immediate) { Feedback = feedback ?? string.Empty };

        public static ActionOutcome Battle(int requiredScore, string modifier, string battleKey) =>
            new ActionOutcome(ActionOutcomeKind.Battle)
            {
                RequiredScore = requiredScore,
                Modifier = modifier ?? string.Empty,
                BattleKey = battleKey ?? string.Empty,
            };

        public static ActionOutcome Event(string eventId) =>
            new ActionOutcome(ActionOutcomeKind.Event) { EventId = eventId ?? string.Empty };

        public static ActionOutcome Shop() => new ActionOutcome(ActionOutcomeKind.Shop);
    }
}
