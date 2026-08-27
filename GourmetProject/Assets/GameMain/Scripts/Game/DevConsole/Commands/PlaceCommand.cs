using System;
using System.Collections.Generic;
using System.Linq;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Presentation.Battle;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Data;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;

namespace GourmetProject.Game.DevConsole.Commands
{
    /// <summary>
    /// 无参数时把出餐口食物随机摆到餐桌；指定食物 id 时直接生成尽可能多的该食物。
    /// 出餐口抽菜仍走 <see cref="BattleSession.PrepareServeAutomatically"/>，摆位使用独立随机流。
    /// 指定食物模式不消耗食谱、出餐口或上菜次数，也不触发正常上菜事件。
    /// </summary>
    public sealed class PlaceCommand : ConsoleCommand
    {
        private const string PlacementStreamKey = "gm_place";
        private const int MinimumIterationGuard = 64;
        private const int IterationGuardPerCell = 8;

        public override string CmdName => "place";

        public override string Args => "[dish-id:string]";

        public override string Description =>
            "无参数时随机摆放出餐口食物；指定 id 时直接放置最大数量的相同食物。";

        public override CmdResult Execute(string[] args)
        {
            args ??= Array.Empty<string>();
            if (args.Length > 1)
            {
                return CmdResult.Fail("用法：place [dish-id]");
            }

            BattleForm battle = BattleForm.Active;
            BattleSession session = battle?.Session;
            if (battle == null || session == null || !battle.InBattle)
            {
                return CmdResult.Fail("需要在经营挑战中使用。");
            }

            if (battle.CurrentView != GameplayView.Food)
            {
                return CmdResult.Fail("请先进入经营挑战餐桌界面。");
            }

            if (session.IsSettled)
            {
                return CmdResult.Fail("当前经营挑战已经结算。");
            }

            BattleWorldController world = battle.ActiveWorld ?? BattleWorldController.Instance;
            if (world != null && world.IsFoodInteractionBusy)
            {
                return CmdResult.Fail("当前正在演出或拖拽，请结束后再摆放。");
            }

            if (args.Length == 1)
            {
                SpecifiedPlaceResult specified = FillSpecified(session, args[0]);
                if (specified.StopReason == SpecifiedPlaceStopReason.DishNotFound)
                {
                    return CmdResult.Fail($"找不到食物 '{args[0]}'。");
                }

                battle.RefreshAfterActiveItem(boardChanged: true);
                return FormatMessage(specified);
            }

            RandomPlaceResult result = Fill(session, CreatePlacementRandom());
            battle.RefreshAfterActiveItem(boardChanged: true);
            return CmdResult.Ok(FormatMessage(result));
        }

        public override IReadOnlyList<string> GetCompletions(string[] args)
        {
            GameplayDatabase database = BattleForm.Active?.Session?.Database;
            if (database == null)
            {
                return Array.Empty<string>();
            }

            string partial = args == null || args.Length == 0
                ? string.Empty
                : args[args.Length - 1];
            return CompleteDishIds(database, partial);
        }

        internal static IReadOnlyList<string> AllDishIds(GameplayDatabase database)
        {
            if (database == null)
            {
                return Array.Empty<string>();
            }

            return database.AllDishes
                .Select(dish => dish.Id)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static IReadOnlyList<string> CompleteDishIds(
            GameplayDatabase database,
            string partial)
            => Match(AllDishIds(database), partial);

        internal static SpecifiedPlaceResult FillSpecified(BattleSession session, string dishId)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var result = new SpecifiedPlaceResult();
            if (session.IsSettled)
            {
                result.StopReason = SpecifiedPlaceStopReason.Settled;
                return result;
            }

            DishDef dish = session.Database.GetDish(dishId);
            if (dish == null)
            {
                result.StopReason = SpecifiedPlaceStopReason.DishNotFound;
                return result;
            }

            result.Dish = dish;
            if (session.HasPendingTablePlacements)
            {
                IReadOnlyList<PendingDishConfirmResult> confirmed =
                    session.ConfirmAllPendingTableDishes();
                result.ConfirmedPendingCount = confirmed.Count;
            }

            RepeatedDishPlacementResult placementResult =
                RepeatedDishPlacementSolver.SolveWithDiagnostics(
                session.DiningTable,
                dish);
            IReadOnlyList<Placement> plan = placementResult.Placements;
            result.SearchTruncated = placementResult.Truncated;
            result.SearchNodes = placementResult.SearchNodes;
            if (plan.Count == 0)
            {
                result.StopReason = SpecifiedPlaceStopReason.NoLegalPlacement;
                return result;
            }

            foreach (Placement placement in plan)
            {
                if (!session.GenerateDishAt(dish.Id, placement.Origin))
                {
                    result.StopReason = SpecifiedPlaceStopReason.GenerateFailed;
                    return result;
                }

                result.PlacedCount++;
            }

            result.StopReason = SpecifiedPlaceStopReason.Completed;
            return result;
        }

        internal static RandomPlaceResult Fill(BattleSession session, IRandomStream placementRandom)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (placementRandom == null)
            {
                throw new ArgumentNullException(nameof(placementRandom));
            }

