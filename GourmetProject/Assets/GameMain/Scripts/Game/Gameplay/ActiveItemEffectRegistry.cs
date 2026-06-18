using GourmetProject.Gameplay.Battle;

namespace GourmetProject.Game.Gameplay
{
    public readonly struct ActiveItemUseResult
    {
        public ActiveItemUseResult(bool success, bool boardChanged, string message)
        {
            Success = success;
            BoardChanged = boardChanged;
            Message = message;
        }

        public bool Success { get; }

        public bool BoardChanged { get; }

        public string Message { get; }
    }

    /// <summary>主动道具效果派发入口。表现层只负责点击与刷新，不直接承载效果逻辑。</summary>
    public static class ActiveItemEffectRegistry
    {
        public static ActiveItemUseResult TryUse(BattleSession session, cfg.Item item)
        {
            if (session == null || item == null || session.IsSettled)
            {
                return new ActiveItemUseResult(false, false, "现在不能使用主动道具。");
            }

            switch (item.EffectType)
            {
                case "ClearBoard":
                    if (session.Board.DishCount <= 0)
                    {
                        return new ActiveItemUseResult(false, false, "重摆铃：棋盘已经是空的。");
                    }

                    session.ClearBoard();
                    return new ActiveItemUseResult(true, true, "重摆铃：已清空棋盘。");

                case "ExtraServe":
                    return TryExtraServe(session, item);

                default:
                    return new ActiveItemUseResult(true, false, $"使用了 {item.Name}。");
            }
        }

        private static ActiveItemUseResult TryExtraServe(BattleSession session, cfg.Item item)
        {
            for (int i = 0; i < session.Slots.Count; i++)
            {
                if (session.Serve(i).Success)
                {
                    return new ActiveItemUseResult(true, true, $"{item.Name}：额外上了一道菜。");
                }
            }

            return new ActiveItemUseResult(false, false, $"{item.Name}：没有能放下的菜了。");
        }
    }
}
