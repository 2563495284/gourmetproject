using System;
using System.Collections.Generic;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.Save;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Tutorial
{
    public static class TutorialId
    {
        public const string FirstAction = "tutorial.core.first_action";
        public const string FirstBattle = "tutorial.core.first_battle";
        public const string Settlement = "tutorial.core.settlement";
        public const string RewardSummary = "tutorial.core.reward_summary";
        public const string SecondAction = "tutorial.core.second_action";
        public const string TimelineNode = "tutorial.core.timeline_node";
        public const string CoreComplete = "tutorial.core.complete";
        public const string Flavor = "tutorial.hook.flavor";
        public const string Material = "tutorial.hook.material";
        public const string Adjustment = "tutorial.hook.adjustment";
        public const string Boss = "tutorial.hook.boss";
        public const string Failure = "tutorial.hook.failure";
        public const string PassiveItem = "tutorial.hook.passive_item";

        public static bool IsCore(string id) =>
            !string.IsNullOrEmpty(id) && id.StartsWith("tutorial.core.", StringComparison.Ordinal);
    }

    public static class TutorialAnchorId
    {
        public const string ActionDeck = "action.deck";
        public const string ActionCard0 = "action.card.0";
        public const string ActionCard1 = "action.card.1";
        public const string ActionCard2 = "action.card.2";
        public const string ActionReward0 = "action.reward.0";
        public const string ActionReward1 = "action.reward.1";
        public const string ActionReward2 = "action.reward.2";
        public const string ActionAxis = "action.axis";
        public const string Score = "battle.score";
        public const string Recipe = "battle.recipe";
        public const string Table = "battle.table";
        public const string FoodInfo = "battle.food_info";
        public const string ServingOutlet = "battle.serving_outlet";
        public const string Discard = "battle.discard";
        public const string Settle = "battle.settle";
        public const string RewardList = "reward.list";
        public const string RewardContinue = "reward.continue";
        public const string HeartBreak = "failure.heart";
        public const string BossRule = "boss.rule";
    }

    public static class TutorialSignal
    {
        public const string ActionPicked = "action.picked";
        public const string DishPlaced = "battle.dish_placed";
        public const string SettleClicked = "battle.settle_clicked";
        public const string TimelineNodePicked = "timeline.node_picked";
    }

    public enum TutorialAdvanceMode
    {
        Continue,
        Signal,
    }

    public sealed class TutorialStepDefinition
    {
        public TutorialStepDefinition(
            string message,
            TutorialAdvanceMode mode = TutorialAdvanceMode.Continue,
            string signal = null,
            params string[] anchors)
        {
            Message = message ?? string.Empty;
            Mode = mode;
            Signal = signal ?? string.Empty;
            Anchors = anchors ?? Array.Empty<string>();
        }

        public string Message { get; }
        public TutorialAdvanceMode Mode { get; }
        public string Signal { get; }
        public IReadOnlyList<string> Anchors { get; }
    }

    public sealed class TutorialSequenceDefinition
    {
        public TutorialSequenceDefinition(string id, params TutorialStepDefinition[] steps)
        {
            Id = id ?? string.Empty;
            Steps = steps ?? Array.Empty<TutorialStepDefinition>();
        }

        public string Id { get; }
        public IReadOnlyList<TutorialStepDefinition> Steps { get; }
    }

    public static class TutorialCatalog
    {
        private static readonly Dictionary<string, TutorialSequenceDefinition> Definitions = Build();

        public static TutorialSequenceDefinition Get(string id) =>
            id != null && Definitions.TryGetValue(id, out TutorialSequenceDefinition value) ? value : null;

        private static Dictionary<string, TutorialSequenceDefinition> Build()
        {
            TutorialStepDefinition C(string text, params string[] anchors) =>
                new TutorialStepDefinition(text, TutorialAdvanceMode.Continue, null, anchors);
            TutorialStepDefinition S(string text, string signal, params string[] anchors) =>
                new TutorialStepDefinition(text, TutorialAdvanceMode.Signal, signal, anchors);

            return new Dictionary<string, TutorialSequenceDefinition>(StringComparer.Ordinal)
            {
                [TutorialId.FirstAction] = new TutorialSequenceDefinition(
                    TutorialId.FirstAction,
                    C("每次行动都会花费一定的时间，并为餐厅带来不同的收益。", TutorialAnchorId.ActionCard0),
                    C("行动卡上的图标代表主要奖励。", TutorialAnchorId.ActionReward0),
                    S("点击这张日常营业卡，开始第一次营业。", TutorialSignal.ActionPicked, TutorialAnchorId.ActionCard0)),

                [TutorialId.FirstBattle] = new TutorialSequenceDefinition(
                    TutorialId.FirstBattle,
                    C("左侧显示了本次营业的美味值目标。", TutorialAnchorId.Score),
                    C("食谱决定本场可能端出的食物。每次出菜都会从食谱中抽取一个尚未处理的食物。", TutorialAnchorId.Recipe),
                    C("餐桌由餐桌格组成。食物的形状必须完整落在可用且未被占用的格子上。", TutorialAnchorId.Table),
                    C("每个食物都有基础美味值，也可能带有技能。技能会在上菜或结算的指定时机改变自己、其他食物或总分。", TutorialAnchorId.FoodInfo),
                    C("单个食物经过技能、风味和材质修正后得到美味值；全桌食物汇总后就是总美味值。", TutorialAnchorId.Score),
                    S("把出菜口的食物拖到餐桌上。绿色位置可以摆放，缺格或被占用的位置不能摆放。", TutorialSignal.DishPlaced, TutorialAnchorId.ServingOutlet, TutorialAnchorId.Table),
                    C("暂时放不下或不适合当前布局时，可以把食物拖进垃圾桶。丢弃会消耗一次机会并重新出菜；这一局不要求你现在丢弃。", TutorialAnchorId.Discard),
                    S("准备好后点击结算。食物会依次触发技能、风味和材质效果，并汇总最终美味值。", TutorialSignal.SettleClicked, TutorialAnchorId.Settle)),

                [TutorialId.Settlement] = new TutorialSequenceDefinition(
                    TutorialId.Settlement,
                    C("这里是本次营业的结果。总美味值达到目标即为成功，否则营业失败。", TutorialAnchorId.Score)),

                [TutorialId.RewardSummary] = new TutorialSequenceDefinition(
                    TutorialId.RewardSummary,
                    C("这里汇总本次获得的食物与主要奖励。领取完成后才能进入下一次行动。", TutorialAnchorId.RewardList)),

                [TutorialId.SecondAction] = new TutorialSequenceDefinition(
                    TutorialId.SecondAction,
                    C("现在有两场日常营业和一场火热营业。选择行动时主要比较难度、用时和奖励。", TutorialAnchorId.ActionDeck),
                    C("日常营业难度较低，这场主要奖励餐桌格，用来扩大或调整餐桌。", TutorialAnchorId.ActionCard0),
                    C("这场日常营业主要奖励装饰品，它会长期强化餐厅。", TutorialAnchorId.ActionCard1),
                    C("火热营业的目标更高，但奖励规格也更高；这场主要奖励金币。", TutorialAnchorId.ActionCard2),
                    C("用时会推进顶部时间轴，途中经过节点时会先处理节点。", TutorialAnchorId.ActionAxis),
                    S("选择任意一张行动卡继续。", TutorialSignal.ActionPicked, TutorialAnchorId.ActionDeck)),

                [TutorialId.TimelineNode] = new TutorialSequenceDefinition(
                    TutorialId.TimelineNode,
                    C("普通行动推进时间时，经过的节点会自动触发。", TutorialAnchorId.ActionAxis),
                    C("节点行动不会再次消耗天数，但会执行自己的特殊效果。这个节点会结算利息。", TutorialAnchorId.ActionCard0),
                    S("点击节点卡结算效果。", TutorialSignal.TimelineNodePicked, TutorialAnchorId.ActionCard0)),

                [TutorialId.Flavor] = new TutorialSequenceDefinition(
                    TutorialId.Flavor,
                    C("风味附着在食物上，会增加或改变它的属性与结算效果。"),
                    C("基础情况下，每个食物只有 1 个风味槽；添加新风味会替换旧风味，特殊效果可以改变这个限制。")),

                [TutorialId.Material] = new TutorialSequenceDefinition(
                    TutorialId.Material,
                    C("材质附着在餐桌格上，每个格子最多拥有 1 种材质。"),
                    C("食物覆盖到有材质的格子时，会在结算中获得该格子的效果；多格食物可能同时受到多个格子的影响。")),

                [TutorialId.Adjustment] = new TutorialSequenceDefinition(
                    TutorialId.Adjustment,
                    C("调整单是一次性消耗品，可以刷新行动、改变下一次行动用时，或添加、删除、提前执行时间轴节点。"),
                    C("先比较当前行动奖励和节点位置，再决定使用时机，才能最大化收益。")),

                [TutorialId.Boss] = new TutorialSequenceDefinition(
                    TutorialId.Boss,
                    C("星级评鉴拥有独立的特殊规则，规则会显示在营业信息区，并改变餐桌、食谱、上菜或结算方式。", TutorialAnchorId.BossRule),
                    C("评鉴获胜会记下 1 颗星；失败会损失 2 颗红心。", TutorialAnchorId.Score)),

                [TutorialId.Failure] = new TutorialSequenceDefinition(
                    TutorialId.Failure,
                    C("本次总美味值没有达到目标。", TutorialAnchorId.HeartBreak),
                    C("日常营业和火热营业失败损失 1 颗红心；星级评鉴失败损失 2 颗。红心归零时，本局结束。", TutorialAnchorId.HeartBreak)),

                [TutorialId.PassiveItem] = new TutorialSequenceDefinition(
                    TutorialId.PassiveItem,
                    C("装饰品获得后会长期生效，不需要主动使用。"),
                    C("每件装饰品都有不同效果，可能影响得分、金币、奖励、时间轴或操作次数。合理组合会让餐厅越来越强。")),
            };
        }
    }

    public static class TutorialProgressService
    {
        public static bool IsCompleted(string id)
        {
            GameSaveData save = GameSavePersistence.Load();
            return save.GuideProgress.CompletedTutorialIds.Contains(id);
        }

        public static IReadOnlyList<string> Pending()
        {
            GameSaveData save = GameSavePersistence.Load();
            return new List<string>(save.GuideProgress.PendingTutorialIds);
        }

        public static bool Enqueue(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            GameSaveData save = GameSavePersistence.Load();
            GuideProgressSaveData progress = save.GuideProgress;
            if (progress.CompletedTutorialIds.Contains(id) || progress.PendingTutorialIds.Contains(id)) return false;
            progress.PendingTutorialIds.Add(id);
            GameSavePersistence.Save(save);
            return true;
        }

        public static void Complete(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            GameSaveData save = GameSavePersistence.Load();
            GuideProgressSaveData progress = save.GuideProgress;
            progress.PendingTutorialIds.Remove(id);
            if (!progress.CompletedTutorialIds.Contains(id)) progress.CompletedTutorialIds.Add(id);
            GameSavePersistence.Save(save);
        }

        /// <summary>
        /// 领取全局唯一的教程局资格。首次调用返回 true 并立即持久化；之后始终返回 false。
        /// </summary>
        public static bool ConsumeTutorialRun()
        {
            GameSaveData save = GameSavePersistence.Load();
            GuideProgressSaveData progress = save.GuideProgress;
            if (!progress.TryConsumeCoreTutorialRun()) return false;
            GameSavePersistence.Save(save);
            return true;
        }
    }

    public static class TutorialActionScheduleOverride
    {
        public static bool TryBuildChoices(GameRun run, out List<ActionChoice> choices)
        {
            return TryBuildChoices(
                run,
                TutorialProgressService.IsCompleted(TutorialId.CoreComplete),
                TutorialProgressService.IsCompleted(TutorialId.FirstAction),
                out choices);
        }

        public static bool TryBuildChoices(GameRun run, bool coreCompleted, out List<ActionChoice> choices)
        {
            return TryBuildChoices(run, coreCompleted, coreStarted: true, out choices);
        }

        public static bool TryBuildChoices(GameRun run, bool coreCompleted, bool coreStarted, out List<ActionChoice> choices)
        {
            choices = null;
            if (run == null || !run.IsTutorialRun || coreCompleted || run.WeekIndex != 1)
            {
                return false;
            }

            cfg.Tables tables = run.Tables ?? GameApp.Config?.Tables;
            if (tables == null) return false;

            if (run.RunActionStepIndex == 0 && run.CurrentDay <= TimelineMath.Epsilon)
            {
                choices = Build(tables, run, "tutorial_first", ("act_food_gold", 1f));
                return choices.Count == 1;
            }

            if (coreStarted
                && run.RunActionStepIndex == 1
                && run.CurrentDay >= 1f - TimelineMath.Epsilon
                && run.CurrentDay < 2f)
            {
                choices = Build(
                    tables,
                    run,
                    "tutorial_second",
                    ("act_food_fragment", 1f),
                    ("act_food_passive", 1f),
                    ("act_food_hard_gold", 1.2f));
                return choices.Count == 3;
            }

            return false;
        }

        private static List<ActionChoice> Build(
            cfg.Tables tables,
            GameRun run,
            string group,
            params (string id, float days)[] entries)
        {
            var result = new List<ActionChoice>(entries.Length);
            foreach ((string id, float days) in entries)
            {
                cfg.GameAction action = tables.TbAction.GetOrDefault(id);
                if (action != null)
                {
                    result.Add(new ActionChoice(action, group, run.ActionStepIndex, run.RunActionStepIndex, days));
                }
            }

            return result;
        }
    }

}
