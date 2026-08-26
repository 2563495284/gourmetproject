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
        internal const string FirstBattleSettleHint = "tutorial.core.first_battle_settle_hint";
        internal const string FirstBattleSettlementOrderHint = "tutorial.core.first_battle_settlement_order_hint";
        public const string Settlement = "tutorial.core.settlement";
        public const string SecondAction = "tutorial.core.second_action";
        public const string TimelineNode = "tutorial.core.timeline_node";
        public const string CoreComplete = "tutorial.core.complete";
        public const string Flavor = "tutorial.hook.flavor";
        public const string Adjustment = "tutorial.hook.adjustment";
        public const string Boss = "tutorial.hook.boss";
        internal const string FirstFailureHeart = "tutorial.hook.first_failure_heart";
        // 旧存档兼容：保留 ID，但不再提供或播放对应教程内容。
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
        public const string AcquiredPassiveItem = "item.acquired.passive";
        public const string AcquiredActiveItem = "item.acquired.active";
        public const string BossRule = "boss.rule";
    }

    public static class TutorialSignal
    {
        public const string ActionPicked = "action.picked";
        public const string DishPlaced = "battle.dish_placed";
        public const string SettleClicked = "battle.settle_clicked";
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

        internal static TutorialSequenceDefinition BuildFirstFailureHeart()
        {
            return new TutorialSequenceDefinition(
                TutorialId.FirstFailureHeart,
                new TutorialStepDefinition(
                    "别灰心，老板！这次没有达到目标，我们会损失1颗红心。",
                    TutorialMascotPose.Remind,
                    TutorialAdvanceMode.Continue,
                    signal: null,
                    enterCommand: null,
                    exitCommand: null,
                    allowTargetInteraction: false,
                    TutorialAnchorId.Hearts),
                new TutorialStepDefinition(
                    "营业失败会损失红心，红心归零本局就会结束。",
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
                [TutorialId.FirstAction] = new TutorialSequenceDefinition(
                    TutorialId.FirstAction,
                    C("这是行动卡，行动会消耗时间，带来收益。", TutorialAnchorId.ActionCard0),
                    S(
                        "卡片上的图标，表示行动的额外奖励，点击开始营业吧！",
                        TutorialSignal.ActionPicked,
                        TutorialAnchorId.ActionCard0)),

                [TutorialId.FirstBattle] = new TutorialSequenceDefinition(
                    TutorialId.FirstBattle,
                    C(
                        "偷偷告诉老板营业的秘诀，就是把尽可能多的食物摆上餐桌。",
                        TutorialAnchorId.Table),
                    new TutorialStepDefinition(
                        "老板，看这里！食物会从食谱中抽取。",
                        TutorialMascotPose.PointRight,
                        TutorialAdvanceMode.Continue,
                        signal: null,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: false,
                        TutorialAnchorId.ServingOutlet),
                    new TutorialStepDefinition(
                        "你可以把它拖到餐桌上，也可以拖进垃圾桶丢弃",
                        TutorialMascotPose.PointRight,
                        TutorialAdvanceMode.Continue,
                        signal: null,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: false,
                        TutorialAnchorId.Table,
                        TutorialAnchorId.Discard),
                    V(
                        "这里是现在的食谱，后续获得的食物也可以从这里查看。",
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
                        "这里是本次营业需要达到的美味值，努力超过它吧！",
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
                        "等你准备好了，点击「结算」就可以完成本次经营！",
                        TutorialMascotPose.Remind,
                        TutorialAdvanceMode.Continue,
                        signal: null,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: false,
                        TutorialAnchorId.Settle)),

                [TutorialId.FirstBattleSettlementOrderHint] = new TutorialSequenceDefinition(
                    TutorialId.FirstBattleSettlementOrderHint,
                    new TutorialStepDefinition(
                        "食物会从上到下，从左到右开始结算。合理摆放位置可以发挥更大的作用！",
                        TutorialMascotPose.Explain,
                        TutorialAdvanceMode.Continue,
                        signal: null,
                        enterCommand: null,
                        exitCommand: null,
                        allowTargetInteraction: false,
                        TutorialAnchorId.Table)),

                [TutorialId.Settlement] = new TutorialSequenceDefinition(
                    TutorialId.Settlement,
                    C("这是本次营业的结果。总美味值达到目标即为成功，否则营业失败。", TutorialAnchorId.Score)),

                [TutorialId.SecondAction] = new TutorialSequenceDefinition(
                    TutorialId.SecondAction,
                    // ActionCard2 对应 TutorialActionScheduleOverride 里 tutorial_second 组的 act_food_hard_gold。
                    C("这是火热营业，目标更高，但奖励规格也更高。", TutorialAnchorId.ActionCard2)),

                [TutorialId.TimelineNode] = new TutorialSequenceDefinition(
                    TutorialId.TimelineNode,
                    C("普通行动推进时间轴时，经过的节点会依次触发。", TutorialAnchorId.ActionAxis)),

                [TutorialId.Flavor] = new TutorialSequenceDefinition(
                    TutorialId.Flavor,
                    C(
                        "老板，这是风味罐，可以帮我们为食物附加风味。",
                        TutorialAnchorId.AcquiredActiveItem),
                    C(
                        "风味会改变食物的属性和结算效果，每个食物只有 1 个风味位哦。",
                        TutorialAnchorId.AcquiredActiveItem)),

                [TutorialId.Adjustment] = new TutorialSequenceDefinition(
                    TutorialId.Adjustment,
                    C(
                        "这是调整单！它可以修改节点和行动。",
                        TutorialAnchorId.AcquiredActiveItem)),

                [TutorialId.Boss] = new TutorialSequenceDefinition(
                    TutorialId.Boss,
                    C("我们终于来到星级评鉴啦！星级评鉴拥有特殊规则，需要的美味值也更高。", TutorialAnchorId.BossRule),
                    C("完成评鉴可以获得星星，以及丰厚的奖励！", TutorialAnchorId.Score)),

                [TutorialId.FirstFailureHeart] = BuildFirstFailureHeart(),

                [TutorialId.PassiveItem] = new TutorialSequenceDefinition(
                    TutorialId.PassiveItem,
                    C(
                        "装饰品获得后会永久生效。这可是我们提升餐厅实力的重要方面呢！",
                        TutorialAnchorId.AcquiredPassiveItem)),
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
        private const string FirstActionGroup = "tutorial_first";
        private const string SecondActionGroup = "tutorial_second";
        private const float TargetScoreMultiplier = 0.5f;

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
                choices = Build(tables, run, FirstActionGroup, ("act_food_gold", 1f));
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
                    SecondActionGroup,
                    ("act_food_fragment", 1f),
                    ("act_food_passive", 1f),
                    ("act_food_hard_gold", 1.2f));
                return choices.Count == 3;
            }

            return false;
        }

        public static int ModifyTargetScore(
            GameRun run,
            ActionExecutionContext context,
            int targetScore)
        {
            return ModifyTargetScore(
                run != null && run.IsTutorialRun,
                context?.ActionGroupId,
                targetScore);
        }

        internal static int ModifyTargetScore(
            bool isTutorialRun,
            string actionGroupId,
            int targetScore)
        {
            if (!isTutorialRun
                || (actionGroupId != FirstActionGroup && actionGroupId != SecondActionGroup))
            {
                return targetScore;
            }

            int modified = (int)Math.Round(
                targetScore * TargetScoreMultiplier,
                MidpointRounding.AwayFromZero);
            return Math.Max(1, modified);
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
