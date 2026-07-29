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
        Event,
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

        /// <summary>
        /// 列出当前情境下该道具的候选目标（供选目标 UI）。
        /// 传入完整定义，使候选校验可感知具体效果语义。
        /// </summary>
        IReadOnlyList<ActiveTarget> EnumerateTargets(ItemDefinition item);

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

        // —— 调味小票 / 铺台小票：仅 Food 战斗中使用，并永久改 Run ——

        /// <summary>能力：给桌上目标菜附加风味，并永久写回其菜谱来源。</summary>
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

        /// <summary>能力：重新随机时间轴上最后一个未结算 Boss 节点的 Debuff。</summary>
        bool ResetLastBossDebuff();

        /// <summary>能力：把指定节点当前行动复制到当前或下一个整数日，返回新节点 id。</summary>
        string CloneTimelineNodeToCurrentOrNextIntegerDay(string nodeId, string sourceItemId);

        /// <summary>能力：在指定当前或未来整数日追加行动轴节点。</summary>
        bool AddTimelineNode(string actionId, int day);

        /// <summary>能力：在指定当前或未来整数日追加行动轴节点并返回新节点 id。</summary>
        string AddTimelineNodeWithId(string actionId, int day, string sourceItemId);

        /// <summary>能力：删除指定尚未结算、尚未开始执行的行动轴节点。</summary>
        bool DeleteTimelineNode(string nodeId);

        /// <summary>能力：为下一次日常行动增加一层半日 Buff。</summary>
        bool AddNextActionHalfCostStack();
    }
}
