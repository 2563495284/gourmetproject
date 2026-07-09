using System.Collections.Generic;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>主动道具的使用情境。可用情境由道具 <c>targetKind</c> 推导（见 docs/design/道具.md）。</summary>
    public enum ActiveUseContextKind
    {
        Battle,
        Shop,
        Map,
        Reward,
    }

    /// <summary>
    /// 主动道具选中的一个目标（跨情境抽象）。<see cref="Id"/> 视 targetKind 为菜/碎片/风味等 id，
    /// <see cref="X"/>/<see cref="Y"/> 为棋盘或胃格坐标（无坐标时为 -1）。
    /// </summary>
    public readonly struct ActiveTarget
    {
        public ActiveTarget(string id, int x = -1, int y = -1)
        {
            Id = id;
            X = x;
            Y = y;
        }

        public string Id { get; }

        public int X { get; }

        public int Y { get; }
    }

    /// <summary>
    /// 主动道具「使用上下文」。战斗与局外（商店/地图/奖励）各实现一个：
    /// 负责回答「本情境有哪些目标」以及提供各操作所需的能力钩子。
    /// 效果语义集中在 <see cref="ActiveItemEffectRegistry"/>，情境只提供能力，避免每情境重写效果。
    /// </summary>
    public interface IActiveUseContext
    {
        ActiveUseContextKind ContextKind { get; }

        /// <summary>当前运行（金币/持有等全局资源类操作用）。</summary>
        GameRun Run { get; }

        /// <summary>列出当前情境下该目标类型的候选目标（供选目标 UI）；无目标类型返回空。</summary>
        IReadOnlyList<ActiveTarget> EnumerateTargets(cfg.ItemTargetKind targetKind);

        /// <summary>能力：清空棋盘（仅战斗支持）。不支持或无法执行返回 false。</summary>
        bool ClearBoard();

        /// <summary>能力：额外上一道菜（仅战斗支持）。不支持或无法执行返回 false。</summary>
        bool ExtraServe();
    }
}