            var result = new RandomPlaceResult();
            if (session.IsSettled)
            {
                result.StopReason = RandomPlaceStopReason.Settled;
                return result;
            }

            int maxIterations = Math.Max(
                MinimumIterationGuard,
                session.DiningTable.CellCapacity * IterationGuardPerCell);
            for (int i = 0; i < maxIterations; i++)
            {
                if (session.HasPendingTablePlacements)
                {
                    IReadOnlyList<PendingDishConfirmResult> confirmed = session.ConfirmAllPendingTableDishes();
                    result.ConfirmedPendingCount += confirmed.Count;
                }

                if (session.PreparedServe == null)
                {
                    ServePrepareResult prepared = session.PrepareServeAutomatically(0);
                    result.LastPrepareOutcome = prepared.Outcome;
                    if (!prepared.Success)
                    {
                        result.StopReason = RandomPlaceStopReason.OutletEmpty;
                        return result;
                    }
                }

                IReadOnlyList<Placement> placements = session.FindPreparedServePlacements();
                if (placements == null || placements.Count == 0)
                {
                    result.StopReason = RandomPlaceStopReason.CannotPlace;
                    result.OutletHasUnplaceableDish = session.PreparedServe != null;
                    return result;
                }

                Placement chosen = placementRandom.Pick(placements);
                ServeResult placed = session.CommitPreparedServe(chosen);
                if (!placed.Success)
                {
                    result.StopReason = RandomPlaceStopReason.CommitFailed;
                    result.LastServeOutcome = placed.Outcome;
                    return result;
                }

                result.PlacedCount++;
                result.LastServeOutcome = placed.Outcome;
            }

            result.StopReason = RandomPlaceStopReason.IterationLimit;
            result.OutletHasUnplaceableDish = session.PreparedServe != null;
            return result;
        }

        private static IRandomStream CreatePlacementRandom()
        {
            return GameApp.Random != null && GameApp.Random.IsInitialized
                ? GameApp.Random.Cosmetic(PlacementStreamKey)
                : new Xoshiro256SS(1UL);
        }

        private static string FormatMessage(RandomPlaceResult result)
        {
            string placed = result.PlacedCount > 0
                ? $"已随机摆放 {result.PlacedCount} 道菜"
                : "没有新摆放的食物";
            switch (result.StopReason)
            {
                case RandomPlaceStopReason.CannotPlace:
                    return $"{placed}。出餐口食物当前无法摆放。";
                case RandomPlaceStopReason.OutletEmpty:
                    return $"{placed}。出餐口已无食物。";
                case RandomPlaceStopReason.CommitFailed:
                    return $"{placed}。确认摆放失败。";
                case RandomPlaceStopReason.IterationLimit:
                    return $"{placed}。达到摆放次数上限。";
                case RandomPlaceStopReason.Settled:
                    return "当前经营挑战已经结算。";
                default:
                    return placed + "。";
            }
        }

        private static CmdResult FormatMessage(SpecifiedPlaceResult result)
        {
            string identity = result.Dish == null
                ? "指定食物"
                : $"'{result.Dish.Id}'（{result.Dish.Name}）";
            switch (result.StopReason)
            {
                case SpecifiedPlaceStopReason.Completed:
                    string searchNote = result.SearchTruncated
                        ? "（搜索达到上限，已采用当前找到的最多布局）"
                        : string.Empty;
                    return CmdResult.Ok(
                        $"已放置 {result.PlacedCount} 个 {identity}{searchNote}。");
                case SpecifiedPlaceStopReason.NoLegalPlacement:
                    return CmdResult.Fail($"{identity} 当前在餐桌上没有合法位置。");
                case SpecifiedPlaceStopReason.GenerateFailed:
                    return CmdResult.Fail(
                        $"放置 {identity} 时发生状态变化，已成功放置 {result.PlacedCount} 个。");
                case SpecifiedPlaceStopReason.Settled:
                    return CmdResult.Fail("当前经营挑战已经结算。");
                default:
                    return CmdResult.Fail($"无法放置 {identity}。");
            }
        }
    }

    internal enum RandomPlaceStopReason
    {
        None,
        OutletEmpty,
        CannotPlace,
        CommitFailed,
        Settled,
        IterationLimit,
    }

    internal sealed class RandomPlaceResult
    {
        public int PlacedCount;
        public int ConfirmedPendingCount;
        public bool OutletHasUnplaceableDish;
        public RandomPlaceStopReason StopReason;
        public ServePrepareOutcome LastPrepareOutcome;
        public ServeOutcome LastServeOutcome;
    }

    internal enum SpecifiedPlaceStopReason
    {
        None,
        Completed,
        DishNotFound,
        NoLegalPlacement,
        GenerateFailed,
        Settled,
    }

    internal sealed class SpecifiedPlaceResult
    {
        public DishDef Dish;
        public int PlacedCount;
        public int ConfirmedPendingCount;
        public int SearchNodes;
        public bool SearchTruncated;
        public SpecifiedPlaceStopReason StopReason;
    }
}
