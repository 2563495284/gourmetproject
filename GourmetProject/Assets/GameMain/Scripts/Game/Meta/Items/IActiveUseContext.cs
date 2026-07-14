using System.Collections.Generic;
using GourmetProject.Game.Run;

namespace GourmetProject.Game.Meta
{
    /// <summary>主动道具的使用情境。可用情境由道具 <c>targetKind</c> 推导（见 docs/design/道具.md）。</summary>
    public enum ActiveUseContextKind
    {
        Battle,
        Shop,
        ActionSelect,
        Reward,
    }

    /// <summary>
    /// 主动道具选中的一个目标（跨情境抽象）。<see cref="TargetKind"/> 标记目标语义；
    /// <see cref="Id"/> 视 targetKind 为菜/材质/风味等 id，<see cref="X"/>/<see cref="Y"/> 为餐桌或菜谱坐标。
    /// </summary>
    public readonly struct ActiveTarget
    {
        public ActiveTarget(string id, int x = -1, int y = -1, cfg.ItemTargetKind targetKind = cfg.ItemTargetKind.None)
        {
            Id = id;
            X = x;
            Y = y;
            TargetKind = targetKind;
        }

        public string Id { get; }

        public int X { get; }

        public int Y { get; }

        public cfg.ItemTargetKind TargetKind { get; }
    }

    /// <summary>
    /// 主动道具「使用上下文」。战斗与局外（商店/行动选择/奖励）各实现一个：
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

        /// <summary>能力：清空餐桌（仅战斗支持）。不支持或无法执行返回 false。</summary>
        bool ClearBoard();

        /// <summary>能力：额外上一道菜（仅战斗支持）。不支持或无法执行返回 false。</summary>
        bool ExtraServe();

        /// <summary>能力：给目标菜永久加分（对标杀戮尖塔2 药水打目标）。不支持或目标无效返回 false。</summary>
        bool AddPermanentScore(ActiveTarget target, float amount);

        /// <summary>能力：复制目标菜。<paramref name="randomKey"/> 供需随机流的情境派生确定性随机。</summary>
        bool DuplicateDish(ActiveTarget target, string randomKey);

        /// <summary>能力：移除目标菜。不支持或目标无效返回 false。</summary>
        bool DestroyDish(ActiveTarget target);

        /// <summary>能力：给目标菜乘区加成（永久乘区）。不支持或目标无效返回 false。</summary>
        bool MultiplyScore(ActiveTarget target, float multiplier);

        /// <summary>能力：给目标菜加「视为食物数」。不支持或目标无效返回 false。</summary>
        bool AddCountAs(ActiveTarget target, int amount);

        // —— 调味小票 / 铺台小票：永久改 Run（任意情境含战斗都可用）——

        /// <summary>能力：给目标菜附加风味。菜谱目标永久写 Run；餐桌菜目标写本局实例。</summary>
        bool AddFlavorToDish(ActiveTarget target, string flavorId);

        /// <summary>能力：移除目标菜的一个风味。<paramref name="flavorId"/> 为空时移除最后一个风味。</summary>
        bool RemoveFlavorFromDish(ActiveTarget target, string flavorId);

        /// <summary>能力：把目标菜的一个风味替换为 <paramref name="toFlavorId"/>。</summary>
        bool ConvertFlavorOnDish(ActiveTarget target, string toFlavorId);

        /// <summary>能力：转换目标菜分类。当前数据模型不一定支持，不能执行时返回 false。</summary>
        bool ConvertDishCategory(ActiveTarget target, string category);

        /// <summary>能力：给餐桌格永久附加材质（<paramref name="target"/>.X/Y=格坐标）。</summary>
        bool AddMaterialToCell(ActiveTarget target, string materialId);

        /// <summary>能力：生成一道菜。格目标用 X/Y 指定原点；无格目标由情境选择位置。</summary>
        bool GenerateDish(ActiveTarget target, string dishId, string randomKey);

        // —— 排程小票：操作行动轴/Boss，行动选择/商店按能力支持，战斗返回 false ——

        /// <summary>能力：重掷当前行动选项（保 Boss）。不支持或非选择态返回 false。</summary>
        bool RerollCurrentAction();

        /// <summary>能力：重置本周 Boss 类型（清 Boss debuff 抽取历史）。不支持返回 false。</summary>
        bool ResetWeekBoss();

        /// <summary>能力：立即执行行动轴上尚未结算的下一个节点。无可执行节点返回 false。</summary>
        bool ExecuteNextTimelineNode();

        /// <summary>能力：在当前行动轴上追加一个奖励节点（<paramref name="actionId"/>=奖励行动 id）。不支持返回 false。</summary>
        bool AddRewardNodeToTimeline(string actionId);
    }
}
