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
        internal const string FirstBattleSettleHint = "tutorial.core.first_battle_settle_hint";
        public const string Settlement = "tutorial.core.settlement";
        public const string RewardSummary = "tutorial.core.reward_summary";
        public const string SecondAction = "tutorial.core.second_action";
        public const string TimelineNode = "tutorial.core.timeline_node";
        public const string CoreComplete = "tutorial.core.complete";
        public const string Flavor = "tutorial.hook.flavor";
        public const string Material = "tutorial.hook.material";
        public const string Adjustment = "tutorial.hook.adjustment";
        public const string Boss = "tutorial.hook.boss";
        internal const string FirstFailureHeart = "tutorial.hook.first_failure_heart";
        // 旧存档兼容：保留 ID 和文案，但不再主动播放。
        public const string ResultHeart = "tutorial.hook.result_heart";
        public const string Failure = "tutorial.hook.failure";
        public const string PassiveItem = "tutorial.hook.passive_item";

        public static bool IsCore(string id) =>
            !string.IsNullOrEmpty(id) && id.StartsWith("tutorial.core.", StringComparison.Ordinal);
    }

    public static class TutorialAnchorId
    {
        public const string Direction = "character.direction";
        public const string DirectionStart = "character.start";
        public const string DirectionTitle = "character.direction.title";
        public const string DirectionName = "character.direction.name";
        public const string DirectionDescription = "character.direction.description";
        public const string DirectionButtons = "character.direction.buttons";
        public const string ActionDeck = "action.deck";
        public const string ActionCard0 = "action.card.0";
        public const string ActionCard1 = "action.card.1";
        public const string ActionCard2 = "action.card.2";
        public const string ActionReward0 = "action.reward.0";
        public const string ActionReward1 = "action.reward.1";
        public const string ActionReward2 = "action.reward.2";
        public const string ActionAxis = "action.axis";
        public const string Score = "battle.score";
        public const string ScoreSection = "battle.score.section";
        public const string ScoreTitle = "battle.score.title";
        public const string ScoreMeter = "battle.score.meter";
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
                ? "太棒了，老板！这次经营成功，❤️红心不会减少。红心代表餐厅还能承受失败的次数：日常营业、火热营业和星级评鉴失败都会损失1颗；红心归零，本局就会结束。"
                : "别灰心，老板！这次没有达到目标，失败会让我们损失❤️红心。日常营业、火热营业和星级评鉴失败都会损失1颗；红心归零，本局就会结束。";
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

        internal static TutorialSequenceDefinition BuildFirstFailureHeart()
        {
            return new TutorialSequenceDefinition(
                TutorialId.FirstFailureHeart,
                new TutorialStepDefinition(
                    "别灰心，老板！这次没有达到目标，我们会损失❤️。",
                    TutorialMascotPose.Remind,
                    TutorialAdvanceMode.Continue,
                    signal: null,
                    enterCommand: null,
                    exitCommand: null,
                    allowTargetInteraction: false,
                    TutorialAnchorId.Hearts),
                new TutorialStepDefinition(
                    "日常营业、火热营业和星级评鉴失败都会损失1颗。❤️归零，本局就会结束。",
                    TutorialMascotPose.Remind,
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
                        "老板，我们来选择餐厅的经营方向吧！这次我们经营「甜品」吧。",
                        TutorialMascotPose.Wave,
                        TutorialAdvanceMode.Continue,
                        signal: null,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: false,
                        TutorialAnchorId.DirectionName,
                        TutorialAnchorId.DirectionDescription),
                    new TutorialStepDefinition(
                        "点击「新游戏」吧。铛铛会陪你一起把店开起来！",
                        TutorialMascotPose.PointRight,
                        TutorialAdvanceMode.Signal,
                        TutorialSignal.DirectionConfirmed,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: true,
                        TutorialAnchorId.DirectionStart)),

                [TutorialId.FirstAction] = new TutorialSequenceDefinition(
                    TutorialId.FirstAction,
                    C("这是行动卡，每次行动都会消耗一定的时间，完成会带来收益。", TutorialAnchorId.ActionCard0),
                    C("卡片上的图标，表示这次行动的额外奖励。", TutorialAnchorId.ActionReward0),
                    S("点击这张日常营业卡，开始我们的营业吧！", TutorialSignal.ActionPicked, TutorialAnchorId.ActionCard0)),

                [TutorialId.FirstBattle] = new TutorialSequenceDefinition(
                    TutorialId.FirstBattle,
                    C(
                        "偷偷告诉老板营业的秘诀，就是把尽可能多的食物摆上餐桌，获得足够的美味值。",
                        TutorialAnchorId.Table),
                    new TutorialStepDefinition(
                        "老板，看这里！食物会从食谱中抽取，展示在出菜口。",
                        TutorialMascotPose.PointRight,
                        TutorialAdvanceMode.Continue,
                        signal: null,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: false,
                        TutorialAnchorId.ServingOutlet),
                    new TutorialStepDefinition(
                        "你可以把它拖到餐桌上，也可以拖进垃圾桶丢弃。",
                        TutorialMascotPose.PointRight,
                        TutorialAdvanceMode.Continue,
                        signal: null,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: false,
                        TutorialAnchorId.ServingOutlet,
                        TutorialAnchorId.Table,
                        TutorialAnchorId.Discard),
                    V(
                        "这里是你的初始食谱，里面是经营会抽到的食物。",
                        TutorialMascotPose.Explain,
                        TutorialCommand.OpenInitialRecipe,
                        TutorialCommand.CloseInitialRecipe,
                        TutorialAnchorId.RecipePanel),
                    V(
                        "每个食物都有自己的特殊效果。老板好好搭配，它们就能发挥更大的作用！",
                        TutorialMascotPose.Explain,
                        TutorialCommand.ShowPreparedFoodTips,
                        TutorialCommand.HidePreparedFoodTips,
                        TutorialAnchorId.FoodTips),
                    new TutorialStepDefinition(
                        "这里是需要达到的美味值。努力超过它吧！",
                        TutorialMascotPose.Remind,
                        TutorialAdvanceMode.Continue,
                        signal: null,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: false,
                        TutorialAnchorId.ScoreSection,
                        TutorialAnchorId.ScoreTitle,
                        TutorialAnchorId.ScoreMeter)),

                [TutorialId.FirstBattleSettleHint] = new TutorialSequenceDefinition(
                    TutorialId.FirstBattleSettleHint,
                    new TutorialStepDefinition(
                        "等你准备好了，点击「结算」就可以结束本次经营！",
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
                    C("这里是本次营业奖励，老板快领取吧。", TutorialAnchorId.RewardList)),

                [TutorialId.SecondAction] = new TutorialSequenceDefinition(
                    TutorialId.SecondAction,
                    S("这是火热营业，目标更高，但奖励规格也更高。", TutorialSignal.ActionPicked, TutorialAnchorId.ActionDeck)),

                [TutorialId.TimelineNode] = new TutorialSequenceDefinition(
                    TutorialId.TimelineNode,
                    C("普通行动推进时间轴时，经过的节点会出现在行动卡中。", TutorialAnchorId.ActionAxis),
                    S("这些是节点行动，不会消耗天数。这个节点会结算利息。", TutorialSignal.TimelineNodePicked, TutorialAnchorId.ActionCard0)),

                [TutorialId.Flavor] = new TutorialSequenceDefinition(
                    TutorialId.Flavor,
                    C("老板，食物现在有风味啦！每个食物只有 1 个风味位哦。"),
                    C("风味会改变食物的属性和结算效果，强化箱道具可以帮我们为食物附加风味。")),

                [TutorialId.Material] = new TutorialSequenceDefinition(
                    TutorialId.Material,
                    C("老板，餐桌现在有材质啦！每个餐桌格只有 1 个材质哦。"),
                    C("放置在餐桌格上的食物会获得对应效果；强化箱道具可以帮我们为餐桌附加材质。")),

                [TutorialId.Adjustment] = new TutorialSequenceDefinition(
                    TutorialId.Adjustment,
                    C("这是调整单！它可以修改节点和行动。")),

                [TutorialId.Boss] = new TutorialSequenceDefinition(
                    TutorialId.Boss,
                    C("我们终于来到星级评鉴啦！星级评鉴拥有特殊规则，需要的美味值也更高。", TutorialAnchorId.BossRule),
                    C("完成评鉴可以获得星星，以及丰厚的奖励！", TutorialAnchorId.Score)),

                [TutorialId.FirstFailureHeart] = BuildFirstFailureHeart(),

                [TutorialId.Failure] = new TutorialSequenceDefinition(
                    TutorialId.Failure,
                    C("别灰心，老板！之后铛铛会在经营结果里说明红心规则。", TutorialAnchorId.HeartBreak)),

                [TutorialId.PassiveItem] = new TutorialSequenceDefinition(
                    TutorialId.PassiveItem,
                    C("装饰品获得后会永久生效。这可是我们提升餐厅实力的重要方面呢！")),
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
            choices = null;
            // 普通局（包括 Balance Lab 的隔离模拟）绝不能为了判断教程覆写而读取
            // 玩家教程存档。先以 run 自身状态短路，再查询仅教程局需要的进度。
            if (run == null || !run.IsTutorialRun)
            {
                return false;
            }

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
