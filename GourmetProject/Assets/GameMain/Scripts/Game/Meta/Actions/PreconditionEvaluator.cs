using GourmetProject.Game.Run;
namespace GourmetProject.Game.Meta
{
    /// <summary>前置条件求值所需的运行状态（由 <see cref="GameRun"/> 实现，便于单测注入假数据）。</summary>
    public interface IPreconditionContext
    {
        int Gold { get; }
        int WeekIndex { get; }
        bool HasItem(string itemId);
    }

    /// <summary>
    /// 行动/事件出现前置条件求值。支持用 '|' 连接多个子条件（全部满足才通过）。
    /// 子条件语法（min skeleton，可扩展）：
    ///   minGold:N   当前金币 &gt;= N
    ///   maxGold:N   当前金币 &lt;= N
    ///   minWeek:N   当前周 &gt;= N
    ///   hasItem:id  持有指定道具
    /// 空串或未知子条件视为满足（宽松默认，避免误杀配置）。
    /// </summary>
    public static class PreconditionEvaluator
    {
        public static bool IsSatisfied(IPreconditionContext ctx, string expression)
        {
            if (ctx == null || string.IsNullOrEmpty(expression))
            {
                return true;
            }

            foreach (string raw in expression.Split('|'))
            {
                string clause = raw.Trim();
                if (clause.Length > 0 && !EvaluateClause(ctx, clause))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EvaluateClause(IPreconditionContext ctx, string clause)
        {
            int colon = clause.IndexOf(':');
            if (colon < 0)
            {
                return true;
            }

            string key = clause.Substring(0, colon).Trim();
            string value = clause.Substring(colon + 1).Trim();

            switch (key)
            {
                case "minGold":
                    return int.TryParse(value, out int minGold) && ctx.Gold >= minGold;
                case "maxGold":
                    return int.TryParse(value, out int maxGold) && ctx.Gold <= maxGold;
                case "minWeek":
                    return int.TryParse(value, out int minWeek) && ctx.WeekIndex >= minWeek;
                case "hasItem":
                    return ctx.HasItem(value);
                default:
                    return true;
            }
        }
    }
}
