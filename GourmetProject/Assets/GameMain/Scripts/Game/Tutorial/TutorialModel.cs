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
        public const string DirectionSelection = "tutorial.prelude.direction_selection";
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
        public const string ResultHeart = "tutorial.hook.result_heart";
        // 旧存档兼容：不再主动播放，新胜败说明统一使用 ResultHeart。
        public const string Failure = "tutorial.hook.failure";
        public const string PassiveItem = "tutorial.hook.passive_item";

        public static bool IsCore(string id) =>
            !string.IsNullOrEmpty(id) && id.StartsWith("tutorial.core.", StringComparison.Ordinal);
    }

    public static class TutorialAnchorId
    {
        public const string Direction = "character.direction";
        public const string DirectionStart = "character.start";
        public const string ActionDeck = "action.deck";
        public const string ActionCard0 = "action.card.0";
        public const string ActionCard1 = "action.card.1";
        public const string ActionCard2 = "action.card.2";
        public const string ActionReward0 = "action.reward.0";
        public const string ActionReward1 = "action.reward.1";
        public const string ActionReward2 = "action.reward.2";
        public const string ActionAxis = "action.axis";
        public const string Score = "battle.score";
        public const string Hearts = "battle.hearts";
        public const string Recipe = "battle.recipe";
        public const string RecipePanel = "battle.recipe_panel";
        public const string Table = "battle.table";
        public const string FoodInfo = "battle.food_info";
        public const string FoodTips = "battle.food_tips";
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
        public const string DirectionConfirmed = "character.direction_confirmed";
        public const string ActionPicked = "action.picked";
        public const string DishPlaced = "battle.dish_placed";
        public const string SettleClicked = "battle.settle_clicked";
        public const string TimelineNodePicked = "timeline.node_picked";
    }

    public static class TutorialCommand
    {
        public const string OpenInitialRecipe = "battle.tutorial.open_initial_recipe";
        public const string CloseInitialRecipe = "battle.tutorial.close_initial_recipe";
        public const string ShowPreparedFoodTips = "battle.tutorial.show_prepared_food_tips";
        public const string HidePreparedFoodTips = "battle.tutorial.hide_prepared_food_tips";
    }

    public enum TutorialAdvanceMode
    {
        Continue,
        Signal,
    }

    public enum TutorialMascotPose
    {
        Explain,
        PointRight,
        Remind,
        Think,
        Wave,
        Celebrate,
    }

    public sealed class TutorialStepDefinition
    {
        public TutorialStepDefinition(
            string message,
            TutorialAdvanceMode mode = TutorialAdvanceMode.Continue,
            string signal = null,
            params string[] anchors)
            : this(
                message,
                TutorialMascotPose.Explain,
                mode,
                signal,
                enterCommand: null,
                exitCommand: null,
                allowTargetInteraction: mode == TutorialAdvanceMode.Signal,
                anchors)
        {
        }

        public TutorialStepDefinition(
            string message,
            TutorialMascotPose pose,
            TutorialAdvanceMode mode,
            string signal,
            string enterCommand,
            string exitCommand,
            bool allowTargetInteraction,
            params string[] anchors)
        {
            Message = message ?? string.Empty;
            Pose = pose;
            Mode = mode;
            Signal = signal ?? string.Empty;
            EnterCommand = enterCommand ?? string.Empty;
            ExitCommand = exitCommand ?? string.Empty;
            AllowTargetInteraction = allowTargetInteraction;
            Anchors = anchors ?? Array.Empty<string>();
        }

        public string Message { get; }
        public TutorialMascotPose Pose { get; }
        public TutorialAdvanceMode Mode { get; }
        public string Signal { get; }
        public string EnterCommand { get; }
        public string ExitCommand { get; }
        public bool AllowTargetInteraction { get; }
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

        public static TutorialSequenceDefinition BuildResultHeart(bool isWin)
        {
            string message = isWin
                ? "太棒了，老板！这次经营成功，❤️红心不会减少。红心代表餐厅还能承受失败的次数：日常营业和火热营业失败会损失 1 颗，星级评鉴失败会损失 2 颗；红心归零，本局就会结束。"
                : "别灰心，老板！这次没有达到目标，失败会让我们损失❤️红心。日常营业和火热营业失败会损失 1 颗，星级评鉴失败会损失 2 颗；红心归零，本局就会结束。";
            return new TutorialSequenceDefinition(
                TutorialId.ResultHeart,
                new TutorialStepDefinition(
                    message,
                    isWin ? TutorialMascotPose.Celebrate : TutorialMascotPose.Remind,
                    TutorialAdvanceMode.Continue,
                    signal: null,
                    enterCommand: null,
                    exitCommand: null,
                    allowTargetInteraction: false,
                    TutorialAnchorId.Hearts));
        }

        private static Dictionary<string, TutorialSequenceDefinition> Build()
        {
            TutorialStepDefinition C(string text, params string[] anchors) =>
                new TutorialStepDefinition(text, TutorialAdvanceMode.Continue, null, anchors);
            TutorialStepDefinition S(string text, string signal, params string[] anchors) =>
                new TutorialStepDefinition(text, TutorialAdvanceMode.Signal, signal, anchors);
            TutorialStepDefinition V(
                string text,
                TutorialMascotPose pose,
                string enterCommand,
                string exitCommand,
                params string[] anchors) =>
                new TutorialStepDefinition(
                    text,
                    pose,
                    TutorialAdvanceMode.Continue,
                    signal: null,
                    enterCommand,
                    exitCommand,
                    allowTargetInteraction: false,
                    anchors);

            return new Dictionary<string, TutorialSequenceDefinition>(StringComparer.Ordinal)
            {
                [TutorialId.DirectionSelection] = new TutorialSequenceDefinition(
                    TutorialId.DirectionSelection,
                    new TutorialStepDefinition(
                        "老板，这里选择的不是店长角色，而是餐厅的经营方向哦！这次我们经营的是「甜品」，糖霜、奶油和烘焙香气，就是复兴餐厅的第一步。",
                        TutorialMascotPose.Wave,
                        TutorialAdvanceMode.Continue,
                        signal: null,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: false,
                        TutorialAnchorId.Direction),
                    new TutorialStepDefinition(
                        "方向选好了，就点击「新游戏」吧。铛铛会陪你一起把店开起来！",
                        TutorialMascotPose.PointRight,
                        TutorialAdvanceMode.Signal,
                        TutorialSignal.DirectionConfirmed,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: true,
                        TutorialAnchorId.DirectionStart)),

                [TutorialId.FirstAction] = new TutorialSequenceDefinition(
                    TutorialId.FirstAction,
                    C("每次行动都会花费一定的时间，并为餐厅带来不同的收益。", TutorialAnchorId.ActionCard0),
                    C("行动卡上的图标代表主要奖励。", TutorialAnchorId.ActionReward0),
                    S("点击这张日常营业卡，开始第一次营业。", TutorialSignal.ActionPicked, TutorialAnchorId.ActionCard0)),

                [TutorialId.FirstBattle] = new TutorialSequenceDefinition(
                    TutorialId.FirstBattle,
                    new TutorialStepDefinition(
                        "老板，看这里！每次出菜时，都会从食谱中抽取一个尚未处理的食物。你可以把它拖到餐桌上，也可以拖进垃圾桶扔掉。",
                        TutorialMascotPose.PointRight,
                        TutorialAdvanceMode.Continue,
                        signal: null,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: false,
                        TutorialAnchorId.ServingOutlet),
                    V(
                        "这里是你的初始食谱，里面装着本次经营会抽到的食物。先记住它们吧！",
                        TutorialMascotPose.Explain,
                        TutorialCommand.OpenInitialRecipe,
                        TutorialCommand.CloseInitialRecipe,
                        TutorialAnchorId.RecipePanel),
                    V(
                        "每个食物都有自己的分数，并带有特殊效果。好好搭配，它们就能发挥更大的作用！",
                        TutorialMascotPose.Explain,
                        TutorialCommand.ShowPreparedFoodTips,
                        TutorialCommand.HidePreparedFoodTips,
                        TutorialAnchorId.FoodTips),
                    new TutorialStepDefinition(
                        "这里是本次经营需要达到的目标美味值。努力让食物结算后的总美味值达到它吧！",
                        TutorialMascotPose.Remind,
                        TutorialAdvanceMode.Continue,
                        signal: null,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: false,
                        TutorialAnchorId.Score),
                    new TutorialStepDefinition(
                        "现在，把出餐口的食物拖到餐桌上吧！绿色位置可以摆放，缺格或被占用的位置不能摆放。",
                        TutorialMascotPose.PointRight,
                        TutorialAdvanceMode.Signal,
                        TutorialSignal.DishPlaced,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: true,
                        TutorialAnchorId.ServingOutlet,
                        TutorialAnchorId.Table),
                    new TutorialStepDefinition(
                        "等你准备好了，点击「结算」就可以结束本次经营。现在先听铛铛说完，决定什么时候结算由老板自己来！",
                        TutorialMascotPose.Remind,
                        TutorialAdvanceMode.Continue,
                        signal: null,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: false,
                        TutorialAnchorId.Settle)),

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
                    C("老板，食物现在有风味了！每个食物只有 1 个风味位，风味会改变食物的属性和结算效果；风味箱和风味强化箱都能帮我们调整它。")),

                [TutorialId.Material] = new TutorialSequenceDefinition(
                    TutorialId.Material,
                    C("餐桌也能变强哦！每个餐桌格只有 1 个材质，放置在格子上的食物会获得对应效果；材质箱和材质强化箱可以改变这些效果。")),

                [TutorialId.Adjustment] = new TutorialSequenceDefinition(
                    TutorialId.Adjustment,
                    C("这是调整单！它可以修改节点和行动。先看看当前奖励与时间轴，再思考怎样使用，才能把收益最大化。")),

                [TutorialId.Boss] = new TutorialSequenceDefinition(
                    TutorialId.Boss,
                    C("星级评鉴拥有独立的特殊规则，规则会显示在营业信息区，并改变餐桌、食谱、上菜或结算方式。", TutorialAnchorId.BossRule),
                    C("评鉴获胜会记下 1 颗星；失败会损失 2 颗红心。", TutorialAnchorId.Score)),

                [TutorialId.Failure] = new TutorialSequenceDefinition(
                    TutorialId.Failure,
                    C("别灰心，老板！之后铛铛会在经营结果里说明红心规则。", TutorialAnchorId.HeartBreak)),

                [TutorialId.PassiveItem] = new TutorialSequenceDefinition(
                    TutorialId.PassiveItem,
                    C("装饰品获得后会持续生效。每件装饰品都有不同的效果，但都会从不同方向让餐厅变得更强！")),
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
